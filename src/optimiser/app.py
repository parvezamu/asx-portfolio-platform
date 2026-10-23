"""FastAPI microservice exposing the optimiser. Stateless: callers send returns."""
from typing import Literal
import numpy as np
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field, model_validator

from optimiser import optimise

app = FastAPI(title="ASX Portfolio Optimiser", version="0.1.0")

MAX_ASSETS, MAX_OBS = 100, 6000   # bound request size (DoS guard)


class OptimiseRequest(BaseModel):
    tickers: list[str] = Field(min_length=2, max_length=MAX_ASSETS)
    returns: list[list[float]] = Field(description="T rows x N columns of daily returns")
    method: Literal["min_variance", "max_sharpe", "risk_parity"] = "min_variance"
    max_weight: float = Field(1.0, gt=0, le=1)
    risk_free_rate: float = Field(0.0, ge=0, le=0.2)

    @model_validator(mode="after")
    def check_shape(self):
        if len(self.returns) > MAX_OBS:
            raise ValueError(f"at most {MAX_OBS} observations")
        if any(len(r) != len(self.tickers) for r in self.returns):
            raise ValueError("every returns row must have one value per ticker")
        return self


class OptimiseResponse(BaseModel):
    weights: dict[str, float]
    expected_return: float
    volatility: float
    sharpe: float | None


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/optimise", response_model=OptimiseResponse)
def optimise_endpoint(req: OptimiseRequest):
    try:
        out = optimise(np.asarray(req.returns, dtype=float), req.method,
                       req.max_weight, req.risk_free_rate)
    except (ValueError, RuntimeError) as e:
        raise HTTPException(status_code=422, detail=str(e))
    out["weights"] = {t: round(w, 6) for t, w in zip(req.tickers, out["weights"])}
    return out

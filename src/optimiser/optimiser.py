"""Portfolio optimisation: min-variance, max-Sharpe, risk parity (long-only, capped)."""
import numpy as np
from scipy.optimize import minimize
from sklearn.covariance import LedoitWolf

TRADING_DAYS = 252


def covariance(returns: np.ndarray) -> np.ndarray:
    """Annualised Ledoit-Wolf shrunk covariance. returns: (T, N) daily returns, no NaNs."""
    return LedoitWolf().fit(returns).covariance_ * TRADING_DAYS


def _solve(objective, n: int, max_weight: float) -> np.ndarray:
    if max_weight * n < 1:
        raise ValueError(f"max_weight={max_weight} infeasible for {n} assets")
    w0 = np.full(n, 1.0 / n)
    res = minimize(
        objective, w0, method="SLSQP",
        bounds=[(0.0, max_weight)] * n,
        constraints=[{"type": "eq", "fun": lambda w: w.sum() - 1.0}],
        options={"maxiter": 500, "ftol": 1e-12},
    )
    if not res.success:
        raise RuntimeError(f"optimiser failed: {res.message}")
    w = np.clip(res.x, 0, None)
    return w / w.sum()


def optimise(returns: np.ndarray, method: str, max_weight: float = 1.0, rf: float = 0.0) -> dict:
    if returns.ndim != 2 or returns.shape[0] < 60:
        raise ValueError("need a (T>=60, N) matrix of daily returns")
    if not np.isfinite(returns).all():
        raise ValueError("returns contain NaN/inf; align and clean before calling")
    n = returns.shape[1]
    cov = covariance(returns)
    mu = returns.mean(axis=0) * TRADING_DAYS

    if method == "min_variance":
        w = _solve(lambda w: w @ cov @ w, n, max_weight)
    elif method == "max_sharpe":
        w = _solve(lambda w: -(w @ mu - rf) / np.sqrt(w @ cov @ w), n, max_weight)
    elif method == "risk_parity":
        def rp(w):
            port_var = w @ cov @ w
            rc = w * (cov @ w) / port_var          # risk contributions, sum to 1
            return ((rc - 1.0 / n) ** 2).sum()
        w = _solve(rp, n, max_weight)
    else:
        raise ValueError(f"unknown method '{method}'")

    vol = float(np.sqrt(w @ cov @ w))
    ret = float(w @ mu)
    return {"weights": w.tolist(), "expected_return": ret, "volatility": vol,
            "sharpe": (ret - rf) / vol if vol > 0 else None}

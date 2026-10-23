"""Golden tests: the harness agents must keep green."""
import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import numpy as np
import pytest
from fastapi.testclient import TestClient
from optimiser import optimise, covariance
from app import app

rng = np.random.default_rng(42)
VOLS = np.array([0.10, 0.20, 0.30]) / np.sqrt(252)
R = rng.normal(0.0004, VOLS, size=(1500, 3))   # independent assets, different vols


@pytest.mark.parametrize("method", ["min_variance", "max_sharpe", "risk_parity"])
def test_weights_valid(method):
    w = np.array(optimise(R, method)["weights"])
    assert np.isclose(w.sum(), 1.0) and (w >= -1e-9).all()


def test_min_variance_prefers_low_vol():
    w = optimise(R, "min_variance")["weights"]
    assert w[0] > w[1] > w[2]


def test_risk_parity_roughly_inverse_vol():
    # independent assets -> risk parity ~ inverse-vol weights
    w = np.array(optimise(R, "risk_parity")["weights"])
    inv = (1 / VOLS) / (1 / VOLS).sum()
    assert np.allclose(w, inv, atol=0.03)


def test_max_weight_cap_respected():
    w = np.array(optimise(R, "min_variance", max_weight=0.4)["weights"])
    assert w.max() <= 0.4 + 1e-6


def test_infeasible_cap_rejected():
    with pytest.raises(ValueError):
        optimise(R, "min_variance", max_weight=0.2)


def test_nan_rejected():
    bad = R.copy(); bad[0, 0] = np.nan
    with pytest.raises(ValueError):
        optimise(bad, "min_variance")


def test_covariance_psd():
    assert (np.linalg.eigvalsh(covariance(R)) > 0).all()


def test_api_roundtrip_and_validation():
    c = TestClient(app)
    ok = c.post("/optimise", json={"tickers": ["A", "B", "C"], "returns": R[:300].tolist()})
    assert ok.status_code == 200 and set(ok.json()["weights"]) == {"A", "B", "C"}
    bad = c.post("/optimise", json={"tickers": ["A", "B"], "returns": R[:300].tolist()})
    assert bad.status_code == 422

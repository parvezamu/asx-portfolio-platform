"""Clean raw ASX CSVs and load into SQL Server.

Usage:
  export SQL_CONN="Driver={ODBC Driver 18 for SQL Server};Server=localhost,1433;Database=asx;Uid=sa;Pwd=...;TrustServerCertificate=yes"
  python tools/load_data.py --raw data/raw --sectors data/asx100_sector_map.csv
Never commit SQL_CONN. In Azure, use managed identity (Authentication=ActiveDirectoryMsi).
"""
import argparse, glob, os, pathlib
import numpy as np
import pandas as pd


def clean_returns(raw_dir: str) -> pd.DataFrame:
    P, V = {}, {}
    for f in glob.glob(os.path.join(raw_dir, "*.csv")):
        d = pd.read_csv(f, parse_dates=["Date"], index_col="Date")
        t = pathlib.Path(f).stem
        P[t], V[t] = d["Adj Close"], d["Volume"]
    P = pd.DataFrame(P).sort_index()
    V = pd.DataFrame(V).reindex(P.index)
    listed = P.ffill().notna().sum(axis=1)
    keep = P.notna().sum(axis=1) >= 0.5 * listed          # drop market holidays
    P, V = P[keep], V[keep]
    R = P.pct_change(fill_method=None)
    if "IPL" in R:                                          # broken dividend adjustment
        R.loc[:"2007-12-31", "IPL"] = np.nan
    nxt = R.shift(-1)                                       # one-day bad prints
    spike = (R.abs() > 0.25) & (nxt.abs() > 0.20) & (np.sign(R) != np.sign(nxt))
    R[spike] = np.nan
    R[spike.shift(1, fill_value=False)] = np.nan
    R[V == 0] = np.nan                                      # stale, untradable
    return R


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", required=True)
    ap.add_argument("--sectors", required=True)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    R = clean_returns(a.raw)
    long = R.stack().dropna().rename("Ret").reset_index()
    long.columns = ["TradeDate", "Ticker", "Ret"]
    sec = pd.read_csv(a.sectors)
    print(f"{len(sec)} companies, {len(long):,} return rows")
    if a.dry_run:
        return

    import pyodbc
    conn = pyodbc.connect(os.environ["SQL_CONN"], autocommit=False)
    cur = conn.cursor(); cur.fast_executemany = True
    cur.execute("DELETE FROM dbo.DailyReturn; DELETE FROM dbo.Company;")
    cur.executemany("INSERT INTO dbo.Company (Ticker, Name, Sector) VALUES (?,?,?)",
                    sec[["ticker", "company", "sector"]].values.tolist())
    rows = [(t, d.date(), float(r)) for d, t, r in long.itertuples(index=False)]
    for i in range(0, len(rows), 50_000):
        cur.executemany("INSERT INTO dbo.DailyReturn (Ticker, TradeDate, Ret) VALUES (?,?,?)",
                        rows[i:i + 50_000])
    conn.commit()
    print("loaded")


if __name__ == "__main__":
    main()

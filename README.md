# ASX Sector Portfolio Platform

**In plain terms:** this system takes 20 years of share prices for Australia's 100 largest listed companies, groups them by industry, and calculates how to split money across companies in an industry to get the best balance of return and risk. It runs as a secure cloud service on Microsoft Azure, and was built using AI coding agents under human review.

**Technically:** .NET 8 C# API + Python optimisation microservice + Azure SQL, deployed on Azure (App Service, Container Apps, Functions, Data Factory, Key Vault, APIM, App Gateway, Monitor), with a DevSecOps pipeline (CodeQL, SCA, Trivy, ZAP) and a governed agentic delivery workflow.

## Architecture
```
Blob (raw CSV) --ADF--> Azure SQL <--Dapper-- C# API (App Service) --HTTP--> Python optimiser (Container Apps)
                                                ^
                               App Gateway (WAF) -> APIM -> API
             Key Vault (managed identity)   App Insights / Log Analytics   Functions (nightly recompute)
```

## Run locally
```bash
cp .env.example .env                # set a strong SA_PASSWORD
docker compose up -d sql optimiser
sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -Q "CREATE DATABASE asx" && sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -d asx -i db/schema.sql
# put the 100 CSVs in data/raw/
pip install pandas pyodbc
export SQL_CONN="Driver={ODBC Driver 18 for SQL Server};Server=localhost,1433;Database=asx;Uid=sa;Pwd=$SA_PASSWORD;TrustServerCertificate=yes"
python tools/load_data.py --raw data/raw --sectors data/asx100_sector_map.csv
docker compose up -d api           # Swagger at http://localhost:8080/swagger
```

## API
| Method | Path | Purpose |
|---|---|---|
| GET | `/api/sectors` | List sectors |
| GET | `/api/sectors/{sector}/tickers` | Tickers in a sector |
| POST | `/api/portfolios/optimise` | Optimal weights for a sector or ticker list |

```json
POST /api/portfolios/optimise
{ "sector": "Health Care", "from": "2012-01-01", "to": "2019-12-31", "method": "risk_parity", "maxWeight": 0.35 }
```

## Data caveats
Universe is the top 100 ASX companies as of 2020, so backtests carry survivorship bias. Cleaning rules (bad prints, stale zero-volume days, a broken dividend adjustment) are in `tools/load_data.py`. Sector map is GICS ~2020, compiled manually.

## How this was built
See `AGENTS.md`, `.github/copilot-instructions.md`, `.github/prompts/`, and `docs/agent-log.md` for the agentic workflow and review evidence.

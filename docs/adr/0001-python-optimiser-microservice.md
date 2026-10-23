# ADR 0001: Optimiser as a separate Python microservice

Status: accepted

## Context
Numerical optimisation (SciPy, scikit-learn Ledoit-Wolf) is mature in Python and weak in .NET. The API and data access are C#.

## Decision
Run the optimiser as a stateless FastAPI container on Azure Container Apps. The C# API owns data access and calls it over HTTP.

## Consequences
+ Right tool per job; optimiser scales independently (scale to zero when idle).
+ Stateless service is easy to test with golden tests.
- Extra network hop and a second deployable; payloads are bounded (≤100 assets × 6000 days).

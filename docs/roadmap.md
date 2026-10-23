# 8-week roadmap: build skills + build evidence

Rule for every week: ship something visible (merged PR, deployed resource, log entry). Interviewers ask "tell me about a time…" — this produces the times.

| Week | Build | Skills gained | Evidence to keep |
|---|---|---|---|
| 1 | Run the stack locally: SQL in Docker, load data, optimiser + API working. Read every line of the C# API; rewrite one endpoint yourself without AI. | C#, ASP.NET Core minimal APIs, Dapper, T-SQL, Docker | Screenshot of Swagger call; notes on what each C# construct does |
| 2 | Add `GET /api/sectors/{sector}/stats` (ann. return, vol, max drawdown) **using Copilot agent mode with the `new-endpoint` prompt**. Add xUnit + WebApplicationFactory integration tests. | Copilot agent mode, prompt files, .NET testing | First agent-log entries: what the agent got wrong |
| 3 | GitHub repo public, CI green, branch protection on, Dependabot on, secret scanning + push protection on. Assign 2 issues to the Copilot coding agent; review its PRs with `security-review` prompt. | CI/CD, SAST, SCA, container scanning, governed agentic SDLC | PR history showing agent PR → review → your changes → approval |
| 4 | Azure: Bicep for Resource Group, Azure SQL (serverless), ACR, Container Apps (optimiser), App Service (API), Key Vault, App Insights. Managed identity everywhere, zero secrets. Deploy via GitHub Actions with OIDC (no stored Azure credentials). | Azure core services, IaC, identity, PowerShell/Bash | Architecture diagram from real deployment; cost notes |
| 5 | Data Factory pipeline Blob → SQL staging; Azure Function (timer) recomputes sector stats nightly; KQL dashboard + alert in Log Analytics. | ADF, Functions, Monitor/KQL | Workbook screenshot; one alert firing in a test |
| 6 | APIM in front of the API (subscription keys, rate limit); App Gateway WAF (deploy for demo, then tear down). Staging slot + OWASP ZAP baseline in CI; production environment with manual approval. | APIM, App Gateway, DAST, release governance | ZAP report; approval gate screenshot |
| 7 | AI feature: per-ticker news summary via an LLM, with prompt-injection tests (malicious headline in test data), output schema validation, no tool access, no secrets in prompt. Build a custom agent (e.g. "SQL migration agent") and a Copilot skill; document them for "team reuse". | Responsible/secure AI, context engineering, reusable AI assets | Injection test cases + results; agent/skill docs |
| 8 | DR runbook (SQL geo-replication, failover test; note why Site Recovery targets VMs, not PaaS). Polish README, record 3-min demo, write a short blog post on "governing coding agents". | DR, communication | Demo video, blog link, runbook |

## Certifications (optional, adds credibility for a career changer)
- **AZ-204 (Azure Developer Associate)**: maps almost exactly to weeks 4–6. Highest value for this JD.
- AZ-900 only if you want a quick warm-up; skip otherwise.

## While building: start applying from week 3
- Applications don't need the project finished; a public repo with green CI and agent PR history is already differentiating.
- Target titles: Software Engineer (.NET/Azure), Cloud Application Developer, AI Engineer / AI-augmented Developer, Platform Engineer, Research Software Engineer (good bridge from PhD).

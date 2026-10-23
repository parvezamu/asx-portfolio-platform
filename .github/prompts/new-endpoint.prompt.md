---
description: Add a new endpoint to the ASX API with validation and tests
---
Add an endpoint to `src/Api/Program.cs`.

Endpoint: ${input:endpoint:e.g. GET /api/sectors/{sector}/stats}
Behaviour: ${input:behaviour:what it returns}

Requirements:
- Request/response as records; validate input with DataAnnotations; return ProblemDetails on errors.
- Data access via `ReturnsRepository` using parameterised Dapper queries only.
- Async with CancellationToken end to end.
- Add or update an integration test using WebApplicationFactory<Program>.
- Update README "API" section.
Finish by listing what you verified and any assumptions.

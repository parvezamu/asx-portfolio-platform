# Agent operating guide

See `.github/copilot-instructions.md` for architecture and rules. This file defines how agents work here.

## Loop
1. Read the issue and restate acceptance criteria in the PR description before coding.
2. Make the smallest change that satisfies them.
3. Run the test harness (`pytest`, `dotnet build`). Iterate until green. **Max 5 iterations**; if still failing, stop and write up what you tried and where you are stuck.
4. Open a draft PR. Never merge. Never push to `main`.

## Escalate to a human (stop, don't guess) when
- A change touches auth, secrets, networking, IAM/RBAC, or `db/schema.sql`.
- A test would need to be deleted or loosened.
- Requirements are ambiguous.

## Review gate
Every PR needs: green CI (tests, CodeQL, SCA, container scan) + independent review (security-review prompt or a second agent) + human approval. Branch protection enforces this.

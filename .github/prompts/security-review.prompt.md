---
description: Independent security review of the current changes
---
Review the diff of the current branch against `main` as a security reviewer. You did not write this code.

Check, and report each as PASS / FAIL / N/A with file:line evidence:
1. Secrets or credentials in code, config, tests or logs.
2. SQL built by string concatenation; unparameterised queries.
3. Missing input validation or unbounded request sizes.
4. Untrusted text (CSV, news, issue bodies) reaching an LLM prompt or a shell/SQL command; prompt-injection paths.
5. New dependencies: pinned? necessary? known CVEs?
6. Containers: non-root, minimal base image, no secrets in layers.
7. Error responses leaking stack traces or internals.
8. OWASP Top 10 items relevant to the change.

Do not fix anything. Output a findings table, then a verdict: APPROVE / REQUEST CHANGES.

# Agent delivery log

Evidence of judgement: what agents produced, what you caught, what you changed.

| Date | Issue/PR | Agent/tool | Task | What went wrong / what I caught | Action taken |
|---|---|---|---|---|---|
| | | | | | |
| 2026-10-24 | key-vault | Claude | Secret rotation | Expected revision restart to load new Key Vault secret version; it didn't (unversioned KV refs refresh periodically). Old key kept working, new key rejected | Forced resync via secret set + restart; follow-up: support primary+secondary API keys for zero-downtime rotation |

# integrations — plan overview

Entry point for the **integrations** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 16 | `16-story-integrations.md` | Integrations | integrations | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan

- **ERP sync is the public API and webhooks, not a bespoke connector.** The intake asks for
  "ERP synchronisation for customers and related master data". Writing a connector against an
  ERP nobody has named would be guessing at its schema and auth; a versioned read/write API
  plus outbound events is the surface any ERP integrates through, and it works for the next
  system as well.
- **API keys are a second authentication scheme, not bearer tokens in disguise.** An
  integration is not a person: it has no refresh token, no roles, and a scope list an
  administrator chose. Keeping the schemes separate is what lets a key read tickets without
  inheriting everything an agent can do.
- **API key requests are desk-level.** Department scoping keeps one team out of another's
  work, and a key belongs to no team. Left to the fail-closed rule an integration saw
  **nothing**, which protects nobody and makes the public API return empty lists — what
  actually bounds a key is its scope list.
- **Only a hash of a key is stored.** The plan left storage open. A key readable from the
  database is a key that leaks with a backup; the prefix is kept in clear so an operator can
  still identify one in a list or a log.
- **Webhook signatures cover a timestamp as well as the body.** Signing the body alone lets a
  captured delivery be replayed with a fresh header and still verify.
- **A delivery stores its payload.** A redelivery resends exactly what the first attempt did.
  Regenerating it would describe the state as it is now, which is not the event that happened.

## Verification

Exercised over HTTP against SQL Server, with a real receiver that verified every signature:

- **API keys** — created with an explicit scope list; the plain value is returned once.
  The key works on `/api/v1/public/tickets`, and is refused (401/403) on `/api/customers`,
  `/api/users` and `/api/reports/tickets`, which are outside its scopes.
- **Key integrity** — a key with one character changed, and an unknown prefix, both return
  401. Revoking one stops it on the very next request.
- **Webhook delivery** — creating a ticket and moving it Open then Resolved queued four
  events; all four delivered on the first attempt with HTTP 200.
- **Signatures** — the receiver recomputed HMAC-SHA256 over `timestamp.body` and **verified
  all four**. Three deliveries from a second subscription with a different secret arrived at
  the same URL and were **correctly rejected**, which is the signature doing its job.
- **Backoff** — a subscription pointed at a dead port went to Retrying after one attempt,
  recorded the connection error, and scheduled the next attempt rather than hammering.
- **Redelivery** — resets the attempt count and resends the stored payload.
- **Validation** — a non-URL, an invented event name and an invented scope are each refused
  with a message naming what was wrong.

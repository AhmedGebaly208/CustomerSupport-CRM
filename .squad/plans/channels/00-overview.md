# channels — plan overview

Entry point for the **channels** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 12 | `12-story-communication-channels.md` | Communication Channels | communication-channels | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan

- **The provider is a working local adapter, not a stub.** Per the decision to build the
  abstraction with a working local provider, `LocalDropChannelAdapter` delivers to a folder
  and the inbound endpoint accepts the same shape a webhook would. Outbound messages become
  real files with a provider message id; inbound genuinely raises and threads tickets. Adding
  a real provider means registering a sibling adapter, with nothing above it changing.
- **Phone matching compares trailing digits, not a canonical prefix.** The plan said
  "normalise to E.164". Nothing in an inbound message says which country a bare
  `0551234567` belongs to, so a prefix-based canonical form would leave it as a different
  customer from `+966551234567`. Matching on the last nine digits joins them; inventing a
  country code would be inventing data.
- **No `Customer.IsIncomplete` flag.** The plan added one for auto-created customers. The
  record already shows what it is missing, and a second source of truth would need clearing
  by hand and would drift.
- **`ArticleTicketLink`-style `CommentId` dropped from `ChannelMessage` threading.** The
  comment id is kept only as the outbound idempotency key; threading uses the reference
  token, the provider conversation id, and mail headers.
- **The inbound endpoint requires authentication.** A real provider webhook authenticates
  with a shared secret, which belongs to the `integrations` story. Until then the endpoint is
  gated on `tickets.create` so nothing can raise tickets anonymously.

## Verification

Exercised over HTTP against SQL Server, with the local adapter:

- **Channel status** — four channels report an adapter; all start disabled.
- **Unknown sender** — an email from an unseen address created both a customer and a ticket.
- **Idempotency** — replaying the same provider message id returned `duplicate: true` and
  created nothing.
- **Threading** — a reply whose subject carried `[TKT-000010]` landed on the same ticket
  without creating a customer or a ticket.
- **Outbound** — a public comment was queued, delivered by the sweep, and **written to disk**
  with the right recipient, the stamped subject `[TKT-000010] مشكلة في الدخول`, and the
  Arabic body intact.
- **Internal notes do not leave** — adding one produced no ledger row.
- **A disabled channel does not queue** — replying after switching Email off added nothing.
- **Phone matching** — an inbound WhatsApp message from `0551234567` matched the existing
  customer stored as `+966551234567` instead of creating a duplicate.

One defect found and fixed during verification: the adapter wrote its JSON with a UTF-8 BOM
(.NET's `Encoding.UTF8` emits one), which breaks strict parsers — a standard JSON reader
rejected the file. It now writes `UTF8Encoding(false)`.

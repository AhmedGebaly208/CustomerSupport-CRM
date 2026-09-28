# customer-portal — plan overview

Entry point for the **customer-portal** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 15 | `15-story-customer-portal.md` | Customer Portal | customer-portal | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan

- **Portal permissions are a separate set, not the staff ones.** The Customer role previously
  held `tickets.view`, `tickets.create`, `tickets.comment` and the attachment permissions.
  Only the fail-closed department scope stood between a portal customer and the desk's queue,
  and a customer given a department claim by mistake would have seen that whole department.
  The role now holds `portal.*` and `kb.view` only, so a staff endpoint refuses them whatever
  their scope says — verified against five staff routes.
- **Portal DTOs are dedicated types, not filtered staff DTOs.** They have no property for an
  agent, a department, an escalation level or an SLA date. Stripping fields is a rule that
  holds until someone adds a field and forgets; a type cannot leak what it has no property
  for.
- **No customer id appears anywhere in the portal surface.** The service resolves it from the
  signed-in user, so there is no argument to tamper with.
- **A dedicated endpoint grants portal access.** The plan assumed `Customer.UserId` could be
  set through the ordinary edit form. Linking a login grants someone sight of a record, which
  is not the same kind of change as correcting a phone number, so it is its own action gated
  on `users.manage`, and one login cannot be linked to two customers.
- **Priority is clamped to High.** A picker that lets every customer choose the top of the
  queue stops meaning anything. The portal UI does not offer Urgent and the server clamps it
  regardless.

## Verification

Exercised over HTTP against SQL Server with two linked portal customers:

- **Isolation** — each sees only their own requests. Alice fetching Bob's ticket, its
  messages, or replying to it all return **404**, not 403: a 403 would confirm the id exists,
  which is itself a leak.
- **Internal notes never reach the portal** — an internal comment on Alice's own ticket was
  absent from her message list, while the public reply was present.
- **Staff endpoints refuse a portal token** — `/api/tickets`, `/api/customers`, `/api/users`
  and `/api/reports/tickets` all returned 403.
- **No staff fields** — the portal ticket payload carries exactly fifteen keys, none of them
  an agent, department, SLA date or escalation level.
- **Priority clamped** — a request asking for Urgent was stored as High.
- **Reopening** — closing a request then replying moved it to Reopened.
- **Help is public-only** — a published internal runbook was invisible to portal search and
  was not quoted by the chatbot.
- **One login per customer** — linking an already-linked login returned 409.

One bug found and fixed during verification: public articles were still passing through the
department scope filter, so a portal customer — who has no department — saw **zero** articles
and the help page was empty. A published, public article is customer-facing help rather than
a department's private material, so that path no longer applies the scope; staff reads, which
can see drafts and internal articles, still do.

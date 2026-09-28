# sla-automation — plan overview

Entry point for the **sla-automation** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 09 | `09-story-sla-and-automation.md` | SLA and Automation | sla-and-automation | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan

The planner ran without file access (`0 reads`), so parts of the plan assumed a shape the
code does not have. What was built instead, and why:

- **`BusinessHours` is already taken.** The plan defines a `BusinessHours` record in
  `Domain/Sla`. That name belongs to the configuration entity the schedule is stored in, so
  the value type is `WorkingWindow` and `BusinessCalendar` is built *from* those rows.
- **No `CalendarJson` on the policy.** The plan stored a serialised calendar per policy.
  The desk's opening hours and holidays already live in `BusinessHours` and `Holiday`;
  duplicating them per policy would give two sources of truth that drift. A policy instead
  carries `CountsBusinessHoursOnly` — it either honours the configured week or runs
  continuously.
- **`Ticket` needed no new columns.** `SlaPolicyId`, `FirstResponseDueAt`,
  `ResolutionDueAt`, `FirstRespondedAt` and `EscalationLevel` were already there from the
  bootstrap.
- **First response was already recorded.** `TicketService.AddCommentAsync` already set
  `FirstRespondedAt` on the first customer-visible reply, so the planned
  `MarkFirstResponseAsync` was dropped rather than becoming a second way to do it.
- **Notifications carry a code, not a sentence.** The plan stored rendered bilingual text.
  The row stores `Kind` plus parameters and the client renders it, matching the `errorCode`
  mechanism already in place — so a reader who switches language does not find a backlog in
  the other one, and no Arabic literals enter the C#.
- **`SlaEscalationEvent` got a matching query filter.** Its rule is soft-deletable and the
  event is not, which EF warns about; the filter mirrors `TicketHistory`'s treatment of its
  parent ticket.

## Verification

Exercised over HTTP against SQL Server:

- **Working-hours arithmetic** — a 4-hour first-response target starting Thursday 16:00
  Riyadh correctly lands Sunday 11:00, skipping Friday and Saturday; a 36-hour resolution
  target lands Wednesday 16:00.
- **Stamping** — a ticket is stamped with its policy and both due dates on create; raising
  or lowering priority re-resolves the policy while the clock still runs from `CreatedAt`.
- **First response** — an internal note leaves the clock `Running`; a customer-visible reply
  moves it to `Met`.
- **Pausing** — moving a ticket to `Pending` reports the resolution clock as `Paused`.
- **Escalation** — with a one-minute target, the sweep reports 0 before the target lapses
  and 1 after; a second sweep reports 0, proving a rule fires exactly once. The ticket's
  escalation level rises, history records the rule and threshold, and a notification is
  raised carrying parameters rather than wording.
- **Auto-assignment** — `LeastBusy` picked an agent on create.
- **Migration** — applied automatically at startup.

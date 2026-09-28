# agent-dashboard — plan overview

Entry point for the **agent-dashboard** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 10 | `10-story-agent-dashboard.md` | Agent Dashboard | agent-dashboard | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan

The planner ran without file access, so parts of it assumed a shape the code does not have:

- **Notifications already existed.** The plan defined `INotificationService` here; the SLA
  story had already introduced it, so this story consumes it rather than redefining it, and
  adds one kind (`TaskDue`).
- **Ids are `Guid`, not `string`.** The plan typed every user id as `string`.
- **`AuditableEntity` already carries soft delete.** The plan re-declared `IsDeleted`,
  `DeletedAt` and `DeletedBy` on each entity; they are inherited.
- **Tasks are single-language.** The plan gave `AgentTask` bilingual title and notes. A
  personal task is written by its owner for themselves, so there is nothing to translate —
  unlike a quick reply, which is customer-facing and stayed bilingual.
- **`TicketMention` got a matching query filter**, mirroring its comment's soft-delete
  filter, for the same reason `SlaEscalationEvent` did.
- **The ticket list projection was extracted**, not duplicated. `TicketService` now exposes
  `GetListItemsAsync` over a single shared `RowProjection`, so search, the agent board, the
  team board, mentions and the watch list cannot drift apart.
- **`TicketDetailDto` gained `CustomerPreferredLanguage`**, so a quick-reply snippet is
  inserted in the customer's language rather than the agent's.

## Verification

Exercised over HTTP against SQL Server:

- **Tasks** — create, complete (sets `CompletedAt`), reopen (clears it), delete.
- **Reminders** — a reminder without a due date is refused; one dated in the past is
  dispatched by the background service and arrives as a `TaskDue` notification.
- **Mentions** — `@handle` in a comment notifies that agent and puts the ticket on their
  board. A self-mention is ignored. An agent with no department sees nothing, which is the
  fail-closed scope rule working, not a gap.
- **Quick replies** — created, listed, and a duplicate shortcut refused with a clear message.
- **Team board** — totals, per-agent load, and the longest-unassigned queue.
- **Migration** — applied automatically at startup.

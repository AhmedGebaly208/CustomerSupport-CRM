# customers — plan overview

Entry point for the **customers** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 08 | `08-story-customer-management.md` | Customer Management | customer-management | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan (story 08)

The planner ran without file access, so parts of the plan assumed a shape the code does
not have. What was built instead, and why:

- **Merge conflict rule.** The plan refused a merge when both customers had open tickets.
  That is the *normal* duplicate case, and refusing it would make the feature useless.
  The merge is refused only when both records carry a portal login (`Customer.UserId`),
  because a customer row holds at most one and merging would silently revoke someone's
  access. Everything else is moved to the survivor: tickets, interactions, notes,
  contacts and attachments.
- **Transactions.** The plan called `BeginTransaction` directly. SQL Server runs with
  `EnableRetryOnFailure`, which rejects a user-initiated transaction unless the whole unit
  is wrapped in the execution strategy. Introduced `ITransactionRunner` for this. The
  delegate may be replayed, so it must load the entities it mutates *inside* the delegate —
  the runner clears the change tracker per attempt.
- **Import parsing.** Kept out of Application behind `ICustomerImportParser` so ClosedXML
  stays an Infrastructure dependency. Headers are matched bilingually (Arabic and English),
  and XLSX cells are read with `GetFormattedString()` so phone numbers do not arrive as
  `5.55E+08`.
- **Attachment delete** is a soft delete and the bytes are deliberately left on disk: the
  audit trail records the removal, and purging orphaned blobs belongs to a retention job.

## Verification

Exercised over HTTP against SQL Server:

- **Import** — 5 rows, 2 succeeded / 3 rejected with per-row reasons (missing English name,
  duplicate email, unknown department code); both `FullNameEn` and `الاسم بالعربية` headers matched.
- **Attachments** — upload, download byte-identical, `.exe` rejected with
  `400 File type '.exe' is not allowed.`
- **Activity timeline** — interleaves ticket / interaction / note / attachment in order.
- **Merge** — 1 ticket, 2 interactions, 1 note, 1 contact and 1 attachment moved; the
  merged record returns 404; merging a customer into itself returns 400.

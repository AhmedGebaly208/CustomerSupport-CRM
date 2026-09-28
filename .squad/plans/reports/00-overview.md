# reports — plan overview

Entry point for the **reports** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 14 | `14-story-reports-and-management.md` | Reports and Management | reports-and-management | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan

- **One timezone, not two.** The plan added `ReportsTimeZoneId` to system configuration. The
  SLA story already introduced `Sla:TimeZone`; a second setting that could disagree with it
  would be a bug waiting to happen, so bucketing uses the existing one.
- **CSAT storage had to be built.** The plan assumed it might exist. `TicketSatisfaction` is
  new: one row per ticket, enforced by a unique index, so a second submission corrects the
  first rather than weighting the average. A check constraint keeps the score inside 1–5,
  because an out-of-range value would poison every mean silently.
- **Timings are computed in C#, not `DateDiffMinute`.** That helper is a SQL Server
  extension, and Application must not know which provider is configured. The rows are
  materialised for the median anyway, so the subtraction costs nothing.
- **Export labels live in an embedded JSON resource**, not in string literals, for the same
  reason the UI keeps its text in locale files — and so no Arabic enters the C#.
- **The "unassigned" bucket returns a key, not a sentence.** The client translates `none`
  like every other label, so no display text is decided on the server.
- **Ids are `Guid`.** The plan typed the filters as `int?`.

## Verification

Exercised over HTTP against SQL Server:

- **Ticket report** — totals, previous-period comparison, and breakdowns by status, channel
  and department, including a bucket for tickets with no department.
- **Empty window** — a range with no tickets returns `null` for every ratio and average, not
  zero, so the UI shows a dash rather than reporting 0% attainment on a desk with no work.
- **Range guards** — a 500-day range and a reversed range are both refused with a clear
  message.
- **Agent report** — handled, resolved, reopen rate and current load per agent.
- **Satisfaction** — rating a resolved ticket works; rating it again corrects the score
  instead of adding a second response; a score of 9 is refused.
- **CSAT report** — average, response count, eligible count, response rate and the
  distribution across all five scores.
- **Exports** — the XLSX is a valid workbook, and the Arabic CSV opens with its headings
  intact (a BOM is written deliberately, since Excel otherwise reads a UTF-8 CSV in the
  system codepage and mangles Arabic). Missing measurements export as empty cells rather
  than zeros.

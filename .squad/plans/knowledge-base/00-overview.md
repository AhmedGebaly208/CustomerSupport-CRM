# knowledge-base — plan overview

Entry point for the **knowledge-base** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| _add rows as stories are planned_ |
| 11 | `11-story-knowledge-base.md` | Knowledge Base | knowledge-base | — |

## Dependency notes

_Describe sequencing, shared contracts, or cross-feature dependencies here._

## Deviations from the plan

- **Search uses stored normalised columns, not SQL full-text.** The plan assumed a full-text
  catalog. That is a server-level feature absent from a default SQL Express install, which
  would have made the story undeployable on the target machine. `SearchTextAr` and
  `SearchTextEn` are written on save and matched with `Contains`; an article base is small
  enough that this answers fast and it behaves identically on every deployment.
- **`ArticleVote.VoterKey` is never an IP address.** The plan left the key open. Keying on an
  address would turn the table into a log of who read what, so it holds a user id or an
  anonymous browser key.
- **`Article` gained `IsPublic`.** The plan had one audience switch — published or not. An
  internal runbook needs to be published to agents without reaching customers, so visibility
  is separate from status.
- **`ArticleTicketLink` dropped `CommentId`.** The plan keyed links on the comment as well.
  What the desk needs to know is that an article answered a ticket, not which sentence
  mentioned it, and the extra column only made the uniqueness rule harder to reason about.
- **Soft-delete filters mirrored onto every child** (`ArticleTag`, `ArticleVersion`,
  `ArticleVote`, `ArticleTicketLink`), as EF warned and as `TicketHistory` already does.
- **Publishing is a separate permission** (`kb.publish`) from authoring (`kb.manage`), so an
  agent can draft what they learned on a ticket without putting it in front of customers.

## Verification

Exercised over HTTP against SQL Server:

- **Category cycle guard** — making a category a child of its own descendant is refused.
- **Bilingual slugs** — generated per language, Arabic kept as Arabic
  (`استعاده-كلمه-المرور`), and **frozen at first publish**: a full retitle after publishing
  left `reset-your-password` unchanged, so shared links keep working.
- **Arabic search folding** — `كلمه المرور` finds `كلمة المرور`, and `استعاده` finds
  `استعادة`. Searching in English finds the same article.
- **Publish guard** — a half-translated article is refused with `kb.article-incomplete`.
- **Versions** — every edit snapshots the previous text; restoring an old version snapshots
  the current one first, so the restore is itself undoable.
- **Votes** — one per voter; voting the same way twice does not double-count, and changing
  your mind moves the vote rather than adding one.
- **Ticket links** — linking twice is idempotent, and deleting a cited article is refused
  with `kb.article-in-use`.

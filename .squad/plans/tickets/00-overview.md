# Tickets — plan overview

Feature slug: `tickets`
PDF area: 2

The bootstrap shipped the single-ticket lifecycle: create, categorise, prioritise, assign,
transition through `TicketWorkflow`, comment, and a full history. This story turns that into
something a desk can run at volume.

## Stories

| NN | File | Title | Status |
|----|------|-------|--------|
| 07 | `07-story-ticket-management.md` | Ticket Management | **Implemented** |

## What landed

**Bulk actions.** `POST /api/tickets/bulk/{assign,priority,status}` return a per-item result
rather than failing the batch. Each item reuses the existing single-ticket service method,
so the workflow rules, scope checks and history writes cannot be bypassed by going through
bulk. Deliberately not one transaction: an agent changing twenty tickets keeps the nineteen
that worked. Verified — closing one ticket then bulk-opening three returned 2 succeeded,
1 failed with the workflow's own reason.

**Typed links.** `DuplicateOf`, `RelatedTo`, `Blocks`. Stored once on the source; the detail
view unions outgoing and incoming so both pages render the link with no second row to keep
in step. Self-links are 400, duplicates 409.

**Merge.** Comments, interactions and attachments are re-pointed at the target with
`ExecuteUpdateAsync`; watchers and tags are moved with de-duplication; an internal note on
the target records where the content came from; the source closes with a history row naming
the target. Merging is the one path that closes a ticket regardless of what
`TicketWorkflow` would normally allow from that state — its content now lives on the
target, so leaving it open would double-count the work. The history row records that
exception rather than hiding it.

**Watchers.** Adding yourself needs no special permission; adding a colleague requires
`dashboard.viewteam`, because it puts a ticket on someone else's radar and will generate
notifications they did not ask for once `sla-automation` lands.

**Tags.** Free-form, created lazily on first attach so an agent can coin one mid-ticket.
Case-insensitive: `VIP` and `vip` resolve to the same tag (verified). A join entity rather
than a delimited column, so filtering stays indexable. Removing a tag from a ticket leaves
the tag itself alone.

**Saved views.** Per-user named filter combinations. The filter blob is opaque to the
server — validated as well-formed JSON and size-capped, never interpreted — so adding a
filter to the list page needs no backend change. Scoped entirely to the caller's token; no
user id appears in any route or payload.

**Category administration.** `views/admin/categories/CategoryAdminView.vue` over
`TicketCategoriesController` with create, update, reorder,
activate/deactivate and delete. Deactivating hides a category from pickers but leaves
existing tickets untouched, so historical reporting does not shift. Deleting is refused
while active tickets or sub-categories reference it. Cycle guard on both single edits and
whole-tree reorders, validated against the *proposed* shape before anything is written.
Writes live here, not on `LookupsController`, so a permission change on the read-only tree
every picker uses cannot accidentally expose category editing.

**Manual escalation.** ±1 with a mandatory reason, capped at 5, floored at 0. Recorded in
ticket history with the old and new level.

Migration `TicketOpsFeatures`: 5 new tables, 8 indexes, no destructive operation in `Up()`.

## Deviations from the plan, and why

- **The plan said add a nullable `int? EscalationLevel`.** It already exists as a
  non-nullable `int` with 0 meaning "not escalated", and the list and detail views already
  render it. Kept as-is; making it nullable would have broken working code for nothing.
- **The plan typed `UserId` as `string`.** This system's Identity uses `Guid` keys. Used
  `Guid` throughout, matching `Ticket.AssignedAgentId`.
- **The plan suggested extending `TicketHistory` with an `Action` discriminator or a
  `Details` column.** Its existing `Field` / `OldValue` / `NewValue` / `Note` already
  express every action the story needs. No schema change.
- **The plan offered an optional `TicketMerge` entity.** Skipped: the two history rows and
  the internal note carry the same information without a table that would need maintaining.
- **The plan suggested a new `Permissions.Categories.Manage`.** Reused the existing
  `Permissions.Lookups.Manage`, which already means exactly "may edit the reference data
  the pickers read" and is already mapped to Admin and Manager.

## Not done

- **No new automated tests.** Explicitly deferred by the repository owner to conserve
  usage; the behaviour above was verified over HTTP against SQL Server instead. The 117
  existing tests still pass. Worth adding later for the merge path and the category cycle
  guard, which are the two places where a regression would be quiet.
- **Drag-and-drop reorder.** The admin screen moves a category among its siblings with
  up/down buttons, which sends the whole sibling list through the same `reorder` endpoint.
  True drag-and-drop would need a tree DnD library; the endpoint already accepts the whole
  proposed shape, so it is a UI change only.

## Notes for whoever picks this up

- FluentValidation's built-in messages are localised by the request culture, so a default
  rule failure returns Arabic while custom `.WithMessage()` text stays English. Consistent
  with a bilingual API, but worth deciding deliberately if the mix looks odd.
- `sla-automation` should read `TicketWatchers` for its notification targets — that is why
  watchers exist before any notification code does.

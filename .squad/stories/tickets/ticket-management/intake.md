# Story intake

- Folder: `.squad/stories/tickets/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Ticket Management
- **Feature slug (folder under `plans/`):** `tickets`
- **PDF area:** 2

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `TICKETS`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Ticket Management
```

---

## Description

```
Round out ticket handling for a real support desk. The core lifecycle is shipped and
working; this story adds the operational features an agent needs at volume: bulk
actions, ticket linking and merging, watchers, tags, saved views, and admin-managed
categories.

Verbatim requirement from the PDF (area 2):

- Create and track tickets
- Categories and priorities
- Assign tickets to agents
- Status and escalation
- Ticket history
```

---

## Acceptance criteria

```
- [ ] An agent can select multiple tickets in the list and bulk-assign, bulk-change-priority, or bulk-change-status; per-ticket failures (e.g. an illegal transition) are reported individually and do not roll back the successes.
- [ ] Two tickets can be linked with a typed relationship (duplicate-of, related-to, blocks) and the links render on both detail pages.
- [ ] A ticket can be merged into another: comments, attachments and interactions move across, the source is closed with a history entry naming the target, and the customer sees one thread.
- [ ] Agents can add themselves or colleagues as watchers on a ticket; watchers are surfaced for the notification work in the `sla-automation` story.
- [ ] Free-form tags can be added to and removed from a ticket, and the ticket list can filter by tag.
- [ ] An agent can save a named filter combination as a personal view and re-select it later; saved views persist per user.
- [ ] Admin/Manager can create, rename, reorder, reparent and deactivate ticket categories through the UI; deactivating a category keeps existing tickets intact and only hides it from new-ticket pickers.
- [ ] Ticket history records every one of the above mutations.
- [ ] Escalation level can be raised and lowered manually with a mandatory reason recorded in history.
```

Cross-cutting constraints that apply to every criterion above:

```
- Arabic and English throughout, Arabic default, full RTL mirroring (PDF area 12).
- Every screen responsive down to a 375px viewport.
- All data scoped by department and branch where the entity supports it.
- New tables use soft delete and are covered by the audit trail.
- Add xUnit tests to `backend/tests/CustomerSupportCRM.Application.Tests/` for new
  business rules; `dotnet test` and `npm run build` must both pass.
- Do not weaken existing authorization: the API authenticates by default via
  `options.FallbackPolicy` and endpoints opt out with `[AllowAnonymous]`.
```

---

## Attachments

| File (relative to this folder) | What it is |
| ------------------------------ | ---------- |
| None. | Requirements are quoted inline above and in `docs/requirements.md`. |

---

## Dependencies

- **Depends on:** nothing. Extends shipped code.
- **Related stories:** `sla-automation` (consumes watchers and escalation),
  `channels` (inbound mail threading needs ticket merge), `customers` (shared `Attachment`).

---

## Already shipped by the bootstrap — extend, do not rebuild

- `Domain/Entities/Ticket.cs`, `TicketCategory.cs` (self-referencing tree), `TicketComment.cs`,
  `TicketHistory.cs`.
- `Domain/Tickets/TicketWorkflow.cs` — the single source of truth for allowed status
  transitions, `ActiveStatuses`, `IsTerminal`. **Every status change must go through it.**
- `Application/Tickets/TicketService.cs` — filtered/sorted/paged search, CRUD, assign
  (auto-opens a New ticket), status change (validated against `TicketWorkflow`, stamps
  `ResolvedAt`/`ClosedAt`, clears them on reopen), comments (public reply sets
  `FirstRespondedAt` and opens a New ticket; internal notes do not), and history with
  agent ids resolved to names.
- `Api/Controllers/TicketsController.cs` — `/api/tickets` CRUD plus `/{id}/assign`,
  `/{id}/status`, `/{id}/comments`, `/{id}/history`.
- `Api/Controllers/LookupsController.cs` — read-only category tree, departments, branches, enums.
- Frontend `views/tickets/TicketListView.vue` (search + status/priority multiselect +
  active/unassigned toggles + lazy paging), `TicketFormView.vue`, `TicketDetailView.vue`
  (status buttons driven by `allowedNextStatuses` from the server, assign dialog,
  comments, history timeline).
- Tests in `TicketServiceTests.cs` and `TicketWorkflowTests.cs`.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Do **not** add a second status-transition table or duplicate the rules in the frontend.
  `TicketDetailDto.AllowedNextStatuses` is populated from `TicketWorkflow.AllowedTransitions`
  and the UI already renders exactly those buttons.
- Bulk operations should reuse the existing single-ticket service methods in a loop and
  collect per-item results, so the workflow and history rules cannot be bypassed.
- Saved views are per-user JSON; a small `UserSavedView` entity keyed on the Identity user id
  is enough — do not add a rules engine.
- Tags: prefer a `TicketTag` join entity over a delimited string column, so filtering stays indexable.
- Category admin needs write endpoints on a new controller; `LookupsController` is deliberately
  read-only and used by every picker.

### Repository layout

```
backend/CustomerSupportCRM.slnx        (.NET 10, note: .slnx not .sln)
backend/src/CustomerSupportCRM.Domain/          entities, enums, TicketWorkflow. No external deps.
backend/src/CustomerSupportCRM.Application/     DTOs, services, validators, interfaces
backend/src/CustomerSupportCRM.Infrastructure/  EF Core, Identity, JWT, storage, seeder
backend/src/CustomerSupportCRM.Api/             controllers, middleware, DI, Swagger
backend/tests/CustomerSupportCRM.Application.Tests/   xUnit, 72 tests, EF InMemory
frontend/                              Vue 3 + Vite + TypeScript + PrimeVue 5 + Tailwind 4
```

Reference graph, already enforced: `Api -> Application -> Domain`, `Api -> Infrastructure -> Application -> Domain`.
Domain must stay free of EF Core, ASP.NET and Identity references.

### Backend conventions already established — follow these, do not reinvent

- Entities derive from `AuditableEntity` (`Domain/Common/AuditableEntity.cs`), which supplies
  `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy` and `IsDeleted/DeletedAt/DeletedBy`.
  `AuditingInterceptor` stamps these and writes `AuditLog` rows automatically. Never set them by hand.
- Soft delete is applied by reflection over `ISoftDeletable` in `AppDbContext.ApplySoftDeleteFilters`.
  A new soft-deletable entity gets its query filter for free.
- One `IEntityTypeConfiguration<T>` per entity under
  `Infrastructure/Persistence/Configurations/`. Never put fluent config inline in `OnModelCreating`.
- Application services take `IAppDbContext` (`Application/Common/Interfaces/IAppDbContext.cs`) —
  add new `DbSet<T>` members there as well as on `AppDbContext`.
- Inject `IClock`, never call `DateTimeOffset.UtcNow` directly, so tests stay deterministic.
- Throw `NotFoundException` / `ConflictException` / `BadRequestException` / `ForbiddenException`
  from `Application/Common/Exceptions/AppExceptions.cs`. `ExceptionHandlingMiddleware` maps them
  to RFC 7807 ProblemDetails. Never return raw status codes from a service.
- Lists return `PagedResult<T>` and accept a query object deriving from `PagedQuery`
  (clamps PageSize to 100). See `Application/Common/Models/`.
- Validators are FluentValidation classes auto-registered by
  `Application/DependencyInjection.cs` via `AddValidatorsFromAssembly`.
- Role names come from `Application/Auth/Roles.cs` constants (`Roles.Staff`, `Roles.Supervisory`) —
  never string literals in `[Authorize]`.
- Reference numbers come from `IReferenceNumberGenerator`, backed by SQL Server SEQUENCE objects
  declared in `AppDbContext.OnModelCreating`. Add a sequence there for any new counter.
- Every bilingual entity stores a `NameAr`/`NameEn` (or `FullNameAr`/`FullNameEn`) pair.

### Frontend conventions already established

- `src/types/api.ts` mirrors the C# DTOs and enums. Update it when the API contract changes.
- `src/api/client.ts` owns the axios instance: JWT header, `Accept-Language`, and a single
  shared 401-refresh-retry. `src/api/services.ts` holds the typed per-resource calls.
- `src/stores/ui.ts` owns locale + direction + theme and writes `lang`/`dir` onto `<html>`.
  Use `ui.localized(entity)` / `ui.localizedFullName(entity)` to pick the ar/en field.
- Every user-visible string is a key in BOTH `src/locales/ar.json` and `src/locales/en.json`.
  No hardcoded literals in templates. Arabic is the default locale.
- RTL comes from `dir` plus Tailwind logical properties (`ms-*`/`me-*`/`ps-*`/`pe-*`, `text-start`).
  Never use `ml-*`/`mr-*`/`left-*`/`right-*` for layout that must mirror.
- Wrap ticket numbers, codes, phone numbers and dates in `class="ltr-nums"` so digits do not
  reorder inside Arabic text.
- Dates and numbers go through `src/composables/useFormat.ts`.
- Reuse `src/components/StatusTag.vue` and `src/components/PageHeader.vue`.
- Screens must stay usable at a 375px viewport.

### Verification commands

```bash
dotnet build backend/CustomerSupportCRM.slnx
dotnet test backend/CustomerSupportCRM.slnx
dotnet ef migrations add <Name> --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api --output-dir Persistence/Migrations
dotnet ef database update --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api
npm run build --prefix frontend
```

SQL Server: `Server=.\SQLEXPRESS;Database=CustomerSupportCRM;Trusted_Connection=True;TrustServerCertificate=True`.
API dev URL `http://localhost:5178` (Swagger at `/swagger`), frontend dev URL `http://localhost:5173`.
Seeded admin: the credentials set in `Seed:AdminEmail` / `Seed:AdminPassword` in the
git-ignored `appsettings.Development.json` (copy it from the `.example` file).

---

## Out of scope

- Automatic assignment and escalation rules — that is the `sla-automation` story.
- AI auto-categorisation and suggested replies — that is the `ai-features` story.
- Inbound ticket creation from email/WhatsApp — that is the `channels` story.

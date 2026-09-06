# Story intake

- Folder: `.squad/stories/sla-automation/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** SLA and Automation
- **Feature slug (folder under `plans/`):** `sla-automation`
- **PDF area:** 5

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `SLA_AUTOMATION`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
SLA and Automation
```

---

## Description

```
Make the desk run itself: measure response and resolution against agreed targets,
route new tickets automatically, escalate what is slipping, and alert the right people.
The `Ticket` table already carries the SLA columns — nothing populates them yet.

Verbatim requirement from the PDF (area 5):

- Response and resolution targets
- Automatic assignment
- Escalation rules
- Alerts and notifications
```

---

## Acceptance criteria

```
- [ ] An `SlaPolicy` entity exists with bilingual name and response/resolution targets per priority, optionally scoped to department, branch or category, with a documented precedence order when several match.
- [ ] Creating a ticket resolves exactly one policy and populates `SlaPolicyId`, `FirstResponseDueAt` and `ResolutionDueAt`; changing a ticket's priority or category recalculates them and records the change in history.
- [ ] Targets are measured against configurable business hours and holidays, not raw wall-clock, and the working-calendar maths is unit-tested including a target that spans a weekend.
- [ ] Putting a ticket On Hold pauses the resolution clock and resuming restarts it; total paused time is retained for reporting.
- [ ] Automatic assignment routes a new unassigned ticket by a configurable strategy (round-robin, least-loaded, or category/department ownership), skipping inactive or out-of-office agents, and records the assignment in history.
- [ ] Escalation rules raise `EscalationLevel`, optionally reassign, and notify a supervisor when a response or resolution target is breached or approaching; each rule action is written to ticket history.
- [ ] A background service evaluates due and breached SLAs on a schedule, is idempotent, and does not double-escalate if it runs twice or in two instances.
- [ ] Alerts reach agents in-app and, where configured, by email; the notification raising is shared with the `agent-dashboard` story rather than duplicated.
- [ ] Admin/Manager can create and edit policies and rules through the UI, and preview which tickets a rule would currently match before saving it.
- [ ] SLA state is visible on the ticket: target, time remaining or overdue, and paused state.
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

- **Depends on:** `tickets` (watchers and escalation-reason history),
  `agent-dashboard` (agree the shared `INotificationService` first).
- **Related stories:** `reports` (SLA performance reporting reads what this story writes),
  `channels` (an outbound reply is what stops the response clock).

---

## Already shipped by the bootstrap — extend, do not rebuild

- `Domain/Entities/Ticket.cs` already has, unpopulated and ready:
  `SlaPolicyId`, `FirstResponseDueAt`, `ResolutionDueAt`, `FirstRespondedAt`,
  `ResolvedAt`, `ClosedAt`, `EscalationLevel`.
- `TicketService.ChangeStatusAsync` already stamps `ResolvedAt`/`ClosedAt` and clears both on
  reopen. `AddCommentAsync` already stamps `FirstRespondedAt` on the first **public** reply
  (an internal note deliberately does not satisfy it).
- `Domain/Tickets/TicketWorkflow.ActiveStatuses` and `IsTerminal` define what still counts
  against SLA.
- `TicketService.GetAgentDashboardAsync` already counts overdue as
  `ResolutionDueAt < now` on active tickets, and the ticket list/dashboard already render an
  overdue due-date in red via `useFormat().isOverdue`.
- `Department`, `Branch`, `TicketCategory` and `TicketPriority` exist as the dimensions a
  policy would match on.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Business-hours arithmetic is the part that goes wrong. Put it in a pure, heavily tested
  Domain service (e.g. `Domain/Sla/BusinessCalendar.cs`) with no EF or clock dependency, and
  inject `IClock` only at the call site.
- Do not bypass `TicketWorkflow` when escalation changes a status; route through
  `TicketService.ChangeStatusAsync` so history and the transition rules still apply.
- The evaluator is a `BackgroundService`. Guard against concurrent runs — a `SELECT ... WITH
  (UPDLOCK, READPAST)` claim pattern or a distributed lock is safer than a naive scan.
- Policy precedence must be explicit and documented, otherwise two overlapping policies make
  due dates non-deterministic. Most-specific-wins with a documented tiebreak is the usual choice.
- Reuse `Application/Common/Models/PagedQuery` for the policy admin list.

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

- Historical SLA dashboards and exports — `reports`.
- AI-based priority prediction — `ai-features`.
- Actually sending email/SMS — the transport is `channels`.

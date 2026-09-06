# Story intake

- Folder: `.squad/stories/agent-dashboard/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Agent Dashboard
- **Feature slug (folder under `plans/`):** `agent-dashboard`
- **PDF area:** 4

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `AGENT_DASHBOARD`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Agent Dashboard
```

---

## Description

```
Turn the dashboard from a read-only summary into the agent's actual workspace. The
counters and assigned-ticket list are shipped; this story adds tasks and reminders,
reusable quick replies, and the collaboration features (mentions, internal discussion,
presence) the PDF calls for.

Verbatim requirement from the PDF (area 4):

- Assigned tickets
- Customer information
- Tasks and reminders
- Quick replies
- Team collaboration
```

---

## Acceptance criteria

```
- [ ] An agent can create a task or reminder, optionally linked to a ticket or customer, with a due date and a done flag; overdue and due-today tasks surface on the dashboard.
- [ ] A reminder produces an in-app notification at its due time.
- [ ] Quick replies (canned responses) are managed per department with bilingual ar/en bodies, support placeholders such as customer name and ticket number, and can be inserted into a ticket reply with the placeholders resolved.
- [ ] Agents can @-mention a colleague in an internal ticket note; the mentioned user gets a notification and the ticket appears in a “mentioning me” filter.
- [ ] An in-app notification centre lists unread items, marks them read, and links to the source ticket.
- [ ] The dashboard shows a team view for Admin/Manager: per-agent open counts, overdue counts and resolved-today, for their department.
- [ ] Dashboard data loads in one request per panel and each panel degrades independently — one failing panel must not blank the page.
- [ ] All new counters respect department and branch scoping.
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

- **Depends on:** `tickets` (watchers feed the notification targets).
- **Related stories:** `sla-automation` (shares the notification delivery mechanism —
  agree one `INotificationService` between the two rather than building two),
  `reports` (team metrics overlap; dashboard is live, reports are historical).

---

## Already shipped by the bootstrap — extend, do not rebuild

- `TicketService.GetAgentDashboardAsync` — counts for active/new/overdue assigned,
  unassigned-in-department, resolved-today, plus breakdowns by status and priority and the
  ten most urgent assigned tickets.
- `Api/Controllers/TicketsController.cs` route `GET /api/dashboard/agent`, with the
  authorization rule that only Admin/Manager may pass `?agentId=` for someone else.
- `Application/Auth/Dtos/AgentDto` carries `OpenTicketCount`, so workload is already visible
  in the assignment picker.
- Frontend `views/dashboard/AgentDashboardView.vue` — stat tiles plus the ticket table.
- `TicketComment.IsInternal` already separates internal notes from customer-visible replies,
  and the detail view styles them differently.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- `AgentDashboardDto.ByStatus`/`ByPriority` are serialised by System.Text.Json with the enum
  **name** as the dictionary key, and `frontend/src/types/api.ts` types them as
  `Record<string, number>`. Keep that shape if you extend them.
- Reminders need a scheduled trigger. Prefer a hosted `BackgroundService` in the API over an
  external scheduler, and make it safe to run in more than one instance.
- Quick-reply placeholder substitution belongs in `Application` so the same resolver serves
  the UI preview and the outbound channel reply.
- The frontend dashboard already uses `Promise.allSettled` for independent panels — follow that
  pattern for the new ones.

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

- Email/SMS delivery of notifications — the transport belongs to `channels`; this story only raises the notification.
- AI-suggested replies — `ai-features`. Quick replies here are human-authored templates.
- Historical performance reporting — `reports`.

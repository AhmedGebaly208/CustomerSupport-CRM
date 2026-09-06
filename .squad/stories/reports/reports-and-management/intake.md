# Story intake

- Folder: `.squad/stories/reports/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Reports and Management
- **Feature slug (folder under `plans/`):** `reports`
- **PDF area:** 9

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `REPORTS`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Reports and Management
```

---

## Description

```
Reporting for supervisors and management: ticket volumes and trends, SLA attainment,
agent performance, customer satisfaction, and a management dashboard that rolls up
across departments and branches. Read-only over data the other stories write.

Verbatim requirement from the PDF (area 9):

- Ticket reports
- SLA performance
- Agent performance
- Customer satisfaction
- Management dashboards
```

---

## Acceptance criteria

```
- [ ] Ticket reports cover volume over time, by status, priority, category, channel, department, branch and agent, with a date range and comparison against the previous period.
- [ ] SLA performance reports show first-response and resolution attainment percentages, average and median times, and breach counts, sliced by the same dimensions.
- [ ] Agent performance shows tickets handled, resolved, average first-response and resolution time, reopen rate and current load, with a fair handling of tickets reassigned mid-life (documented in the plan).
- [ ] Customer satisfaction aggregates the CSAT ratings captured by the `customer-portal` story: average score, response rate and trend.
- [ ] A management dashboard rolls the above up across departments and branches with drill-down into the underlying ticket list.
- [ ] Every report can be exported to XLSX and CSV with the column headers in the active language.
- [ ] Reports are restricted to Admin/Manager; an Agent can see only their own performance.
- [ ] Report queries are aggregate SQL and stay responsive on a large table — verified against a seeded dataset of at least 100k tickets, with the measurement recorded.
- [ ] All charts and tables are readable in both light and dark themes and in RTL, and numbers use Latin digits consistently with `useFormat`.
- [ ] Percentages and averages handle the empty-set case without dividing by zero or rendering NaN.
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

- **Depends on:** `sla-automation` (nothing populates the SLA due/breach columns until it
  ships, so SLA attainment is unmeasurable before then), `customer-portal` (CSAT capture).
- **Related stories:** `agent-dashboard` (live counters vs. historical reporting — keep the
  distinction clear and do not duplicate the queries).

---

## Already shipped by the bootstrap — extend, do not rebuild

- `TicketService.GetAgentDashboardAsync` shows the live-counters pattern to follow, including
  grouping to a dictionary keyed by enum name.
- `Ticket` already carries every timestamp reporting needs once `sla-automation` populates
  them: `CreatedAt`, `FirstRespondedAt`, `ResolvedAt`, `ClosedAt`, `FirstResponseDueAt`,
  `ResolutionDueAt`, plus `EscalationLevel`.
- `Department`, `Branch`, `TicketCategory`, `TicketPriority`, `CommunicationChannel` are the
  slicing dimensions, all already bilingual.
- `TicketHistory` gives per-change timing for cycle-time analysis.
- Indexes already exist on `(AssignedAgentId, Status, Priority)`, `(Status, Priority)`,
  `(DepartmentId, Status)` and `ResolutionDueAt`.
- Frontend has the `nav.reports` locale key reserved in both catalogues.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Do the aggregation in SQL via EF `GroupBy` projections. Never materialise ticket rows into
  memory to count them — that is what makes reporting pages time out.
- Consider read-only projections or indexed views for the heaviest rollups, and say so in the
  plan rather than adding them silently.
- For charting, pick one library and keep it a single wrapper component so the report pages stay
  declarative. Ensure it honours `dir="rtl"`.
- Export: generate server-side so the same numbers appear in the file and on screen.
- Beware timezone drift: the API stores `DateTimeOffset` in UTC. Decide once whether reports
  bucket by UTC or by a configured business timezone, document it, and apply it consistently —
  a mismatch makes daily totals disagree with the dashboard.

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

- Live operational counters — `agent-dashboard`.
- Scheduled emailing of reports — needs `channels`; add it there if wanted.
- AI narrative summaries of reports — `ai-features`.

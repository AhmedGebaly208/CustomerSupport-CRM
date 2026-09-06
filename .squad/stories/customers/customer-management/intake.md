# Story intake

- Folder: `.squad/stories/customers/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Customer Management
- **Feature slug (folder under `plans/`):** `customers`
- **PDF area:** 1

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `CUSTOMERS`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Customer Management
```

---

## Description

```
Complete the customer record so an agent has the full picture on one screen. Profiles,
contact details, interaction history and notes already work end to end; this story adds
file attachments, merging of duplicate customer records, bulk import, and a
consolidated activity timeline that interleaves tickets, interactions and notes.

Verbatim requirement from the PDF (area 1):

- Customer profiles
- Contact details
- Interaction history
- Notes and attachments
```

---

## Acceptance criteria

```
- [ ] An agent can upload one or more files against a customer, see them listed with name/size/uploader/date, download them, and delete them.
- [ ] Upload enforces the allow-list and size cap already configured in `FileStorageOptions`; a rejected file returns a 400 with a clear reason, not a 500.
- [ ] Attachments also attach to a ticket and to a ticket comment, reusing the same `AttachmentOwnerType` discriminator rather than new tables.
- [ ] Two duplicate customer records can be merged: tickets, interactions, notes, contacts and attachments move to the surviving record, and the merged-away record is soft-deleted with an audit entry naming the survivor.
- [ ] Merge is refused with a clear message when both records have active tickets that would conflict, and is restricted to Admin/Manager.
- [ ] A customer's detail page shows a single chronological activity timeline interleaving tickets, interactions, notes and attachments, newest first, paged.
- [ ] Customers can be bulk-imported from a CSV/XLSX file: the response reports per-row success and per-row validation failure without aborting the whole import.
- [ ] Import and merge both write audit-log entries.
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

- **Depends on:** nothing. This story only extends code that already exists.
- **Related stories:** `tickets` (shares `Attachment`), `security-admin` (audit log),
  `ai-features` (may summarise the activity timeline).

---

## Already shipped by the bootstrap — extend, do not rebuild

- `Domain/Entities/Customer.cs`, `CustomerContact.cs`, `CustomerNote.cs`, `Interaction.cs`,
  and `Attachment.cs` (entity + EF configuration + migration all exist).
- `Application/Customers/CustomerService.cs` — paged bilingual search (name ar/en, code,
  email, phone, WhatsApp, company), CRUD, notes, interaction history, duplicate-email
  guard, and refusal to delete a customer that still has active tickets.
- `Api/Controllers/CustomersController.cs` — `/api/customers` CRUD plus `/{id}/notes`
  and `/{id}/interactions`.
- Frontend `views/customers/CustomerListView.vue`, `CustomerFormView.vue`,
  `CustomerDetailView.vue` (tabs: tickets / interactions / notes).
- Tests in `CustomerServiceTests.cs`.

**`Attachment` is the gap:** the entity, table and `IFileStorage`/`LocalFileStorage`
implementation exist, but no controller endpoint, service method or UI uses them yet.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- `IFileStorage` (`Application/Common/Interfaces/IFileStorage.cs`) is already implemented by
  `Infrastructure/Storage/LocalFileStorage.cs`, which sanitises paths, refuses traversal, and
  renames uploads to a GUID on disk while keeping the original name on the DB row. Use it;
  do not write files directly.
- Attachment endpoints need `[FromForm] IFormFile`, so use `[Consumes("multipart/form-data")]`
  and keep the byte handling in the controller — `IFileStorage` takes a `Stream`.
- For the merge, prefer a single transaction and `ExecuteUpdateAsync` for the FK re-pointing.
- The activity timeline is a union of four sources; project each to a common DTO in the
  service rather than trying to express it as one EF query.

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

- Customer-facing views of their own profile — that is the `customer-portal` story.
- AI-generated customer summaries — that is the `ai-features` story.
- Syncing customers with an ERP — that is the `integrations` story.

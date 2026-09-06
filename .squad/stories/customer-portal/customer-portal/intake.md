# Story intake

- Folder: `.squad/stories/customer-portal/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Customer Portal
- **Feature slug (folder under `plans/`):** `customer-portal`
- **PDF area:** 8

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `CUSTOMER_PORTAL`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Customer Portal
```

---

## Description

```
A separate, customer-facing area of the same application where a customer signs in to
raise and track their own tickets, read FAQs and leave feedback. The security boundary
is the whole point of this story: a customer must never see another customer's data, an
internal note, or any staff-only field.

Verbatim requirement from the PDF (area 8):

- Submit tickets
- Track requests
- View history
- Access FAQs
- Submit feedback
```

---

## Acceptance criteria

```
- [ ] A customer can register or be invited, verify their email, sign in, reset a forgotten password, and their login is linked to exactly one `Customer` record.
- [ ] A customer sees only their own tickets; requesting another customer's ticket by id returns 404, not 403, so ids cannot be probed for existence.
- [ ] Internal notes, internal customer notes, audit data, agent workload and all staff-only fields are absent from every portal response — verified by a test that asserts on the serialised payload, not just the UI.
- [ ] A customer can submit a ticket with attachments, choosing a category, and receives the ticket number.
- [ ] A customer can track status, read the public reply thread, reply, and see their full request history.
- [ ] A customer can browse and search published FAQs and knowledge-base articles.
- [ ] A customer can rate a resolved ticket and leave a comment (CSAT), once per ticket, and the score is stored for the `reports` story to aggregate.
- [ ] The portal has its own layout and navigation, distinct from the staff app, and is fully bilingual ar/en with RTL and responsive down to 375px.
- [ ] Portal endpoints are separately rate-limited and the customer role cannot reach any staff endpoint even by direct URL.
- [ ] A customer whose account is deactivated cannot sign in, and an existing token stops working.
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

- **Depends on:** `knowledge-base` (FAQ content to display), `tickets` (public reply thread),
  `customers` (attachment endpoints).
- **Related stories:** `channels` (Portal is a channel; a portal reply should behave like any
  other outbound reply), `reports` (consumes CSAT), `ai-features` (chatbot placement).

---

## Already shipped by the bootstrap — extend, do not rebuild

- `Customer.UserId` and `ApplicationUser.CustomerId` already exist to link a portal login to
  a CRM customer record, and the JWT already carries a `customer_id` claim when set
  (`Infrastructure/Identity/IdentityService.cs`).
- `Roles.Customer` already exists in `Application/Auth/Roles.cs` and is seeded by `DbSeeder`.
- `CommunicationChannel.Portal` already exists as a ticket source.
- `TicketComment.IsInternal` already separates internal notes, and
  `TicketService.GetCommentsAsync(id, includeInternal)` already takes the flag —
  `TicketsController.GetComments` computes it from the caller's roles.
- `CustomerNote.IsInternal` exists for the same reason.
- The frontend router (`src/router/index.ts`) already notes that every current route is
  staff-facing and that the portal needs its own route tree and layout.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Prefer a dedicated set of `/api/portal/**` controllers with their own DTOs over reusing the
  staff controllers with conditional field-stripping. Shared DTOs are how internal fields leak.
- Scope every portal query by the `customer_id` claim server-side. Never accept a customer id
  from the request body or query string.
- `Roles.Staff` and `Roles.Supervisory` guard the staff controllers today; add an explicit
  portal policy rather than leaving portal endpoints on the default fallback policy.
- Registration and password reset are the anonymous surfaces — they need rate limiting,
  and password reset must not reveal whether an email is registered.
- Identity's `AddDefaultTokenProviders()` is already wired in
  `Infrastructure/DependencyInjection.cs`, so email-confirmation and reset tokens are available.

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

- The AI chatbot itself — `ai-features`.
- Aggregated satisfaction reporting — `reports` (this story only captures the rating).
- Social or SSO login — not requested by the PDF.

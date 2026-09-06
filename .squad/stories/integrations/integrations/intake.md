# Story intake

- Folder: `.squad/stories/integrations/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Integrations
- **Feature slug (folder under `plans/`):** `integrations`
- **PDF area:** 11

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `INTEGRATIONS`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Integrations
```

---

## Description

```
Make the CRM integrable: a documented, versioned public API for external systems, ERP
synchronisation for customers and related master data, and a general outbound webhook
mechanism. The messaging-provider work (email, SMS, WhatsApp) is owned by the `channels`
story — this story owns the credential and gateway plumbing they share.

Verbatim requirement from the PDF (area 11):

- APIs
- ERP
- Email, SMS & WhatsApp
- External systems
```

---

## Acceptance criteria

```
- [ ] A versioned public API surface exists (e.g. `/api/v1/**`) with a documented deprecation policy, so internal refactors cannot break external consumers.
- [ ] External systems authenticate with API keys or client credentials, scoped to a set of permissions, separate from interactive user JWTs; keys can be issued, scoped, rotated and revoked by an Admin.
- [ ] API keys are stored hashed, shown once at creation, and every call is rate-limited per key with a clear 429 response.
- [ ] OpenAPI documentation is published for the public API, accurate enough to generate a working client, and available outside Development.
- [ ] ERP synchronisation imports and updates customers and related master data on a schedule and on demand, is idempotent, and reconciles by a stable external id rather than by name.
- [ ] A sync conflict (the same record changed on both sides) is detected and surfaced for a human decision rather than silently overwritten, and the resolution rule is documented.
- [ ] Outbound webhooks notify external systems of ticket and customer events, signed so the receiver can verify authenticity, with retry-with-backoff and a delivery log an admin can inspect and replay.
- [ ] Shared messaging-provider credentials (email, SMS, WhatsApp) are configured in one place, read from secrets, never committed, and never returned by any endpoint.
- [ ] Every integration failure is logged with enough context to diagnose, and a failing integration degrades gracefully without taking ticket handling down.
- [ ] Integration health is visible via `/health` and in the admin UI.
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

- **Depends on:** `security-admin` (the permission model that API-key scopes attach to).
- **Related stories:** `channels` owns the actual send/receive adapters for email, SMS and
  WhatsApp — agree the boundary explicitly so provider clients are written once. Coordinate
  with `customers` on the ERP field mapping.

---

## Already shipped by the bootstrap — extend, do not rebuild

- A REST API with Swagger/OpenAPI at `/swagger` in Development, including a JWT security
  scheme (`Api/Program.cs`, Swashbuckle 10 with Microsoft.OpenApi 2.x — note the v2 API
  shape: `OpenApiSecuritySchemeReference` and the document callback overload of
  `AddSecurityRequirement`).
- Consistent RFC 7807 ProblemDetails error contract via
  `Api/Middleware/ExceptionHandlingMiddleware.cs`.
- JWT bearer authentication with refresh, and CORS configured from `Cors:AllowedOrigins`.
- `Serilog` request logging and a `/health` endpoint (with a `DbContext` check).
- Health checks and configuration binding patterns to follow for new integrations.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Do not expose the internal DTOs as the public contract. A public API needs its own stable
  DTOs, or every internal rename becomes a breaking change for consumers.
- API keys: store a hash (not the key), keep a prefix for identification, and support two live
  keys at once so rotation needs no downtime.
- Webhook signing: HMAC over the raw body with a per-subscription secret, plus a timestamp to
  prevent replay. Document the verification steps for receivers.
- Make the outbound webhook dispatcher a queue plus a `BackgroundService`; sending inline from
  the request path makes ticket saves depend on a third party being up.
- ERP sync must be restartable. Track a watermark or change token, and make every upsert
  idempotent on the external id.
- ASP.NET Core has built-in rate limiting — prefer it over a hand-rolled limiter.

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

- The email/WhatsApp/SMS channel adapters themselves — `channels`.
- A specific named ERP product's proprietary connector — build the generic sync plus one adapter interface.
- A GraphQL surface — not requested by the PDF.

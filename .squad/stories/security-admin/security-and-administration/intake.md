# Story intake

- Folder: `.squad/stories/security-admin/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Security and Administration
- **Feature slug (folder under `plans/`):** `security-admin`
- **PDF area:** 10

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `SECURITY_ADMIN`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Security and Administration
```

---

## Description

```
The administration area: managing users and roles, moving from coarse roles to
fine-grained permissions, exposing the audit trail, and letting an administrator
configure the system without a redeploy. Authentication, roles and the audit trail
already exist; this story makes them administrable and more granular.

Verbatim requirement from the PDF (area 10):

- Users and roles
- Permissions
- Audit logs
- System configuration
```

---

## Acceptance criteria

```
- [ ] Admin can list, search, create, edit, deactivate and reactivate users, assign departments/branches, and assign roles; deactivating a user immediately prevents sign-in and invalidates their refresh token.
- [ ] Admin cannot remove the last remaining administrator, and cannot deactivate their own account, so the system can never be locked out.
- [ ] A permission model exists (named permissions grouped into roles) and `[Authorize]` checks move from role names to permissions, with the four existing roles mapped onto permission sets so current behaviour is preserved.
- [ ] The audit log is browsable with filters for entity, action, user and date range, paged, and readable — showing the JSON diff in a human-friendly form.
- [ ] The audit log is append-only through the application: no endpoint can edit or delete an entry.
- [ ] System configuration (business hours, holidays, branding, feature flags, channel toggles) is editable by an Admin at runtime, validated on save, and cached with an explicit invalidation on change.
- [ ] An admin action that changes another user's roles or permissions is itself audited, recording who did it.
- [ ] A user can change their own password with the current password required, and their refresh tokens are invalidated on change.
- [ ] Sensitive values (API keys, secrets) are never returned by a configuration endpoint, only write-only or masked.
- [ ] Admin screens are bilingual, RTL-correct, and restricted so a Manager sees only what they are permitted.
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

- **Depends on:** nothing — extends shipped infrastructure.
- **Related stories:** every other story consumes the permission model, so plan and land this
  early if possible. `sla-automation` reads business hours and holidays from the configuration
  this story provides. `platform` reads branding from it.

---

## Already shipped by the bootstrap — extend, do not rebuild

- ASP.NET Core Identity with `ApplicationUser`/`ApplicationRole` (Guid keys), tables renamed to
  `Users`/`Roles`/`UserRoles`/... in `AppDbContext.OnModelCreating`.
- JWT access tokens plus rotating single-use refresh tokens
  (`Infrastructure/Identity/IdentityService.cs`). Login is deliberately vague about whether an
  email exists. Password policy and lockout are configured in
  `Infrastructure/DependencyInjection.cs`.
- Startup fails fast if `Jwt:SigningKey` is missing or under 32 characters
  (`Api/Program.cs`) — verified working.
- Four seeded roles from `Application/Auth/Roles.cs` (Admin, Manager, Agent, Customer) and a
  seeded administrator. `DbSeeder` is idempotent and repairs a missing admin role assignment.
- **The audit trail already works**: `Infrastructure/Persistence/Interceptors/AuditingInterceptor.cs`
  writes an `AuditLog` row for every insert/update/soft-delete, records a JSON diff of changed
  properties, redacts `PasswordHash`/`SecurityStamp`/`RefreshToken`/etc., records a soft delete
  as `Deleted` rather than `Updated`, and excludes `AuditLog`/`TicketHistory` to avoid recursion.
  Covered by tests in `CustomerServiceTests.cs`. **There is no UI or endpoint for it yet.**
- `Api` authenticates by default via `options.FallbackPolicy`; endpoints opt out explicitly.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Moving from roles to permissions is the risky part. Keep `Roles.Staff`/`Roles.Supervisory`
  working during the transition by mapping roles to permission sets, then migrate endpoints
  incrementally. A big-bang swap will silently open or close endpoints.
- Implement permissions as claims on the JWT plus a policy provider, so no extra DB round-trip
  per request. Remember the token then caches the permission set: define how long a revoked
  permission may remain effective, and keep access-token lifetime short enough to match.
- The audit log will become the largest table in the database. Index for the filter
  combinations you expose and plan a retention/archival policy.
- `AuditingInterceptor.SensitiveProperties` is the redaction allow-list — extend it whenever a
  new secret-bearing column is added, or it will be written into the trail in plaintext.
- Configuration cache: `IOptionsMonitor` plus a database-backed source is cleaner than
  hand-rolled statics, and testable.

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

- SSO/SAML/OIDC federation — not requested by the PDF.
- Two-factor authentication — Identity supports it but the PDF does not ask for it.
- Customer-facing account management — `customer-portal`.

# Story intake

- Folder: `.squad/stories/platform/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Platform
- **Feature slug (folder under `plans/`):** `platform`
- **PDF area:** 12

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `PLATFORM`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Platform
```

---

## Description

```
Area 12 is a constraint on every other area rather than a screen of its own, and the
bootstrap already builds it in from the first commit. This story closes the remaining
gaps: runtime-configurable branding per tenant, enforced department/branch data scoping,
a mobile-grade experience, and guards that stop the bilingual/RTL promise from
regressing as the other eleven stories land.

Verbatim requirement from the PDF (area 12):

- Arabic & English
- Web and mobile friendly
- Multi-department
- Multi-branch
- Custom branding
```

---

## Acceptance criteria

```
- [ ] Branding (logo, primary colour, application name) is configurable at runtime by an Admin and applied without a redeploy, driving the existing CSS custom properties rather than new component styles.
- [ ] Branding can differ per department or branch if the deployment is multi-tenant, with a documented resolution order and a safe default.
- [ ] Department and branch scoping is enforced server-side, not merely offered as a filter: a user restricted to one department cannot read another's tickets or customers by passing a different id or a direct record id.
- [ ] That scoping rule is covered by tests that attempt cross-department and cross-branch access and assert they fail.
- [ ] A regression guard fails the build when a key exists in one locale catalogue but not the other, so the two files cannot drift as other stories add strings.
- [ ] A check or documented review step catches physical-direction Tailwind classes (`ml-*`, `mr-*`, `left-*`, `right-*`) used for layout that must mirror.
- [ ] Every screen shipped so far is verified usable at a 375px viewport in both directions, with no horizontal page scroll.
- [ ] Arabic renders correctly with a bundled or reliably available Arabic font rather than depending on whatever the OS happens to have.
- [ ] The app is installable/usable on mobile to the standard the PDF's “web and mobile friendly” implies — decide and document whether that means responsive-only or a PWA, then deliver it.
- [ ] Switching language preserves the current route and unsaved form state where practical, and never leaves the layout half-mirrored.
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

- **Depends on:** `security-admin` (runtime configuration storage and the permission model
  that the scoping enforcement checks against).
- **Related stories:** all of them — this story sets the rules the other eleven must follow,
  so land the locale-drift guard and the scoping tests early where they will catch regressions.

---

## Already shipped by the bootstrap — extend, do not rebuild

- **Bilingual ar/en with RTL, working end to end.** `frontend/src/i18n.ts` (vue-i18n, Arabic
  default), `src/stores/ui.ts` writes `lang` and `dir` onto `<html>`, PrimeVue mirrors from
  `dir`, Tailwind uses logical properties, and `.ltr-nums` in `src/assets/main.css` keeps
  numbers and references from reordering inside Arabic text. Verified in a browser.
- Full ar/en message catalogues in `src/locales/ar.json` and `src/locales/en.json`.
- Backend request localization for `ar-SA`/`en-US` with Arabic as the default culture
  (`Api/Program.cs`), and `Accept-Language` sent by the axios client.
- Every bilingual entity stores a `NameAr`/`NameEn` (or `FullNameAr`/`FullNameEn`) pair, and
  Arabic round-trips correctly through the API into `nvarchar` columns (verified).
- **Responsive shell.** `layouts/AppLayout.vue` — permanent sidebar on desktop, drawer on
  mobile that opens from the correct side per direction; verified usable at a 415px viewport.
- **Multi-department and multi-branch as data.** `Department` and `Branch` entities, seeded,
  with `DepartmentId`/`BranchId` on `Customer`, `Ticket` and `ApplicationUser`, filter support
  in both list services, and indexes on `(DepartmentId, Status)`.
- Light/dark theme with the choice persisted, and branding tokens declared as CSS custom
  properties on `:root` in `src/assets/main.css` (`--brand-primary`, `--brand-surface`,
  `--brand-radius`) — the hook for the branding work below.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Branding: serve the resolved brand values from an endpoint the SPA fetches at startup and
  apply them by setting the CSS custom properties on `:root` at runtime. Do not rebuild the
  stylesheet per tenant.
- Scoping is the security-relevant half of this story. A query filter or an explicit scope
  applied in one place in `Application` is far safer than remembering to add `Where` clauses
  per endpoint. Consider an `IScopeProvider` derived from the JWT's `department_id`/`branch_id`
  claims, and apply it centrally — but be careful not to break Admin's legitimate global view.
- The locale-drift guard can be a small unit test that loads both JSON files and compares the
  flattened key sets. Cheap to write, and it will pay for itself across eleven stories.
- Arabic font: bundle a licensed Arabic face (or a well-supported open one) and reference it
  from the existing font stack in `main.css`, which currently names `Noto Sans Arabic` without
  shipping it.
- Do not re-do the RTL plumbing. `dir` on `<html>` plus logical properties already works; the
  job is keeping it working and covering the gaps listed above.

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

- A native mobile application — the PDF asks for mobile-friendly, not native.
- Additional languages beyond Arabic and English.
- Re-implementing the existing i18n/RTL/theme plumbing, which is shipped and verified.

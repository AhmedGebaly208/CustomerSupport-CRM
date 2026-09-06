# Story intake

- Folder: `.squad/stories/knowledge-base/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Knowledge Base
- **Feature slug (folder under `plans/`):** `knowledge-base`
- **PDF area:** 6

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `KNOWLEDGE_BASE`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Knowledge Base
```

---

## Description

```
A bilingual knowledge base that serves two audiences from one body of content: agents
looking for a solution while working a ticket, and customers self-serving in the portal.
Nothing exists for this area yet — it is a greenfield vertical inside an established
codebase.

Verbatim requirement from the PDF (area 6):

- FAQs
- Help articles
- Solutions and guides
- Search
```

---

## Acceptance criteria

```
- [ ] An `Article` entity supports a bilingual title and body (ar/en), a slug, a category, tags, a draft/published/archived state, and an author.
- [ ] Articles are organised in a category tree, and an article can be marked as an FAQ so FAQs can be listed separately.
- [ ] Full-text search returns useful results for both Arabic and English queries and is verified against Arabic text in a test.
- [ ] Search results are ranked and the query is highlighted in the excerpt.
- [ ] Only published articles are visible to non-staff readers; drafts are visible to their author and to Admin/Manager.
- [ ] An agent working a ticket can search the knowledge base from the ticket screen and insert a link to an article into a reply.
- [ ] Article usefulness is tracked: view counts, and a helpful/not-helpful vote per reader that cannot be cast repeatedly by the same reader.
- [ ] An article records which tickets it was linked from, so the desk can see which articles actually resolve tickets.
- [ ] Article edits are versioned: an editor can see previous versions and what changed.
- [ ] Rich body content is sanitised on the way in — stored HTML must not be able to execute script when rendered.
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

- **Depends on:** `customers` (attachment endpoints, if article files reuse them).
- **Related stories:** `customer-portal` (renders published articles to customers),
  `ai-features` (suggested solutions search this content), `tickets` (link from a reply).

---

## Already shipped by the bootstrap — extend, do not rebuild

Nothing for this feature area. Build it following the conventions below.

Relevant existing pieces to reuse rather than duplicate:
- `TicketCategory` already models a bilingual self-referencing tree — mirror its shape for
  article categories instead of inventing a different one.
- `Attachment` + `IFileStorage` handle file uploads (article images, PDF guides).
- `Application/Auth/Roles.cs` for the author/publisher authorization split.
- The frontend already has `PageHeader.vue` and the `nav.knowledgeBase` locale key reserved
  in both `locales/ar.json` and `locales/en.json`.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Use SQL Server full-text search with a language-appropriate configuration for Arabic;
  `LIKE '%term%'` will not rank and will not stem. If full-text is unavailable in the target
  environment, document the fallback explicitly in the plan.
- Arabic search quality depends on normalising alef/hamza variants and stripping diacritics
  before indexing and querying. Put that normalisation in one Domain helper and test it.
- Sanitise HTML server-side on save (an allow-list of tags/attributes), not only on render —
  the portal and any future channel will render the same stored content.
- Versioning: an append-only `ArticleVersion` row per save is simpler and safer than diffing.
- Slugs must be unique per language and stable once published, so external links do not rot.

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

- AI-generated article drafts and AI answer selection — `ai-features`.
- The customer-facing portal shell and navigation — `customer-portal`.
- Translating article content automatically — not requested by the PDF.

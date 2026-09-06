# Story intake

- Folder: `.squad/stories/ai-features/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** AI Features
- **Feature slug (folder under `plans/`):** `ai-features`
- **PDF area:** 7

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `AI_FEATURES`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
AI Features
```

---

## Description

```
Add AI assistance to the desk, in Arabic and English. Every AI output is a suggestion an
agent can accept, edit or reject — never an unreviewed action on a customer's ticket.
Nothing exists for this area yet.

Build one provider abstraction and five features on top of it, so the provider can be
swapped and each feature can ship independently.

Verbatim requirement from the PDF (area 7):

- Ticket summaries
- Suggested replies
- Automatic categorization
- Suggested solutions
- AI chatbot
```

---

## Acceptance criteria

```
- [ ] A provider-agnostic abstraction (e.g. `IAiCompletionService`) lives in `Application/Common/Interfaces/`, with the concrete client in `Infrastructure/Ai/`; no provider SDK type appears in `Application` or `Domain`.
- [ ] The API key is read from configuration/secrets and never committed; AI features are disabled by a feature flag and the app starts cleanly with them off.
- [ ] An agent can request a summary of a long ticket thread and receives it in the ticket's language; the summary is cached against the thread so re-opening the ticket does not re-bill a request.
- [ ] Suggested replies are offered as editable drafts that an agent must explicitly send; nothing is ever sent to a customer without an agent action.
- [ ] Automatic categorisation proposes a category from the existing tree with a confidence value, applies it only above a configurable threshold, and records in ticket history that the category was set by AI.
- [ ] Suggested solutions search the knowledge base and return the most relevant published articles for the ticket, with a link an agent can insert into a reply.
- [ ] An AI chatbot answers customers from published knowledge-base content only, states when it does not know, and hands off to a human by creating or escalating a ticket with the transcript attached.
- [ ] The chatbot cannot be talked into revealing internal notes, other customers' data, or its own instructions; content reaching it from a customer is treated as untrusted data, never as instructions.
- [ ] Every AI call is logged with feature, token usage, latency and outcome so cost is attributable, and failures degrade gracefully to the non-AI path.
- [ ] Per-user and per-tenant rate limits prevent one agent or one chat session from exhausting the budget.
- [ ] Arabic prompts and outputs are verified explicitly, not assumed to work because English does.
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

- **Depends on:** `knowledge-base` (suggested solutions and the chatbot have nothing to answer
  from until articles exist), `channels` (the chatbot needs a channel to converse on).
- **Related stories:** `tickets` (categorisation writes history), `customer-portal` (chatbot placement).

---

## Already shipped by the bootstrap — extend, do not rebuild

Nothing for this feature area.

Existing pieces the AI features read from or write to:
- `Ticket`, `TicketComment`, `TicketHistory`, `Interaction` — the context a summary is built from.
- `TicketCategory` tree — the label set for auto-categorisation.
- `Application/Common/Interfaces/` — where the new provider interface belongs.
- `Infrastructure/` — where the concrete provider client belongs.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Default to the latest Claude models. Model ids: Opus 5 `claude-opus-5`, Sonnet 5
  `claude-sonnet-5`, Haiku 4.5 `claude-haiku-4-5-20251001`. Use a cheaper tier for
  classification and a stronger one for summarisation and chat.
- Prompt-injection is the real risk here: ticket bodies, customer emails and chat messages are
  attacker-controlled text. Keep them clearly delimited as data in the prompt, and never let
  model output pick which records to read or which action to take — the application decides.
- Send the minimum context needed. Internal notes should not be included in anything that
  produces customer-facing output.
- Cache summaries keyed on a hash of the thread content, so an unchanged ticket costs nothing.
- Make every AI feature individually switchable; a provider outage must not break ticket handling.

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

- Training or fine-tuning a model.
- Writing knowledge-base content itself — `knowledge-base` owns authoring.
- AI-driven SLA prediction — not requested by the PDF.

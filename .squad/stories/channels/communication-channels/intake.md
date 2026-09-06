# Story intake

- Folder: `.squad/stories/channels/`
- Source of truth for requirements: `docs/requirements.md` (transcription of
  `azm_squad_customer_support_crm.pdf`). The relevant section is quoted verbatim below.

## Feature

- **Feature name (display):** Communication Channels
- **Feature slug (folder under `plans/`):** `channels`
- **PDF area:** 3

## Tracker (metadata only)

- **Tracker type:** `none`
- **Work item id:** `CHANNELS`
- **Work item type:** `Feature`
- **Status:** `Ready for planning`

---

## Title

```
Communication Channels
```

---

## Description

```
Let tickets arrive and be answered on the channels customers actually use, instead of
only through the agent UI. Every channel must create or thread onto a ticket, record an
`Interaction`, and let an agent reply back out on the same channel.

This is the largest story in the set. Plan it as a channel-agnostic core plus one
adapter per channel, so channels can ship one at a time.

Verbatim requirement from the PDF (area 3):

- Email
- WhatsApp
- Live chat
- SMS
- Web forms
```

---

## Acceptance criteria

```
- [ ] A channel-agnostic abstraction exists (e.g. `IChannelAdapter` with send/receive plus a normalised inbound message model) and each concrete channel is a separate implementation registered in DI.
- [ ] Inbound email creates a new ticket, or appends to the existing one when the subject or headers carry a known ticket reference; the raw message is stored as an `Interaction` and attachments become `Attachment` rows.
- [ ] An agent's public reply on a ticket is delivered outbound on the channel the ticket arrived on, and delivery success or failure is recorded.
- [ ] WhatsApp and SMS inbound webhooks are accepted, signature-verified, matched to a customer by phone number, and threaded onto the customer's most recent active ticket or a new one.
- [ ] An unrecognised sender creates a new customer record rather than dropping the message, flagged for an agent to complete.
- [ ] A public web form submits a ticket without authentication, is rate-limited and bot-protected, and confirms with the ticket number.
- [ ] Live chat supports a real-time agent/customer conversation that can be converted into a ticket, retaining the transcript as an `Interaction`.
- [ ] Every channel credential is read from configuration/secrets, never committed, and the app fails fast at startup when an enabled channel is misconfigured.
- [ ] A delivery failure is retried with backoff and surfaced to the agent rather than lost silently.
- [ ] An outbound send is idempotent under webhook retries — the same provider message id is never sent or recorded twice.
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

- **Depends on:** `customers` (attachment plumbing is reused for inbound files),
  `tickets` (merge is needed to deduplicate threads).
- **Related stories:** `integrations` (shares provider credentials and outbound gateways),
  `sla-automation` (a first outbound reply stops the response clock),
  `customer-portal` (Portal is itself a channel).

---

## Already shipped by the bootstrap — extend, do not rebuild

- `Domain/Enums/CommunicationChannel.cs` — Email, WhatsApp, LiveChat, Sms, WebForm, Portal,
  Phone, Internal. Every `Ticket` and `Interaction` already records its channel.
- `Domain/Entities/Interaction.cs` plus `InteractionDirection` (Inbound/Outbound) — the
  touchpoint log the channels must write to.
- `TicketService.CreateAsync` already writes an inbound `Interaction`, and a public
  `AddCommentAsync` already writes an outbound one. Reuse both paths.
- Customers already carry `Email`, `Phone` and `WhatsAppNumber`, plus `CustomerContact`
  rows for additional addresses — that is how an inbound message is matched to a customer.
- Frontend renders channel labels from `locales/*.json` under the `channel.*` keys.

---

## Technical hints

Repos/roots: `backend`, `frontend`. Primary language: `csharp` (backend), `typescript` (frontend).

- Prefer webhook-based inbound over polling where the provider supports it, and treat every
  webhook as untrusted: verify the signature before doing any work.
- Ticket threading by reference: put a token such as `[TKT-000123]` in the outbound subject and
  parse it on the way back in; fall back to `In-Reply-To`/`References` headers for email.
- Live chat needs a persistent connection — SignalR is the natural fit for an ASP.NET Core host
  and works with the existing JWT auth.
- Keep provider SDK types out of `Application`; the adapter interface belongs in
  `Application/Common/Interfaces/` and the implementations in `Infrastructure/Channels/`.
- The public web-form endpoint is the only anonymous write path in the system. It needs
  `[AllowAnonymous]`, rate limiting, and its own validator — review it carefully.

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

- The AI chatbot answering on these channels — that is the `ai-features` story.
- Customer-authenticated ticket submission — that is the `customer-portal` story.
- SLA timers reacting to channel events — that is the `sla-automation` story.

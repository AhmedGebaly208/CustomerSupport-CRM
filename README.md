# Customer Support CRM

An Arabic-first customer support system with full RTL: ASP.NET Core 10 + SQL Server on the
back end, Vue 3 on the front end. Requirements live in `docs/requirements.md` (transcribed
from `azm_squad_customer_support_crm.pdf`), and the remaining work is planned through
[squad-kit](https://github.com/AzmSquad/squad-kit) under `.squad/`.

---

## 1. Prerequisites

| | |
|---|---|
| .NET SDK | 10.0+ (`dotnet --list-sdks`) |
| Node.js | 20+ (`node -v`) |
| SQL Server | Express, local, at `.\SQLEXPRESS` |
| `dotnet-ef` | `dotnet tool install --global dotnet-ef` |

---

## 2. Running the app

### First time: create your development config

This file is git-ignored because it holds the JWT signing key and the seed admin password:

```bash
cp backend/src/CustomerSupportCRM.Api/appsettings.Development.example.json backend/src/CustomerSupportCRM.Api/appsettings.Development.json
```

Then open it and replace the two values marked `REPLACE-ME`:

- **`Jwt:SigningKey`** — at least 32 characters. The app **refuses to start** with a
  shorter or empty key.
- **`Seed:AdminPassword`** — the administrator created on first run. There is no fallback:
  leave it unset and no admin is created, and the log says why.

### Back end + Swagger

```bash
dotnet run --project backend/src/CustomerSupportCRM.Api --launch-profile http
```

- Opens **Swagger** automatically at <http://localhost:5178/swagger>
- API root: <http://localhost:5178>
- `GET /health` confirms the service is up

### Front end

In a second terminal:

```bash
npm install --prefix frontend
```

```bash
npm run dev --prefix frontend
```

The app runs at <http://localhost:5173>. Vite proxies `/api` to the back end, so the
browser stays same-origin and no CORS preflight is involved in development.

### Signing in

Use the email and password you set in `Seed:AdminEmail` and `Seed:AdminPassword` during
setup above.

---

## 3. Migrations apply automatically on run

In **Development**, every pending migration is applied when the project starts, along with
reference-data seeding. The log names exactly what ran:

```
[17:33:51 WRN] Applying 1 pending migration(s): 20260825141752_SystemConfiguration
[17:33:52 WRN] Applied 1 migration(s) successfully.
```

And when there is nothing to do:

```
[17:34:02 INF] Database is up to date; no migrations to apply.
```

The switches live in `appsettings.Development.json`:

```jsonc
"Database": {
  "MigrateOnStartup": true,   // apply any pending migration
  "SeedOnStartup": true       // roles, admin, departments, branches, categories, config
}
```

**Both are `false` in `appsettings.json` on purpose.** Auto-migrating a shared or
production database races between instances and gives a deploy no chance to be reviewed, so
those environments apply migrations as a deliberate step.

### Adding a migration

```bash
dotnet ef migrations add <MigrationName> --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api --output-dir Persistence/Migrations
```

Then just run the project — it applies itself.

### Applying manually (for production)

```bash
dotnet ef database update --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api
```

---

## 4. Trying out what is built

Every endpoint is in Swagger. Protected ones need a token:

1. In Swagger open **`POST /api/auth/login`** → Try it out → send:
   ```json
   { "email": "<Seed:AdminEmail>", "password": "<Seed:AdminPassword>" }
   ```
2. Copy `accessToken` from the response.
3. Click **Authorize** (top right) and paste the token.
4. Every endpoint is now callable.

> The token expires after **15 minutes** by design: the permission set is cached inside the
> JWT, so revoking a permission or deactivating an account cannot take effect any sooner
> than the token expires. Fifteen minutes is the upper bound on that delay. When it lapses,
> log in again or use `POST /api/auth/refresh`.

### Worth exercising

**User administration and its lockout rails** — `/api/users`

| Try | Expected |
|---|---|
| `POST /api/users` with role `Agent` and a department | 201 |
| `POST /api/users/{your own id}/deactivate` | **409** — you cannot deactivate yourself |
| `PUT /api/users/{admin id}/roles` with `["Manager"]` | **409** — last administrator |
| Create a user with roles `["Customer","Agent"]` | **400** — the combination is refused |

**Department scoping** — the security-critical part

1. Create two agents, one in `SUP` and one in `BIL`.
2. Sign in as each and keep their tokens.
3. As the admin (who sees everything), create a customer and a ticket in each department.
4. The `SUP` agent calls `GET /api/tickets` → sees only their own department's ticket.
5. The `SUP` agent calls `GET /api/tickets/{the BIL ticket}` → **403**.

**Permissions** — with an `Agent` token:

| Endpoint | Expected |
|---|---|
| `/api/tickets`, `/api/customers`, `/api/dashboard/agent` | 200 |
| `/api/users`, `/api/audit-logs`, `/api/system-config/**` | **403** |
| `DELETE /api/customers/{id}`, `PUT /api/branding` | **403** |

**Audit log** — `/api/audit-logs`

- Every change is recorded automatically with a JSON diff.
- Edit a customer, then `GET /api/audit-logs?entityName=Customer`.
- GET verbs only — there is deliberately no write path.

**Write-only channel credentials**

1. `PUT /api/system-config/channels/1` with an `apiKey`.
2. `GET /api/system-config/channels` → `hasCredentials: true`, but **the key itself is
   never returned**.
3. `PUT` again **without** `apiKey` → the stored value survives.
4. `GET /api/audit-logs?entityName=ChannelToggle` → the secret is absent from the trail.

**Branding** — `/api/branding`

- `GET` works unauthenticated (the login screen needs it before anyone has a token).
- `PUT` with `"primaryColor": "#7c3aed"` → refresh the browser, the colour changes at once.
- `"primaryColor": "not-a-colour"` → **400**.
- `"logoUrl": "javascript:alert(1)"` → **400**.

### From the UI

Signed in as an administrator, the sidebar shows six entries: Dashboard, Tickets,
Customers, Users & permissions, Audit log, System settings.

The last three appear only with the matching permission. Sign in as an `Agent` and they are
gone; type the URL by hand and the router sends you back — and the API refuses regardless,
because hiding a control is never the security boundary.

Also try the **EN/ع** button in the top bar: it flips language and mirrors the entire
layout. The sun/moon button toggles the theme.

---

## 5. Verification

```bash
dotnet test backend/CustomerSupportCRM.slnx
```

```bash
npm run build --prefix frontend
```

`npm run build` runs the locale guard before building: if a key exists in one catalogue and
not the other, or a `{placeholder}` differs between languages, the build fails.

---

## 6. Layout

```
backend/
  CustomerSupportCRM.slnx                     .NET 10 — note .slnx, not .sln
  src/CustomerSupportCRM.Domain/              entities and ticket workflow, no dependencies
  src/CustomerSupportCRM.Application/         DTOs, services, validators, interfaces
  src/CustomerSupportCRM.Infrastructure/      EF Core, Identity, JWT, storage, seeder
  src/CustomerSupportCRM.Api/                 controllers, middleware, DI, Swagger
  tests/CustomerSupportCRM.Application.Tests/ 117 tests
frontend/                                     Vue 3 + Vite + TS + PrimeVue 5 + Tailwind 4
docs/requirements.md                          the PDF requirements, as text
.squad/                                       squad-kit stories and plans
```

### Conventions worth knowing before contributing

- **English only** in code comments, documentation and commit messages. Arabic appears in
  the repository solely as product data: `locales/ar.json`, the `NameAr` seed values, and
  Arabic test fixtures.
- Entities derive from `AuditableEntity`; the persistence interceptor stamps the audit
  columns and writes the trail. Never set them by hand.
- Authorise against `Permissions.*`, never a role name.
- List endpoints return `PagedResult<T>` and take a query deriving from `PagedQuery`.
- Throw the exceptions in `AppExceptions.cs`; the middleware maps them to RFC 7807.
- Every user-visible string is a key in **both** `locales/ar.json` and `locales/en.json`.
- Use Tailwind logical properties (`ms-*`, `me-*`, `text-start`) so RTL mirrors correctly.

---

## 7. Status

**Implemented:** user administration · permission model (21 permissions) · audit log ·
system configuration (business hours, holidays, branding, feature flags, channels) ·
self-service password change · department and branch scoping · customers and tickets
(foundation) · agent dashboard · locale-drift guard.

**12 stories** live in `.squad/stories/`, of which **two plans** are generated and
implemented (`security-admin`, `platform`). The other ten areas need a plan first:

```bash
squad new-plan .squad/stories/<feature>/<story>/intake.md --api -y
```

```bash
squad list
```

Each plan's `00-overview.md` records what landed, which deviations were made and why, and
what was deliberately left out.

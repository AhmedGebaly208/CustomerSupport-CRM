# Story 03 — Audit log browser (Story: SECURITY_ADMIN)

## Prerequisites

- [Story 02 completed](./02-story-permissions-model-SECURITY_ADMIN.md) — the new endpoint uses `Permissions.AuditLogs.View`.

---

## Story Goal

Make the existing `AuditLogs` table browsable through an admin UI:
paged, filterable by entity, action, user and date range, and readable
— the JSON diff is presented in a human-friendly form. No endpoint may
edit or delete an entry: the audit log is append-only through the
application.

---

## Context — Read These Files First

1. `backend/src/CustomerSupportCRM.Domain/Entities/AuditLog.cs` — confirm columns (`EntityType`, `EntityId`, `Action`, `Changes` (JSON), `UserId`, timestamps). Do not modify — this is the append-only shape.
2. `backend/src/CustomerSupportCRM.Domain/Enums/AuditAction.cs` — the action enum used for filtering.
3. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Interceptors/AuditingInterceptor.cs` — read end-to-end to understand the JSON shape written into `Changes`, and the redaction list. The UI will render this JSON.
4. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/AppDbContext.cs` — confirm `DbSet<AuditLog>` and how the interceptor is wired.
5. `backend/src/CustomerSupportCRM.Application/Common/Interfaces/IAppDbContext.cs` — the interface exposes `DbSet<AuditLog>`; if not, add it.
6. `backend/src/CustomerSupportCRM.Application/Common/Models/PagedQuery.cs` / `PagedResult.cs` — the paging shape.
7. `backend/src/CustomerSupportCRM.Application/Customers/CustomerService.cs` — precedent for a paged, filterable list backed by `IAppDbContext`.
8. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Migrations/20260825122552_InitialCreate.cs` — inspect the `AuditLogs` table creation to plan the covering index.

---

## Product rules (from story)

- **Current:** rows are written by `AuditingInterceptor` and never read.
- **New:** an admin can browse them via `GET api/audit-logs`, filtered and paged.
- **Invariant:** there is no `POST`, `PUT`, `PATCH` or `DELETE` endpoint on `audit-logs`. The service exposes read methods only. `AuditLog` remains marked in `AuditingInterceptor` as excluded from auditing (already the case) so browsing does not create meta-rows.

---

## Backend Tasks

### 1 — Application service

**Create file:** `backend/src/CustomerSupportCRM.Application/AuditLogs/IAuditLogService.cs`

```csharp
Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct);
Task<AuditLogDto> GetAsync(Guid id, CancellationToken ct);
```

**Create file:** `backend/src/CustomerSupportCRM.Application/AuditLogs/AuditLogService.cs`

- Queries `IAppDbContext.AuditLogs`, `.AsNoTracking()`.
- Filters: `EntityType`, `EntityId`, `Action`, `UserId`, `DateFrom`, `DateTo`. All optional.
- Sorted by `CreatedAt DESC` by default.
- Joins to `Users` for the display name (`FullNameAr`/`FullNameEn`) so the UI does not need a second call.

**Create file:** `backend/src/CustomerSupportCRM.Application/AuditLogs/Dtos/AuditLogDtos.cs`

```csharp
public sealed record AuditLogDto(
    Guid Id,
    string EntityType,
    string EntityId,
    AuditAction Action,
    string Changes,               // raw JSON string, the UI parses
    Guid? UserId,
    string? UserFullNameAr,
    string? UserFullNameEn,
    DateTimeOffset CreatedAt);

public sealed class AuditLogQuery : PagedQuery
{
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public AuditAction? Action { get; init; }
    public Guid? UserId { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
}
```

### 2 — Controller

**Create file:** `backend/src/CustomerSupportCRM.Api/Controllers/AuditLogsController.cs`

- Route: `api/audit-logs`.
- `[Authorize(Policy = Permissions.AuditLogs.View)]`.
- Endpoints: `GET api/audit-logs` and `GET api/audit-logs/{id:guid}`. **No** other verbs.
- Do not expose a raw DbSet or an OData endpoint.

### 3 — Index for the filter combinations

**Create migration:** `AuditLogFilterIndexes`.

- Composite index on `(CreatedAt DESC, EntityType, Action)` — most common list.
- Non-clustered index on `UserId` filtered `WHERE UserId IS NOT NULL`.
- Non-clustered index on `(EntityType, EntityId)`.

Command: `dotnet ef migrations add AuditLogFilterIndexes --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api --output-dir Persistence/Migrations`.

Retention: this story does not implement archival. Add a `// TODO(retention):` comment in `AuditLog.cs` recording the intent to move rows older than N months to a cold table; do not implement here.

### 4 — Confirm append-only

- No new interface members on `IAuditLogService` besides list/get.
- Grep for any code that writes to `AuditLog` outside `AuditingInterceptor`; there should be none. Add a code comment on `AuditLog.cs`: "Rows are only written by AuditingInterceptor; there is no application-level create/update/delete path."

---

## Frontend Tasks

### 1 — API types + service

**File:** `frontend/src/types/api.ts`

Add `AuditLogDto`, `AuditLogQuery`, mirror of the `AuditAction` enum.

**File:** `frontend/src/api/services.ts`

Add `auditLogs.list(query)` and `auditLogs.get(id)`.

### 2 — Views

Create under `frontend/src/views/admin/audit-logs/`:

- `AuditLogListView.vue` — filters row (entity type dropdown, action dropdown, user picker, date range), PrimeVue DataTable, columns: timestamp (wrap in `ltr-nums`), entity, action, user (bilingual). Row click opens the detail.
- `AuditLogDetailView.vue` — pretty-print the `Changes` JSON. For each key present in `changed`, show `before → after` in a two-column layout. If the interceptor emitted the redaction sentinel for a key, render "•••" and label it as "redacted" via i18n; do not attempt to reveal it.

### 3 — Router + menu

**File:** `frontend/src/router/index.ts`

- Add `/admin/audit-logs` and `/admin/audit-logs/:id` with `meta.permission = 'audit-logs.view'`.

**File:** `frontend/src/layouts/AppLayout.vue`

- Add "Audit logs" under Administration; hidden without permission.

### 4 — Localisation

- Every filter label, column header, action name (localised names for `AuditAction` values) in both `ar.json` and `en.json`.
- The literal string "redacted" must be an i18n key.

---

## Edge Cases & Failure Modes

- **Very large `Changes` JSON.** Cap the raw string returned to 64 KB on the DTO; log a warning server-side if the row exceeds it (should not happen given the interceptor's diff logic).
- **Missing user.** `UserId` is `null` when the change originated from a system context (e.g. the seeder). Render as "System" via i18n.
- **Redacted values.** `AuditingInterceptor` writes a sentinel for `PasswordHash`, `SecurityStamp`, `RefreshToken`. The UI must not attempt to display the original value.
- **Filter on future date range.** Validator rejects `DateFrom > DateTo` with 400.
- **User picker on 375 px.** Use a searchable dropdown that opens full-screen on mobile.
- **RTL date range picker.** Confirm the two inputs mirror correctly.
- **Deep pagination.** `PagedQuery` clamps `PageSize` to 100; the composite index makes `CreatedAt DESC` scans fast.
- **Someone tries `DELETE /api/audit-logs/{id}`.** Returns 404 (route does not exist), never 405 that hints the resource is mutable elsewhere.

---

## Test Plan

Add xUnit tests to `backend/tests/CustomerSupportCRM.Application.Tests/`:

1. `AuditLogServiceTests.cs`
   - `List_FiltersByEntityType`
   - `List_FiltersByAction`
   - `List_FiltersByUser`
   - `List_FiltersByDateRange_InclusiveFrom_InclusiveTo`
   - `List_ReturnsMostRecentFirst`
   - `List_ClampsPageSizeAt100` (via `PagedQuery`)
   - `Get_UnknownId_ThrowsNotFound`
2. Extend `CustomerServiceTests.cs` or add `AuditingRedactionTests.cs` — assert that after creating a user with a password, the `Changes` for the `Users` insert contains the sentinel for `PasswordHash`, not the actual value.

---

## Migration / Rollback

The new migration is additive (indexes only). Rollback = `dotnet ef migrations remove` and redeploy; no data loss.

---

## Verification Steps

1. **Backend builds:** `dotnet build backend/CustomerSupportCRM.slnx`.
2. **Backend tests:** `dotnet test backend/CustomerSupportCRM.slnx`.
3. **DB migration:** `dotnet ef database update --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api`. Confirm the new indexes with `sp_helpindex 'AuditLogs'`.
4. **Manual, backend:** create a customer via `POST /api/customers`, then `GET /api/audit-logs?entityType=Customer` — one row visible; `Changes` JSON contains the created fields.
5. **Manual, redaction:** create a user via Story 01's endpoint, `GET /api/audit-logs?entityType=Users` — `PasswordHash` value is the sentinel.
6. **Manual, append-only:** attempt `DELETE /api/audit-logs/{id}` → 404; attempt `PUT` → 404.
7. **Frontend builds:** `npm run build --prefix frontend`.
8. **Frontend manual:** browse the list at LTR and RTL, 375 px; filter combinations return sane results; the detail view renders JSON diffs readably.

---

## Done Criteria

- [ ] `GET /api/audit-logs` and `GET /api/audit-logs/{id}` exist, are permission-gated, and paged.
- [ ] No other verbs are exposed on the resource.
- [ ] New composite index migration applied.
- [ ] `Changes` JSON is rendered human-friendly; redacted keys never leak the original value.
- [ ] Admin UI is bilingual, RTL-correct, and usable at 375 px.
- [ ] `dotnet test` and `npm run build` both pass.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 04.**

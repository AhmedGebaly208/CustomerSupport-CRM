# Story 04 — System configuration (Story: SECURITY_ADMIN)

## Prerequisites

- [Story 02 completed](./02-story-permissions-model-SECURITY_ADMIN.md) — endpoints require `Permissions.SystemConfig.Manage` / `View`.
- Coordinate with the owners of `sla-automation` (business hours + holidays) and `platform` (branding) — this story provides the source of truth they read.

---

## Story Goal

Admins can edit business hours, holidays, branding, feature flags, and
channel toggles at runtime, without a redeploy. Values are validated on
save, persisted, cached in memory, and cache-invalidated on change.
Sensitive values (API keys, secrets) are never returned by a
configuration endpoint — they are write-only or masked.

---

## Context — Read These Files First

1. `backend/src/CustomerSupportCRM.Domain/Common/AuditableEntity.cs` — the new configuration entity inherits from it (audit stamps + soft delete "for free").
2. `backend/src/CustomerSupportCRM.Domain/Common/ISoftDeletable.cs` — soft delete comes via reflection in `AppDbContext.ApplySoftDeleteFilters`.
3. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/AppDbContext.cs` — confirm the reflection-driven filter application.
4. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Configurations/CustomerConfigurations.cs` — precedent for `IEntityTypeConfiguration<T>`; follow the same layout.
5. `backend/src/CustomerSupportCRM.Application/Common/Interfaces/IAppDbContext.cs` — add the new `DbSet<T>` here **and** in `AppDbContext`.
6. `backend/src/CustomerSupportCRM.Application/Common/Interfaces/IClock.cs` — inject in place of `DateTimeOffset.UtcNow`.
7. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Interceptors/AuditingInterceptor.cs` — the redaction list. Any secret column added in this story must be added to `SensitiveProperties`.
8. `backend/src/CustomerSupportCRM.Api/appsettings.json` — for `IOptionsMonitor` patterns already present.
9. Sibling plan: read the `sla-automation` and `platform` intakes to confirm the field names they expect.

---

## Product rules (from story)

- **Current:** business hours and branding are hard-coded or absent; feature flags do not exist.
- **New:** all five configuration domains live in the database, are editable at runtime, are validated on save, and are cached with explicit invalidation.
- **Invariant:** sensitive values on write are stored (encrypted at rest is a follow-up); on read they are masked or omitted entirely. A GET must never round-trip a secret.

---

## Backend Tasks

### 1 — Domain entities

**Create file:** `backend/src/CustomerSupportCRM.Domain/Entities/BusinessHours.cs`

```csharp
public sealed class BusinessHours : AuditableEntity
{
    public DayOfWeek Day { get; set; }
    public TimeSpan? OpenAt { get; set; }   // null on a non-working day
    public TimeSpan? CloseAt { get; set; }
    public bool IsWorkingDay { get; set; }
}
```

**Create file:** `backend/src/CustomerSupportCRM.Domain/Entities/Holiday.cs`

```csharp
public sealed class Holiday : AuditableEntity
{
    public DateOnly Date { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
}
```

**Create file:** `backend/src/CustomerSupportCRM.Domain/Entities/BrandingSetting.cs`

```csharp
public sealed class BrandingSetting : AuditableEntity
{
    public string CompanyNameAr { get; set; } = "";
    public string CompanyNameEn { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }   // hex
    public string? SecondaryColor { get; set; }
    public string DefaultLocale { get; set; } = "ar";
}
```

Row semantics: a single row (guarded in configuration and service). Modelled as an entity, not a keyless singleton, so it participates in the audit trail.

**Create file:** `backend/src/CustomerSupportCRM.Domain/Entities/FeatureFlag.cs`

```csharp
public sealed class FeatureFlag : AuditableEntity
{
    public string Key { get; set; } = "";        // unique
    public bool IsEnabled { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
}
```

**Create file:** `backend/src/CustomerSupportCRM.Domain/Entities/ChannelToggle.cs`

```csharp
public sealed class ChannelToggle : AuditableEntity
{
    public CommunicationChannel Channel { get; set; }   // enum already exists
    public bool IsEnabled { get; set; }
    // Secrets live on this row only when the channel needs them.
    // Never round-tripped on read; write-only.
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
}
```

### 2 — Configurations

**Create file:** `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Configurations/SystemConfigConfigurations.cs`

- `BusinessHours`: unique index on `Day`.
- `Holiday`: unique index on `Date` (composite with `IsDeleted`).
- `BrandingSetting`: no unique constraint on business columns; service enforces the "single row" rule.
- `FeatureFlag`: unique index on `Key`.
- `ChannelToggle`: unique index on `Channel`.

### 3 — Extend `AuditingInterceptor` redaction list

**File:** `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Interceptors/AuditingInterceptor.cs`

- Extend `SensitiveProperties` with `"ApiKey"` and `"ApiSecret"`. This is the executor's checkpoint moment — the intake calls it out explicitly.

### 4 — Application service

**Create file:** `backend/src/CustomerSupportCRM.Application/SystemConfig/ISystemConfigService.cs`

```csharp
Task<IReadOnlyList<BusinessHoursDto>> GetBusinessHoursAsync(CancellationToken ct);
Task SetBusinessHoursAsync(IReadOnlyList<BusinessHoursDto> hours, CancellationToken ct);

Task<PagedResult<HolidayDto>> ListHolidaysAsync(HolidayQuery query, CancellationToken ct);
Task<HolidayDto> UpsertHolidayAsync(HolidayDto dto, CancellationToken ct);
Task DeleteHolidayAsync(Guid id, CancellationToken ct);

Task<BrandingSettingDto> GetBrandingAsync(CancellationToken ct);
Task<BrandingSettingDto> SetBrandingAsync(BrandingSettingDto dto, CancellationToken ct);

Task<IReadOnlyList<FeatureFlagDto>> ListFlagsAsync(CancellationToken ct);
Task<FeatureFlagDto> SetFlagAsync(string key, bool isEnabled, CancellationToken ct);

Task<IReadOnlyList<ChannelToggleDto>> ListChannelsAsync(CancellationToken ct);
Task<ChannelToggleDto> SetChannelAsync(ChannelToggleWriteDto dto, CancellationToken ct);
```

**Create file:** `backend/src/CustomerSupportCRM.Application/SystemConfig/Dtos/SystemConfigDtos.cs`

- `BusinessHoursDto` mirrors the entity.
- `HolidayDto` mirrors the entity.
- `BrandingSettingDto` mirrors the entity — logo URL public; hex colours public; no secrets on this DTO.
- `FeatureFlagDto` mirrors the entity.
- `ChannelToggleDto` (read shape) — includes `Channel`, `IsEnabled`, and `bool HasApiKey`, `bool HasApiSecret` — booleans only, never values.
- `ChannelToggleWriteDto` (write shape) — includes optional `ApiKey`, `ApiSecret`. When either field is `null` on write, the existing stored value is preserved; when the empty string is sent, the value is cleared. Document this policy in a code comment.

### 5 — Cache with explicit invalidation

**Create file:** `backend/src/CustomerSupportCRM.Application/SystemConfig/ISystemConfigCache.cs`

- Methods: `Task<T?> GetAsync<T>(string key, Func<CancellationToken, Task<T>> loader, CancellationToken ct); void Invalidate(string key); void InvalidateAll();`.

**Create file:** `backend/src/CustomerSupportCRM.Infrastructure/SystemConfig/SystemConfigCache.cs`

- Backed by `IMemoryCache` with a documented default TTL (e.g. 5 minutes).
- Keys are string constants co-located in `ISystemConfigCache`: `"config:business-hours"`, `"config:holidays"`, `"config:branding"`, `"config:flags"`, `"config:channels"`.
- Every mutation in `SystemConfigService` calls `cache.Invalidate(<key>)` after `SaveChangesAsync` succeeds.

Register in `Application/DependencyInjection.cs` (interface) and `Infrastructure/DependencyInjection.cs` (implementation) — mirror the existing pattern.

### 6 — Validators

**Create file:** `backend/src/CustomerSupportCRM.Application/SystemConfig/Validators/SystemConfigValidators.cs`

- `BusinessHoursDtoValidator`: for a working day, `OpenAt` and `CloseAt` required and `OpenAt < CloseAt`; for a non-working day, both null.
- `SetBusinessHoursRequestValidator`: exactly seven rows covering `DayOfWeek.Sunday..Saturday`, no duplicates.
- `HolidayDtoValidator`: `Date` required; bilingual names required.
- `BrandingSettingDtoValidator`: colours match `^#[0-9A-Fa-f]{6}$`; `DefaultLocale in { "ar", "en" }`; both company names required.
- `FeatureFlagValidator`: `Key` non-empty, `[a-z][a-z0-9\.\-]*`.
- `ChannelToggleWriteDtoValidator`: `Channel` in enum; when enabling a channel that requires a key (call out `Email`, `WhatsApp`, `Sms` explicitly per the enum values in `CommunicationChannel.cs`), require the corresponding secret **or** an existing stored value (checked in the service, not the validator).

### 7 — Controller

**Create file:** `backend/src/CustomerSupportCRM.Api/Controllers/SystemConfigController.cs`

- Route: `api/system-config`.
- `[Authorize(Policy = Permissions.SystemConfig.View)]` on GETs; `[Authorize(Policy = Permissions.SystemConfig.Manage)]` on writes.
- Endpoints under sub-routes: `/business-hours`, `/holidays`, `/branding`, `/feature-flags`, `/channels`.
- GET responses use the read DTOs (no secrets). PUT/POST accept the write DTOs.

### 8 — Migration

`SystemConfigInitial` — creates all five tables, indexes, and seeds:

- Business hours: seven rows, Sun–Thu working 08:00–17:00, Fri/Sat non-working (Saudi default; document the choice in the migration comment).
- Branding: a single row with `CompanyNameAr = "دعم العملاء"`, `CompanyNameEn = "Customer Support"`, `DefaultLocale = "ar"`. Adjust values later via UI.
- Feature flags: empty.
- Channel toggles: one row per `CommunicationChannel` enum value, all disabled.

Command: `dotnet ef migrations add SystemConfigInitial --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api --output-dir Persistence/Migrations`.

### 9 — Seeder guard

**File:** `backend/src/CustomerSupportCRM.Infrastructure/Persistence/DbSeeder.cs`

- Make the seeder idempotent for the new tables: only insert if the row set is empty.

---

## Frontend Tasks

### 1 — API types + service

**File:** `frontend/src/types/api.ts` and `frontend/src/api/services.ts`

- Mirror the read and write DTOs.
- The channel service's `set` method must clearly split "clear secret" (empty string) from "keep secret" (absent field); mirror the backend documentation.

### 2 — Views

Create under `frontend/src/views/admin/system-config/`:

- `BusinessHoursView.vue` — seven rows editor; disable time inputs when `IsWorkingDay = false`.
- `HolidaysView.vue` — paged list, add/edit/delete dialog.
- `BrandingView.vue` — single-form editor with a colour picker and logo URL input.
- `FeatureFlagsView.vue` — toggle-only list; add-new dialog for a new key.
- `ChannelsView.vue` — per-channel row with `Enabled` toggle and secret fields. The secret fields render as password inputs; the stored value is never returned — the placeholder reads "value stored" (i18n key) when `HasApiKey === true`, and typing replaces the stored value; a distinct "Clear" button sends the empty-string signal.

### 3 — Router + menu

- Add `/admin/system-config/*` routes, each with `meta.permission = 'system-config.view'` for reads and stricter guards on the write buttons.
- Add "System configuration" under Administration in `AppLayout.vue`.

### 4 — Localisation

- Every field label, section title, validation message in both `ar.json` and `en.json`.
- Colour hex previews wrap the code in `class="ltr-nums"`.

### 5 — Consumers of branding + i18n defaults

**File:** `frontend/src/stores/ui.ts`

- After login, fetch `GET /api/system-config/branding` once, and apply `DefaultLocale` as the initial locale for anonymous visits (a follow-up story exposes an unauthenticated read of a subset).

---

## Edge Cases & Failure Modes

- **Two admins edit branding at once.** Last-write-wins with an audit trail row per save; call out the concurrency choice in the controller comment. If pessimistic locking is later needed, migrate to a row version column.
- **Feature flag toggled during a request.** The cache is invalidated after commit, so a subsequent request reads the new value; in-flight requests see the old value — acceptable.
- **Secret roundtrip attempt.** Test asserts `GET /channels` never contains `ApiKey` / `ApiSecret` fields.
- **Empty string vs null on secrets.** Documented policy: `null` = keep existing, `""` = clear. Controller-level test.
- **Business hours where `OpenAt > CloseAt`.** 400 from validator.
- **Missing day in business hours PUT.** 400.
- **Holiday date in the past.** Allowed (historical archiving); documented.
- **Cache eviction under memory pressure.** `IMemoryCache` may evict; loader repopulates transparently; no correctness impact.
- **Cache invalidation but transaction rolled back.** Order matters — `SaveChangesAsync` first, `Invalidate` second, both inside the same method. Document.

---

## Test Plan

Add xUnit tests to `backend/tests/CustomerSupportCRM.Application.Tests/`:

1. `SystemConfigServiceTests.cs`
   - `BusinessHours_SetRequiresAllSevenDays`
   - `BusinessHours_WorkingDayRequiresOpenBeforeClose`
   - `Branding_Get_ReturnsSeededRow`
   - `Branding_Set_UpdatesRow_AndInvalidatesCache`
   - `FeatureFlag_Set_UpsertsAndInvalidatesCache`
   - `Channel_Set_NullSecret_PreservesExistingValue`
   - `Channel_Set_EmptyStringSecret_ClearsExistingValue`
   - `Channel_Get_NeverReturnsSecrets`
2. `SystemConfigCacheTests.cs`
   - `Get_UsesLoaderOnMiss_CachesResult`
   - `Invalidate_RemovesKey`
3. `AuditingRedactionTests.cs` (extend)
   - `SettingApiKey_OnChannelToggle_IsRedactedInAuditLog`

---

## Migration / Rollback

Additive schema. Seed values are idempotent. Rollback = `dotnet ef migrations remove SystemConfigInitial`, then redeploy; the runtime falls back to hard-coded defaults (add a code comment noting there is no runtime fallback in this story — the seeder guarantees rows exist).

Half-applied state (migration ran, code did not deploy) is safe: tables exist and are empty until seed runs.

---

## Verification Steps

1. **Backend builds:** `dotnet build backend/CustomerSupportCRM.slnx`.
2. **Backend tests:** `dotnet test backend/CustomerSupportCRM.slnx`.
3. **DB migration:** `dotnet ef database update --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api`. Confirm the seven business-hours rows and per-channel rows exist.
4. **Manual, backend:** `GET /api/system-config/branding`; `PUT /api/system-config/branding` with a new company name; `GET` again returns the updated value.
5. **Manual, secret round-trip:** `PUT /api/system-config/channels` with an `ApiKey`; `GET /api/system-config/channels` — the response includes `HasApiKey: true` but no `ApiKey` field.
6. **Manual, audit redaction:** `GET /api/audit-logs?entityType=ChannelToggles` — the `Changes` for `ApiKey` shows the sentinel, not the value.
7. **Manual, cache:** hit a downstream consumer (Story `sla-automation`) after changing business hours — the new value takes effect on the next request.
8. **Frontend builds:** `npm run build --prefix frontend`.
9. **Frontend manual:** every editor works at 375 px, LTR and RTL; colour hex input uses `ltr-nums`.

---

## Done Criteria

- [ ] Five configuration domains (business hours, holidays, branding, feature flags, channel toggles) are editable at runtime.
- [ ] All writes are validated; the seeded values are correct.
- [ ] Cache invalidates on write; in-memory fallback works when the cache is empty.
- [ ] Secrets are never returned by any GET; `HasApiKey`/`HasApiSecret` booleans replace them.
- [ ] `ApiKey` and `ApiSecret` are in `AuditingInterceptor.SensitiveProperties`.
- [ ] Admin UI is bilingual, RTL-correct, and usable at 375 px.
- [ ] `dotnet test` and `npm run build` both pass.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 05.**

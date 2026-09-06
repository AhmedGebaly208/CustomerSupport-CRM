# Story 01 — User administration (Story: SECURITY_ADMIN)

## Prerequisites

- None. Extends shipped Identity, JWT and audit trail.
- Coordinate with the owner of `Infrastructure/Identity/IdentityService.cs` — this story adds admin-scoped methods to the same service.

---

## Story Goal

An Admin can list, search, create, edit, deactivate and reactivate users;
assign departments/branches; and assign roles. Deactivating a user
immediately prevents sign-in and invalidates their refresh token.

Two safety rails must hold at all times:

1. The system can never be locked out — an Admin cannot remove the last
   remaining administrator, and cannot deactivate their own account.
2. Any change to another user's roles is recorded in the audit trail by
   the existing `AuditingInterceptor`.

Not in scope: permission-based authorization (Story 02), self-service
password change (Story 05), audit-log browsing UI (Story 03).

---

## Context — Read These Files First

1. `backend/src/CustomerSupportCRM.Infrastructure/Identity/ApplicationUser.cs` — confirm shape of the Identity user (Guid key, `FullNameAr`/`FullNameEn`, `DepartmentId`/`BranchId`, `IsActive`).
2. `backend/src/CustomerSupportCRM.Infrastructure/Identity/IdentityService.cs` — read end-to-end. This is where the admin methods will live. Note how `RefreshToken` rotation is implemented; the deactivate flow must reuse the same invalidation path.
3. `backend/src/CustomerSupportCRM.Application/Common/Interfaces/IIdentityService.cs` — extend this interface with the new admin methods; do **not** add a parallel interface.
4. `backend/src/CustomerSupportCRM.Application/Auth/Roles.cs` — the four role constants (`Roles.Admin`, `Roles.Manager`, `Roles.Agent`, `Roles.Customer`) and the `Roles.Staff`/`Roles.Supervisory` groupings that must keep working.
5. `backend/src/CustomerSupportCRM.Application/Auth/Dtos/AuthDtos.cs` — existing DTO patterns; keep the new admin DTOs alongside.
6. `backend/src/CustomerSupportCRM.Api/Controllers/AuthController.cs` — existing controller style (attribute routing, `[ProducesResponseType]`, uses `ICurrentUser`).
7. `backend/src/CustomerSupportCRM.Application/Common/Exceptions/AppExceptions.cs` — the four exception types the middleware maps to ProblemDetails.
8. `backend/src/CustomerSupportCRM.Application/Common/Models/PagedQuery.cs` and `PagedResult.cs` — inherit `PagedQuery` for the list endpoint.
9. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/DbSeeder.cs` — reference for how the seeded admin account is created and repaired; the "last admin" check will consult the same Admin role.
10. `backend/src/CustomerSupportCRM.Api/Program.cs` — confirm the `FallbackPolicy` and how `[AllowAnonymous]` opts out; the new endpoints inherit auth by default.
11. `backend/src/CustomerSupportCRM.Application/Customers/CustomerService.cs` — precedent for a paged, filterable list + validators + exceptions. Follow the same shape.
12. `backend/tests/CustomerSupportCRM.Application.Tests/CustomerServiceTests.cs` and `TestSupport/TestContext.cs` — precedent for xUnit + EF InMemory. New tests follow the same fixture pattern.
13. Grep `[Authorize(Roles =` in `backend/src/CustomerSupportCRM.Api/` — capture every current site so Story 02 can migrate them.

---

## Product rules (from story)

- **Current:** roles are assigned but there is no admin surface for user CRUD.
- **New:** admins manage users through dedicated endpoints and screens.
- **Current:** deactivation is a boolean on `ApplicationUser` but is not surfaced.
- **New:** deactivation is atomic — set `IsActive = false` **and** clear the refresh token in the same transaction, so an already-issued access token can only survive until its short expiry.
- **Invariant:** the count of enabled users in the `Admin` role must be ≥ 1 at all times. Any transaction that would reduce it below 1 throws `ConflictException`.

---

## Backend Tasks

### 1 — Extend `IIdentityService` and `IdentityService`

**File:** `backend/src/CustomerSupportCRM.Application/Common/Interfaces/IIdentityService.cs`

Add admin-facing methods (all return `Task`, all accept a `CancellationToken`):

```csharp
Task<PagedResult<UserAdminDto>> ListUsersAsync(UserListQuery query, CancellationToken ct);
Task<UserAdminDto> GetUserAsync(Guid userId, CancellationToken ct);
Task<UserAdminDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct);
Task<UserAdminDto> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken ct);
Task DeactivateUserAsync(Guid userId, CancellationToken ct);
Task ReactivateUserAsync(Guid userId, CancellationToken ct);
Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct);
Task SetUserRolesAsync(Guid userId, IReadOnlyList<string> roleNames, CancellationToken ct);
```

**File:** `backend/src/CustomerSupportCRM.Infrastructure/Identity/IdentityService.cs`

Implement each. Rules for the executor:

- All admin methods must resolve the acting user via `ICurrentUser`. They may **not** operate anonymously.
- `DeactivateUserAsync`, `SetUserRolesAsync` and `UpdateUserAsync` (when it changes activation or roles) must be inside a single `IDbContextTransaction`.
- `DeactivateUserAsync`:
  1. Throw `ConflictException("Users.CannotDeactivateSelf")` if `userId == currentUser.Id`.
  2. Load the target with roles. If the target is in `Roles.Admin` and would be the last active Admin, throw `ConflictException("Users.LastAdminCannotBeDeactivated")`.
  3. Set `IsActive = false`, `RefreshToken = null`, `RefreshTokenExpiresAt = null`, then `SaveChangesAsync`. The `AuditingInterceptor` records the change; `RefreshToken` is already in the sensitive redaction list so its value is not leaked.
- `SetUserRolesAsync`:
  1. Resolve the diff (`toAdd = new − existing`, `toRemove = existing − new`).
  2. If `Roles.Admin` is in `toRemove` and the target is the last enabled Admin, throw `ConflictException("Users.LastAdminCannotLoseAdminRole")`.
  3. Apply via `UserManager.AddToRolesAsync` / `RemoveFromRolesAsync`.
  4. **Do not** clear the refresh token here — a role change should not force sign-out; permission caching is addressed in Story 02.
- All exceptions come from `Application/Common/Exceptions/AppExceptions.cs`. Return values are DTOs; do not leak `ApplicationUser`.

### 2 — DTOs and query object

**Create file:** `backend/src/CustomerSupportCRM.Application/Auth/Dtos/UserAdminDtos.cs`

```csharp
public sealed record UserAdminDto(
    Guid Id,
    string Email,
    string FullNameAr,
    string FullNameEn,
    Guid? DepartmentId,
    Guid? BranchId,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(
    string Email,
    string Password,
    string FullNameAr,
    string FullNameEn,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(
    string FullNameAr,
    string FullNameEn,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<string> Roles);

public sealed class UserListQuery : PagedQuery
{
    public string? Search { get; init; }        // matches Email, FullNameAr, FullNameEn
    public Guid? DepartmentId { get; init; }
    public Guid? BranchId { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
}
```

### 3 — Validators

**Create file:** `backend/src/CustomerSupportCRM.Application/Auth/Validators/UserAdminValidators.cs`

- `CreateUserRequestValidator`: email required + valid; password required + Identity password policy already enforced by `UserManager` (do not duplicate rules — call it out in a comment); `FullNameAr`, `FullNameEn` required, max 200; `Roles` must be a non-empty subset of the four constants in `Roles.cs`.
- `UpdateUserRequestValidator`: same as above minus password/email; roles subset check.
- `UserListQueryValidator`: PageSize handled by `PagedQuery`; `Role`, when supplied, must be one of the known constants.

These are auto-registered by `Application/DependencyInjection.cs` via `AddValidatorsFromAssembly`.

### 4 — Controller

**Create file:** `backend/src/CustomerSupportCRM.Api/Controllers/UsersController.cs`

- Route: `api/users`.
- Class-level `[Authorize(Roles = Roles.Admin)]` — this is deliberate for Story 01. Story 02 rewrites this attribute to a permission policy; do not skip it now or the endpoint is silently open.
- Endpoints:
  - `GET api/users` → `ListUsersAsync`.
  - `GET api/users/{id:guid}` → `GetUserAsync`.
  - `POST api/users` → `CreateUserAsync`.
  - `PUT api/users/{id:guid}` → `UpdateUserAsync`.
  - `POST api/users/{id:guid}/deactivate` → `DeactivateUserAsync`.
  - `POST api/users/{id:guid}/reactivate` → `ReactivateUserAsync`.
  - `PUT api/users/{id:guid}/roles` → `SetUserRolesAsync` with `{ roles: string[] }`.
- Every endpoint carries `[ProducesResponseType]` and returns typed DTOs or `NoContent()`.

### 5 — Deny sign-in for deactivated users

**File:** `backend/src/CustomerSupportCRM.Infrastructure/Identity/IdentityService.cs` — the `LoginAsync` method.

- After finding the user by email, before calling `CheckPasswordSignInAsync`, verify `user.IsActive`. On `false`, return the same deliberately-vague failure as an unknown email — do **not** distinguish "deactivated" from "unknown" over the wire.
- On the refresh-token endpoint, if the row's user is inactive, revoke and refuse. Reason string in the ProblemDetails: `Auth.Inactive`.

### 6 — Guarantee `AuditingInterceptor` still redacts

Grep `SensitiveProperties` in `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Interceptors/AuditingInterceptor.cs`. Confirm the list already includes `PasswordHash`, `SecurityStamp`, `RefreshToken`. If a new secret-bearing column is added on `ApplicationUser` in this story (there should not be one), extend the list in the **same commit**.

---

## Frontend Tasks

### 1 — API types + service

**File:** `frontend/src/types/api.ts`

Add mirrors of `UserAdminDto`, `CreateUserRequest`, `UpdateUserRequest`, `UserListQuery`.

**File:** `frontend/src/api/services.ts`

Add a typed `users` service (`list`, `get`, `create`, `update`, `deactivate`, `reactivate`, `setRoles`) using the shared axios instance from `src/api/client.ts`.

### 2 — Views

Create under `frontend/src/views/admin/users/`:

- `UserListView.vue` — reuse `PageHeader.vue`, `StatusTag.vue`, `useFormat.ts`; PrimeVue DataTable; server-side paging; filter by search, department, branch, role, active/inactive.
- `UserFormView.vue` — bilingual full name inputs, role multiselect (options from `Roles.cs` constants echoed by a lookup, or inline constants keyed by i18n), department/branch selectors reusing existing lookups.
- `UserDetailView.vue` — read-only summary with Deactivate/Reactivate/Set Roles actions. Confirm dialogs use i18n keys.

### 3 — Router + layout

**File:** `frontend/src/router/index.ts`

Add `/admin/users`, `/admin/users/new`, `/admin/users/:id`, `/admin/users/:id/edit`. Guard the branch with a `meta.roles = ['Admin']` check; Story 02 will replace this with a permission guard.

**File:** `frontend/src/layouts/AppLayout.vue`

Add an "Administration" nav group visible only to Admins.

### 4 — Localisation

**Files:** `frontend/src/locales/ar.json`, `frontend/src/locales/en.json`

Every user-facing string from the three new views must land in **both** files. Confirmation messages for deactivate/reactivate must call out the sign-out effect explicitly.

---

## Edge Cases & Failure Modes

- **Self-deactivation.** `POST /users/{me}/deactivate` from the current admin → 409 `Users.CannotDeactivateSelf`. Enforced in `DeactivateUserAsync`.
- **Last admin lockout via deactivation.** Deactivating the only enabled Admin → 409 `Users.LastAdminCannotBeDeactivated`. Enforced in `DeactivateUserAsync`.
- **Last admin lockout via role removal.** Removing `Admin` from the only enabled Admin → 409 `Users.LastAdminCannotLoseAdminRole`. Enforced in `SetUserRolesAsync`.
- **Race between two admins.** Both remove the last admin concurrently. The transaction wraps the count + write, so the second commit fails on optimistic concurrency or the recount inside the transaction; behaviour is 409 either way. The executor must run the count inside the same transaction, not before it.
- **Refresh token replay after deactivation.** Access token still valid until expiry; refresh token was cleared, so no new access token can be minted. Access-token lifetime in `JwtOptions` must remain short (≤ 15 minutes); confirm the configured value in `Infrastructure/DependencyInjection.cs` before shipping.
- **Deactivated user still authenticates on refresh.** `RefreshTokenAsync` must re-check `IsActive`.
- **Assigning `Customer` alongside `Admin`.** Validator refuses combinations that conflict (Customer + any staff role) if the intake later demands it; for now, subset-of-known-roles is enough. Note it in a `// TODO(Story 02):` comment.
- **Empty roles list.** Validator rejects with 400.
- **RTL layout at 375 px.** Verify the DataTable's action column mirrors and does not overflow.
- **Unicode names.** `FullNameAr` includes diacritics; ensure `Search` uses a case-insensitive contains that works on Arabic in SQL Server (default collation is fine; do not `.ToLower()` in LINQ).

---

## Test Plan

Add xUnit tests to `backend/tests/CustomerSupportCRM.Application.Tests/`:

1. `IdentityServiceAdminTests.cs`
   - `DeactivateUser_Self_Throws409`
   - `DeactivateUser_LastAdmin_Throws409`
   - `DeactivateUser_ClearsRefreshToken_AndDisablesLogin`
   - `SetUserRoles_RemovingLastAdminRole_Throws409`
   - `SetUserRoles_DiffAppliesAddAndRemove`
   - `ReactivateUser_RestoresLogin_ButDoesNotIssueTokens`
   - `Login_InactiveUser_ReturnsGenericFailure` (no leak of the reason)
   - `CreateUser_InvalidPassword_SurfacesIdentityErrors_AsBadRequest`

2. `UserValidatorTests.cs`
   - `CreateUserRequest_RejectsUnknownRole`
   - `UpdateUserRequest_RequiresBilingualName`

Reuse the fixture pattern in `TestSupport/TestContext.cs`. Use `IdentityService` with EF InMemory the same way the customer tests do. Fake `ICurrentUser` via `TestSupport/Fakes.cs`.

Frontend: no unit tests currently ship — call out that manual smoke covers the three views at 375 px, LTR and RTL.

---

## Migration / Rollback

No schema change. `IsActive`, `RefreshToken`, `DepartmentId`, `BranchId` already exist on `ApplicationUser`.

Rollback: revert the controller registration and the front-end route; the domain and DB are untouched.

---

## Verification Steps

1. **Backend builds:** `dotnet build backend/CustomerSupportCRM.slnx`.
2. **Backend tests:** `dotnet test backend/CustomerSupportCRM.slnx` — all existing + new tests pass.
3. **Frontend builds:** `npm run build --prefix frontend`.
4. **Manual, backend:** with the seeded admin, `POST /api/users`, verify audit row exists via a raw SQL query on `AuditLogs` (Story 03 will add the UI).
5. **Manual, lockout guard:** attempt to deactivate the seeded admin → 409 with `Users.CannotDeactivateSelf`.
6. **Manual, sign-in denied:** deactivate a secondary account, attempt login → generic failure (no "inactive" leak); refresh token endpoint refuses.
7. **Regression:** all existing `[Authorize(Roles = …)]` endpoints continue to function under the seeded admin.

---

## Done Criteria

- [ ] Admin can list, search, create, edit, deactivate and reactivate users through the new endpoints and screens.
- [ ] Deactivating a user clears the refresh token in the same transaction and login is refused with a generic failure.
- [ ] The last remaining Admin cannot be deactivated or stripped of the Admin role.
- [ ] An admin cannot deactivate their own account.
- [ ] All user-facing strings exist in both `ar.json` and `en.json`; the screens mirror correctly at 375 px.
- [ ] `dotnet test` and `npm run build` both pass.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 02.**

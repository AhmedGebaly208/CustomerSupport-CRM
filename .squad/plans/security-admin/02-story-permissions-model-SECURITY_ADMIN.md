# Story 02 — Permission model + policy provider (Story: SECURITY_ADMIN)

## Prerequisites

- [Story 01 completed](./01-story-users-admin-SECURITY_ADMIN.md) — needed to exercise the model against real user administration.
- Coordinate with owners of every controller in `backend/src/CustomerSupportCRM.Api/Controllers/` — this story rewrites their `[Authorize(Roles = …)]` attributes and can silently open or close endpoints if done incorrectly.

---

## Story Goal

Introduce a permission model — named permissions grouped into roles — and
move `[Authorize]` checks off role names onto permissions, **without**
breaking the four shipped roles. The four roles (`Admin`, `Manager`,
`Agent`, `Customer`) map onto explicit permission sets; existing endpoints
authorise against permissions rather than role names.

Permissions are represented as claims on the JWT plus a policy provider,
so no extra DB round-trip per request. Because the token caches the
permission set, access-token lifetime must remain short so a revoked
permission cannot outlive it by more than that lifetime.

Not in scope: fully custom, admin-editable roles. This story hardcodes
the four role → permission mappings; a follow-up story can move them to
a table.

---

## Context — Read These Files First

1. `backend/src/CustomerSupportCRM.Application/Auth/Roles.cs` — the four role constants and the `Roles.Staff`/`Roles.Supervisory` groupings. Understand every current consumer.
2. `backend/src/CustomerSupportCRM.Infrastructure/Identity/IdentityService.cs` — the token issuance path; you will add permission claims here.
3. `backend/src/CustomerSupportCRM.Infrastructure/Identity/JwtOptions.cs` and `backend/src/CustomerSupportCRM.Infrastructure/DependencyInjection.cs` — access-token lifetime is set here. Confirm the value; if longer than 15 minutes, this story tightens it.
4. `backend/src/CustomerSupportCRM.Api/Program.cs` — `FallbackPolicy` setup. You will register the new `IAuthorizationPolicyProvider` alongside it.
5. Grep for `[Authorize(Roles ` in `backend/src/CustomerSupportCRM.Api/Controllers/` — enumerate every controller/method that needs migration.
6. Grep for `User.IsInRole(` and `IsInRoleAsync(` in `backend/src/` — anywhere role membership is checked in application code must migrate to a permission check.
7. `backend/src/CustomerSupportCRM.Application/Common/Interfaces/ICurrentUser.cs` — extend with a `HasPermission(string)` helper if not present.
8. `backend/src/CustomerSupportCRM.Api/Services/CurrentUser.cs` — the concrete implementation reading claims off `HttpContext.User`. Add the permission read here.
9. Sibling controllers (`CustomersController.cs`, `TicketsController.cs`, `LookupsController.cs`, `AuthController.cs`) for the current shape of `[Authorize]` usage.

---

## Product rules (from story)

- **Current:** `[Authorize(Roles = Roles.Staff)]` and similar; role names are the unit of check.
- **New:** `[Authorize(Policy = Permissions.Tickets.View)]` and similar; permission names are the unit of check.
- **Invariant during transition:** any endpoint that today grants access to a given role must still grant access to the mapped permission set for the same role. No endpoint gains or loses reachable users purely as a side effect of this refactor.

---

## Backend Tasks

### 1 — Permission catalog

**Create file:** `backend/src/CustomerSupportCRM.Application/Auth/Permissions.cs`

- Static, nested constants. Naming convention: `Area.Verb`, all string values `"area.verb"`.
- Cover, at minimum, the surfaces already shipped:
  - `Permissions.Users` — `View`, `Manage`.
  - `Permissions.Customers` — `View`, `Create`, `Edit`, `Delete`.
  - `Permissions.Tickets` — `View`, `Create`, `Edit`, `Assign`, `Comment`, `Close`.
  - `Permissions.Lookups` — `View`, `Manage`.
  - `Permissions.AuditLogs` — `View`.
  - `Permissions.SystemConfig` — `View`, `Manage`.
- Expose an `IReadOnlyList<string> All` for enumeration.

### 2 — Role → permission map

**Create file:** `backend/src/CustomerSupportCRM.Application/Auth/RolePermissions.cs`

- A dictionary from role name (from `Roles.cs`) to a `HashSet<string>` of permissions.
- The four mappings must reproduce today's behaviour. Executor: build the mapping by inspecting the grep output from context step 5:
  - Every endpoint currently reachable by `Roles.Admin` → its permission is in `Admin`'s set.
  - Same for `Manager`, `Agent`, `Customer`.
- Admin's set is the union of all permissions.

### 3 — Custom policy provider

**Create file:** `backend/src/CustomerSupportCRM.Api/Auth/PermissionAuthorizationPolicyProvider.cs`

- Inherits `DefaultAuthorizationPolicyProvider`.
- For any policy name that matches a permission (i.e. exists in `Permissions.All`), returns a policy that requires an authenticated user with a claim of type `"permission"` whose value equals the policy name.
- Falls back to `base.GetPolicyAsync` for the default and fallback policies.

**Create file:** `backend/src/CustomerSupportCRM.Api/Auth/PermissionRequirement.cs` + `PermissionAuthorizationHandler.cs`

- `PermissionRequirement(string permission)`.
- Handler succeeds when the user has a `"permission"` claim with that value.

**File:** `backend/src/CustomerSupportCRM.Api/Program.cs`

- Register `services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>()`.
- Register `services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>()`.
- Keep `FallbackPolicy` unchanged — endpoints without `[AllowAnonymous]` still require authentication.

### 4 — Emit permission claims on token issue

**File:** `backend/src/CustomerSupportCRM.Infrastructure/Identity/IdentityService.cs`

- In the method that assembles the JWT (search for `new JwtSecurityToken` / `SigningCredentials`), after resolving the user's roles, union `RolePermissions.For(role)` across all roles and add one `Claim("permission", p)` per permission.
- Keep the existing role claims for now — Story 05's refresh-token flow and any legacy checks may still consult them. A follow-up story removes them once every consumer is migrated.
- Ensure the same claims are added on refresh-token exchange.
- **Access-token lifetime:** if `JwtOptions.AccessTokenMinutes` (or the equivalent) exceeds 15, tighten it to ≤ 15 in `appsettings.json`. Document the reason in a code comment near the option binding: "Permission set is cached in the JWT; revocation cannot take effect until the token expires."

### 5 — Migrate `[Authorize(Roles = …)]` sites

For each controller/method captured in context step 5, change the attribute to `[Authorize(Policy = Permissions.<Area>.<Verb>)]`. The mapping must be documented at the top of each controller as a comment. Do not delete role-based attributes on `UsersController` from Story 01 in the same commit as this file rewrite; convert them together to `Permissions.Users.Manage` and `Permissions.Users.View`.

### 6 — `ICurrentUser` convenience

**File:** `backend/src/CustomerSupportCRM.Application/Common/Interfaces/ICurrentUser.cs`

- Add `bool HasPermission(string permission);`.

**File:** `backend/src/CustomerSupportCRM.Api/Services/CurrentUser.cs`

- Implement by checking `HttpContext.User.HasClaim("permission", permission)`.

Application services that today branch on `IsInRole` (grep and confirm) migrate to `HasPermission`.

---

## Frontend Tasks

### 1 — Auth store exposes permissions

**File:** `frontend/src/stores/auth.ts`

- Decode the JWT (there is already a decode step for `exp`; extend it) and store the array of `permission` claims. Do **not** store roles in a way that breaks — keep the existing role field in parallel until Story 04 depends on permissions alone.
- Expose a `hasPermission(name: string): boolean` getter.

### 2 — Router guard

**File:** `frontend/src/router/index.ts`

- Introduce `meta.permission?: string`. Update the global `beforeEach` to check `auth.hasPermission(to.meta.permission)` when set.
- Convert the Story 01 admin routes from `meta.roles` to `meta.permission = 'users.manage'`.

### 3 — Menu visibility

**File:** `frontend/src/layouts/AppLayout.vue`

- Hide nav entries whose route requires a permission the current user lacks.

---

## Edge Cases & Failure Modes

- **Silent open endpoint.** A migrated controller loses its `[Authorize]` accidentally. Mitigation: `FallbackPolicy` in `Program.cs` still enforces authentication; per-endpoint tests (see Test Plan) assert 403 for a role that should not have the permission.
- **Silent closed endpoint.** A migrated controller demands a permission the role does not have. Mitigation: the same per-endpoint tests assert 2xx for the role that should.
- **Token bloat.** A user with many roles carries many claims. Cap at a documented per-role permission set count; do not de-duplicate by hashing (breaks downstream consumers).
- **Revoked permission but active token.** Documented behaviour — permission remains effective until access-token expiry (≤ 15 minutes). Called out in the code comment on `AccessTokenMinutes`.
- **Old client on new server.** Client checks role names for menu visibility; server checks permissions. Story 02's frontend swaps the client to permissions in the same release — do not ship the backend alone.
- **`[Authorize]` with no policy.** After migration, any bare `[Authorize]` on an endpoint means "authenticated" — call the ambiguity out during code review; prefer an explicit policy.
- **Refresh path claims drift.** If the refresh-token exchange forgot to add permission claims, the second access token silently loses authorisation. The tests below assert it.

---

## Test Plan

Add xUnit tests to `backend/tests/CustomerSupportCRM.Application.Tests/`:

1. `PermissionPolicyTests.cs`
   - `RolePermissions_Admin_UnionOfAllOtherRoles` — property test-ish sanity.
   - `RolePermissions_Manager_MatchesLegacyManagerEndpoints` — assert against a static snapshot list to catch drift.
2. `IdentityService_TokenClaims_Tests.cs`
   - `Login_EmitsPermissionClaimsPerRole`
   - `Refresh_EmitsSamePermissionClaims_AsLogin`
3. `AuthorizationSmokeTests.cs` (integration-lite, using `WebApplicationFactory` if already available; otherwise a controller-level test with a fake `ClaimsPrincipal`).
   - For each migrated endpoint, assert 200 for a principal carrying the mapped permission and 403 for one without.

Extend one existing test: `TicketServiceTests.cs` or `CustomerServiceTests.cs` that today asserts role-based branching migrates to permission-based.

---

## Migration / Rollback

No schema change. Rollback = revert `Program.cs` registration, revert `IdentityService` claim emission, restore role-based `[Authorize]` attributes. Since roles keep being emitted on the JWT throughout, rollback is safe within a single deploy.

Deployment sequence:

1. Deploy backend with both role claims and permission claims on the JWT, and both `[Authorize(Roles=)]` and `[Authorize(Policy=)]` accepted — this is enforced automatically because the policy provider only handles known permission policy names; role attributes still work.
2. Deploy frontend using permissions.
3. Verify. Only then remove role checks in a follow-up.

---

## Verification Steps

1. **Backend builds:** `dotnet build backend/CustomerSupportCRM.slnx`.
2. **Backend tests:** `dotnet test backend/CustomerSupportCRM.slnx`.
3. **Manual:** decode the JWT after login as each seeded role; confirm the expected `permission` claims are present.
4. **Regression:** log in with the seeded administrator; every previously-reachable endpoint remains reachable.
5. **Negative:** issue a token for a Manager, hit an Admin-only endpoint → 403.
6. **Frontend builds:** `npm run build --prefix frontend`.
7. **Manual, frontend:** menu items for permissions the current user lacks are hidden; deep-linking to those routes redirects to a "not authorised" screen.

---

## Done Criteria

- [ ] `Permissions.cs` and `RolePermissions.cs` exist; the four role → permission mappings are explicit.
- [ ] `PermissionAuthorizationPolicyProvider` is registered; permission-name policies resolve correctly.
- [ ] Every previously-role-checked endpoint is now permission-checked, and the mapping is documented in a comment on each controller.
- [ ] The JWT carries `permission` claims on both login and refresh.
- [ ] Access-token lifetime is ≤ 15 minutes and the rationale is documented in code.
- [ ] `ICurrentUser.HasPermission` replaces every `IsInRole` in application services.
- [ ] Frontend router and menu drive off permissions.
- [ ] `dotnet test` and `npm run build` both pass; no endpoint's reachable-role set changes.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 03.**

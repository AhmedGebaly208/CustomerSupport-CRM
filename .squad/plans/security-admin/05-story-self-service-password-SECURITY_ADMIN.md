# Story 05 — Self-service password change (Story: SECURITY_ADMIN)

## Prerequisites

- [Story 02 completed](./02-story-permissions-model-SECURITY_ADMIN.md) — endpoint is authenticated; permission is not required (any signed-in user may change their own password).

---

## Story Goal

Any authenticated user can change their own password by supplying the
current password and a new password. On success, their refresh token is
invalidated so no other session can carry on with the old credentials.
The change is recorded in the audit trail by the existing
`AuditingInterceptor` (with `PasswordHash` redacted).

---

## Context — Read These Files First

1. `backend/src/CustomerSupportCRM.Infrastructure/Identity/IdentityService.cs` — reuse `UserManager.ChangePasswordAsync`; add the refresh-token invalidation immediately after it succeeds.
2. `backend/src/CustomerSupportCRM.Application/Common/Interfaces/IIdentityService.cs` — add the interface method.
3. `backend/src/CustomerSupportCRM.Application/Auth/Dtos/AuthDtos.cs` — precedent for auth DTOs.
4. `backend/src/CustomerSupportCRM.Api/Controllers/AuthController.cs` — the new endpoint lives here.
5. `backend/src/CustomerSupportCRM.Infrastructure/Persistence/Interceptors/AuditingInterceptor.cs` — confirm `PasswordHash` and `SecurityStamp` are already redacted.

---

## Backend Tasks

### 1 — DTO + validator

**File:** `backend/src/CustomerSupportCRM.Application/Auth/Dtos/AuthDtos.cs`

Add:

```csharp
public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
```

**Create file:** `backend/src/CustomerSupportCRM.Application/Auth/Validators/ChangePasswordRequestValidator.cs`

- Both fields required.
- `NewPassword != CurrentPassword`.
- Do not duplicate Identity's password policy — comment: "Enforced by UserManager."

### 2 — Interface + implementation

**File:** `backend/src/CustomerSupportCRM.Application/Common/Interfaces/IIdentityService.cs`

Add `Task ChangeOwnPasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct);`.

**File:** `backend/src/CustomerSupportCRM.Infrastructure/Identity/IdentityService.cs`

- Load the user by id (from `ICurrentUser.Id`).
- `UserManager.ChangePasswordAsync(user, currentPassword, newPassword)` — surface Identity failures as `BadRequestException` with the aggregated error messages (no leak of whether the current password was wrong vs the new one violated policy — return a generic "Auth.ChangePassword.Invalid" code, details in the ProblemDetails errors dictionary).
- On success: `user.RefreshToken = null; user.RefreshTokenExpiresAt = null;` — inside the same DbContext SaveChanges. `SecurityStamp` is already rotated by `UserManager` on password change.

### 3 — Controller endpoint

**File:** `backend/src/CustomerSupportCRM.Api/Controllers/AuthController.cs`

- `[Authorize] [HttpPost("change-password")]` — no permission required; `FallbackPolicy` guarantees authentication.
- Reads current user from `ICurrentUser.Id`; body is `ChangePasswordRequest`.
- Returns `NoContent()` on success.

---

## Frontend Tasks

### 1 — Service + type

**File:** `frontend/src/types/api.ts` — add `ChangePasswordRequest`.
**File:** `frontend/src/api/services.ts` — add `auth.changePassword(request)`.

### 2 — View

**Create file:** `frontend/src/views/profile/ChangePasswordView.vue`

- Three inputs: current password, new password, confirm new password. Confirm is client-side only.
- On success: show a success toast, sign the user out (clear the auth store, redirect to login), because their refresh token has been invalidated server-side.

### 3 — Router + menu

- Add `/profile/change-password` behind `meta.requiresAuth` (no permission).
- Add "Change password" under the current-user menu in `AppLayout.vue`.

### 4 — Localisation

- All labels, validation messages, and the "you will be signed out" hint in both `ar.json` and `en.json`.

---

## Edge Cases & Failure Modes

- **Wrong current password.** 400 with generic `Auth.ChangePassword.Invalid`; no distinction leaked over the wire.
- **New password fails policy.** Same generic code, error details in `ProblemDetails.errors`.
- **Concurrent sessions.** The other session's refresh token has been cleared — that session survives until access-token expiry (≤ 15 minutes) and then cannot refresh. Documented.
- **User is inactive (Story 01 flag).** Blocked earlier by `[Authorize]` since the deactivation flow clears the refresh token and login is denied; an already-signed-in inactive user can still hit this endpoint but the change is harmless.
- **RTL password fields.** Use `text-start` and `dir="ltr"` on the input itself so the password characters do not reorder in Arabic layout.
- **Audit trail.** The `Users` row's `Update` audit entry shows `PasswordHash` and `SecurityStamp` redacted; no plaintext leak. Test covers this.

---

## Test Plan

Add to `backend/tests/CustomerSupportCRM.Application.Tests/`:

1. `ChangePasswordTests.cs`
   - `ChangePassword_WrongCurrent_ReturnsGeneric400`
   - `ChangePassword_WeakNew_ReturnsGeneric400`
   - `ChangePassword_Success_ClearsRefreshToken`
   - `ChangePassword_Success_AuditRedactsPasswordHash`

---

## Migration / Rollback

No schema change. Rollback = revert the endpoint and view.

---

## Verification Steps

1. **Backend builds:** `dotnet build backend/CustomerSupportCRM.slnx`.
2. **Backend tests:** `dotnet test backend/CustomerSupportCRM.slnx`.
3. **Frontend builds:** `npm run build --prefix frontend`.
4. **Manual:** sign in as the seeded admin; change the password; the current session is signed out; re-log in with the new password succeeds; the old password is refused.
5. **Manual, second session:** open two browser sessions; change the password in one; the other cannot refresh its token after access-token expiry.
6. **Manual, audit:** `GET /api/audit-logs?entityType=Users&action=Updated` — the row for the change shows `PasswordHash` and `SecurityStamp` as the redaction sentinel.

---

## Done Criteria

- [ ] `POST /api/auth/change-password` requires authentication and validates the current password.
- [ ] Refresh token is cleared in the same transaction as the password change.
- [ ] Failure surface is generic (no oracle on which field failed).
- [ ] Audit trail contains an `Updated` row with redacted secrets.
- [ ] Frontend view is bilingual, RTL-correct, and usable at 375 px.
- [ ] `dotnet test` and `npm run build` both pass.

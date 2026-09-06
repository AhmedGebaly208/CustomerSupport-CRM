# Security and Administration — plan overview

Feature slug: `security-admin`
PDF area: 10
Tracker id: SECURITY_ADMIN (metadata only; tracker type = none)

Extends already-shipped infrastructure (ASP.NET Core Identity, JWT + rotating refresh
tokens, `AuditingInterceptor`, seeded roles) with user administration, a permission model,
a browsable audit log, runtime configuration, and self-service password change.

## Stories

| # | File | Title | Status |
|---|------|-------|--------|
| 01 | `01-story-users-admin-SECURITY_ADMIN.md` | User administration | **Implemented** |
| 02 | `02-story-permissions-model-SECURITY_ADMIN.md` | Permission model + policy provider | **Implemented** |
| 03 | `03-story-audit-log-browser-SECURITY_ADMIN.md` | Audit log browser | **Implemented** |
| 04 | `04-story-system-configuration-SECURITY_ADMIN.md` | System configuration | **Implemented** |
| 05 | `05-story-self-service-password-SECURITY_ADMIN.md` | Self-service password change | **Implemented** |

`01-story-security-and-administration.md` is the raw planner output; the five files above
were extracted from it so each can be attached to an executor session on its own.

## What landed

**01 — User administration.** `IIdentityService` gained the admin surface, implemented in
`IdentityService.Admin.cs`. `UsersController` at `/api/users` plus `GET /api/roles`.
Frontend `views/admin/users/`. Lockout rails hold: an admin cannot deactivate their own
account, and the last active administrator can neither be deactivated nor lose the Admin
role — both counted inside the transaction. Deactivation clears the refresh token; login
returns one indistinguishable message for unknown / wrong-password / deactivated.

**02 — Permission model.** `Permissions.cs` (21 permissions) and `RolePermissions.cs` map
the four roles onto sets that reproduce the previous reachability exactly. Permissions ride
the JWT as `permission` claims; `PermissionAuthorizationPolicyProvider` resolves a
permission name used as a policy name. **Every** `[Authorize(Roles = …)]` is gone, and the
`IsInRole` checks in `TicketsController` and `ScopeProvider` now key off permissions.
`CurrentUserDto` returns the resolved set so the UI cannot disagree with the API.

**03 — Audit log browser.** `AuditLogService` + `/api/audit-logs` (GET only — no write verb
exists, by design) and `views/admin/audit/AuditLogView.vue` with a JSON-diff dialog.
Migration `AuditLogFilterIndexes`.

**04 — System configuration.** `BusinessHours`, `Holiday`, `BrandingSetting`, `FeatureFlag`,
`ChannelToggle` entities; `SystemConfigService` behind `IConfigCache` with explicit
invalidation on write; `/api/system-config/**` plus `/api/branding`. Channel credentials are
write-only: never returned, preserved when the field is omitted, and redacted from the audit
trail. Migration `SystemConfiguration`; seeds a Sunday–Thursday working week.

**05 — Self-service password.** `POST /api/auth/change-password` +
`views/account/ChangePasswordView.vue`. Invalidates the refresh token; one generic failure
message. Access-token lifetime tightened 60 → 15 minutes, because deactivation and
permission revocation can only take effect when the current token expires.

## Deviations from the plan, and why

- **Story 02 asked the client to decode the JWT** for permission claims. There was no
  existing decode step to extend, so the server returns the resolved set on
  `CurrentUserDto` instead. Fewer moving parts and it cannot drift from the API's view.
- **Story 03 assumed `EntityType`/`CreatedAt`** on `AuditLog`; the shipped entity uses
  `EntityName`/`OccurredAt`. Implemented against the real schema.
- **Story 03's DTO had `UserFullNameAr`/`UserFullNameEn`.** The name lookup returns a single
  display name, so both fields would have been identical; collapsed to one `UserName`.
- **Stories 04 and 12 both specified branding.** Implemented once, as `BrandingSetting`
  under system configuration, rather than twice.

## Not done

- Admin-editable roles. The four role → permission mappings are hardcoded in
  `RolePermissions.cs`; a follow-up story can move them to a table without touching a
  single `[Authorize]` attribute.
- Audit-log retention/archival. Marked `TODO(retention)` on `AuditLog.cs` — the retention
  period is a business decision.
- Role claims still ride the JWT alongside permissions, because `DbSeeder` and the
  last-admin guard consult role membership directly. Removing them is a follow-up.

## Cross-feature consumers

- `sla-automation` reads business hours and holidays from story 04.
- `platform` reads branding from story 04.
- `customer-portal` must not grant `tickets.viewinternal` to `Roles.Customer`.
- Every feature authorises against `Permissions.*`, never a role name.

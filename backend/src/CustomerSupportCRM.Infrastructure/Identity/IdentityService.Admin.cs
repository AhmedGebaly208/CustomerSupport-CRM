using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Auth.Dtos;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Infrastructure.Identity;

/// <summary>User administration (PDF area 10). Split from the authentication half of
/// <see cref="IdentityService"/> to keep each file readable.
///
/// The invariant that matters most here: the system must never end up with zero enabled
/// administrators. Both routes to that state — deactivating the last admin and removing
/// the Admin role from the last admin — are blocked, and both checks run inside the same
/// transaction as the write so two concurrent admins cannot slip past them.</summary>
public sealed partial class IdentityService
{
    public async Task<PagedResult<UserAdminDto>> ListUsersAsync(
        UserListQuery query, CancellationToken cancellationToken = default)
    {
        RequireAuthenticatedCaller();

        var q = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // No ToLower(): SQL Server's default collation is already case-insensitive, and
            // lowering in LINQ both defeats the index and behaves oddly on Arabic.
            var term = query.Search.Trim();
            q = q.Where(u =>
                (u.Email != null && u.Email.Contains(term)) ||
                u.FullNameAr.Contains(term) ||
                u.FullNameEn.Contains(term));
        }

        if (query.DepartmentId is { } departmentId) q = q.Where(u => u.DepartmentId == departmentId);
        if (query.BranchId is { } branchId) q = q.Where(u => u.BranchId == branchId);
        if (query.IsActive is { } isActive) q = q.Where(u => u.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var roleName = query.Role.Trim();
            var userIds = from userRole in db.UserRoles
                          join role in db.Roles on userRole.RoleId equals role.Id
                          where role.Name == roleName
                          select userRole.UserId;

            q = q.Where(u => userIds.Contains(u.Id));
        }

        var total = await q.CountAsync(cancellationToken);

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "email" => query.SortDescending ? q.OrderByDescending(u => u.Email) : q.OrderBy(u => u.Email),
            "namear" => query.SortDescending ? q.OrderByDescending(u => u.FullNameAr) : q.OrderBy(u => u.FullNameAr),
            "nameen" => query.SortDescending ? q.OrderByDescending(u => u.FullNameEn) : q.OrderBy(u => u.FullNameEn),
            "createdat" => query.SortDescending ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
            _ => q.OrderBy(u => u.FullNameEn)
        };

        var rows = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(u => new UserRow(
                u.Id, u.Email, u.FullNameAr, u.FullNameEn, u.PreferredLanguage,
                u.DepartmentId, u.BranchId, u.IsActive, u.CreatedAt, u.LastLoginAt))
            .ToListAsync(cancellationToken);

        var items = await ToDtosAsync(rows, cancellationToken);
        return PagedResult<UserAdminDto>.Create(items, total, query.Page, query.PageSize);
    }

    public async Task<UserAdminDto> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireAuthenticatedCaller();

        var row = await userManager.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserRow(
                u.Id, u.Email, u.FullNameAr, u.FullNameEn, u.PreferredLanguage,
                u.DepartmentId, u.BranchId, u.IsActive, u.CreatedAt, u.LastLoginAt))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("User", userId);

        return (await ToDtosAsync([row], cancellationToken))[0];
    }

    public async Task<UserAdminDto> CreateUserAsync(
        CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        RequireAuthenticatedCaller();
        await GuardRolesExistAsync(request.Roles, cancellationToken);
        await GuardLookupsAsync(request.DepartmentId, request.BranchId, cancellationToken);

        var email = request.Email.Trim();

        if (await userManager.FindByEmailAsync(email) is not null)
            throw new ConflictException($"A user with the email '{email}' already exists.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullNameAr = request.FullNameAr.Trim(),
            FullNameEn = request.FullNameEn.Trim(),
            PreferredLanguage = NormalizeLanguage(request.PreferredLanguage),
            DepartmentId = request.DepartmentId,
            BranchId = request.BranchId,
            IsActive = true,
            CreatedAt = clock.UtcNow
        };

        // UserManager owns the password policy (configured in Infrastructure DI); reproducing
        // those rules in a validator would let the two drift apart.
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded) throw IdentityFailure(created);

        var addedRoles = await userManager.AddToRolesAsync(user, request.Roles.Distinct());
        if (!addedRoles.Succeeded)
        {
            // Roll back the half-created account rather than leaving a role-less user behind.
            await userManager.DeleteAsync(user);
            throw IdentityFailure(addedRoles);
        }

        return await GetUserAsync(user.Id, cancellationToken);
    }

    public async Task<UserAdminDto> UpdateUserAsync(
        Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        RequireAuthenticatedCaller();
        await GuardRolesExistAsync(request.Roles, cancellationToken);
        await GuardLookupsAsync(request.DepartmentId, request.BranchId, cancellationToken);

        await InTransactionAsync(async () =>
        {
            var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User", userId);

            user.FullNameAr = request.FullNameAr.Trim();
            user.FullNameEn = request.FullNameEn.Trim();
            user.PreferredLanguage = NormalizeLanguage(request.PreferredLanguage);
            user.DepartmentId = request.DepartmentId;
            user.BranchId = request.BranchId;

            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded) throw IdentityFailure(updated);

            await ApplyRolesAsync(user, request.Roles, cancellationToken);
        }, cancellationToken);

        return await GetUserAsync(userId, cancellationToken);
    }

    public async Task DeactivateUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var callerId = RequireAuthenticatedCaller();

        if (userId == callerId)
            throw new ConflictException("You cannot deactivate your own account.");

        await InTransactionAsync(async () =>
        {
            var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User", userId);

            if (!user.IsActive) return;

            // Counted inside the transaction: doing it before would let two admins each see
            // a count of two and both deactivate.
            if (await userManager.IsInRoleAsync(user, Roles.Admin)
                && await CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                throw new ConflictException("The last active administrator cannot be deactivated.");
            }

            user.IsActive = false;
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;

            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded) throw IdentityFailure(result);
        }, cancellationToken);
    }

    public async Task ReactivateUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireAuthenticatedCaller();

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        if (user.IsActive) return;

        user.IsActive = true;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) throw IdentityFailure(result);
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        RequireAuthenticatedCaller();

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        return (await userManager.GetRolesAsync(user)).ToList();
    }

    public async Task SetUserRolesAsync(
        Guid userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default)
    {
        RequireAuthenticatedCaller();
        await GuardRolesExistAsync(roleNames, cancellationToken);

        await InTransactionAsync(async () =>
        {
            var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User", userId);

            await ApplyRolesAsync(user, roleNames, cancellationToken);
        }, cancellationToken);
    }

    public async Task ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var callerId = RequireAuthenticatedCaller();

        if (userId != callerId)
            throw new ForbiddenException("You can only change your own password.");

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        // One generic message: distinguishing "current password wrong" from "new password too
        // weak" gives an attacker with a stolen session a way to confirm the old password.
        if (!result.Succeeded)
            throw new BadRequestException("The password could not be changed. Check the current password and the new password requirements.");

        // Changing a password signs other sessions out.
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        await userManager.UpdateAsync(user);
    }

    // ---- helpers ----

    /// <summary>Flat projection so the DTO mapping can resolve roles and lookups in bulk.</summary>
    private sealed record UserRow(
        Guid Id, string? Email, string FullNameAr, string FullNameEn, string PreferredLanguage,
        Guid? DepartmentId, Guid? BranchId, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

    private async Task<IReadOnlyList<UserAdminDto>> ToDtosAsync(
        IReadOnlyList<UserRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.Id).ToList();

        // One query for every user's roles rather than UserManager.GetRolesAsync per row.
        var roleMap = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where ids.Contains(userRole.UserId)
            select new { userRole.UserId, RoleName = role.Name! })
            .ToListAsync(cancellationToken);

        var rolesByUser = roleMap
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.RoleName).ToList());

        var departmentIds = rows.Where(r => r.DepartmentId.HasValue).Select(r => r.DepartmentId!.Value).Distinct().ToList();
        var branchIds = rows.Where(r => r.BranchId.HasValue).Select(r => r.BranchId!.Value).Distinct().ToList();

        var departments = await db.Departments.AsNoTracking()
            .Where(d => departmentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new { d.NameAr, d.NameEn }, cancellationToken);

        var branches = await db.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => new { b.NameAr, b.NameEn }, cancellationToken);

        return rows.Select(r =>
        {
            var department = r.DepartmentId is { } d && departments.TryGetValue(d, out var dep) ? dep : null;
            var branch = r.BranchId is { } b && branches.TryGetValue(b, out var br) ? br : null;

            return new UserAdminDto(
                r.Id,
                r.Email ?? string.Empty,
                r.FullNameAr,
                r.FullNameEn,
                r.PreferredLanguage,
                r.DepartmentId, department?.NameAr, department?.NameEn,
                r.BranchId, branch?.NameAr, branch?.NameEn,
                r.IsActive,
                rolesByUser.TryGetValue(r.Id, out var roles) ? roles : [],
                r.CreatedAt,
                r.LastLoginAt);
        }).ToList();
    }

    /// <summary>Applies a role diff, refusing any change that would remove the last admin.</summary>
    private async Task ApplyRolesAsync(
        ApplicationUser user, IReadOnlyList<string> desired, CancellationToken cancellationToken)
    {
        var target = desired.Distinct().ToList();
        var existing = await userManager.GetRolesAsync(user);

        var toAdd = target.Except(existing).ToList();
        var toRemove = existing.Except(target).ToList();

        if (toRemove.Count == 0 && toAdd.Count == 0) return;

        if (toRemove.Contains(Roles.Admin)
            && user.IsActive
            && await CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            throw new ConflictException("The last active administrator cannot lose the Admin role.");
        }

        if (toRemove.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded) throw IdentityFailure(removed);
        }

        if (toAdd.Count > 0)
        {
            var added = await userManager.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded) throw IdentityFailure(added);
        }
    }

    private async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        await (from userRole in db.UserRoles
               join role in db.Roles on userRole.RoleId equals role.Id
               join user in db.Users on userRole.UserId equals user.Id
               where role.Name == Roles.Admin && user.IsActive
               select user.Id)
            .Distinct()
            .CountAsync(cancellationToken);

    private async Task GuardRolesExistAsync(IReadOnlyList<string> roleNames, CancellationToken cancellationToken)
    {
        if (roleNames.Count == 0)
            throw new BadRequestException("At least one role must be assigned.");

        var unknown = roleNames.Distinct().Except(Roles.All).ToList();
        if (unknown.Count > 0)
            throw new BadRequestException($"Unknown role(s): {string.Join(", ", unknown)}.");

        var existing = await db.Roles
            .Where(r => r.Name != null && roleNames.Contains(r.Name))
            .Select(r => r.Name!)
            .ToListAsync(cancellationToken);

        var missing = roleNames.Distinct().Except(existing).ToList();
        if (missing.Count > 0)
            throw new BadRequestException($"Role(s) not present in the database: {string.Join(", ", missing)}.");
    }

    private async Task GuardLookupsAsync(Guid? departmentId, Guid? branchId, CancellationToken cancellationToken)
    {
        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, cancellationToken))
            throw new BadRequestException("The selected department does not exist.");

        if (branchId is { } b && !await db.Branches.AnyAsync(x => x.Id == b, cancellationToken))
            throw new BadRequestException("The selected branch does not exist.");
    }

    /// <summary>Runs a unit of work in a transaction that survives the retrying execution
    /// strategy configured for SQL Server.
    ///
    /// EnableRetryOnFailure refuses user-initiated transactions unless the whole transaction
    /// is wrapped in the strategy, so it can be replayed as one retriable unit. The delegate
    /// may therefore run more than once: it reloads its own state and the change tracker is
    /// cleared per attempt, so a replay starts from a clean slate rather than from the
    /// half-applied state of the failed attempt.</summary>
    private async Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await work();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private Guid RequireAuthenticatedCaller() =>
        currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

    private static string NormalizeLanguage(string? language) =>
        language is "en" ? "en" : "ar";

    private static AppException IdentityFailure(Microsoft.AspNetCore.Identity.IdentityResult result) =>
        new BadRequestException(string.Join(" ", result.Errors.Select(e => e.Description)));
}

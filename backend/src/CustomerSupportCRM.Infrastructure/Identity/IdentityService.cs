using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Auth.Dtos;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Tickets;
using CustomerSupportCRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomerSupportCRM.Infrastructure.Identity;

/// <summary>Authentication and agent lookup. User administration lives in the
/// <c>IdentityService.Admin.cs</c> part of this class.
///
/// Explicit fields rather than a primary constructor, so both parts of the partial class
/// reference the same dependencies unambiguously.</summary>
public sealed partial class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> userManager;
    private readonly AppDbContext db;
    private readonly ICurrentUser currentUser;
    private readonly IClock clock;
    private readonly JwtOptions _jwt;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        ICurrentUser currentUser,
        IOptions<JwtOptions> jwtOptions,
        IClock clock)
    {
        this.userManager = userManager;
        this.db = db;
        this.currentUser = currentUser;
        this.clock = clock;
        _jwt = jwtOptions.Value;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        // One message for "no such user", "wrong password" and "deactivated account", so the
        // endpoint cannot be used to enumerate which emails have accounts or which are
        // disabled. The administrator sees the real state in the admin screens.
        if (user is null
            || !user.IsActive
            || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            throw new ForbiddenException("Invalid email or password.");
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new ForbiddenException("Invalid refresh token.");

        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken, cancellationToken);

        if (user is null || user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt < clock.UtcNow)
            throw new ForbiddenException("The refresh token is invalid or has expired.");

        // An account deactivated after the token was issued must not be able to refresh.
        // Revoke on the way out so a stolen token is spent as well as refused.
        if (!user.IsActive)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await userManager.UpdateAsync(user);

            throw new ForbiddenException("The refresh token is invalid or has expired.");
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var roles = await userManager.GetRolesAsync(user);
        return ToDto(user, roles);
    }

    public async Task<IReadOnlyList<AgentDto>> GetAgentsAsync(Guid? departmentId, CancellationToken cancellationToken = default)
    {
        // Managers and admins work tickets too, so the assignable pool is all staff roles.
        var staffRoles = new[] { Roles.Admin, Roles.Manager, Roles.Agent };

        var staffIds = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where staffRoles.Contains(role.Name!)
            select userRole.UserId).Distinct().ToListAsync(cancellationToken);

        var q = userManager.Users.Where(u => staffIds.Contains(u.Id) && u.IsActive);
        if (departmentId is { } id)
            q = q.Where(u => u.DepartmentId == id);

        var users = await q
            .OrderBy(u => u.FullNameEn)
            .Select(u => new { u.Id, u.FullNameAr, u.FullNameEn, u.Email, u.DepartmentId })
            .ToListAsync(cancellationToken);

        var ids = users.Select(u => u.Id).ToList();
        var activeStatuses = TicketWorkflow.ActiveStatuses;

        // Workload counts drive the assignment picker, so a supervisor can see who is
        // already loaded before handing over another ticket.
        var openCounts = await db.Tickets
            .Where(t => t.AssignedAgentId != null
                        && ids.Contains(t.AssignedAgentId.Value)
                        && activeStatuses.Contains(t.Status))
            .GroupBy(t => t.AssignedAgentId!.Value)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, cancellationToken);

        return users
            .Select(u => new AgentDto(
                u.Id, u.FullNameAr, u.FullNameEn, u.Email ?? string.Empty, u.DepartmentId,
                openCounts.GetValueOrDefault(u.Id)))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(
        IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullNameEn, u.FullNameAr, u.Email })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            u => u.Id,
            u => Coalesce(u.FullNameEn, u.FullNameAr, u.Email) ?? u.Id.ToString());
    }

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, Coalesce(user.FullNameEn, user.FullNameAr, user.Email) ?? user.Id.ToString()),
            new("preferred_language", user.PreferredLanguage)
        };

        if (user.DepartmentId is { } departmentId)
            claims.Add(new Claim("department_id", departmentId.ToString()));

        if (user.BranchId is { } branchId)
            claims.Add(new Claim("branch_id", branchId.ToString()));

        if (user.CustomerId is { } customerId)
            claims.Add(new Claim("customer_id", customerId.ToString()));

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Permissions are the unit of authorization; the role claims stay for now because
        // DbSeeder and the last-admin guard still consult role membership directly.
        // Both login and refresh come through here, so a refreshed token picks up any role
        // change made in the meantime.
        claims.AddRange(RolePermissions.ForRoles(roles)
            .Select(permission => new Claim(Permissions.ClaimType, permission)));

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        // Rotate the refresh token on every issue so a leaked one is single-use.
        user.RefreshToken = GenerateRefreshToken();
        user.RefreshTokenExpiresAt = now.AddDays(_jwt.RefreshTokenDays);
        user.LastLoginAt = now;
        await userManager.UpdateAsync(user);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            user.RefreshToken,
            expiresAt,
            ToDto(user, roles));
    }

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static CurrentUserDto ToDto(ApplicationUser user, IList<string> roles) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.FullNameAr,
        user.FullNameEn,
        user.PreferredLanguage,
        user.DepartmentId,
        user.BranchId,
        roles.ToList(),
        RolePermissions.ForRoles(roles).OrderBy(permission => permission, StringComparer.Ordinal).ToList());
}

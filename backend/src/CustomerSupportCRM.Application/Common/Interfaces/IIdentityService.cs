using CustomerSupportCRM.Application.Auth.Dtos;
using CustomerSupportCRM.Application.Common.Models;

namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Authentication, agent lookup and user administration. Implemented over
/// ASP.NET Identity in Infrastructure so Application never references Identity types.</summary>
public interface IIdentityService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentDto>> GetAgentsAsync(Guid? departmentId, CancellationToken cancellationToken = default);

    /// <summary>Used when writing ticket history so the trail shows names, not raw GUIDs.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);

    // ---- Administration (area 10). Every method here requires an authenticated caller. ----

    Task<PagedResult<UserAdminDto>> ListUsersAsync(UserListQuery query, CancellationToken cancellationToken = default);
    Task<UserAdminDto> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserAdminDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserAdminDto> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Clears the refresh token in the same save, so the account cannot mint a new
    /// access token once the current short-lived one expires.</summary>
    Task DeactivateUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ReactivateUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Ids of the active users in a role. Used by the SLA escalation rules, which
    /// notify a role rather than named individuals so the rule survives staff changes.</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default);
    Task SetUserRolesAsync(Guid userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default);

    /// <summary>Self-service. Invalidates the caller's refresh token on success.</summary>
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>The caller behind the current request. Implemented in the API from the JWT
/// claims; stubbed in tests.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    string? Email { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }

    /// <summary>Role membership. Prefer <see cref="HasPermission"/> for authorization
    /// decisions; this remains for the few places that genuinely reason about roles.</summary>
    bool IsInRole(string role);

    /// <summary>The authorization check application code should use.</summary>
    bool HasPermission(string permission);

    /// <summary>Reads a single claim by type. Used for the department/branch scope claims
    /// so Application code never has to reach for HttpContext.</summary>
    string? FindClaim(string claimType);
}

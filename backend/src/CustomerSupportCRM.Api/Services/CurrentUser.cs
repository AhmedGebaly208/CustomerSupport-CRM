using System.Security.Claims;
using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Interfaces;

namespace CustomerSupportCRM.Api.Services;

/// <summary>Reads the caller's identity from the JWT claims on the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(Permissions.ClaimType, permission) ?? false;

    public string? FindClaim(string claimType) => Principal?.FindFirstValue(claimType);
}

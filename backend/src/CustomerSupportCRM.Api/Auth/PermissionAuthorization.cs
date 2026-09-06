using CustomerSupportCRM.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CustomerSupportCRM.Api.Auth;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>Turns a permission name used as a policy name into a real policy on demand, so
/// controllers can write <c>[Authorize(Policy = Permissions.Tickets.View)]</c> without every
/// permission being registered by hand in Program.cs.
///
/// Only names in <see cref="Permissions.All"/> are recognised. Anything else falls through to
/// the default provider, which means a typo in a policy name produces a hard authorization
/// failure rather than an accidentally open endpoint.</summary>
public sealed class PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    private static readonly HashSet<string> KnownPermissions = new(Permissions.All, StringComparer.Ordinal);

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // An explicitly registered policy of the same name wins, so a future story can
        // override one permission's policy without changing the attribute.
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null) return existing;

        if (!KnownPermissions.Contains(policyName)) return null;

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}

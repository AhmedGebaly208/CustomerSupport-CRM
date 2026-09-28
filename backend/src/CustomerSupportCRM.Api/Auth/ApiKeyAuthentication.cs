using System.Security.Claims;
using System.Text.Encodings.Web;
using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Integrations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CustomerSupportCRM.Api.Auth;

/// <summary>Authenticates a request carrying an API key (PDF area 11).
///
/// A second scheme alongside JWT rather than a bearer token in disguise. An integration is
/// not a person: it has no refresh token, no roles, and a scope list an administrator chose
/// rather than a role's worth of permissions. Keeping the schemes separate is what lets a
/// key read tickets without inheriting everything an agent can do.</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService keys) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No header is "not this scheme", not a failure. Returning NoResult lets the JWT
        // scheme handle an ordinary user request on the same endpoint.
        if (!Request.Headers.TryGetValue(HeaderName, out var header)) return AuthenticateResult.NoResult();

        var presented = header.ToString();
        if (string.IsNullOrWhiteSpace(presented)) return AuthenticateResult.NoResult();

        var key = await keys.AuthenticateAsync(presented, Context.RequestAborted);

        // One message for an unknown prefix, a wrong secret, a revoked key and an expired
        // one, so the response cannot be used to work out which part was right.
        if (key is null) return AuthenticateResult.Fail("Invalid API key.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new(ClaimTypes.Name, key.Name),
            new(ApiKeyClaims.KeyId, key.Id.ToString())
        };

        // Scopes become permission claims, so [Authorize(Permissions.X)] works unchanged for
        // both a signed-in agent and an integration.
        claims.AddRange(key.Scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(scope => new Claim(Permissions.ClaimType, scope)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

public static class ApiKeyClaims
{
    /// <summary>Marks a principal as an integration rather than a person. Code that must
    /// behave differently for the two reads this instead of guessing from the absence of a
    /// user id.</summary>
    public const string KeyId = "api_key_id";
}

/// <summary>Reads the authenticated key out of the current request.</summary>
public sealed class ApiKeyContext(IHttpContextAccessor accessor) : IApiKeyContext
{
    public Guid? KeyId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(ApiKeyClaims.KeyId)?.Value, out var id)
            ? id
            : null;

    public IReadOnlySet<string> Scopes =>
        accessor.HttpContext?.User
            .FindAll(Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>();

    public bool IsApiKeyRequest => KeyId is not null;
}

namespace CustomerSupportCRM.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CustomerSupportCRM";
    public string Audience { get; set; } = "CustomerSupportCRM.Client";

    /// <summary>Signing key. Must be supplied per-environment (user-secrets, environment
    /// variable, or key vault) — never committed. Validated at startup.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Kept short on purpose: deactivating a user revokes their refresh token,
    /// but an access token already in the wild stays valid until it expires. This is the
    /// upper bound on how long a deactivated account keeps working.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

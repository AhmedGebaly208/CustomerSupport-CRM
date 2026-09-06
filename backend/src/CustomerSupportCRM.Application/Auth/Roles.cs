namespace CustomerSupportCRM.Application.Auth;

/// <summary>Role names as constants — string literals scattered across [Authorize]
/// attributes are the classic source of silent authorization holes (area 10).</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Agent = "Agent";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Manager, Agent, Customer];

    /// <summary>Everyone who works tickets from the inside. Excludes portal customers.</summary>
    public const string Staff = $"{Admin},{Manager},{Agent}";

    /// <summary>Roles allowed to configure the system and read the audit trail.</summary>
    public const string Supervisory = $"{Admin},{Manager}";
}

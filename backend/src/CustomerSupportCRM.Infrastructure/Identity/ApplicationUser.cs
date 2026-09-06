using Microsoft.AspNetCore.Identity;

namespace CustomerSupportCRM.Infrastructure.Identity;

/// <summary>Agents, managers, admins and portal customers all live in this one table;
/// roles decide what each can do (area 10). Kept in Infrastructure so the Domain layer
/// stays free of the ASP.NET Identity dependency.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string FullNameAr { get; set; } = string.Empty;
    public string FullNameEn { get; set; } = string.Empty;

    public string PreferredLanguage { get; set; } = "ar";

    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }

    /// <summary>Set for portal logins, linking back to the CRM customer record (area 8).</summary>
    public Guid? CustomerId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Opaque refresh token. Rotated on every refresh so a stolen token is
    /// usable at most once.</summary>
    public string? RefreshToken { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string name) : base(name) { }

    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
}

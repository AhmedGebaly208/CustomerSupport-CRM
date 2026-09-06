using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>One day of the working week (PDF area 10 "System configuration").
///
/// The SLA story measures response and resolution targets against these hours rather than
/// wall-clock time, so a ticket raised on Friday afternoon is not reported as breached by
/// Monday morning. Exactly seven rows, one per day, seeded on first boot.</summary>
public class BusinessHours : AuditableEntity
{
    public DayOfWeek Day { get; set; }

    /// <summary>Null on a non-working day.</summary>
    public TimeSpan? OpenAt { get; set; }
    public TimeSpan? CloseAt { get; set; }

    public bool IsWorkingDay { get; set; }
}

/// <summary>A non-working date that overrides the weekly pattern.</summary>
public class Holiday : AuditableEntity
{
    public DateOnly Date { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
}

/// <summary>Runtime branding (PDF area 12 "Custom branding").
///
/// A single row. Modelled as a normal entity rather than a static config file so changes
/// participate in the audit trail and can be made without a redeploy. The values feed the
/// CSS custom properties already declared on <c>:root</c> in the frontend, so no component
/// styles need to change.</summary>
public class BrandingSetting : AuditableEntity
{
    public string CompanyNameAr { get; set; } = string.Empty;
    public string CompanyNameEn { get; set; } = string.Empty;

    public string? LogoUrl { get; set; }

    /// <summary>Hex colour, e.g. <c>#0f766e</c>.</summary>
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }

    /// <summary>Locale a first-time visitor sees before they choose one.</summary>
    public string DefaultLocale { get; set; } = "ar";
}

/// <summary>An on/off switch an administrator can flip without a redeploy. Used by the AI
/// and channel stories so a provider outage can be contained by disabling one feature.</summary>
public class FeatureFlag : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
}

/// <summary>Per-channel enablement (PDF area 3).
///
/// Credentials are write-only: the service never returns <see cref="ApiKey"/> or
/// <see cref="ApiSecret"/>, and the audit interceptor redacts them. A configuration screen
/// shows only whether a secret is set, never its value.</summary>
public class ChannelToggle : AuditableEntity
{
    public CommunicationChannel Channel { get; set; }
    public bool IsEnabled { get; set; }

    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }

    /// <summary>Provider endpoint or sender identity; not secret, so it is safe to return.</summary>
    public string? Endpoint { get; set; }
}

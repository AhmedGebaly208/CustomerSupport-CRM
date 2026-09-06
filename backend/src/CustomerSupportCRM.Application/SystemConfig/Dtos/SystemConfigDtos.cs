using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.SystemConfig.Dtos;

public sealed record BusinessHoursDto(DayOfWeek Day, bool IsWorkingDay, TimeSpan? OpenAt, TimeSpan? CloseAt);

public sealed record SaveBusinessHoursRequest(IReadOnlyList<BusinessHoursDto> Days);

public sealed record HolidayDto(Guid Id, DateOnly Date, string NameAr, string NameEn);

public sealed record SaveHolidayRequest(DateOnly Date, string NameAr, string NameEn);

/// <summary>Branding as the SPA consumes it. Safe to serve unauthenticated: it carries no
/// customer data, and the login screen needs it before anyone signs in.</summary>
public sealed record BrandingDto(
    string CompanyNameAr,
    string CompanyNameEn,
    string? LogoUrl,
    string? PrimaryColor,
    string? SecondaryColor,
    string DefaultLocale);

public sealed record UpdateBrandingRequest(
    string CompanyNameAr,
    string CompanyNameEn,
    string? LogoUrl,
    string? PrimaryColor,
    string? SecondaryColor,
    string DefaultLocale);

public sealed record FeatureFlagDto(Guid Id, string Key, bool IsEnabled, string? DescriptionAr, string? DescriptionEn);

public sealed record SaveFeatureFlagRequest(string Key, bool IsEnabled, string? DescriptionAr, string? DescriptionEn);

/// <summary>Channel state. <c>HasCredentials</c> replaces the secret values: an admin can see
/// that a key is configured, but no endpoint ever returns the key itself.</summary>
public sealed record ChannelToggleDto(
    Guid Id,
    CommunicationChannel Channel,
    bool IsEnabled,
    string? Endpoint,
    bool HasCredentials);

/// <summary>Null credential fields mean "leave unchanged"; empty string means "clear".
/// Without that distinction, saving the form would wipe a secret the admin never touched.</summary>
public sealed record UpdateChannelToggleRequest(
    bool IsEnabled,
    string? Endpoint,
    string? ApiKey,
    string? ApiSecret);

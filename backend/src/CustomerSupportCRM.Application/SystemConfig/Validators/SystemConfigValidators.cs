using System.Text.RegularExpressions;
using CustomerSupportCRM.Application.SystemConfig.Dtos;
using FluentValidation;

namespace CustomerSupportCRM.Application.SystemConfig.Validators;

public sealed class UpdateBrandingRequestValidator : AbstractValidator<UpdateBrandingRequest>
{
    // #rgb, #rrggbb or #rrggbbaa — the forms a CSS custom property accepts.
    private static readonly Regex HexColor = new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public UpdateBrandingRequestValidator()
    {
        RuleFor(x => x.CompanyNameAr).NotEmpty().MaximumLength(120);
        RuleFor(x => x.CompanyNameEn).NotEmpty().MaximumLength(120);

        RuleFor(x => x.PrimaryColor)
            .Must(BeHexColor)
            .When(x => !string.IsNullOrWhiteSpace(x.PrimaryColor))
            .WithMessage("Primary colour must be a hex value such as #0f766e.");

        RuleFor(x => x.SecondaryColor)
            .Must(BeHexColor)
            .When(x => !string.IsNullOrWhiteSpace(x.SecondaryColor))
            .WithMessage("Secondary colour must be a hex value such as #0f766e.");

        RuleFor(x => x.LogoUrl)
            .MaximumLength(500)
            .Must(BeSafeLogoUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.LogoUrl))
            .WithMessage("The logo must be an https URL or a path under /uploads/.");

        RuleFor(x => x.DefaultLocale)
            .Must(locale => locale is "ar" or "en")
            .WithMessage("Default locale must be 'ar' or 'en'.");
    }

    private static bool BeHexColor(string? value) => value is not null && HexColor.IsMatch(value);

    /// <summary>The logo URL is injected into an img src on every page, including the
    /// anonymous login screen. Restricting it to https or a local upload path keeps an
    /// admin-supplied value from becoming a javascript: or data: vector.</summary>
    private static bool BeSafeLogoUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (value.StartsWith("/uploads/", StringComparison.Ordinal)) return true;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps;
    }
}

public sealed class SaveBusinessHoursRequestValidator : AbstractValidator<SaveBusinessHoursRequest>
{
    public SaveBusinessHoursRequestValidator()
    {
        RuleFor(x => x.Days).NotEmpty();

        // All seven days must be present: a partial save would silently leave a day at its
        // previous value, and the SLA calendar would disagree with what the admin saw.
        RuleFor(x => x.Days)
            .Must(days => days.Select(d => d.Day).Distinct().Count() == 7)
            .WithMessage("All seven days of the week must be supplied.");

        RuleForEach(x => x.Days).ChildRules(day =>
        {
            day.RuleFor(d => d.OpenAt)
                .NotNull()
                .When(d => d.IsWorkingDay)
                .WithMessage("A working day needs an opening time.");

            day.RuleFor(d => d.CloseAt)
                .NotNull()
                .When(d => d.IsWorkingDay)
                .WithMessage("A working day needs a closing time.");

            day.RuleFor(d => d.CloseAt)
                .GreaterThan(d => d.OpenAt)
                .When(d => d.IsWorkingDay && d.OpenAt.HasValue && d.CloseAt.HasValue)
                .WithMessage("Closing time must be after opening time.");
        });
    }
}

public sealed class SaveHolidayRequestValidator : AbstractValidator<SaveHolidayRequest>
{
    public SaveHolidayRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Date).NotEmpty();
    }
}

public sealed class SaveFeatureFlagRequestValidator : AbstractValidator<SaveFeatureFlagRequest>
{
    public SaveFeatureFlagRequestValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z0-9]+([.-][a-z0-9]+)*$")
            .WithMessage("Feature flag keys are lowercase, dot- or dash-separated, e.g. 'ai.suggested-replies'.");

        RuleFor(x => x.DescriptionAr).MaximumLength(300);
        RuleFor(x => x.DescriptionEn).MaximumLength(300);
    }
}

public sealed class UpdateChannelToggleRequestValidator : AbstractValidator<UpdateChannelToggleRequest>
{
    public UpdateChannelToggleRequestValidator()
    {
        RuleFor(x => x.Endpoint).MaximumLength(500);
        RuleFor(x => x.ApiKey).MaximumLength(500);
        RuleFor(x => x.ApiSecret).MaximumLength(500);
    }
}

using CustomerSupportCRM.Application.Auth.Dtos;
using FluentValidation;

namespace CustomerSupportCRM.Application.Auth.Validators;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);

        // Length only. The strength rules (digit, upper, lower, symbol) belong to
        // UserManager's configured IdentityOptions — restating them here would let the two
        // definitions drift apart and produce contradictory error messages.
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);

        RuleFor(x => x.FullNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FullNameEn).NotEmpty().MaximumLength(200);

        RuleFor(x => x.PreferredLanguage)
            .Must(BeSupportedLanguage)
            .When(x => !string.IsNullOrWhiteSpace(x.PreferredLanguage))
            .WithMessage("Preferred language must be 'ar' or 'en'.");

        RuleFor(x => x.Roles).NotEmpty().WithMessage("At least one role must be assigned.");
        RuleForEach(x => x.Roles).Must(BeKnownRole).WithMessage("'{PropertyValue}' is not a known role.");
        RuleFor(x => x.Roles).Must(NotMixPortalAndStaff)
            .WithMessage("A portal Customer cannot also hold a staff role.");
    }

    internal static bool BeSupportedLanguage(string? language) => language is "ar" or "en";

    internal static bool BeKnownRole(string role) => Roles.All.Contains(role);

    /// <summary>A portal customer must not also be staff: the portal scopes every query to
    /// the caller's own customer record, and a staff role would widen that unintentionally.</summary>
    internal static bool NotMixPortalAndStaff(IReadOnlyList<string> roles)
    {
        var hasCustomer = roles.Contains(Roles.Customer);
        var hasStaff = roles.Any(r => r is Roles.Admin or Roles.Manager or Roles.Agent);
        return !(hasCustomer && hasStaff);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FullNameEn).NotEmpty().MaximumLength(200);

        RuleFor(x => x.PreferredLanguage)
            .Must(CreateUserRequestValidator.BeSupportedLanguage)
            .When(x => !string.IsNullOrWhiteSpace(x.PreferredLanguage))
            .WithMessage("Preferred language must be 'ar' or 'en'.");

        RuleFor(x => x.Roles).NotEmpty().WithMessage("At least one role must be assigned.");
        RuleForEach(x => x.Roles).Must(CreateUserRequestValidator.BeKnownRole)
            .WithMessage("'{PropertyValue}' is not a known role.");
        RuleFor(x => x.Roles).Must(CreateUserRequestValidator.NotMixPortalAndStaff)
            .WithMessage("A portal Customer cannot also hold a staff role.");
    }
}

public sealed class SetUserRolesRequestValidator : AbstractValidator<SetUserRolesRequest>
{
    public SetUserRolesRequestValidator()
    {
        RuleFor(x => x.Roles).NotEmpty().WithMessage("At least one role must be assigned.");
        RuleForEach(x => x.Roles).Must(CreateUserRequestValidator.BeKnownRole)
            .WithMessage("'{PropertyValue}' is not a known role.");
        RuleFor(x => x.Roles).Must(CreateUserRequestValidator.NotMixPortalAndStaff)
            .WithMessage("A portal Customer cannot also hold a staff role.");
    }
}

public sealed class UserListQueryValidator : AbstractValidator<UserListQuery>
{
    public UserListQueryValidator()
    {
        // Page and PageSize are clamped by PagedQuery, so they need no rule here.
        RuleFor(x => x.Role)
            .Must(role => CreateUserRequestValidator.BeKnownRole(role!))
            .When(x => !string.IsNullOrWhiteSpace(x.Role))
            .WithMessage("Unknown role filter.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must differ from the current one.");
    }
}

using CustomerSupportCRM.Application.Customers.Dtos;
using FluentValidation;

namespace CustomerSupportCRM.Application.Customers.Validators;

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FullNameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.WhatsAppNumber).MaximumLength(32);
        RuleFor(x => x.CompanyName).MaximumLength(200);
        RuleFor(x => x.NationalId).MaximumLength(32);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.PreferredLanguage)
            .Must(BeSupportedLanguage)
            .When(x => !string.IsNullOrWhiteSpace(x.PreferredLanguage))
            .WithMessage("Preferred language must be 'ar' or 'en'.");

        RuleForEach(x => x.Contacts).SetValidator(new SaveCustomerContactRequestValidator());

        // At least one way to reach the customer, or the record is useless to the desk.
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Email)
                       || !string.IsNullOrWhiteSpace(x.Phone)
                       || !string.IsNullOrWhiteSpace(x.WhatsAppNumber)
                       || (x.Contacts?.Count ?? 0) > 0)
            .WithName("contact")
            .WithMessage("Provide at least one contact method: email, phone, WhatsApp, or a contact entry.");
    }

    internal static bool BeSupportedLanguage(string? language) =>
        language is "ar" or "en";
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FullNameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.WhatsAppNumber).MaximumLength(32);
        RuleFor(x => x.CompanyName).MaximumLength(200);
        RuleFor(x => x.NationalId).MaximumLength(32);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.PreferredLanguage)
            .Must(CreateCustomerRequestValidator.BeSupportedLanguage)
            .When(x => !string.IsNullOrWhiteSpace(x.PreferredLanguage))
            .WithMessage("Preferred language must be 'ar' or 'en'.");

        RuleForEach(x => x.Contacts).SetValidator(new SaveCustomerContactRequestValidator());
    }
}

public sealed class SaveCustomerContactRequestValidator : AbstractValidator<SaveCustomerContactRequest>
{
    public SaveCustomerContactRequestValidator()
    {
        RuleFor(x => x.Value).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Label).MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public sealed class CreateCustomerNoteRequestValidator : AbstractValidator<CreateCustomerNoteRequest>
{
    public CreateCustomerNoteRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}

public sealed class CreateInteractionRequestValidator : AbstractValidator<CreateInteractionRequest>
{
    public CreateInteractionRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Subject).MaximumLength(300);
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Direction).IsInEnum();
        // Backdating is expected (an agent logging yesterday's call); future-dating is not.
        RuleFor(x => x.OccurredAt)
            .Must(at => at!.Value <= DateTimeOffset.UtcNow.AddMinutes(5))
            .When(x => x.OccurredAt.HasValue)
            .WithMessage("An interaction cannot be recorded in the future.");
    }
}

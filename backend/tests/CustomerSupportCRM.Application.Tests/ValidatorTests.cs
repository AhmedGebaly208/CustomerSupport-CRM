using CustomerSupportCRM.Application.Customers.Dtos;
using CustomerSupportCRM.Application.Customers.Validators;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Application.Tickets.Validators;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Tests;

public class CustomerValidatorTests
{
    private readonly CreateCustomerRequestValidator _validator = new();

    private static CreateCustomerRequest Valid() =>
        new("شركة أكمي", "Acme Ltd", "acme@example.com", null, null, null, null, null, "ar", null, null, null);

    [Fact]
    public void A_complete_request_passes() =>
        Assert.True(_validator.Validate(Valid()).IsValid);

    [Fact]
    public void Both_name_languages_are_required()
    {
        Assert.False(_validator.Validate(Valid() with { FullNameAr = "" }).IsValid);
        Assert.False(_validator.Validate(Valid() with { FullNameEn = "  " }).IsValid);
    }

    [Fact]
    public void A_malformed_email_is_rejected() =>
        Assert.False(_validator.Validate(Valid() with { Email = "not-an-email" }).IsValid);

    [Fact]
    public void At_least_one_contact_method_is_required()
    {
        var noContactMethod = Valid() with { Email = null, Phone = null, WhatsAppNumber = null, Contacts = null };

        var result = _validator.Validate(noContactMethod);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one contact method"));
    }

    [Fact]
    public void A_contact_row_alone_satisfies_the_contact_requirement()
    {
        var request = Valid() with
        {
            Email = null,
            Phone = null,
            WhatsAppNumber = null,
            Contacts = [new SaveCustomerContactRequest(ContactType.Mobile, "0500000000", null, true)]
        };

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Only_ar_and_en_are_accepted_as_the_preferred_language()
    {
        Assert.True(_validator.Validate(Valid() with { PreferredLanguage = "en" }).IsValid);
        Assert.False(_validator.Validate(Valid() with { PreferredLanguage = "fr" }).IsValid);
    }
}

public class InteractionValidatorTests
{
    private readonly CreateInteractionRequestValidator _validator = new();

    [Fact]
    public void Backdating_an_interaction_is_allowed()
    {
        var request = new CreateInteractionRequest(
            CommunicationChannel.Phone, InteractionDirection.Inbound, "Call", "Body",
            DateTimeOffset.UtcNow.AddDays(-1), null);

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Future_dating_an_interaction_is_rejected()
    {
        var request = new CreateInteractionRequest(
            CommunicationChannel.Phone, InteractionDirection.Inbound, "Call", "Body",
            DateTimeOffset.UtcNow.AddDays(1), null);

        Assert.False(_validator.Validate(request).IsValid);
    }
}

public class TicketValidatorTests
{
    private readonly CreateTicketRequestValidator _validator = new();

    private static CreateTicketRequest Valid() =>
        new(Guid.NewGuid(), "Cannot sign in", "The portal rejects my password.",
            TicketPriority.Normal, CommunicationChannel.Email, null, null, null, null);

    [Fact]
    public void A_complete_request_passes() =>
        Assert.True(_validator.Validate(Valid()).IsValid);

    [Fact]
    public void A_customer_is_required() =>
        Assert.False(_validator.Validate(Valid() with { CustomerId = Guid.Empty }).IsValid);

    [Fact]
    public void Subject_and_description_are_required()
    {
        Assert.False(_validator.Validate(Valid() with { Subject = "" }).IsValid);
        Assert.False(_validator.Validate(Valid() with { Description = "" }).IsValid);
    }

    [Fact]
    public void An_out_of_range_priority_is_rejected() =>
        Assert.False(_validator.Validate(Valid() with { Priority = (TicketPriority)99 }).IsValid);

    [Fact]
    public void An_overlong_subject_is_rejected() =>
        Assert.False(_validator.Validate(Valid() with { Subject = new string('x', 301) }).IsValid);
}

public class StatusChangeValidatorTests
{
    private readonly ChangeTicketStatusRequestValidator _validator = new();

    [Fact]
    public void An_undefined_status_value_is_rejected() =>
        Assert.False(_validator.Validate(new ChangeTicketStatusRequest((TicketStatus)42, null)).IsValid);

    [Fact]
    public void A_note_is_optional() =>
        Assert.True(_validator.Validate(new ChangeTicketStatusRequest(TicketStatus.Open, null)).IsValid);
}

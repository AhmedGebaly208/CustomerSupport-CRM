using CustomerSupportCRM.Application.Tickets.Dtos;
using FluentValidation;

namespace CustomerSupportCRM.Application.Tickets.Validators;

public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum();
    }
}

public sealed class UpdateTicketRequestValidator : AbstractValidator<UpdateTicketRequest>
{
    public UpdateTicketRequestValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public sealed class ChangeTicketStatusRequestValidator : AbstractValidator<ChangeTicketStatusRequest>
{
    public ChangeTicketStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class AssignTicketRequestValidator : AbstractValidator<AssignTicketRequest>
{
    public AssignTicketRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class CreateTicketCommentRequestValidator : AbstractValidator<CreateTicketCommentRequest>
{
    public CreateTicketCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(8000);
    }
}

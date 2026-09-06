using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportCRM.Application.Tickets.Dtos;
using FluentValidation;

namespace CustomerSupportCRM.Application.Tickets.Validators;

/// <summary>Shared rules for the three bulk requests. A cap and a duplicate check keep one
/// careless selection from turning into a thousand-row transaction.</summary>
internal static class BulkRules
{
    public const int MaxItems = 200;

    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>> ValidTicketIds<T>(
        this IRuleBuilder<T, IReadOnlyList<Guid>> rule) =>
        rule.NotEmpty().WithMessage("Select at least one ticket.")
            .Must(ids => ids.Count <= MaxItems)
            .WithMessage($"At most {MaxItems} tickets can be changed in one operation.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The selection contains duplicate tickets.")
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("The selection contains an empty ticket id.");
}

public sealed class BulkAssignRequestValidator : AbstractValidator<BulkAssignRequest>
{
    public BulkAssignRequestValidator()
    {
        RuleFor(x => x.TicketIds).ValidTicketIds();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class BulkPriorityRequestValidator : AbstractValidator<BulkPriorityRequest>
{
    public BulkPriorityRequestValidator()
    {
        RuleFor(x => x.TicketIds).ValidTicketIds();
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public sealed class BulkStatusRequestValidator : AbstractValidator<BulkStatusRequest>
{
    public BulkStatusRequestValidator()
    {
        RuleFor(x => x.TicketIds).ValidTicketIds();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class CreateTicketLinkRequestValidator : AbstractValidator<CreateTicketLinkRequest>
{
    public CreateTicketLinkRequestValidator()
    {
        RuleFor(x => x.TargetTicketId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
    }
}

public sealed class MergeTicketRequestValidator : AbstractValidator<MergeTicketRequest>
{
    public MergeTicketRequestValidator()
    {
        RuleFor(x => x.TargetTicketId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class AddWatcherRequestValidator : AbstractValidator<AddWatcherRequest>
{
    public AddWatcherRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class AddTagRequestValidator : AbstractValidator<AddTagRequest>
{
    private static readonly Regex HexColor =
        new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public AddTagRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(64)
            .Must(name => !name.Contains(',') && !name.Contains(';'))
            .WithMessage("A tag name cannot contain a comma or semicolon.");

        RuleFor(x => x.ColorHex)
            .Must(c => HexColor.IsMatch(c!))
            .When(x => !string.IsNullOrWhiteSpace(x.ColorHex))
            .WithMessage("Colour must be a hex value such as #7c3aed.");
    }
}

public sealed class ChangeEscalationRequestValidator : AbstractValidator<ChangeEscalationRequest>
{
    public ChangeEscalationRequestValidator()
    {
        RuleFor(x => x.Delta)
            .Must(d => d is 1 or -1)
            .WithMessage("Escalation moves one level at a time: send +1 or -1.");

        // Mandatory: an escalation with no stated cause cannot be reviewed afterwards.
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}

public sealed class CategoryUpsertRequestValidator : AbstractValidator<CategoryUpsertRequest>
{
    public CategoryUpsertRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);

        // The cycle check needs the existing tree, so it lives in the service. This only
        // catches the degenerate self-parent case.
        RuleFor(x => x.ParentId)
            .NotEqual(Guid.Empty)
            .When(x => x.ParentId.HasValue)
            .WithMessage("Parent category id cannot be empty.");
    }
}

public sealed class CategoryReorderRequestValidator : AbstractValidator<CategoryReorderRequest>
{
    public CategoryReorderRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();

        RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.Id).Distinct().Count() == items.Count)
            .WithMessage("The same category appears more than once.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).NotEmpty();
            item.RuleFor(i => i.SortOrder).GreaterThanOrEqualTo(0);
            item.RuleFor(i => i.ParentId)
                .Must((row, parentId) => parentId != row.Id)
                .When(i => i.ParentId.HasValue)
                .WithMessage("A category cannot be its own parent.");
        });
    }
}

public sealed class UpsertSavedViewRequestValidator : AbstractValidator<UpsertSavedViewRequest>
{
    private const int MaxFiltersBytes = 8000;

    public UpsertSavedViewRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EntityKind).NotEmpty().MaximumLength(32);

        RuleFor(x => x.FiltersJson)
            .NotEmpty()
            .MaximumLength(MaxFiltersBytes)
            .Must(BeWellFormedJson)
            .WithMessage("Filters must be valid JSON.");
    }

    /// <summary>The server never interprets the blob — a new filter on the list page should
    /// not need a backend change — but it does refuse to store something that is not JSON,
    /// so a corrupt value cannot break every later read.</summary>
    private static bool BeWellFormedJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

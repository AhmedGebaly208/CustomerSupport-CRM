using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Customers.Dtos;

// ---- Attachments ----

/// <summary>An attached file. Only metadata: the bytes live behind <c>IFileStorage</c> and
/// are streamed by a separate download endpoint, so a list never carries file content.</summary>
public sealed record AttachmentDetailDto(
    Guid Id,
    AttachmentOwnerType OwnerType,
    Guid OwnerId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid? UploadedBy,
    string? UploadedByName,
    DateTimeOffset CreatedAt);

// ---- Merge ----

public sealed record CustomerMergeRequest(Guid SurvivorId, Guid LoserId, string? Reason);

/// <summary>What the merge actually moved. Returned so the operator can see the scale of
/// what just happened rather than a bare 204.</summary>
public sealed record CustomerMergeResultDto(
    Guid SurvivorId,
    Guid MergedCustomerId,
    int TicketsMoved,
    int InteractionsMoved,
    int NotesMoved,
    int ContactsMoved,
    int AttachmentsMoved);

// ---- Import ----

public sealed record CustomerImportRowResultDto(
    int RowNumber,
    bool Succeeded,
    Guid? CustomerId,
    string? Code,
    IReadOnlyList<string> Errors);

public sealed record CustomerImportResultDto(
    int TotalRows,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<CustomerImportRowResultDto> Rows);

/// <summary>One parsed spreadsheet row, before validation. Every field is a raw string
/// because a spreadsheet cell can hold anything; conversion and validation happen per row
/// so one bad cell cannot abort the import.</summary>
public sealed record CustomerImportRow(
    int RowNumber,
    string? FullNameAr,
    string? FullNameEn,
    string? Email,
    string? Phone,
    string? WhatsAppNumber,
    string? CompanyName,
    string? NationalId,
    string? Address,
    string? PreferredLanguage,
    string? DepartmentCode,
    string? BranchCode);

// ---- Activity timeline ----

public enum CustomerActivityType
{
    Ticket = 0,
    Interaction = 1,
    Note = 2,
    Attachment = 3
}

/// <summary>One entry in the consolidated customer timeline. A flat shape across four very
/// different sources, so the UI renders one list instead of four interleaved ones.</summary>
public sealed record CustomerActivityItemDto(
    CustomerActivityType Type,
    Guid Id,
    DateTimeOffset OccurredAt,
    string? TitleAr,
    string? TitleEn,
    string? Snippet,
    string? RefNumber,
    string? Status,
    Guid? ActorId,
    string? ActorName);

public sealed class CustomerActivityQuery : PagedQuery
{
    /// <summary>Restricts the feed to certain sources. Empty means all four.</summary>
    public CustomerActivityType[]? Types { get; set; }
}

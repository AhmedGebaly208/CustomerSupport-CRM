using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Customers.Dtos;

public sealed class CustomerQuery : PagedQuery
{
    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }
    public bool? IsActive { get; set; }
}

public sealed record CustomerListItemDto(
    Guid Id,
    string Code,
    string FullNameAr,
    string FullNameEn,
    string? Email,
    string? Phone,
    string? CompanyName,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    string? BranchNameAr,
    string? BranchNameEn,
    bool IsActive,
    int OpenTicketCount,
    DateTimeOffset CreatedAt);

public sealed record CustomerDetailDto(
    Guid Id,
    string Code,
    string FullNameAr,
    string FullNameEn,
    string? Email,
    string? Phone,
    string? WhatsAppNumber,
    string? CompanyName,
    string? NationalId,
    string? Address,
    string PreferredLanguage,
    Guid? DepartmentId,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    Guid? BranchId,
    string? BranchNameAr,
    string? BranchNameEn,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<CustomerContactDto> Contacts);

public sealed record CustomerContactDto(
    Guid Id,
    ContactType Type,
    string Value,
    string? Label,
    bool IsPrimary);

public sealed record SaveCustomerContactRequest(
    ContactType Type,
    string Value,
    string? Label,
    bool IsPrimary);

public sealed record CreateCustomerRequest(
    string FullNameAr,
    string FullNameEn,
    string? Email,
    string? Phone,
    string? WhatsAppNumber,
    string? CompanyName,
    string? NationalId,
    string? Address,
    string? PreferredLanguage,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<SaveCustomerContactRequest>? Contacts);

public sealed record UpdateCustomerRequest(
    string FullNameAr,
    string FullNameEn,
    string? Email,
    string? Phone,
    string? WhatsAppNumber,
    string? CompanyName,
    string? NationalId,
    string? Address,
    string? PreferredLanguage,
    Guid? DepartmentId,
    Guid? BranchId,
    bool IsActive,
    IReadOnlyList<SaveCustomerContactRequest>? Contacts);

public sealed record CustomerNoteDto(
    Guid Id,
    string Body,
    bool IsInternal,
    Guid? CreatedBy,
    string? CreatedByName,
    DateTimeOffset CreatedAt);

public sealed record CreateCustomerNoteRequest(string Body, bool IsInternal);

public sealed record InteractionDto(
    Guid Id,
    Guid CustomerId,
    Guid? TicketId,
    string? TicketNumber,
    CommunicationChannel Channel,
    InteractionDirection Direction,
    string? Subject,
    string Body,
    DateTimeOffset OccurredAt,
    Guid? AgentId,
    string? AgentName);

public sealed record CreateInteractionRequest(
    CommunicationChannel Channel,
    InteractionDirection Direction,
    string? Subject,
    string Body,
    DateTimeOffset? OccurredAt,
    Guid? TicketId);

public sealed record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy);

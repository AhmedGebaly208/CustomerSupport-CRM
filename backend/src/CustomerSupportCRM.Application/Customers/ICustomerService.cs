using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Customers.Dtos;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> SearchAsync(CustomerQuery query, CancellationToken ct = default);
    Task<CustomerDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default);
    /// <summary>Grants or revokes a customer's access to the portal by linking a login to
    /// their record. A deliberate, audited action of its own rather than a field on the edit
    /// form, because it changes who can see the record rather than what it says.</summary>
    Task<CustomerDetailDto> SetPortalUserAsync(
        Guid id, Guid? userId, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerNoteDto>> GetNotesAsync(Guid customerId, CancellationToken ct = default);
    Task<CustomerNoteDto> AddNoteAsync(Guid customerId, CreateCustomerNoteRequest request, CancellationToken ct = default);
    Task DeleteNoteAsync(Guid customerId, Guid noteId, CancellationToken ct = default);

    Task<PagedResult<InteractionDto>> GetInteractionsAsync(Guid customerId, int page, int pageSize, CancellationToken ct = default);
    Task<InteractionDto> AddInteractionAsync(Guid customerId, CreateInteractionRequest request, CancellationToken ct = default);

    // ---- Customer 360 (see CustomerService.Extended.cs) ----

    Task<AttachmentDetailDto> AddAttachmentAsync(
        AttachmentOwnerType ownerType, Guid ownerId, Stream content, string fileName,
        string? contentType, CancellationToken ct = default);

    Task<IReadOnlyList<AttachmentDetailDto>> ListAttachmentsAsync(
        AttachmentOwnerType ownerType, Guid ownerId, CancellationToken ct = default);

    Task<(Stream Content, string ContentType, string FileName)> OpenAttachmentAsync(
        AttachmentOwnerType ownerType, Guid ownerId, Guid attachmentId, CancellationToken ct = default);

    Task DeleteAttachmentAsync(
        AttachmentOwnerType ownerType, Guid ownerId, Guid attachmentId, CancellationToken ct = default);

    Task<CustomerMergeResultDto> MergeAsync(CustomerMergeRequest request, CancellationToken ct = default);

    Task<CustomerImportResultDto> ImportAsync(
        Stream content, string fileName, string? contentType, CancellationToken ct = default);

    Task<PagedResult<CustomerActivityItemDto>> GetActivityAsync(
        Guid customerId, CustomerActivityQuery query, CancellationToken ct = default);
}

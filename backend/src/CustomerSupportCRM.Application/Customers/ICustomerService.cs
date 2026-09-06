using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Customers.Dtos;

namespace CustomerSupportCRM.Application.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> SearchAsync(CustomerQuery query, CancellationToken ct = default);
    Task<CustomerDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerNoteDto>> GetNotesAsync(Guid customerId, CancellationToken ct = default);
    Task<CustomerNoteDto> AddNoteAsync(Guid customerId, CreateCustomerNoteRequest request, CancellationToken ct = default);
    Task DeleteNoteAsync(Guid customerId, Guid noteId, CancellationToken ct = default);

    Task<PagedResult<InteractionDto>> GetInteractionsAsync(Guid customerId, int page, int pageSize, CancellationToken ct = default);
    Task<InteractionDto> AddInteractionAsync(Guid customerId, CreateInteractionRequest request, CancellationToken ct = default);
}

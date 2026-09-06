using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Tickets.Dtos;

namespace CustomerSupportCRM.Application.Tickets;

public interface ITicketService
{
    Task<PagedResult<TicketListItemDto>> SearchAsync(TicketQuery query, CancellationToken ct = default);
    Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, CancellationToken ct = default);
    Task<TicketDetailDto> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<TicketDetailDto> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken ct = default);
    Task<TicketDetailDto> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<TicketCommentDto>> GetCommentsAsync(Guid ticketId, bool includeInternal, CancellationToken ct = default);
    Task<TicketCommentDto> AddCommentAsync(Guid ticketId, CreateTicketCommentRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(Guid ticketId, CancellationToken ct = default);

    Task<AgentDashboardDto> GetAgentDashboardAsync(Guid agentId, CancellationToken ct = default);
}

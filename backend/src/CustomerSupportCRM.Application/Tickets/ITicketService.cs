using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Enums;

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

    // ---- Desk-scale operations (see TicketService.Operations.cs) ----

    Task<TicketDetailDto> ChangePriorityAsync(Guid id, TicketPriority priority, CancellationToken ct = default);

    Task<BulkOperationResult> BulkAssignAsync(BulkAssignRequest request, CancellationToken ct = default);
    Task<BulkOperationResult> BulkChangePriorityAsync(BulkPriorityRequest request, CancellationToken ct = default);
    Task<BulkOperationResult> BulkChangeStatusAsync(BulkStatusRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<TicketLinkDto>> GetLinksAsync(Guid ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<TicketLinkDto>> AddLinkAsync(Guid ticketId, CreateTicketLinkRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TicketLinkDto>> RemoveLinkAsync(Guid ticketId, Guid linkId, CancellationToken ct = default);

    Task<TicketDetailDto> MergeAsync(Guid sourceId, MergeTicketRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<WatcherDto>> GetWatchersAsync(Guid ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<WatcherDto>> AddWatcherAsync(Guid ticketId, AddWatcherRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WatcherDto>> RemoveWatcherAsync(Guid ticketId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<TagDto>> GetTagsAsync(Guid ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<TagDto>> AddTagAsync(Guid ticketId, AddTagRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TagDto>> RemoveTagAsync(Guid ticketId, Guid tagId, CancellationToken ct = default);
    Task<IReadOnlyList<TagDto>> SearchTagsAsync(string? query, CancellationToken ct = default);

    Task<TicketDetailDto> ChangeEscalationAsync(Guid ticketId, ChangeEscalationRequest request, CancellationToken ct = default);
}

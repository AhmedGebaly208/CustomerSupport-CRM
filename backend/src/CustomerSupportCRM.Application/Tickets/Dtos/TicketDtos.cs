using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Tickets.Dtos;

public sealed class TicketQuery : PagedQuery
{
    public TicketStatus[]? Statuses { get; set; }
    public TicketPriority[]? Priorities { get; set; }
    public CommunicationChannel[]? Channels { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }

    /// <summary>Shortcut for the agent dashboard: everything still needing work.</summary>
    public bool? OnlyActive { get; set; }

    /// <summary>True = tickets with no assignee, for the unassigned queue.</summary>
    public bool? Unassigned { get; set; }

    public DateTimeOffset? CreatedFrom { get; set; }
    public DateTimeOffset? CreatedTo { get; set; }
}

public sealed record TicketListItemDto(
    Guid Id,
    string Number,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    CommunicationChannel Channel,
    Guid CustomerId,
    string CustomerNameAr,
    string CustomerNameEn,
    Guid? CategoryId,
    string? CategoryNameAr,
    string? CategoryNameEn,
    Guid? AssignedAgentId,
    string? AssignedAgentName,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    int EscalationLevel,
    DateTimeOffset? ResolutionDueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record TicketDetailDto(
    Guid Id,
    string Number,
    string Subject,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    CommunicationChannel Channel,
    Guid CustomerId,
    string CustomerCode,
    string CustomerNameAr,
    string CustomerNameEn,
    string? CustomerEmail,
    string? CustomerPhone,
    Guid? CategoryId,
    string? CategoryNameAr,
    string? CategoryNameEn,
    Guid? AssignedAgentId,
    string? AssignedAgentName,
    DateTimeOffset? AssignedAt,
    Guid? DepartmentId,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    Guid? BranchId,
    string? BranchNameAr,
    string? BranchNameEn,
    int EscalationLevel,
    DateTimeOffset? FirstResponseDueAt,
    DateTimeOffset? ResolutionDueAt,
    DateTimeOffset? FirstRespondedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<TicketStatus> AllowedNextStatuses);

public sealed record CreateTicketRequest(
    Guid CustomerId,
    string Subject,
    string Description,
    TicketPriority Priority,
    CommunicationChannel Channel,
    Guid? CategoryId,
    Guid? DepartmentId,
    Guid? BranchId,
    Guid? AssignedAgentId);

public sealed record UpdateTicketRequest(
    string Subject,
    string Description,
    TicketPriority Priority,
    Guid? CategoryId,
    Guid? DepartmentId,
    Guid? BranchId);

public sealed record AssignTicketRequest(Guid? AgentId, string? Note);

public sealed record ChangeTicketStatusRequest(TicketStatus Status, string? Note);

public sealed record TicketCommentDto(
    Guid Id,
    Guid TicketId,
    string Body,
    bool IsInternal,
    Guid? AuthorId,
    string? AuthorName,
    DateTimeOffset CreatedAt);

public sealed record CreateTicketCommentRequest(string Body, bool IsInternal);

public sealed record TicketHistoryDto(
    Guid Id,
    string Field,
    string? OldValue,
    string? NewValue,
    string? Note,
    Guid? ChangedBy,
    string? ChangedByName,
    DateTimeOffset ChangedAt);

/// <summary>Payload for the agent dashboard (area 4).</summary>
public sealed record AgentDashboardDto(
    int AssignedActive,
    int AssignedNew,
    int AssignedOverdue,
    int UnassignedInDepartment,
    int ResolvedToday,
    IReadOnlyDictionary<TicketStatus, int> ByStatus,
    IReadOnlyDictionary<TicketPriority, int> ByPriority,
    IReadOnlyList<TicketListItemDto> RecentAssigned);

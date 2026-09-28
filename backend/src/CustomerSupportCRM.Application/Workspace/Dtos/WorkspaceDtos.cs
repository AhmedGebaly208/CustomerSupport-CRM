using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Workspace.Dtos;

// ---- Tasks and reminders ----

public sealed record AgentTaskDto(
    Guid Id,
    string Title,
    string? Notes,
    Guid OwnerUserId,
    string? OwnerName,
    DateTimeOffset? DueAt,
    bool IsDone,
    DateTimeOffset? CompletedAt,
    bool IsReminder,
    Guid? TicketId,
    string? TicketNumber,
    Guid? CustomerId,
    string? CustomerName,
    DateTimeOffset CreatedAt);

public sealed record SaveAgentTaskRequest(
    string Title,
    string? Notes,
    DateTimeOffset? DueAt,
    bool IsReminder,
    Guid? TicketId,
    Guid? CustomerId);

public sealed record AgentTaskQuery(
    bool IncludeDone = false,
    /// <summary>Only tasks due on or before this instant. Drives the "due today" filter.</summary>
    DateTimeOffset? DueBefore = null,
    int Take = 50);

// ---- Quick replies ----

public sealed record QuickReplyDto(
    Guid Id,
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    string? Shortcut,
    bool IsActive,
    Guid? DepartmentId,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    Guid? BranchId);

public sealed record SaveQuickReplyRequest(
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    string? Shortcut,
    bool IsActive,
    Guid? DepartmentId,
    Guid? BranchId);

// ---- Dashboard ----

/// <summary>One agent's row in the supervisor's team view.</summary>
public sealed record TeamMemberLoadDto(
    Guid AgentId,
    string AgentNameAr,
    string AgentNameEn,
    Guid? DepartmentId,
    int Active,
    int Overdue,
    int AtRisk,
    int ResolvedToday);

public sealed record TeamDashboardDto(
    int TotalActive,
    int TotalUnassigned,
    int TotalOverdue,
    int TotalAtRisk,
    int ResolvedToday,
    IReadOnlyDictionary<TicketStatus, int> ByStatus,
    IReadOnlyDictionary<TicketPriority, int> ByPriority,
    IReadOnlyList<TeamMemberLoadDto> Members,
    /// <summary>The oldest tickets still waiting for an owner — the queue a supervisor
    /// acts on first.</summary>
    IReadOnlyList<TicketListItemDto> OldestUnassigned);

/// <summary>Everything the agent's own board needs, in one request. Assembled server-side
/// because a dashboard that fires eight parallel calls is slower and harder to keep
/// consistent than one that fires a single query per panel.</summary>
public sealed record AgentWorkspaceDto(
    AgentDashboardDto Tickets,
    IReadOnlyList<AgentTaskDto> OpenTasks,
    int OverdueTaskCount,
    IReadOnlyList<TicketListItemDto> MentionedTickets,
    IReadOnlyList<TicketListItemDto> WatchedTickets);

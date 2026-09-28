using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Sla.Dtos;

// ---- Policies ----

public sealed record SlaTargetDto(
    TicketPriority Priority,
    int FirstResponseMinutes,
    int ResolutionMinutes);

public sealed record SlaEscalationRuleDto(
    Guid Id,
    string NameAr,
    string NameEn,
    bool IsActive,
    SlaTargetKind Target,
    int ThresholdPercent,
    int RaiseLevelBy,
    Guid? ReassignToUserId,
    string? ReassignToName,
    string? NotifyRole);

public sealed record SlaPolicyDto(
    Guid Id,
    string NameAr,
    string NameEn,
    bool IsActive,
    int Rank,
    Guid? DepartmentId,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    Guid? BranchId,
    string? BranchNameAr,
    string? BranchNameEn,
    Guid? CategoryId,
    string? CategoryNameAr,
    string? CategoryNameEn,
    bool CountsBusinessHoursOnly,
    IReadOnlyList<TicketStatus> PausedStatuses,
    AutoAssignmentStrategy AssignmentStrategy,
    IReadOnlyList<SlaTargetDto> Targets,
    IReadOnlyList<SlaEscalationRuleDto> EscalationRules);

public sealed record SaveSlaTargetRequest(
    TicketPriority Priority,
    int FirstResponseMinutes,
    int ResolutionMinutes);

public sealed record SaveSlaPolicyRequest(
    string NameAr,
    string NameEn,
    bool IsActive,
    int Rank,
    Guid? DepartmentId,
    Guid? BranchId,
    Guid? CategoryId,
    bool CountsBusinessHoursOnly,
    IReadOnlyList<TicketStatus>? PausedStatuses,
    AutoAssignmentStrategy AssignmentStrategy,
    IReadOnlyList<SaveSlaTargetRequest> Targets);

public sealed record SaveSlaEscalationRuleRequest(
    string NameAr,
    string NameEn,
    bool IsActive,
    SlaTargetKind Target,
    int ThresholdPercent,
    int RaiseLevelBy,
    Guid? ReassignToUserId,
    string? NotifyRole);

// ---- Live status on a ticket ----

/// <summary>Where one of a ticket's two clocks stands right now. Computed on read rather
/// than stored, so a policy edit is reflected immediately instead of leaving stale badges
/// until the next sweep.</summary>
public sealed record SlaClockDto(
    SlaTargetKind Kind,
    SlaState State,
    DateTimeOffset? DueAt,
    int? TargetMinutes,
    int? ElapsedMinutes,
    int? PercentConsumed,
    /// <summary>Working minutes left; negative once the target is missed.</summary>
    int? RemainingMinutes);

public sealed record TicketSlaStatusDto(
    Guid TicketId,
    Guid? PolicyId,
    string? PolicyNameAr,
    string? PolicyNameEn,
    int EscalationLevel,
    SlaClockDto FirstResponse,
    SlaClockDto Resolution);

/// <summary>Answers "what would this policy promise?" without saving anything, so an admin
/// can see that a four-hour target raised on a Thursday afternoon lands on Sunday before
/// committing to it.</summary>
public sealed record SlaPreviewRequest(
    Guid PolicyId,
    TicketPriority Priority,
    DateTimeOffset? StartAt);

public sealed record SlaPreviewDto(
    DateTimeOffset StartAt,
    DateTimeOffset FirstResponseDueAt,
    DateTimeOffset ResolutionDueAt,
    bool CountsBusinessHoursOnly);

// ---- Notifications ----

/// <summary>The event, not its wording. The client renders it from Kind and Parameters so
/// the message follows the reader's language rather than the language the server happened to
/// be running in.</summary>
public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    IReadOnlyDictionary<string, string>? Parameters,
    Guid? TicketId,
    string? TicketNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationListDto(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount);

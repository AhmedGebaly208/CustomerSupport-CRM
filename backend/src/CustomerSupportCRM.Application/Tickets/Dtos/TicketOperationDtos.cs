using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Tickets.Dtos;

// ---- Bulk operations ----

public sealed record BulkAssignRequest(IReadOnlyList<Guid> TicketIds, Guid? AgentId, string? Note);

public sealed record BulkPriorityRequest(IReadOnlyList<Guid> TicketIds, TicketPriority Priority);

public sealed record BulkStatusRequest(IReadOnlyList<Guid> TicketIds, TicketStatus Status, string? Note);

/// <summary>Outcome for one ticket in a bulk call. Failures are reported per item rather
/// than aborting the batch: an agent selecting twenty tickets should not lose nineteen
/// successful changes because one had an illegal status transition.</summary>
public sealed record BulkItemResult(Guid TicketId, bool Succeeded, string? ErrorCode, string? ErrorMessage);

public sealed record BulkOperationResult(
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<BulkItemResult> Items);

// ---- Links ----

/// <summary>A link as seen from one ticket. <c>OtherTicketId</c> is always the far end from
/// the caller's viewpoint, so the same DTO renders on both detail pages.</summary>
public sealed record TicketLinkDto(
    Guid Id,
    TicketLinkType Type,
    bool IsOutgoing,
    Guid OtherTicketId,
    string OtherTicketNumber,
    string OtherTicketSubject,
    TicketStatus OtherTicketStatus);

public sealed record CreateTicketLinkRequest(Guid TargetTicketId, TicketLinkType Type);

// ---- Merge ----

public sealed record MergeTicketRequest(Guid TargetTicketId, string? Reason);

// ---- Watchers ----

public sealed record WatcherDto(Guid UserId, string DisplayName);

public sealed record AddWatcherRequest(Guid UserId);

// ---- Tags ----

public sealed record TagDto(Guid Id, string Name, string? ColorHex);

public sealed record AddTagRequest(string Name, string? ColorHex);

// ---- Escalation ----

/// <summary>Manual escalation. <c>Delta</c> is +1 or -1; the reason is mandatory because an
/// escalation with no stated cause is unreviewable later.</summary>
public sealed record ChangeEscalationRequest(int Delta, string Reason);

// ---- Category administration ----

public sealed record CategoryUpsertRequest(
    string NameAr,
    string NameEn,
    Guid? ParentId,
    Guid? DepartmentId,
    int SortOrder,
    bool IsActive);

public sealed record CategoryReorderItem(Guid Id, Guid? ParentId, int SortOrder);

public sealed record CategoryReorderRequest(IReadOnlyList<CategoryReorderItem> Items);

// ---- Saved views ----

public sealed record SavedViewDto(Guid Id, string Name, string EntityKind, string FiltersJson);

public sealed record UpsertSavedViewRequest(string Name, string EntityKind, string FiltersJson);

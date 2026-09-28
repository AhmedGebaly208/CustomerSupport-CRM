using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Portal;

/// <summary>What a customer sees of their own ticket.
///
/// A dedicated shape, not the staff DTO. The staff detail carries the assigned agent,
/// department, escalation level, SLA due dates and internal notes — none of which is a
/// customer's business. Reusing it and remembering to strip fields is a rule that holds
/// until someone adds a field and forgets; a separate type cannot leak what it has no
/// property for.</summary>
public sealed record PortalTicketDto(
    Guid Id,
    string Number,
    string Subject,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    CommunicationChannel Channel,
    string? CategoryNameAr,
    string? CategoryNameEn,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    bool CanReply,
    bool CanRate,
    int? SatisfactionScore);

public sealed record PortalTicketListItemDto(
    Guid Id,
    string Number,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt);

/// <summary>A message on the customer's own ticket. Only ever built from a public comment —
/// the query never selects internal ones.</summary>
public sealed record PortalMessageDto(
    Guid Id,
    string Body,
    /// <summary>True when the desk wrote it. The customer sees "support" rather than an
    /// agent's name: naming the individual invites them being chased directly, and tells a
    /// customer who is handling their case when it may be reassigned tomorrow.</summary>
    bool FromSupport,
    DateTimeOffset CreatedAt);

public sealed record PortalAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);

public sealed record CreatePortalTicketRequest(
    string Subject,
    string Description,
    TicketPriority Priority,
    Guid? CategoryId);

public sealed record PortalReplyRequest(string Body);

public sealed record PortalProfileDto(
    Guid CustomerId,
    string Code,
    string FullNameAr,
    string FullNameEn,
    string? Email,
    string? Phone,
    string PreferredLanguage);

public sealed record PortalTicketQuery(
    bool OpenOnly = false,
    int Page = 1,
    int PageSize = 20)
{
    public int Skip => (Math.Max(Page, 1) - 1) * PageSize;
}

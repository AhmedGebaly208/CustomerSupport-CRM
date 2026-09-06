using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>One field-level change on a ticket (area 2 "Ticket history").
/// Append-only: rows are written by the ticket service and never updated.</summary>
public class TicketHistory : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Property that changed, e.g. "Status", "Priority", "AssignedAgentId".</summary>
    public string Field { get; set; } = string.Empty;

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>Optional free-text reason supplied by the agent (e.g. why it was put on hold).</summary>
    public string? Note { get; set; }

    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

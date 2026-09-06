using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>A reply or internal note on a ticket (areas 2 and 4).</summary>
public class TicketComment : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Internal notes are visible to agents only — never returned to the
    /// customer portal (area 8) or included in outbound channel replies.</summary>
    public bool IsInternal { get; set; }

    public Guid? AuthorId { get; set; }
}

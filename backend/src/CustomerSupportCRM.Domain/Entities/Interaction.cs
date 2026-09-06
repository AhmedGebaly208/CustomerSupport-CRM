using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>One recorded touchpoint with a customer, on any channel (area 1
/// "Interaction history", area 3). Optionally tied to the ticket it belongs to.</summary>
public class Interaction : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public CommunicationChannel Channel { get; set; }
    public InteractionDirection Direction { get; set; }

    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Agent involved, when there was one. Null for automated or customer-initiated traffic.</summary>
    public Guid? AgentId { get; set; }
}

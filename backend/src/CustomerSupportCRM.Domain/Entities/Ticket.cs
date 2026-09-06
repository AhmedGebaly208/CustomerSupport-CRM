using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>Support ticket (area 2) — the core record of the system.</summary>
public class Ticket : AuditableEntity, IScopedEntity
{
    /// <summary>Human-facing reference (e.g. TKT-000123). Unique, generated on create.</summary>
    public string Number { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? CategoryId { get; set; }
    public TicketCategory? Category { get; set; }

    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.WebForm;

    /// <summary>Identity user id of the assigned agent. Deliberately not a navigation
    /// property: Domain stays free of the ASP.NET Identity dependency.</summary>
    public Guid? AssignedAgentId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>0 = not escalated. Raised by the escalation rules in the `sla-automation` story.</summary>
    public int EscalationLevel { get; set; }

    // --- SLA columns (area 5). Written by the SLA engine; the bootstrap only reads them. ---
    public Guid? SlaPolicyId { get; set; }
    public DateTimeOffset? FirstResponseDueAt { get; set; }
    public DateTimeOffset? ResolutionDueAt { get; set; }
    public DateTimeOffset? FirstRespondedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public ICollection<TicketComment> Comments { get; set; } = new List<TicketComment>();
    public ICollection<TicketHistory> History { get; set; } = new List<TicketHistory>();
    public ICollection<Interaction> Interactions { get; set; } = new List<Interaction>();

    public ICollection<TicketTag> TicketTags { get; set; } = new List<TicketTag>();
    public ICollection<TicketWatcher> Watchers { get; set; } = new List<TicketWatcher>();

    /// <summary>Links where this ticket is the source. A link is stored once; the detail
    /// view unions outgoing and incoming so both sides render it.</summary>
    public ICollection<TicketLink> OutgoingLinks { get; set; } = new List<TicketLink>();
    public ICollection<TicketLink> IncomingLinks { get; set; } = new List<TicketLink>();
}

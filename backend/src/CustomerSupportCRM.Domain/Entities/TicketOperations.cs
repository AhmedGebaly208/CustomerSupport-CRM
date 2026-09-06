using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>A free-form label an agent can attach to tickets (PDF area 2).
///
/// Deliberately not bilingual: tags are coined by agents in whatever language suits the
/// desk, and forcing an ar/en pair on every ad-hoc label would make tagging a chore.</summary>
public class Tag : AuditableEntity
{
    /// <summary>Stored trimmed. Matched case-insensitively, so "VIP" and "vip" are one tag.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional hex colour, e.g. #7c3aed.</summary>
    public string? ColorHex { get; set; }

    public ICollection<TicketTag> TicketTags { get; set; } = new List<TicketTag>();
}

/// <summary>Join between a ticket and a tag. A join entity rather than a delimited string
/// column so filtering by tag stays indexable.</summary>
public class TicketTag : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid TagId { get; set; }
    public Tag? Tag { get; set; }
}

/// <summary>An agent following a ticket they are not assigned to (PDF area 4).
///
/// The `sla-automation` story reads this to decide who to notify on a breach, which is why
/// it exists before any notification code does.</summary>
public class TicketWatcher : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Identity user id. No navigation property: Domain stays free of the
    /// ASP.NET Identity dependency, exactly as Ticket.AssignedAgentId does.</summary>
    public Guid UserId { get; set; }
}

/// <summary>A typed relationship between two tickets (PDF area 2).
///
/// Stored once, on the source. Both detail pages render it by loading outgoing and
/// incoming links together, so there is no second row to keep in step.</summary>
public class TicketLink : AuditableEntity
{
    public Guid SourceTicketId { get; set; }
    public Ticket? SourceTicket { get; set; }

    public Guid TargetTicketId { get; set; }
    public Ticket? TargetTicket { get; set; }

    public TicketLinkType Type { get; set; }
}

/// <summary>A named filter combination an agent saved for themselves (PDF area 2).
///
/// The filters are an opaque JSON blob owned by the frontend: the server validates that it
/// is well-formed JSON and bounded in size, but never interprets it. That keeps a new
/// filter on the list page from needing a backend change.</summary>
public class UserSavedView : AuditableEntity
{
    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Which list the view belongs to. "Ticket" today; the column exists so the
    /// same table can serve the customer and audit lists later.</summary>
    public string EntityKind { get; set; } = "Ticket";

    public string FiltersJson { get; set; } = "{}";
}

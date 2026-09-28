using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>An in-app alert for one user (PDF area 5, "Alerts and notifications").
///
/// Stored rather than pushed: an agent who was offline when their ticket breached still
/// needs to see it.
///
/// The row carries the event, not its wording — a Kind and the values that fill it. The
/// reader's language is a property of the reader, not of the event, so rendering happens at
/// display time and an agent who switches to English does not find a backlog of Arabic.
/// This mirrors how API failures carry an error code rather than a sentence.</summary>
public class Notification : BaseEntity
{
    /// <summary>Identity user id of the recipient. Not a navigation property, for the same
    /// reason Ticket.AssignedAgentId is not: Domain stays clear of ASP.NET Identity.</summary>
    public Guid UserId { get; set; }

    public NotificationKind Kind { get; set; }

    /// <summary>JSON object of values the message needs, e.g. {"percent":120}. Names that
    /// end in Ar/En are bilingual pairs the reader picks from — rule and category names come
    /// from the database already translated.</summary>
    public string? ParametersJson { get; set; }

    /// <summary>The ticket this is about, when there is one. Lets the UI deep-link straight
    /// to the work rather than just announcing it.</summary>
    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public bool IsRead => ReadAt is not null;
}

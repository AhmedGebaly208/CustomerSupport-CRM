using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>A personal to-do or reminder on an agent's board (PDF area 4).
///
/// Deliberately not a ticket: not every piece of follow-up work is customer-facing, and
/// forcing "ring the supplier back on Sunday" through the ticket workflow would pollute
/// every queue and SLA report with work no customer is waiting on.</summary>
public class AgentTask : AuditableEntity, IScopedEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }

    /// <summary>Identity user id of the owner. Not a navigation property, matching
    /// Ticket.AssignedAgentId — Domain stays free of the ASP.NET Identity dependency.</summary>
    public Guid OwnerUserId { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public bool IsDone { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When true and DueAt has passed, the dispatcher raises a notification.</summary>
    public bool IsReminder { get; set; }

    /// <summary>Set once the reminder has been sent, so a task that stays overdue for days
    /// does not notify on every sweep.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }

    /// <summary>Optional anchors, so a task opened from a ticket can link back to it.</summary>
    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }
}

/// <summary>A saved snippet an agent can drop into a reply (PDF area 4, "Quick replies").
///
/// Bilingual because the snippet is customer-facing: the agent picks by title in their own
/// language and inserts the body in the customer's.</summary>
public class QuickReply : AuditableEntity, IScopedEntity
{
    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;

    public string BodyAr { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;

    /// <summary>Optional typed shortcut, e.g. "/greet". Unique when set so two snippets
    /// cannot answer to the same keystrokes.</summary>
    public string? Shortcut { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Null means available everywhere; otherwise the snippet belongs to one
    /// department or branch.</summary>
    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }
}

/// <summary>A colleague named in a ticket comment (PDF area 4, "@ mentions").
///
/// Stored rather than re-parsed from comment bodies, so "tickets where I was mentioned"
/// is an indexed query instead of a scan, and so editing a comment later cannot silently
/// revoke a mention someone has already been notified about.</summary>
public class TicketMention : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid TicketCommentId { get; set; }
    public TicketComment? Comment { get; set; }

    public Guid MentionedUserId { get; set; }
    public Guid MentionedByUserId { get; set; }

    public DateTimeOffset MentionedAt { get; set; }
}

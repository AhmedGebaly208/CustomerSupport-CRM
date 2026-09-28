using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>An agreed set of response and resolution targets (PDF area 5).
///
/// A policy is selected for a ticket by how specifically it matches — a policy naming the
/// ticket's category beats one naming only its department. Targets are per priority, so one
/// policy covers the whole priority ladder instead of needing four.</summary>
public class SlaPolicy : AuditableEntity, IScopedEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Breaks ties between policies that match a ticket equally specifically.
    /// Higher wins.</summary>
    public int Rank { get; set; }

    // --- Match criteria. Null means "any", which is what makes a policy a fallback. ---
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public Guid? CategoryId { get; set; }
    public TicketCategory? Category { get; set; }

    /// <summary>When true the targets are counted in working minutes against the configured
    /// business hours; when false the clock runs continuously.
    ///
    /// Stored as a flag rather than a per-policy calendar: the desk's opening hours and
    /// holidays already live in BusinessHours and Holiday, and duplicating them per policy
    /// would give two sources of truth that drift apart.</summary>
    public bool CountsBusinessHoursOnly { get; set; } = true;

    /// <summary>Statuses that stop the clock, stored as a comma-separated list of
    /// TicketStatus names. "Pending" by default: time waiting on the customer is not time
    /// the desk owes back.</summary>
    public string PausedStatuses { get; set; } = nameof(TicketStatus.Pending);

    public AutoAssignmentStrategy AssignmentStrategy { get; set; } = AutoAssignmentStrategy.None;

    public ICollection<SlaTarget> Targets { get; set; } = new List<SlaTarget>();
    public ICollection<SlaEscalationRule> EscalationRules { get; set; } = new List<SlaEscalationRule>();

    /// <summary>How many of the match criteria this policy actually constrains. The
    /// resolver orders by this so the most specific policy wins.</summary>
    public int Specificity =>
        (CategoryId is null ? 0 : 4) + (DepartmentId is null ? 0 : 2) + (BranchId is null ? 0 : 1);
}

/// <summary>The targets for one priority within a policy, in minutes.</summary>
public class SlaTarget : AuditableEntity
{
    public Guid SlaPolicyId { get; set; }
    public SlaPolicy? Policy { get; set; }

    public TicketPriority Priority { get; set; }

    public int FirstResponseMinutes { get; set; }
    public int ResolutionMinutes { get; set; }
}

/// <summary>What to do when a ticket approaches or misses a target (PDF area 5,
/// "Escalation rules").
///
/// Rules are evaluated by a background service rather than on request, because a ticket that
/// nobody opens still breaches.</summary>
public class SlaEscalationRule : AuditableEntity
{
    public Guid SlaPolicyId { get; set; }
    public SlaPolicy? Policy { get; set; }

    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public SlaTargetKind Target { get; set; }

    /// <summary>Percentage of the target consumed before the rule fires. 100 or more means
    /// the rule only fires on an actual breach.</summary>
    public int ThresholdPercent { get; set; } = 80;

    /// <summary>How much to raise Ticket.EscalationLevel by. Zero notifies without
    /// escalating, which is how a "heads up" rule is expressed.</summary>
    public int RaiseLevelBy { get; set; } = 1;

    /// <summary>Reassign the ticket to this agent when the rule fires. Null leaves the
    /// assignment alone.</summary>
    public Guid? ReassignToUserId { get; set; }

    /// <summary>Role whose members are notified. Null notifies only the assigned agent and
    /// the ticket's watchers.</summary>
    public string? NotifyRole { get; set; }
}

/// <summary>Records that a rule has already fired for a ticket, so a rule that stays true
/// for hours does not re-notify on every sweep.</summary>
public class SlaEscalationEvent : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid RuleId { get; set; }
    public SlaEscalationRule? Rule { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public int PercentConsumed { get; set; }
}

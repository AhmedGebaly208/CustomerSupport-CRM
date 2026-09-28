namespace CustomerSupportCRM.Domain.Enums;

/// <summary>Which of a policy's two clocks a rule or measurement refers to.</summary>
public enum SlaTargetKind
{
    FirstResponse = 0,
    Resolution = 1
}

/// <summary>Where a ticket stands against one of its targets.</summary>
public enum SlaState
{
    /// <summary>No policy matched, so nothing is being measured.</summary>
    None = 0,
    Met = 1,
    Running = 2,
    /// <summary>Past the warning threshold but not yet over the target.</summary>
    AtRisk = 3,
    Breached = 4,
    /// <summary>The clock is stopped because the ticket sits in a paused status.</summary>
    Paused = 5
}

/// <summary>How a policy picks an agent for a new ticket (PDF area 5,
/// "Automatic assignment").</summary>
public enum AutoAssignmentStrategy
{
    /// <summary>Leave the ticket unassigned for a human to pick up.</summary>
    None = 0,
    /// <summary>The eligible agent with the fewest active tickets. Evens out load rather
    /// than round-robin's even count of handovers.</summary>
    LeastBusy = 1,
    /// <summary>Next agent in a stable order, continuing after whoever got the last one.</summary>
    RoundRobin = 2
}

/// <summary>Why a notification was raised, so the UI can group and icon them.</summary>
public enum NotificationKind
{
    TicketAssigned = 0,
    TicketEscalated = 1,
    SlaAtRisk = 2,
    SlaBreached = 3,
    TicketCommented = 4,
    Mention = 5,
    /// <summary>A personal reminder the agent set for themselves (PDF area 4).</summary>
    TaskDue = 6
}

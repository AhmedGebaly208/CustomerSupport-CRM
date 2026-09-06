using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Tickets;

/// <summary>The single source of truth for ticket status transitions (area 2
/// "Status and escalation"). Kept in Domain so the API, the SLA escalation job and the
/// customer portal all enforce the same rules instead of each inventing their own.</summary>
public static class TicketWorkflow
{
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> Allowed =
        new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.New] =
                [TicketStatus.Open, TicketStatus.Pending, TicketStatus.OnHold, TicketStatus.Resolved, TicketStatus.Closed],
            [TicketStatus.Open] =
                [TicketStatus.Pending, TicketStatus.OnHold, TicketStatus.Resolved, TicketStatus.Closed],
            [TicketStatus.Pending] =
                [TicketStatus.Open, TicketStatus.OnHold, TicketStatus.Resolved, TicketStatus.Closed],
            [TicketStatus.OnHold] =
                [TicketStatus.Open, TicketStatus.Pending, TicketStatus.Resolved, TicketStatus.Closed],
            // A resolved ticket is either accepted (closed) or rejected by the customer (reopened).
            [TicketStatus.Resolved] =
                [TicketStatus.Closed, TicketStatus.Reopened],
            // Closed is terminal apart from a reopen — closing must not silently lose history.
            [TicketStatus.Closed] =
                [TicketStatus.Reopened],
            [TicketStatus.Reopened] =
                [TicketStatus.Open, TicketStatus.Pending, TicketStatus.OnHold, TicketStatus.Resolved, TicketStatus.Closed]
        };

    /// <summary>Statuses that still count against SLA and appear in an agent's active queue.</summary>
    public static readonly TicketStatus[] ActiveStatuses =
        [TicketStatus.New, TicketStatus.Open, TicketStatus.Pending, TicketStatus.OnHold, TicketStatus.Reopened];

    public static IReadOnlyList<TicketStatus> AllowedTransitions(TicketStatus from) =>
        Allowed.TryGetValue(from, out var next) ? next : [];

    public static bool CanTransition(TicketStatus from, TicketStatus to) =>
        from != to && AllowedTransitions(from).Contains(to);

    public static bool IsActive(TicketStatus status) => ActiveStatuses.Contains(status);

    /// <summary>True once the customer no longer waits on the agent — used by SLA reporting.</summary>
    public static bool IsTerminal(TicketStatus status) =>
        status is TicketStatus.Resolved or TicketStatus.Closed;
}

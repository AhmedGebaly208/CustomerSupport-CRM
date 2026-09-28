using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Sla;

/// <summary>Picks an agent for a new ticket (PDF area 5, "Automatic assignment").
///
/// Eligibility is department-first: a ticket raised for Billing goes to someone in Billing.
/// When the ticket names no department, every active agent is eligible — that is the desk
/// working as one queue, which is the sensible reading of an unset department.</summary>
public sealed class AutoAssignmentService(
    IAppDbContext db,
    IIdentityService identity) : IAutoAssignmentService
{
    public async Task<Guid?> PickAgentAsync(Ticket ticket, SlaPolicy? policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var strategy = policy?.AssignmentStrategy ?? AutoAssignmentStrategy.None;
        if (strategy == AutoAssignmentStrategy.None) return null;

        var candidates = await identity.GetAgentsAsync(ticket.DepartmentId, ct);
        if (candidates.Count == 0) return null;

        return strategy switch
        {
            // GetAgentsAsync already counts each agent's open tickets for the assignment
            // picker, so the load is read from there rather than queried again here.
            AutoAssignmentStrategy.LeastBusy => candidates
                .OrderBy(c => c.OpenTicketCount)
                .ThenBy(c => c.Id)
                .First().Id,

            AutoAssignmentStrategy.RoundRobin => await PickRoundRobinAsync(
                candidates.Select(c => c.Id).ToList(), ct),

            _ => null
        };
    }

    private async Task<Guid?> PickRoundRobinAsync(List<Guid> candidateIds, CancellationToken ct)
    {
        // "Next after whoever got the last one", derived from the tickets themselves rather
        // than a stored cursor: a cursor would drift whenever agents are added or removed,
        // and would need its own concurrency story.
        var lastAssigned = await db.Tickets.AsNoTracking()
            .Where(t => t.AssignedAgentId != null && candidateIds.Contains(t.AssignedAgentId.Value))
            .OrderByDescending(t => t.AssignedAt)
            .Select(t => t.AssignedAgentId)
            .FirstOrDefaultAsync(ct);

        var ordered = candidateIds.OrderBy(id => id).ToList();

        if (lastAssigned is null) return ordered[0];

        var index = ordered.IndexOf(lastAssigned.Value);
        if (index < 0) return ordered[0];

        return ordered[(index + 1) % ordered.Count];
    }
}

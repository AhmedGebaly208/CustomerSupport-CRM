using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Application.Sla;

/// <summary>Sweeps open tickets for approaching and missed targets and fires the escalation
/// rules (PDF area 5, "Escalation rules").
///
/// Runs on a schedule rather than on request because a ticket nobody opens still breaches.
/// Each rule fires at most once per ticket, enforced by a unique index on
/// SlaEscalationEvents rather than by a flag the sweep could race itself on.</summary>
public sealed class SlaEvaluator(
    IAppDbContext db,
    ISlaService sla,
    INotificationService notifications,
    IIdentityService identity,
    IClock clock,
    ILogger<SlaEvaluator> logger) : ISlaEvaluator
{
    /// <summary>How many tickets one sweep will look at. A desk with more open tickets than
    /// this is evaluated across successive sweeps rather than in one long transaction that
    /// holds locks while it runs.</summary>
    private const int SweepBatchSize = 500;

    public async Task<int> EvaluateAsync(CancellationToken ct = default)
    {
        var open = SlaService.OpenStatuses;

        var tickets = await db.Tickets
            .Where(t => t.SlaPolicyId != null && open.Contains(t.Status))
            .OrderBy(t => t.ResolutionDueAt)
            .Take(SweepBatchSize)
            .ToListAsync(ct);

        if (tickets.Count == 0) return 0;

        var ticketIds = tickets.Select(t => t.Id).ToList();
        var statuses = await sla.GetStatusesAsync(ticketIds, ct);

        var policyIds = tickets.Select(t => t.SlaPolicyId!.Value).Distinct().ToList();

        var rules = await db.SlaEscalationRules.AsNoTracking()
            .Where(r => policyIds.Contains(r.SlaPolicyId) && r.IsActive)
            .ToListAsync(ct);

        if (rules.Count == 0) return 0;

        // One read of what has already fired, so the sweep does not query per ticket.
        var alreadyFired = await db.SlaEscalationEvents.AsNoTracking()
            .Where(e => ticketIds.Contains(e.TicketId))
            .Select(e => new { e.TicketId, e.RuleId })
            .ToListAsync(ct);

        var fired = alreadyFired.Select(e => (e.TicketId, e.RuleId)).ToHashSet();
        var now = clock.UtcNow;
        var count = 0;

        foreach (var ticket in tickets)
        {
            if (!statuses.TryGetValue(ticket.Id, out var status)) continue;

            foreach (var rule in rules.Where(r => r.SlaPolicyId == ticket.SlaPolicyId))
            {
                if (fired.Contains((ticket.Id, rule.Id))) continue;

                var slaClock = rule.Target == SlaTargetKind.FirstResponse
                    ? status.FirstResponse
                    : status.Resolution;

                // A clock that never started, is stopped, or is paused cannot breach.
                if (slaClock.State is SlaState.None or SlaState.Met or SlaState.Paused) continue;
                if (slaClock.PercentConsumed is not { } percent) continue;
                if (percent < rule.ThresholdPercent) continue;

                await FireAsync(ticket, rule, percent, now, ct);
                fired.Add((ticket.Id, rule.Id));
                count++;
            }
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SLA sweep fired {Count} escalation(s) across {Tickets} ticket(s).",
                count, tickets.Count);
        }

        return count;
    }

    private async Task FireAsync(
        Ticket ticket, SlaEscalationRule rule, int percent, DateTimeOffset now, CancellationToken ct)
    {
        ticket.EscalationLevel += rule.RaiseLevelBy;

        if (rule.ReassignToUserId is { } reassignTo && ticket.AssignedAgentId != reassignTo)
        {
            db.TicketHistory.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                Field = nameof(Ticket.AssignedAgentId),
                OldValue = ticket.AssignedAgentId?.ToString(),
                NewValue = reassignTo.ToString(),
                Note = $"Escalation rule '{rule.NameEn}'",
                ChangedAt = now
            });

            ticket.AssignedAgentId = reassignTo;
            ticket.AssignedAt = now;
        }

        db.TicketHistory.Add(new TicketHistory
        {
            TicketId = ticket.Id,
            Field = nameof(Ticket.EscalationLevel),
            OldValue = (ticket.EscalationLevel - rule.RaiseLevelBy).ToString(),
            NewValue = ticket.EscalationLevel.ToString(),
            Note = $"Escalation rule '{rule.NameEn}' at {percent}% of target",
            ChangedAt = now
        });

        db.SlaEscalationEvents.Add(new SlaEscalationEvent
        {
            TicketId = ticket.Id,
            RuleId = rule.Id,
            OccurredAt = now,
            PercentConsumed = percent
        });

        await NotifyAsync(ticket, rule, percent, ct);
    }

    private async Task NotifyAsync(Ticket ticket, SlaEscalationRule rule, int percent, CancellationToken ct)
    {
        var recipients = new List<Guid>();

        if (ticket.AssignedAgentId is { } assignee) recipients.Add(assignee);

        var watchers = await db.TicketWatchers.AsNoTracking()
            .Where(w => w.TicketId == ticket.Id)
            .Select(w => w.UserId)
            .ToListAsync(ct);

        recipients.AddRange(watchers);

        if (!string.IsNullOrWhiteSpace(rule.NotifyRole))
            recipients.AddRange(await identity.GetUserIdsInRoleAsync(rule.NotifyRole, ct));

        if (recipients.Count == 0) return;

        var breached = percent >= 100;

        // Rule names come from the database already translated, so both are passed through
        // and the client picks the one matching the reader.
        await notifications.NotifyAsync(
            recipients,
            new NotificationRequest(
                breached ? NotificationKind.SlaBreached : NotificationKind.SlaAtRisk,
                new Dictionary<string, string>
                {
                    ["ticketNumber"] = ticket.Number,
                    ["percent"] = percent.ToString(),
                    ["target"] = rule.Target.ToString(),
                    ["ruleNameAr"] = rule.NameAr,
                    ["ruleNameEn"] = rule.NameEn
                },
                ticket.Id),
            ct);
    }
}

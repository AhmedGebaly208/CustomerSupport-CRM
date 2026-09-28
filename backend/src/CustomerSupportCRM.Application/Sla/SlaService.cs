using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Sla;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Sla;

/// <summary>Applies SLA policy to tickets (PDF area 5).
///
/// Due dates are stamped on the ticket when it is created or re-targeted, so a list of a
/// thousand tickets can sort and filter by them in SQL. Everything derived from them — how
/// much time is left, whether a target is at risk — is computed on read instead, because it
/// changes every minute and storing it would mean rewriting every open ticket on a
/// schedule.</summary>
public sealed class SlaService(
    IAppDbContext db,
    IBusinessCalendarProvider calendars,
    IClock clock) : ISlaService
{
    /// <summary>Fraction of a target consumed before a clock reads "at risk" in the UI. The
    /// escalation rules carry their own thresholds; this one only drives the badge colour,
    /// so a desk with no rules configured still gets a warning.</summary>
    private const int AtRiskPercent = 80;

    public async Task<SlaPolicy?> ResolvePolicyAsync(Ticket ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        // A criterion matches when the policy leaves it open or names the ticket's own value.
        var candidates = await db.SlaPolicies
            .AsNoTracking()
            .Include(p => p.Targets)
            .Where(p => p.IsActive
                        && (p.CategoryId == null || p.CategoryId == ticket.CategoryId)
                        && (p.DepartmentId == null || p.DepartmentId == ticket.DepartmentId)
                        && (p.BranchId == null || p.BranchId == ticket.BranchId))
            .ToListAsync(ct);

        // Ordered in memory: Specificity is derived from the criteria columns and has no
        // SQL translation.
        return candidates
            .OrderByDescending(p => p.Specificity)
            .ThenByDescending(p => p.Rank)
            .ThenBy(p => p.NameEn)
            .FirstOrDefault();
    }

    public async Task ApplyPolicyAsync(Ticket ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var policy = await ResolvePolicyAsync(ticket, ct);
        var target = policy?.Targets.FirstOrDefault(t => t.Priority == ticket.Priority);

        if (policy is null || target is null)
        {
            // No policy covers this ticket. Clearing rather than leaving the previous dates
            // is deliberate: a stale due date would show a breach against a promise that no
            // longer exists.
            ticket.SlaPolicyId = null;
            ticket.FirstResponseDueAt = null;
            ticket.ResolutionDueAt = null;
            return;
        }

        var calendar = await CalendarForAsync(policy, ct);
        var start = ticket.CreatedAt == default ? clock.UtcNow : ticket.CreatedAt;

        ticket.SlaPolicyId = policy.Id;
        ticket.FirstResponseDueAt = WorkingTimeCalculator.Add(start, target.FirstResponseMinutes, calendar);
        ticket.ResolutionDueAt = WorkingTimeCalculator.Add(start, target.ResolutionMinutes, calendar);
    }

    public async Task<TicketSlaStatusDto> GetStatusAsync(Guid ticketId, CancellationToken ct = default)
    {
        var statuses = await GetStatusesAsync([ticketId], ct);

        return statuses.TryGetValue(ticketId, out var status)
            ? status
            : throw new NotFoundException(nameof(Ticket), ticketId);
    }

    public async Task<IReadOnlyDictionary<Guid, TicketSlaStatusDto>> GetStatusesAsync(
        IReadOnlyCollection<Guid> ticketIds, CancellationToken ct = default)
    {
        if (ticketIds.Count == 0) return new Dictionary<Guid, TicketSlaStatusDto>();

        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => ticketIds.Contains(t.Id))
            .ToListAsync(ct);

        if (tickets.Count == 0) return new Dictionary<Guid, TicketSlaStatusDto>();

        var policyIds = tickets.Where(t => t.SlaPolicyId is not null)
            .Select(t => t.SlaPolicyId!.Value).Distinct().ToList();

        var policies = await db.SlaPolicies.AsNoTracking()
            .Include(p => p.Targets)
            .Where(p => policyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // Status changes are read once for the whole batch; going per ticket would issue a
        // query per row on a list page.
        var pauseHistory = await LoadStatusHistoryAsync(tickets.Select(t => t.Id).ToList(), ct);

        var businessCalendar = await calendars.GetAsync(ct);
        var alwaysOpen = BusinessCalendar.TwentyFourSeven(businessCalendar.TimeZone);
        var now = clock.UtcNow;

        var result = new Dictionary<Guid, TicketSlaStatusDto>(tickets.Count);

        foreach (var ticket in tickets)
        {
            SlaPolicy? policy = null;
            if (ticket.SlaPolicyId is not null) policies.TryGetValue(ticket.SlaPolicyId.Value, out policy);

            var target = policy?.Targets.FirstOrDefault(t => t.Priority == ticket.Priority);
            var calendar = policy is null || policy.CountsBusinessHoursOnly ? businessCalendar : alwaysOpen;

            var paused = policy is null
                ? []
                : PausedIntervals(
                    pauseHistory.TryGetValue(ticket.Id, out var rows) ? rows : [],
                    ParsePausedStatuses(policy.PausedStatuses),
                    ticket,
                    now);

            var isPausedNow = policy is not null
                               && ParsePausedStatuses(policy.PausedStatuses).Contains(ticket.Status);

            result[ticket.Id] = new TicketSlaStatusDto(
                ticket.Id,
                policy?.Id,
                policy?.NameAr,
                policy?.NameEn,
                ticket.EscalationLevel,
                BuildClock(
                    SlaTargetKind.FirstResponse,
                    ticket.FirstResponseDueAt,
                    target?.FirstResponseMinutes,
                    ticket.CreatedAt,
                    ticket.FirstRespondedAt,
                    calendar, paused, isPausedNow, now),
                BuildClock(
                    SlaTargetKind.Resolution,
                    ticket.ResolutionDueAt,
                    target?.ResolutionMinutes,
                    ticket.CreatedAt,
                    ticket.ResolvedAt ?? ticket.ClosedAt,
                    calendar, paused, isPausedNow, now));
        }

        return result;
    }

    public async Task<SlaPreviewDto> PreviewAsync(SlaPreviewRequest request, CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies.AsNoTracking()
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == request.PolicyId, ct)
            ?? throw new NotFoundException(nameof(SlaPolicy), request.PolicyId);

        var target = policy.Targets.FirstOrDefault(t => t.Priority == request.Priority)
            ?? throw new BadRequestException(
                $"This policy has no target for priority '{request.Priority}'.");

        var calendar = await CalendarForAsync(policy, ct);
        var start = request.StartAt ?? clock.UtcNow;

        return new SlaPreviewDto(
            start,
            WorkingTimeCalculator.Add(start, target.FirstResponseMinutes, calendar),
            WorkingTimeCalculator.Add(start, target.ResolutionMinutes, calendar),
            policy.CountsBusinessHoursOnly);
    }

    private async Task<BusinessCalendar> CalendarForAsync(SlaPolicy policy, CancellationToken ct)
    {
        var configured = await calendars.GetAsync(ct);

        return policy.CountsBusinessHoursOnly
            ? configured
            : BusinessCalendar.TwentyFourSeven(configured.TimeZone);
    }

    private SlaClockDto BuildClock(
        SlaTargetKind kind,
        DateTimeOffset? dueAt,
        int? targetMinutes,
        DateTimeOffset startedAt,
        DateTimeOffset? stoppedAt,
        BusinessCalendar calendar,
        IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> paused,
        bool isPausedNow,
        DateTimeOffset now)
    {
        if (dueAt is null || targetMinutes is null)
            return new SlaClockDto(kind, SlaState.None, null, null, null, null, null);

        var measureTo = stoppedAt ?? now;
        var elapsed = WorkingTimeCalculator.Elapsed(startedAt, measureTo, calendar, paused);
        var percent = WorkingTimeCalculator.PercentConsumed(elapsed, targetMinutes.Value);
        var remaining = targetMinutes.Value - elapsed;

        var state = stoppedAt is not null
            // A finished clock is judged on whether it beat the target, not on the time now.
            ? (elapsed <= targetMinutes.Value ? SlaState.Met : SlaState.Breached)
            : isPausedNow
                ? SlaState.Paused
                : percent >= 100
                    ? SlaState.Breached
                    : percent >= AtRiskPercent
                        ? SlaState.AtRisk
                        : SlaState.Running;

        return new SlaClockDto(kind, state, dueAt, targetMinutes, elapsed, percent, remaining);
    }

    private async Task<Dictionary<Guid, List<TicketHistory>>> LoadStatusHistoryAsync(
        IReadOnlyCollection<Guid> ticketIds, CancellationToken ct)
    {
        var rows = await db.TicketHistory.AsNoTracking()
            .Where(h => ticketIds.Contains(h.TicketId) && h.Field == "Status")
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(ct);

        return rows.GroupBy(h => h.TicketId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Turns the status-change trail into the windows during which the clock was
    /// stopped. A ticket currently sitting in a paused status has its last window left open
    /// to "now", so time keeps not accruing while it waits.</summary>
    private static List<(DateTimeOffset From, DateTimeOffset To)> PausedIntervals(
        IReadOnlyList<TicketHistory> history,
        IReadOnlySet<TicketStatus> pausedStatuses,
        Ticket ticket,
        DateTimeOffset now)
    {
        var intervals = new List<(DateTimeOffset From, DateTimeOffset To)>();
        if (pausedStatuses.Count == 0) return intervals;

        DateTimeOffset? openedAt = null;

        foreach (var row in history)
        {
            if (!Enum.TryParse<TicketStatus>(row.NewValue, out var newStatus)) continue;

            var isPaused = pausedStatuses.Contains(newStatus);

            if (isPaused && openedAt is null)
            {
                openedAt = row.ChangedAt;
            }
            else if (!isPaused && openedAt is not null)
            {
                intervals.Add((openedAt.Value, row.ChangedAt));
                openedAt = null;
            }
        }

        if (openedAt is not null)
        {
            // Still paused. Close the window at the moment the ticket finished if it has,
            // otherwise at now.
            var closeAt = ticket.ResolvedAt ?? ticket.ClosedAt ?? now;
            if (closeAt > openedAt.Value) intervals.Add((openedAt.Value, closeAt));
        }

        return intervals;
    }

    internal static HashSet<TicketStatus> ParsePausedStatuses(string? value)
    {
        var statuses = new HashSet<TicketStatus>();
        if (string.IsNullOrWhiteSpace(value)) return statuses;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Unknown names are skipped rather than throwing: a status removed from the enum
            // must not make every SLA calculation fail.
            if (Enum.TryParse<TicketStatus>(part, ignoreCase: true, out var status))
                statuses.Add(status);
        }

        return statuses;
    }

    /// <summary>Statuses that still owe the customer work. Used by the evaluator and the
    /// auto-assignment load count.</summary>
    internal static TicketStatus[] OpenStatuses => TicketWorkflow.ActiveStatuses;
}

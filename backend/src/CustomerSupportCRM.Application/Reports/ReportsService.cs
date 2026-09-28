using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Reports.Dtos;
using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Reports;

public interface IReportsService
{
    Task<TicketReportDto> GetTicketReportAsync(ReportQuery query, CancellationToken ct = default);
    Task<SlaReportDto> GetSlaReportAsync(ReportQuery query, CancellationToken ct = default);
    Task<AgentReportDto> GetAgentReportAsync(ReportQuery query, CancellationToken ct = default);
    Task<CsatReportDto> GetCsatReportAsync(ReportQuery query, CancellationToken ct = default);

    Task<SatisfactionDto> SubmitSatisfactionAsync(
        Guid ticketId, SubmitSatisfactionRequest request, CancellationToken ct = default);
}

/// <summary>Historical reporting (PDF area 8).
///
/// Two rules run through all of it. Aggregation happens in SQL — a report over a year of
/// tickets must never pull rows into memory to count them. And every ratio is nullable: with
/// nothing in the window the honest answer is "no data", where a zero would read as total
/// failure and a NaN would reach the UI as a crash.</summary>
public sealed class ReportsService(
    IAppDbContext db,
    IIdentityService identity,
    ICurrentUser currentUser,
    IScopeProvider scope,
    IClock clock) : IReportsService
{
    /// <summary>Longest window a single report will cover. Beyond a year the answer is a
    /// data-warehouse question, and letting it through would let one request table-scan the
    /// whole history.</summary>
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    public async Task<TicketReportDto> GetTicketReportAsync(
        ReportQuery query, CancellationToken ct = default)
    {
        Validate(query);
        EnsureMayReadDeskWide();

        var current = Filtered(query);
        var previous = Filtered(query with
        {
            From = query.From - (query.To - query.From),
            To = query.From
        });

        var total = await current.CountAsync(ct);
        var previousTotal = await previous.CountAsync(ct);

        return new TicketReportDto(
            total,
            previousTotal,
            await BucketAsync(current, query.Granularity, ct),
            await BucketAsync(previous, query.Granularity, ct),
            await GroupAsync(current, t => t.Status, s => (s.ToString(), s.ToString(), s.ToString()), ct),
            await GroupAsync(current, t => t.Priority, p => (p.ToString(), p.ToString(), p.ToString()), ct),
            await GroupAsync(current, t => t.Channel, c => (c.ToString(), c.ToString(), c.ToString()), ct),
            await GroupByLookupAsync(current, t => t.CategoryId, db.TicketCategories, ct),
            await GroupByLookupAsync(current, t => t.DepartmentId, db.Departments, ct));
    }

    public async Task<SlaReportDto> GetSlaReportAsync(ReportQuery query, CancellationToken ct = default)
    {
        Validate(query);
        EnsureMayReadDeskWide();

        // Only tickets that carried a policy can be measured against one. Counting the rest
        // as met would flatter the number; counting them as breached would be a lie.
        var measured = Filtered(query).Where(t => t.SlaPolicyId != null);

        var firstResponse = await measured
            .Where(t => t.FirstResponseDueAt != null && t.FirstRespondedAt != null)
            // The timestamps come back and the subtraction happens here. DateDiffMinute is a
            // SQL Server extension, and Application does not know which provider is in use.
            .Select(t => new
            {
                Met = t.FirstRespondedAt <= t.FirstResponseDueAt,
                t.CreatedAt,
                At = t.FirstRespondedAt!.Value
            })
            .ToListAsync(ct);

        var resolution = await measured
            .Where(t => t.ResolutionDueAt != null && t.ResolvedAt != null)
            .Select(t => new
            {
                Met = t.ResolvedAt <= t.ResolutionDueAt,
                t.CreatedAt,
                At = t.ResolvedAt!.Value
            })
            .ToListAsync(ct);

        var frMet = firstResponse.Count(x => x.Met);
        var resMet = resolution.Count(x => x.Met);

        var breaches = measured.Where(t =>
            (t.FirstResponseDueAt != null && t.FirstRespondedAt != null && t.FirstRespondedAt > t.FirstResponseDueAt)
            || (t.ResolutionDueAt != null && t.ResolvedAt != null && t.ResolvedAt > t.ResolutionDueAt));

        return new SlaReportDto(
            await measured.CountAsync(ct),
            frMet,
            firstResponse.Count - frMet,
            Ratio(frMet, firstResponse.Count),
            resMet,
            resolution.Count - resMet,
            Ratio(resMet, resolution.Count),
            Average(firstResponse.Select(x => (x.At - x.CreatedAt).TotalMinutes)),
            Median(firstResponse.Select(x => (x.At - x.CreatedAt).TotalMinutes)),
            Average(resolution.Select(x => (x.At - x.CreatedAt).TotalMinutes)),
            Median(resolution.Select(x => (x.At - x.CreatedAt).TotalMinutes)),
            await BucketAsync(breaches, query.Granularity, ct),
            await GroupByLookupAsync(breaches, t => t.DepartmentId, db.Departments, ct));
    }

    /// <summary>Per-agent performance.
    ///
    /// A ticket that changes hands is credited carefully, because the naive reading punishes
    /// whoever happens to hold it last:
    /// <list type="bullet">
    /// <item><b>Handled</b> counts every agent the ticket was ever assigned to, once each —
    /// they all did work on it.</item>
    /// <item><b>Resolved</b> is credited only to the agent holding it when it was resolved,
    /// since that is who finished it.</item>
    /// <item><b>Reopened</b> counts tickets that came back after that agent resolved them,
    /// which is the signal a resolution was premature.</item>
    /// </list>
    /// Assignment history comes from TicketHistory, which is append-only, so the credit does
    /// not change when a ticket is reassigned again later.</summary>
    public async Task<AgentReportDto> GetAgentReportAsync(ReportQuery query, CancellationToken ct = default)
    {
        Validate(query);

        // An agent may look at their own row and nobody else's.
        if (!currentUser.HasPermission(Permissions.Dashboard.ViewTeam))
        {
            var self = currentUser.UserId;

            if (query.AgentId is null || query.AgentId != self)
                throw new ForbiddenException("You may only view your own performance.");
        }

        var tickets = Filtered(query);
        var ticketIds = await tickets.Select(t => t.Id).ToListAsync(ct);

        if (ticketIds.Count == 0) return new AgentReportDto([]);

        // Every agent the ticket touched, from the append-only history plus the current
        // assignment (a ticket assigned once has no history row for it).
        var assignments = await db.TicketHistory.AsNoTracking()
            .Where(h => ticketIds.Contains(h.TicketId)
                        && h.Field == nameof(Ticket.AssignedAgentId)
                        && h.NewValue != null)
            .Select(h => new { h.TicketId, h.NewValue })
            .ToListAsync(ct);

        var currentAssignments = await tickets
            .Where(t => t.AssignedAgentId != null)
            .Select(t => new { t.Id, AgentId = t.AssignedAgentId!.Value })
            .ToListAsync(ct);

        var handled = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var row in assignments)
        {
            if (Guid.TryParse(row.NewValue, out var agentId)) Add(handled, agentId, row.TicketId);
        }

        foreach (var row in currentAssignments) Add(handled, row.AgentId, row.Id);

        // Resolved and reopened are read from the ticket's own state, credited to whoever
        // held it at the end.
        var outcomes = await tickets
            .Where(t => t.AssignedAgentId != null)
            .Select(t => new
            {
                AgentId = t.AssignedAgentId!.Value,
                Resolved = t.ResolvedAt != null,
                Reopened = t.Status == TicketStatus.Reopened,
                t.CreatedAt,
                t.FirstRespondedAt,
                t.ResolvedAt,
                TicketId = t.Id
            })
            .ToListAsync(ct);

        var satisfaction = await db.TicketSatisfaction.AsNoTracking()
            .Where(s => ticketIds.Contains(s.TicketId))
            .Select(s => new { s.TicketId, s.Score })
            .ToListAsync(ct);

        var scoreByTicket = satisfaction.ToDictionary(s => s.TicketId, s => s.Score);

        var open = TicketWorkflow.ActiveStatuses;
        var load = await scope.Apply(db.Tickets.AsNoTracking())
            .Where(t => t.AssignedAgentId != null && open.Contains(t.Status))
            .GroupBy(t => t.AssignedAgentId!.Value)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, ct);

        var agentIds = handled.Keys.Union(outcomes.Select(o => o.AgentId)).Distinct().ToList();
        if (query.AgentId is { } only) agentIds = agentIds.Where(id => id == only).ToList();

        var names = await identity.GetUserDisplayNamesAsync(agentIds, ct);
        var agents = await identity.GetAgentsAsync(null, ct);

        var rows = agentIds.Select(agentId =>
        {
            var mine = outcomes.Where(o => o.AgentId == agentId).ToList();
            var agent = agents.FirstOrDefault(a => a.Id == agentId);

            var scores = mine
                .Where(o => scoreByTicket.ContainsKey(o.TicketId))
                .Select(o => (double)scoreByTicket[o.TicketId])
                .ToList();

            var resolved = mine.Count(o => o.Resolved);

            return new AgentRowDto(
                agentId,
                agent?.FullNameAr ?? names.GetValueOrDefault(agentId) ?? string.Empty,
                agent?.FullNameEn ?? names.GetValueOrDefault(agentId) ?? string.Empty,
                handled.TryGetValue(agentId, out var set) ? set.Count : 0,
                resolved,
                mine.Count(o => o.Reopened),
                Ratio(mine.Count(o => o.Reopened), resolved),
                Average(mine.Where(o => o.FirstRespondedAt != null)
                    .Select(o => (o.FirstRespondedAt!.Value - o.CreatedAt).TotalMinutes)),
                Average(mine.Where(o => o.ResolvedAt != null)
                    .Select(o => (o.ResolvedAt!.Value - o.CreatedAt).TotalMinutes)),
                Average(scores),
                load.GetValueOrDefault(agentId));
        })
        .OrderByDescending(r => r.Resolved)
        .ThenByDescending(r => r.Handled)
        .ToList();

        return new AgentReportDto(rows);

        static void Add(Dictionary<Guid, HashSet<Guid>> map, Guid agentId, Guid ticketId)
        {
            if (!map.TryGetValue(agentId, out var set)) map[agentId] = set = [];
            set.Add(ticketId);
        }
    }

    public async Task<CsatReportDto> GetCsatReportAsync(ReportQuery query, CancellationToken ct = default)
    {
        Validate(query);
        EnsureMayReadDeskWide();

        var tickets = Filtered(query);

        // Only a resolved ticket can be rated, so it is the only fair denominator for a
        // response rate.
        var eligible = await tickets.CountAsync(t => t.ResolvedAt != null, ct);

        var ticketIds = await tickets.Select(t => t.Id).ToListAsync(ct);

        var responses = await db.TicketSatisfaction.AsNoTracking()
            .Where(s => ticketIds.Contains(s.TicketId))
            .Select(s => new { s.Score, s.SubmittedAt })
            .ToListAsync(ct);

        var trend = responses
            .GroupBy(r => BucketStart(r.SubmittedAt, query.Granularity))
            .Select(g => new TimeBucketDto(g.Key, g.Count()))
            .OrderBy(b => b.Start)
            .ToList();

        var distribution = Enumerable.Range(1, 5)
            .Select(score => new DimensionBucketDto(
                score.ToString(), score.ToString(), score.ToString(),
                responses.Count(r => r.Score == score)))
            .ToList();

        return new CsatReportDto(
            Average(responses.Select(r => (double)r.Score)),
            responses.Count,
            eligible,
            Ratio(responses.Count, eligible),
            trend,
            distribution);
    }

    public async Task<SatisfactionDto> SubmitSatisfactionAsync(
        Guid ticketId, SubmitSatisfactionRequest request, CancellationToken ct = default)
    {
        if (request.Score is < 1 or > 5)
            throw new BadRequestException("A rating must be between 1 and 5.");

        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        if (ticket.ResolvedAt is null && ticket.ClosedAt is null)
        {
            // Rating work that is still in progress measures impatience, not satisfaction.
            throw new ConflictException(
                "This ticket has not been resolved yet.", ErrorCodes.TicketNotResolved);
        }

        var existing = await db.TicketSatisfaction.FirstOrDefaultAsync(s => s.TicketId == ticketId, ct);

        if (existing is null)
        {
            existing = new TicketSatisfaction { TicketId = ticketId };
            db.TicketSatisfaction.Add(existing);
        }

        // A second submission corrects the first rather than adding a row, so one customer
        // cannot weight the average.
        existing.Score = request.Score;
        existing.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        existing.RespondentUserId = currentUser.UserId;
        existing.SubmittedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return new SatisfactionDto(ticketId, existing.Score, existing.Comment, existing.SubmittedAt);
    }

    // ---- helpers ----

    private static void Validate(ReportQuery query)
    {
        if (query.To < query.From)
            throw new BadRequestException("The end of the range is before its start.");

        if (query.To - query.From > MaxRange)
            throw new BadRequestException("A report may cover at most 366 days.");
    }

    /// <summary>Everything except the agent's own performance report is desk-wide, and an
    /// agent has no business reading it.</summary>
    private void EnsureMayReadDeskWide()
    {
        if (!currentUser.HasPermission(Permissions.Reports.View))
            throw new ForbiddenException("You may not view reports.");
    }

    private IQueryable<Ticket> Filtered(ReportQuery query)
    {
        // Scope first, so no later filter can widen it back out.
        var q = scope.Apply(db.Tickets.AsNoTracking())
            .Where(t => t.CreatedAt >= query.From && t.CreatedAt < query.To);

        if (query.DepartmentId is { } d) q = q.Where(t => t.DepartmentId == d);
        if (query.BranchId is { } b) q = q.Where(t => t.BranchId == b);
        if (query.CategoryId is { } c) q = q.Where(t => t.CategoryId == c);
        if (query.Priority is { } p) q = q.Where(t => t.Priority == p);
        if (query.Status is { } s) q = q.Where(t => t.Status == s);
        if (query.Channel is { } ch) q = q.Where(t => t.Channel == ch);
        if (query.AgentId is { } a) q = q.Where(t => t.AssignedAgentId == a);

        return q;
    }

    /// <summary>Counts per time bucket. The grouping key is computed in SQL from the date
    /// parts, because grouping on a formatted string would drag every row into memory.</summary>
    private static async Task<IReadOnlyList<TimeBucketDto>> BucketAsync(
        IQueryable<Ticket> source, ReportGranularity granularity, CancellationToken ct)
    {
        var rows = await source
            .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month, t.CreatedAt.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
            .ToListAsync(ct);

        var days = rows.Select(r => (
            Date: new DateTimeOffset(new DateTime(r.Year, r.Month, r.Day), TimeSpan.Zero),
            r.Count));

        return days
            .GroupBy(d => BucketStart(d.Date, granularity))
            .Select(g => new TimeBucketDto(g.Key, g.Sum(x => x.Count)))
            .OrderBy(b => b.Start)
            .ToList();
    }

    /// <summary>Start of the bucket a moment falls in. Weeks start on Sunday, which is the
    /// working week here — an ISO Monday week would split every Saudi week in two.</summary>
    private static DateTimeOffset BucketStart(DateTimeOffset value, ReportGranularity granularity)
    {
        var date = value.Date;

        return granularity switch
        {
            ReportGranularity.Week => new DateTimeOffset(date.AddDays(-(int)date.DayOfWeek), TimeSpan.Zero),
            ReportGranularity.Month => new DateTimeOffset(new DateTime(date.Year, date.Month, 1), TimeSpan.Zero),
            _ => new DateTimeOffset(date, TimeSpan.Zero)
        };
    }

    private static async Task<IReadOnlyList<DimensionBucketDto>> GroupAsync<TKey>(
        IQueryable<Ticket> source,
        System.Linq.Expressions.Expression<Func<Ticket, TKey>> selector,
        Func<TKey, (string Key, string LabelAr, string LabelEn)> label,
        CancellationToken ct)
        where TKey : struct
    {
        var rows = await source
            .GroupBy(selector)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var (key, ar, en) = label(r.Value);
                return new DimensionBucketDto(key, ar, en, r.Count);
            })
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    /// <summary>Counts by a foreign key, resolving names from the lookup table. Unset keys
    /// are reported as their own bucket rather than dropped — "no category" is a finding,
    /// not a gap in the chart.</summary>
    private static async Task<IReadOnlyList<DimensionBucketDto>> GroupByLookupAsync<TLookup>(
        IQueryable<Ticket> source,
        System.Linq.Expressions.Expression<Func<Ticket, Guid?>> selector,
        IQueryable<TLookup> lookups,
        CancellationToken ct)
        where TLookup : class, INamedLookup
    {
        var rows = await source
            .GroupBy(selector)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var ids = rows.Where(r => r.Id != null).Select(r => r.Id!.Value).ToList();

        var names = await lookups.AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => new { l.NameAr, l.NameEn }, ct);

        return rows
            .Select(r =>
            {
                // The key, not a sentence: the client translates "none" like every other
                // label, so no display text is decided here.
                if (r.Id is null) return new DimensionBucketDto("none", "none", "none", r.Count);

                var name = names.GetValueOrDefault(r.Id.Value);

                return new DimensionBucketDto(
                    r.Id.Value.ToString(),
                    name?.NameAr ?? string.Empty,
                    name?.NameEn ?? string.Empty,
                    r.Count);
            })
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    /// <summary>Null, not zero, when there is nothing to divide by. The frontend renders it
    /// as a dash — a 0% attainment on a desk with no tickets would read as total failure.</summary>
    private static double? Ratio(int numerator, int denominator) =>
        denominator == 0 ? null : Math.Round(numerator * 100.0 / denominator, 1);

    private static double? Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 1);
    }

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return null;

        var middle = sorted.Count / 2;

        return Math.Round(
            sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0,
            1);
    }
}

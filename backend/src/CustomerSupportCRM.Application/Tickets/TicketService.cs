using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Tickets;

public sealed partial class TicketService : ITicketService
{
    private readonly IAppDbContext db;
    private readonly ICurrentUser currentUser;
    private readonly IClock clock;
    private readonly IReferenceNumberGenerator numbers;
    private readonly IIdentityService identity;
    private readonly IScopeProvider scope;

    public TicketService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IReferenceNumberGenerator numbers,
        IIdentityService identity,
        IScopeProvider scope)
    {
        this.db = db;
        this.currentUser = currentUser;
        this.clock = clock;
        this.numbers = numbers;
        this.identity = identity;
        this.scope = scope;
    }

    public async Task<PagedResult<TicketListItemDto>> SearchAsync(TicketQuery query, CancellationToken ct = default)
    {
        // Scope first, so no later filter can widen it back out.
        var q = ApplyFilters(scope.Apply(db.Tickets.AsNoTracking()), query);

        var total = await q.CountAsync(ct);

        q = ApplySort(q, query.SortBy, query.SortDescending);

        var rows = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(t => new TicketRow(
                t.Id, t.Number, t.Subject, t.Status, t.Priority, t.Channel,
                t.CustomerId,
                t.Customer!.FullNameAr,
                t.Customer.FullNameEn,
                t.CategoryId,
                t.Category != null ? t.Category.NameAr : null,
                t.Category != null ? t.Category.NameEn : null,
                t.AssignedAgentId,
                t.Department != null ? t.Department.NameAr : null,
                t.Department != null ? t.Department.NameEn : null,
                t.EscalationLevel,
                t.ResolutionDueAt,
                t.CreatedAt,
                t.ModifiedAt))
            .ToListAsync(ct);

        var items = await ToListItemsAsync(rows, ct);
        return PagedResult<TicketListItemDto>.Create(items, total, query.Page, query.PageSize);
    }

    private static IQueryable<Ticket> ApplyFilters(IQueryable<Ticket> q, TicketQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(t =>
                t.Number.Contains(term) ||
                t.Subject.Contains(term) ||
                t.Description.Contains(term) ||
                t.Customer!.FullNameAr.Contains(term) ||
                t.Customer.FullNameEn.Contains(term) ||
                t.Customer.Code.Contains(term));
        }

        if (query.Statuses is { Length: > 0 }) q = q.Where(t => query.Statuses.Contains(t.Status));
        if (query.Priorities is { Length: > 0 }) q = q.Where(t => query.Priorities.Contains(t.Priority));
        if (query.Channels is { Length: > 0 }) q = q.Where(t => query.Channels.Contains(t.Channel));

        if (query.CustomerId is { } customerId) q = q.Where(t => t.CustomerId == customerId);
        if (query.CategoryId is { } categoryId) q = q.Where(t => t.CategoryId == categoryId);
        if (query.AssignedAgentId is { } agentId) q = q.Where(t => t.AssignedAgentId == agentId);
        if (query.DepartmentId is { } departmentId) q = q.Where(t => t.DepartmentId == departmentId);
        if (query.BranchId is { } branchId) q = q.Where(t => t.BranchId == branchId);

        if (query.OnlyActive == true)
        {
            var activeStatuses = TicketWorkflow.ActiveStatuses;
            q = q.Where(t => activeStatuses.Contains(t.Status));
        }

        if (query.Unassigned is { } unassigned)
            q = unassigned ? q.Where(t => t.AssignedAgentId == null) : q.Where(t => t.AssignedAgentId != null);

        if (query.TagIds is { Length: > 0 })
        {
            var tagIds = query.TagIds;
            q = q.Where(t => t.TicketTags.Any(tt => tagIds.Contains(tt.TagId)));
        }

        if (query.WatchedBy is { } watcherId)
            q = q.Where(t => t.Watchers.Any(w => w.UserId == watcherId));

        if (query.CreatedFrom is { } from) q = q.Where(t => t.CreatedAt >= from);
        if (query.CreatedTo is { } to) q = q.Where(t => t.CreatedAt <= to);

        return q;
    }

    private static IQueryable<Ticket> ApplySort(IQueryable<Ticket> q, string? sortBy, bool desc) =>
        sortBy?.ToLowerInvariant() switch
        {
            "number" => desc ? q.OrderByDescending(t => t.Number) : q.OrderBy(t => t.Number),
            "subject" => desc ? q.OrderByDescending(t => t.Subject) : q.OrderBy(t => t.Subject),
            "status" => desc ? q.OrderByDescending(t => t.Status) : q.OrderBy(t => t.Status),
            "priority" => desc ? q.OrderByDescending(t => t.Priority) : q.OrderBy(t => t.Priority),
            "duedate" => desc ? q.OrderByDescending(t => t.ResolutionDueAt) : q.OrderBy(t => t.ResolutionDueAt),
            "modifiedat" => desc ? q.OrderByDescending(t => t.ModifiedAt) : q.OrderBy(t => t.ModifiedAt),
            "createdat" => desc ? q.OrderByDescending(t => t.CreatedAt) : q.OrderBy(t => t.CreatedAt),
            // Default queue order: most urgent first, then oldest — the order an agent
            // should actually work the list in.
            _ => q.OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt)
        };

    public async Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var ticket = await db.Tickets
            .AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.Category)
            .Include(t => t.Department)
            .Include(t => t.Branch)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Ticket), id);

        scope.EnsureCanAccess(ticket);

        var names = await ResolveNamesAsync([ticket.AssignedAgentId], ct);

        return ToDetail(
            ticket,
            Lookup(names, ticket.AssignedAgentId),
            await LoadTagsAsync(id, ct),
            await LoadWatchersAsync(id, ct),
            await LoadLinksAsync(id, ct));
    }

    public async Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new BadRequestException("The selected customer does not exist.");

        await GuardLookupsAsync(request.CategoryId, request.DepartmentId, request.BranchId, ct);

        var now = clock.UtcNow;

        var ticket = new Ticket
        {
            Number = await numbers.NextTicketNumberAsync(ct),
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            CustomerId = request.CustomerId,
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            Channel = request.Channel,
            Status = TicketStatus.New,
            // Fall back to the customer's own department/branch so tickets stay routable
            // even when the agent leaves the fields blank.
            DepartmentId = request.DepartmentId ?? customer.DepartmentId,
            BranchId = request.BranchId ?? customer.BranchId,
            AssignedAgentId = request.AssignedAgentId,
            AssignedAt = request.AssignedAgentId is null ? null : now
        };

        db.Tickets.Add(ticket);

        AddHistory(ticket, "Created", null, ticket.Number, null, now);
        if (ticket.AssignedAgentId is { } assignee)
            AddHistory(ticket, nameof(Ticket.AssignedAgentId), null, assignee.ToString(), null, now);

        // Every ticket is also a customer touchpoint, so it shows up in interaction history.
        db.Interactions.Add(new Interaction
        {
            CustomerId = ticket.CustomerId,
            TicketId = ticket.Id,
            Channel = ticket.Channel,
            Direction = InteractionDirection.Inbound,
            Subject = ticket.Subject,
            Body = ticket.Description,
            OccurredAt = now,
            AgentId = currentUser.UserId
        });

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(ticket.Id, ct);
    }

    public async Task<TicketDetailDto> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Ticket), id);

        scope.EnsureCanAccess(ticket);

        if (ticket.Status == TicketStatus.Closed)
            throw new ConflictException("A closed ticket cannot be edited. Reopen it first.");

        await GuardLookupsAsync(request.CategoryId, request.DepartmentId, request.BranchId, ct);

        var now = clock.UtcNow;

        TrackChange(ticket, nameof(Ticket.Subject), ticket.Subject, request.Subject.Trim(), now);
        TrackChange(ticket, nameof(Ticket.Priority), ticket.Priority.ToString(), request.Priority.ToString(), now);
        TrackChange(ticket, nameof(Ticket.CategoryId), ticket.CategoryId?.ToString(), request.CategoryId?.ToString(), now);
        TrackChange(ticket, nameof(Ticket.DepartmentId), ticket.DepartmentId?.ToString(), request.DepartmentId?.ToString(), now);
        TrackChange(ticket, nameof(Ticket.BranchId), ticket.BranchId?.ToString(), request.BranchId?.ToString(), now);

        ticket.Subject = request.Subject.Trim();
        ticket.Description = request.Description.Trim();
        ticket.Priority = request.Priority;
        ticket.CategoryId = request.CategoryId;
        ticket.DepartmentId = request.DepartmentId;
        ticket.BranchId = request.BranchId;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(ticket.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Ticket), id);

        scope.EnsureCanAccess(ticket);

        ticket.IsDeleted = true;
        ticket.DeletedAt = clock.UtcNow;
        ticket.DeletedBy = currentUser.UserId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<TicketDetailDto> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Ticket), id);

        scope.EnsureCanAccess(ticket);

        if (ticket.AssignedAgentId == request.AgentId)
            return await GetByIdAsync(ticket.Id, ct);

        if (request.AgentId is { } agentId)
        {
            var agents = await identity.GetAgentsAsync(null, ct);
            if (agents.All(a => a.Id != agentId))
                throw new BadRequestException("The selected user is not an assignable agent.");
        }

        var now = clock.UtcNow;
        AddHistory(ticket, nameof(Ticket.AssignedAgentId),
            ticket.AssignedAgentId?.ToString(), request.AgentId?.ToString(), request.Note, now);

        ticket.AssignedAgentId = request.AgentId;
        ticket.AssignedAt = request.AgentId is null ? null : now;

        // Picking up a brand-new ticket moves it into the working queue in one step,
        // so an agent does not have to assign and then separately open it.
        if (request.AgentId is not null && ticket.Status == TicketStatus.New)
        {
            AddHistory(ticket, nameof(Ticket.Status), TicketStatus.New.ToString(), TicketStatus.Open.ToString(), null, now);
            ticket.Status = TicketStatus.Open;
        }

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(ticket.Id, ct);
    }

    public async Task<TicketDetailDto> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Ticket), id);

        scope.EnsureCanAccess(ticket);

        if (ticket.Status == request.Status)
            return await GetByIdAsync(ticket.Id, ct);

        if (!TicketWorkflow.CanTransition(ticket.Status, request.Status))
            throw new ConflictException($"Cannot move a ticket from {ticket.Status} to {request.Status}.");

        var now = clock.UtcNow;
        AddHistory(ticket, nameof(Ticket.Status), ticket.Status.ToString(), request.Status.ToString(), request.Note, now);

        var previous = ticket.Status;
        ticket.Status = request.Status;

        switch (request.Status)
        {
            case TicketStatus.Resolved:
                ticket.ResolvedAt = now;
                break;
            case TicketStatus.Closed:
                ticket.ClosedAt = now;
                // Closing straight from an active state still counts as a resolution for
                // SLA reporting; without this the resolution time would read as null.
                ticket.ResolvedAt ??= now;
                break;
            case TicketStatus.Reopened:
                // The clock restarts: the previous resolution no longer stands.
                ticket.ResolvedAt = null;
                ticket.ClosedAt = null;
                break;
        }

        // First agent response stops the response-time clock (area 5).
        if (ticket.FirstRespondedAt is null && previous == TicketStatus.New)
            ticket.FirstRespondedAt = now;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(ticket.Id, ct);
    }

    // ---- Comments ----

    public async Task<IReadOnlyList<TicketCommentDto>> GetCommentsAsync(Guid ticketId, bool includeInternal, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        var q = db.TicketComments.AsNoTracking().Where(c => c.TicketId == ticketId);
        if (!includeInternal) q = q.Where(c => !c.IsInternal);

        var comments = await q.OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var names = await ResolveNamesAsync(comments.Select(c => c.AuthorId), ct);

        return comments
            .Select(c => new TicketCommentDto(c.Id, c.TicketId, c.Body, c.IsInternal, c.AuthorId, Lookup(names, c.AuthorId), c.CreatedAt))
            .ToList();
    }

    public async Task<TicketCommentDto> AddCommentAsync(Guid ticketId, CreateTicketCommentRequest request, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        scope.EnsureCanAccess(ticket);

        var now = clock.UtcNow;

        var comment = new TicketComment
        {
            TicketId = ticketId,
            Body = request.Body.Trim(),
            IsInternal = request.IsInternal,
            AuthorId = currentUser.UserId
        };

        db.TicketComments.Add(comment);

        // Only a customer-visible reply counts as the first response; an internal note
        // between agents does not satisfy the SLA.
        if (!request.IsInternal)
        {
            ticket.FirstRespondedAt ??= now;

            db.Interactions.Add(new Interaction
            {
                CustomerId = ticket.CustomerId,
                TicketId = ticket.Id,
                Channel = ticket.Channel,
                Direction = InteractionDirection.Outbound,
                Subject = ticket.Subject,
                Body = comment.Body,
                OccurredAt = now,
                AgentId = currentUser.UserId
            });

            if (ticket.Status == TicketStatus.New)
            {
                AddHistory(ticket, nameof(Ticket.Status), TicketStatus.New.ToString(), TicketStatus.Open.ToString(), null, now);
                ticket.Status = TicketStatus.Open;
            }
        }

        await db.SaveChangesAsync(ct);

        var names = await ResolveNamesAsync([comment.AuthorId], ct);
        return new TicketCommentDto(comment.Id, ticketId, comment.Body, comment.IsInternal,
            comment.AuthorId, Lookup(names, comment.AuthorId), comment.CreatedAt);
    }

    // ---- History ----

    public async Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(Guid ticketId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        var rows = await db.TicketHistory.AsNoTracking()
            .Where(h => h.TicketId == ticketId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync(ct);

        // History stores raw ids; resolve both the actor and any agent id that appears
        // as a value so the trail reads as names rather than GUIDs.
        var actorIds = rows.Select(h => h.ChangedBy);
        var valueIds = rows
            .Where(h => h.Field == nameof(Ticket.AssignedAgentId))
            .SelectMany(h => new[] { h.OldValue, h.NewValue })
            .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null);

        var names = await ResolveNamesAsync(actorIds.Concat(valueIds), ct);

        return rows.Select(h => new TicketHistoryDto(
            h.Id,
            h.Field,
            Humanize(h.Field, h.OldValue, names),
            Humanize(h.Field, h.NewValue, names),
            h.Note,
            h.ChangedBy,
            Lookup(names, h.ChangedBy),
            h.ChangedAt)).ToList();
    }

    private static string? Humanize(string field, string? value, IReadOnlyDictionary<Guid, string> names)
    {
        if (value is null) return null;
        if (field != nameof(Ticket.AssignedAgentId)) return value;
        return Guid.TryParse(value, out var id) && names.TryGetValue(id, out var name) ? name : value;
    }

    // ---- Agent dashboard (area 4) ----

    public async Task<AgentDashboardDto> GetAgentDashboardAsync(Guid agentId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var activeStatuses = TicketWorkflow.ActiveStatuses;

        var mine = scope.Apply(db.Tickets.AsNoTracking()).Where(t => t.AssignedAgentId == agentId);

        var byStatus = await mine
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byPriority = await mine
            .Where(t => activeStatuses.Contains(t.Status))
            .GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var assignedActive = byStatus.Where(s => activeStatuses.Contains(s.Status)).Sum(s => s.Count);
        var assignedNew = byStatus.Where(s => s.Status == TicketStatus.New).Sum(s => s.Count);

        var assignedOverdue = await mine
            .CountAsync(t => activeStatuses.Contains(t.Status)
                             && t.ResolutionDueAt != null
                             && t.ResolutionDueAt < now, ct);

        // The agent's own department defines "my team's queue"; agents without a
        // department see the global unassigned pool.
        var departmentId = await mine.Select(t => t.DepartmentId).FirstOrDefaultAsync(ct);

        var unassigned = await scope.Apply(db.Tickets.AsNoTracking())
            .CountAsync(t => t.AssignedAgentId == null
                             && activeStatuses.Contains(t.Status)
                             && (departmentId == null || t.DepartmentId == departmentId), ct);

        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var resolvedToday = await mine.CountAsync(t => t.ResolvedAt != null && t.ResolvedAt >= startOfDay, ct);

        var recentRows = await mine
            .Where(t => activeStatuses.Contains(t.Status))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(10)
            .Select(t => new TicketRow(
                t.Id, t.Number, t.Subject, t.Status, t.Priority, t.Channel,
                t.CustomerId, t.Customer!.FullNameAr, t.Customer.FullNameEn,
                t.CategoryId,
                t.Category != null ? t.Category.NameAr : null,
                t.Category != null ? t.Category.NameEn : null,
                t.AssignedAgentId,
                t.Department != null ? t.Department.NameAr : null,
                t.Department != null ? t.Department.NameEn : null,
                t.EscalationLevel, t.ResolutionDueAt, t.CreatedAt, t.ModifiedAt))
            .ToListAsync(ct);

        return new AgentDashboardDto(
            assignedActive,
            assignedNew,
            assignedOverdue,
            unassigned,
            resolvedToday,
            byStatus.ToDictionary(s => s.Status, s => s.Count),
            byPriority.ToDictionary(p => p.Priority, p => p.Count),
            await ToListItemsAsync(recentRows, ct));
    }

    // ---- helpers ----

    /// <summary>Flat projection of the columns every ticket list needs. Kept as a record so
    /// the same EF projection serves search and the dashboard.</summary>
    private sealed record TicketRow(
        Guid Id, string Number, string Subject, TicketStatus Status, TicketPriority Priority,
        CommunicationChannel Channel, Guid CustomerId, string CustomerNameAr, string CustomerNameEn,
        Guid? CategoryId, string? CategoryNameAr, string? CategoryNameEn, Guid? AssignedAgentId,
        string? DepartmentNameAr, string? DepartmentNameEn, int EscalationLevel,
        DateTimeOffset? ResolutionDueAt, DateTimeOffset CreatedAt, DateTimeOffset? ModifiedAt);

    private async Task<IReadOnlyList<TicketListItemDto>> ToListItemsAsync(IReadOnlyList<TicketRow> rows, CancellationToken ct)
    {
        var names = await ResolveNamesAsync(rows.Select(r => r.AssignedAgentId), ct);

        return rows.Select(r => new TicketListItemDto(
            r.Id, r.Number, r.Subject, r.Status, r.Priority, r.Channel,
            r.CustomerId, r.CustomerNameAr, r.CustomerNameEn,
            r.CategoryId, r.CategoryNameAr, r.CategoryNameEn,
            r.AssignedAgentId, Lookup(names, r.AssignedAgentId),
            r.DepartmentNameAr, r.DepartmentNameEn,
            r.EscalationLevel, r.ResolutionDueAt, r.CreatedAt, r.ModifiedAt)).ToList();
    }

    private void AddHistory(Ticket ticket, string field, string? oldValue, string? newValue, string? note, DateTimeOffset at)
    {
        var entry = new TicketHistory
        {
            TicketId = ticket.Id,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ChangedBy = currentUser.UserId,
            ChangedAt = at
        };

        db.TicketHistory.Add(entry);
    }

    private void TrackChange(Ticket ticket, string field, string? oldValue, string? newValue, DateTimeOffset at)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
        AddHistory(ticket, field, oldValue, newValue, null, at);
    }

    private static string? Lookup(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } key && names.TryGetValue(key, out var name) ? name : null;

    private Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct) =>
        identity.GetUserDisplayNamesAsync(ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct(), ct);

    /// <summary>The single scope checkpoint for comments and history: both are only ever
    /// reached through their ticket, so gating the parent gates both.</summary>
    private async Task EnsureTicketExistsAsync(Guid ticketId, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => new ScopeCheck(t.DepartmentId, t.BranchId))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        scope.EnsureCanAccess(ticket);
    }

    /// <summary>Lightweight carrier so a projection can be scope-checked without loading
    /// the whole entity.</summary>
    private sealed record ScopeCheck(Guid? DepartmentId, Guid? BranchId) : IScopedEntity;

    private async Task GuardLookupsAsync(Guid? categoryId, Guid? departmentId, Guid? branchId, CancellationToken ct)
    {
        if (categoryId is { } c && !await db.TicketCategories.AnyAsync(x => x.Id == c, ct))
            throw new BadRequestException("The selected category does not exist.");

        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct))
            throw new BadRequestException("The selected department does not exist.");

        if (branchId is { } b && !await db.Branches.AnyAsync(x => x.Id == b, ct))
            throw new BadRequestException("The selected branch does not exist.");
    }

    private static TicketDetailDto ToDetail(
        Ticket t,
        string? agentName,
        IReadOnlyList<TagDto> tags,
        IReadOnlyList<WatcherDto> watchers,
        IReadOnlyList<TicketLinkDto> links) => new(
        t.Id, t.Number, t.Subject, t.Description, t.Status, t.Priority, t.Channel,
        t.CustomerId, t.Customer!.Code, t.Customer.FullNameAr, t.Customer.FullNameEn,
        t.Customer.Email, t.Customer.Phone,
        t.CategoryId, t.Category?.NameAr, t.Category?.NameEn,
        t.AssignedAgentId, agentName, t.AssignedAt,
        t.DepartmentId, t.Department?.NameAr, t.Department?.NameEn,
        t.BranchId, t.Branch?.NameAr, t.Branch?.NameEn,
        t.EscalationLevel,
        t.FirstResponseDueAt, t.ResolutionDueAt, t.FirstRespondedAt,
        t.ResolvedAt, t.ClosedAt, t.CreatedAt, t.ModifiedAt,
        TicketWorkflow.AllowedTransitions(t.Status),
        tags,
        watchers,
        links);
}

using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Application.Tickets;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Application.Workspace.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Workspace;

public interface IWorkspaceService
{
    Task<AgentWorkspaceDto> GetMyWorkspaceAsync(CancellationToken ct = default);
    Task<TeamDashboardDto> GetTeamDashboardAsync(Guid? departmentId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentTaskDto>> ListTasksAsync(AgentTaskQuery query, CancellationToken ct = default);
    Task<AgentTaskDto> CreateTaskAsync(SaveAgentTaskRequest request, CancellationToken ct = default);
    Task<AgentTaskDto> UpdateTaskAsync(Guid id, SaveAgentTaskRequest request, CancellationToken ct = default);
    Task<AgentTaskDto> SetTaskDoneAsync(Guid id, bool isDone, CancellationToken ct = default);
    Task DeleteTaskAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<QuickReplyDto>> ListQuickRepliesAsync(CancellationToken ct = default);
    Task<QuickReplyDto> CreateQuickReplyAsync(SaveQuickReplyRequest request, CancellationToken ct = default);
    Task<QuickReplyDto> UpdateQuickReplyAsync(Guid id, SaveQuickReplyRequest request, CancellationToken ct = default);
    Task DeleteQuickReplyAsync(Guid id, CancellationToken ct = default);
}

/// <summary>The agent's own workspace (PDF area 4): their board, personal tasks and
/// reminders, saved replies, and the supervisor's view across a team.</summary>
public sealed class WorkspaceService(
    IAppDbContext db,
    ITicketService tickets,
    ISlaService sla,
    IIdentityService identity,
    ICurrentUser currentUser,
    IScopeProvider scope,
    IClock clock) : IWorkspaceService
{
    /// <summary>How many rows each board panel shows. Small on purpose: a board is a
    /// starting point, and anything longer belongs in the ticket list with its filters.</summary>
    private const int PanelSize = 10;

    // ---- Boards ----

    public async Task<AgentWorkspaceDto> GetMyWorkspaceAsync(CancellationToken ct = default)
    {
        var userId = RequireUser();

        var dashboard = await tickets.GetAgentDashboardAsync(userId, ct);

        var openTasks = await TasksQuery(userId, includeDone: false)
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .Take(PanelSize)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var overdueTasks = await TasksQuery(userId, includeDone: false)
            .CountAsync(t => t.DueAt != null && t.DueAt < now, ct);

        var mentionedIds = await db.TicketMentions.AsNoTracking()
            .Where(m => m.MentionedUserId == userId)
            .OrderByDescending(m => m.MentionedAt)
            .Select(m => m.TicketId)
            .Distinct()
            .Take(PanelSize)
            .ToListAsync(ct);

        var watchedIds = await db.TicketWatchers.AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => w.TicketId)
            .Take(PanelSize)
            .ToListAsync(ct);

        return new AgentWorkspaceDto(
            dashboard,
            await ToTaskDtosAsync(openTasks, ct),
            overdueTasks,
            await tickets.GetListItemsAsync(mentionedIds, ct),
            await tickets.GetListItemsAsync(watchedIds, ct));
    }

    public async Task<TeamDashboardDto> GetTeamDashboardAsync(Guid? departmentId, CancellationToken ct = default)
    {
        if (!currentUser.HasPermission(Auth.Permissions.Dashboard.ViewTeam))
            throw new ForbiddenException("You may not view the team dashboard.");

        var open = TicketWorkflow.ActiveStatuses;
        var now = clock.UtcNow;
        var today = now.Date;

        var q = scope.Apply(db.Tickets.AsNoTracking());
        if (departmentId is { } id) q = q.Where(t => t.DepartmentId == id);

        var activeQuery = q.Where(t => open.Contains(t.Status));

        var totalActive = await activeQuery.CountAsync(ct);
        var totalUnassigned = await activeQuery.CountAsync(t => t.AssignedAgentId == null, ct);
        var totalOverdue = await activeQuery.CountAsync(t => t.ResolutionDueAt != null && t.ResolutionDueAt < now, ct);
        var resolvedToday = await q.CountAsync(t => t.ResolvedAt != null && t.ResolvedAt >= today, ct);

        var byStatus = await q.GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);

        var byPriority = await activeQuery.GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() }).ToListAsync(ct);

        // Per-agent load, counted in SQL rather than by walking tickets in memory.
        var perAgent = await activeQuery
            .Where(t => t.AssignedAgentId != null)
            .GroupBy(t => t.AssignedAgentId!.Value)
            .Select(g => new
            {
                AgentId = g.Key,
                Active = g.Count(),
                Overdue = g.Count(t => t.ResolutionDueAt != null && t.ResolutionDueAt < now)
            })
            .ToListAsync(ct);

        var resolvedTodayPerAgent = await q
            .Where(t => t.ResolvedAt != null && t.ResolvedAt >= today && t.AssignedAgentId != null)
            .GroupBy(t => t.AssignedAgentId!.Value)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, ct);

        var agents = await identity.GetAgentsAsync(departmentId, ct);

        // At-risk is an SLA judgement, not a column, so it is computed from the clocks of the
        // active tickets rather than guessed from the due date alone.
        var activeIds = await activeQuery.Select(t => t.Id).ToListAsync(ct);
        var statuses = await sla.GetStatusesAsync(activeIds, ct);

        var atRiskByAgent = new Dictionary<Guid, int>();
        var totalAtRisk = 0;

        var assignments = await activeQuery
            .Where(t => t.AssignedAgentId != null)
            .Select(t => new { t.Id, AgentId = t.AssignedAgentId!.Value })
            .ToListAsync(ct);

        foreach (var (ticketId, status) in statuses)
        {
            var atRisk = status.FirstResponse.State == SlaState.AtRisk
                         || status.Resolution.State == SlaState.AtRisk;

            if (!atRisk) continue;

            totalAtRisk++;

            var owner = assignments.FirstOrDefault(a => a.Id == ticketId)?.AgentId;
            if (owner is { } agentId)
                atRiskByAgent[agentId] = atRiskByAgent.GetValueOrDefault(agentId) + 1;
        }

        var members = agents.Select(agent =>
        {
            var load = perAgent.FirstOrDefault(p => p.AgentId == agent.Id);

            return new TeamMemberLoadDto(
                agent.Id, agent.FullNameAr, agent.FullNameEn, agent.DepartmentId,
                load?.Active ?? 0,
                load?.Overdue ?? 0,
                atRiskByAgent.GetValueOrDefault(agent.Id),
                resolvedTodayPerAgent.GetValueOrDefault(agent.Id));
        })
        .OrderByDescending(m => m.Overdue)
        .ThenByDescending(m => m.Active)
        .ToList();

        var oldestUnassignedIds = await activeQuery
            .Where(t => t.AssignedAgentId == null)
            .OrderBy(t => t.CreatedAt)
            .Take(PanelSize)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return new TeamDashboardDto(
            totalActive, totalUnassigned, totalOverdue, totalAtRisk, resolvedToday,
            byStatus.ToDictionary(s => s.Status, s => s.Count),
            byPriority.ToDictionary(p => p.Priority, p => p.Count),
            members,
            await tickets.GetListItemsAsync(oldestUnassignedIds, ct));
    }

    // ---- Tasks ----

    public async Task<IReadOnlyList<AgentTaskDto>> ListTasksAsync(
        AgentTaskQuery query, CancellationToken ct = default)
    {
        var userId = RequireUser();
        var q = TasksQuery(userId, query.IncludeDone);

        if (query.DueBefore is { } before) q = q.Where(t => t.DueAt != null && t.DueAt <= before);

        var rows = await q
            // Undated tasks sort last: a task with a deadline is the one that needs deciding.
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .ThenByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(query.Take, 1, 200))
            .ToListAsync(ct);

        return await ToTaskDtosAsync(rows, ct);
    }

    public async Task<AgentTaskDto> CreateTaskAsync(SaveAgentTaskRequest request, CancellationToken ct = default)
    {
        var userId = RequireUser();
        Guard(request);

        var task = new AgentTask
        {
            OwnerUserId = userId,
            DepartmentId = scope.DepartmentId,
            BranchId = scope.BranchId
        };

        Apply(task, request);
        await ResolveLinksAsync(task, request, ct);

        db.AgentTasks.Add(task);
        await db.SaveChangesAsync(ct);

        return (await ToTaskDtosAsync([task], ct))[0];
    }

    public async Task<AgentTaskDto> UpdateTaskAsync(
        Guid id, SaveAgentTaskRequest request, CancellationToken ct = default)
    {
        Guard(request);

        var task = await OwnTaskAsync(id, ct);
        Apply(task, request);
        await ResolveLinksAsync(task, request, ct);

        await db.SaveChangesAsync(ct);
        return (await ToTaskDtosAsync([task], ct))[0];
    }

    public async Task<AgentTaskDto> SetTaskDoneAsync(Guid id, bool isDone, CancellationToken ct = default)
    {
        var task = await OwnTaskAsync(id, ct);

        task.IsDone = isDone;
        task.CompletedAt = isDone ? clock.UtcNow : null;

        await db.SaveChangesAsync(ct);
        return (await ToTaskDtosAsync([task], ct))[0];
    }

    public async Task DeleteTaskAsync(Guid id, CancellationToken ct = default)
    {
        var task = await OwnTaskAsync(id, ct);

        task.IsDeleted = true;
        task.DeletedAt = clock.UtcNow;
        task.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    // ---- Quick replies ----

    public async Task<IReadOnlyList<QuickReplyDto>> ListQuickRepliesAsync(CancellationToken ct = default)
    {
        var rows = await scope.Apply(db.QuickReplies.AsNoTracking())
            .Where(r => r.IsActive)
            .OrderBy(r => r.TitleEn)
            .Select(r => new
            {
                r.Id, r.TitleAr, r.TitleEn, r.BodyAr, r.BodyEn, r.Shortcut, r.IsActive,
                r.DepartmentId, r.BranchId
            })
            .ToListAsync(ct);

        var departments = await db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => new { d.NameAr, d.NameEn }, ct);

        return rows.Select(r =>
        {
            departments.TryGetValue(r.DepartmentId ?? Guid.Empty, out var department);

            return new QuickReplyDto(
                r.Id, r.TitleAr, r.TitleEn, r.BodyAr, r.BodyEn, r.Shortcut, r.IsActive,
                r.DepartmentId, department?.NameAr, department?.NameEn, r.BranchId);
        }).ToList();
    }

    public async Task<QuickReplyDto> CreateQuickReplyAsync(
        SaveQuickReplyRequest request, CancellationToken ct = default)
    {
        await GuardQuickReplyAsync(request, null, ct);

        var reply = new QuickReply();
        Apply(reply, request);

        db.QuickReplies.Add(reply);
        await db.SaveChangesAsync(ct);

        return (await ListQuickRepliesAsync(ct)).First(r => r.Id == reply.Id);
    }

    public async Task<QuickReplyDto> UpdateQuickReplyAsync(
        Guid id, SaveQuickReplyRequest request, CancellationToken ct = default)
    {
        await GuardQuickReplyAsync(request, id, ct);

        var reply = await db.QuickReplies.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(QuickReply), id);

        scope.EnsureCanAccess(reply);
        Apply(reply, request);

        await db.SaveChangesAsync(ct);

        return (await ListQuickRepliesAsync(ct)).First(r => r.Id == reply.Id);
    }

    public async Task DeleteQuickReplyAsync(Guid id, CancellationToken ct = default)
    {
        var reply = await db.QuickReplies.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(QuickReply), id);

        scope.EnsureCanAccess(reply);

        reply.IsDeleted = true;
        reply.DeletedAt = clock.UtcNow;
        reply.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    private IQueryable<AgentTask> TasksQuery(Guid userId, bool includeDone)
    {
        // Scoped to the owner, not to the department: a personal task list is personal, and
        // a supervisor reading it would be surprising rather than useful.
        var q = db.AgentTasks.AsNoTracking().Where(t => t.OwnerUserId == userId);

        return includeDone ? q : q.Where(t => !t.IsDone);
    }

    private async Task<AgentTask> OwnTaskAsync(Guid id, CancellationToken ct)
    {
        var userId = RequireUser();

        // Ownership is in the predicate, so someone else's task reads as missing rather than
        // forbidden — which also avoids confirming that it exists.
        return await db.AgentTasks.FirstOrDefaultAsync(t => t.Id == id && t.OwnerUserId == userId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), id);
    }

    private static void Guard(SaveAgentTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new BadRequestException("A task needs a title.");

        if (request.IsReminder && request.DueAt is null)
            throw new BadRequestException("A reminder needs a due date to fire at.");
    }

    private static void Apply(AgentTask task, SaveAgentTaskRequest request)
    {
        task.Title = request.Title.Trim();
        task.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        task.IsReminder = request.IsReminder;

        // Moving the due date re-arms the reminder: the agent has asked to be told at the
        // new time, not reminded that the old one passed.
        if (task.DueAt != request.DueAt) task.ReminderSentAt = null;
        task.DueAt = request.DueAt;
    }

    private async Task ResolveLinksAsync(AgentTask task, SaveAgentTaskRequest request, CancellationToken ct)
    {
        task.TicketId = null;
        task.CustomerId = null;

        if (request.TicketId is { } ticketId)
        {
            var ticket = await db.Tickets.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
                ?? throw new NotFoundException(nameof(Ticket), ticketId);

            scope.EnsureCanAccess(ticket);
            task.TicketId = ticketId;
        }

        if (request.CustomerId is { } customerId)
        {
            var customer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId, ct)
                ?? throw new NotFoundException(nameof(Customer), customerId);

            scope.EnsureCanAccess(customer);
            task.CustomerId = customerId;
        }
    }

    private async Task<IReadOnlyList<AgentTaskDto>> ToTaskDtosAsync(
        IReadOnlyList<AgentTask> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var names = await identity.GetUserDisplayNamesAsync(rows.Select(r => r.OwnerUserId), ct);

        var ticketIds = rows.Where(r => r.TicketId is not null).Select(r => r.TicketId!.Value).ToList();
        var ticketNumbers = ticketIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Tickets.AsNoTracking()
                .Where(t => ticketIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Number, ct);

        var customerIds = rows.Where(r => r.CustomerId is not null).Select(r => r.CustomerId!.Value).ToList();
        var customerNames = customerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullNameEn, ct);

        return rows.Select(r => new AgentTaskDto(
            r.Id, r.Title, r.Notes, r.OwnerUserId,
            names.TryGetValue(r.OwnerUserId, out var name) ? name : null,
            r.DueAt, r.IsDone, r.CompletedAt, r.IsReminder,
            r.TicketId, r.TicketId is { } tid && ticketNumbers.TryGetValue(tid, out var number) ? number : null,
            r.CustomerId, r.CustomerId is { } cid && customerNames.TryGetValue(cid, out var cname) ? cname : null,
            r.CreatedAt)).ToList();
    }

    private static void Apply(QuickReply reply, SaveQuickReplyRequest request)
    {
        reply.TitleAr = request.TitleAr.Trim();
        reply.TitleEn = request.TitleEn.Trim();
        reply.BodyAr = request.BodyAr.Trim();
        reply.BodyEn = request.BodyEn.Trim();
        reply.Shortcut = string.IsNullOrWhiteSpace(request.Shortcut) ? null : request.Shortcut.Trim();
        reply.IsActive = request.IsActive;
        reply.DepartmentId = request.DepartmentId;
        reply.BranchId = request.BranchId;
    }

    private async Task GuardQuickReplyAsync(SaveQuickReplyRequest request, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TitleAr) || string.IsNullOrWhiteSpace(request.TitleEn))
            throw new BadRequestException("A quick reply needs a title in both languages.");

        if (string.IsNullOrWhiteSpace(request.BodyAr) || string.IsNullOrWhiteSpace(request.BodyEn))
            throw new BadRequestException("A quick reply needs a body in both languages.");

        if (string.IsNullOrWhiteSpace(request.Shortcut)) return;

        var shortcut = request.Shortcut.Trim();

        // Checked here as well as by the unique index, so the caller gets a clear message
        // rather than a database error.
        var taken = await db.QuickReplies
            .AnyAsync(r => r.Shortcut == shortcut && (excludeId == null || r.Id != excludeId), ct);

        if (taken) throw new ConflictException($"The shortcut '{shortcut}' is already in use.");
    }

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new ForbiddenException(
            "Not authenticated.", ErrorCodes.NotAuthenticated);
}

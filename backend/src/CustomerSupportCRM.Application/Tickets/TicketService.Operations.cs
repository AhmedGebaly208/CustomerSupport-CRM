using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Tickets;

/// <summary>Desk-scale ticket operations (PDF area 2): bulk actions, links, merge, watchers,
/// tags and manual escalation.
///
/// Split from the core lifecycle in TicketService.cs to keep each file readable. Everything
/// here goes through the same scope checks and the same TicketWorkflow rules as the
/// single-ticket paths — these are conveniences over that behaviour, never a way around it.</summary>
public sealed partial class TicketService
{
    /// <summary>Highest level a ticket can be escalated to. A cap keeps a stuck automation
    /// or a frustrated agent from producing a meaningless number.</summary>
    private const int MaxEscalationLevel = 5;

    // ---- Bulk operations ----

    public Task<BulkOperationResult> BulkAssignAsync(BulkAssignRequest request, CancellationToken ct = default) =>
        RunBulkAsync(request.TicketIds,
            id => AssignAsync(id, new AssignTicketRequest(request.AgentId, request.Note), ct));

    public Task<BulkOperationResult> BulkChangePriorityAsync(BulkPriorityRequest request, CancellationToken ct = default) =>
        RunBulkAsync(request.TicketIds, id => ChangePriorityAsync(id, request.Priority, ct));

    public Task<BulkOperationResult> BulkChangeStatusAsync(BulkStatusRequest request, CancellationToken ct = default) =>
        RunBulkAsync(request.TicketIds,
            id => ChangeStatusAsync(id, new ChangeTicketStatusRequest(request.Status, request.Note), ct));

    /// <summary>Applies a per-ticket operation across a selection.
    ///
    /// Deliberately not wrapped in one transaction: an agent changing twenty tickets should
    /// keep the nineteen that worked rather than lose them because one had an illegal
    /// status transition. Each item reuses the single-ticket method, so the workflow rules,
    /// scope checks and history writes cannot be bypassed by going through bulk.</summary>
    private async Task<BulkOperationResult> RunBulkAsync(
        IReadOnlyList<Guid> ticketIds, Func<Guid, Task> operation)
    {
        var results = new List<BulkItemResult>(ticketIds.Count);

        foreach (var id in ticketIds)
        {
            try
            {
                await operation(id);
                results.Add(new BulkItemResult(id, true, null, null));
            }
            catch (AppException ex)
            {
                // Expected, per-item failures: forbidden by scope, illegal transition,
                // already deleted. Reported, not thrown, so the batch continues.
                results.Add(new BulkItemResult(id, false, ex.GetType().Name, ex.Message));
            }
        }

        return new BulkOperationResult(
            results.Count(r => r.Succeeded),
            results.Count(r => !r.Succeeded),
            results);
    }

    /// <summary>Priority-only change. Exists so bulk priority does not have to go through
    /// UpdateAsync, which would require the full subject and description.</summary>
    public async Task<TicketDetailDto> ChangePriorityAsync(Guid id, TicketPriority priority, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Ticket), id);

        scope.EnsureCanAccess(ticket);

        if (ticket.Status == TicketStatus.Closed)
            throw new ConflictException("A closed ticket cannot be edited. Reopen it first.");

        if (ticket.Priority == priority)
            return await GetByIdAsync(id, ct);

        AddHistory(ticket, nameof(Ticket.Priority),
            ticket.Priority.ToString(), priority.ToString(), null, clock.UtcNow);

        ticket.Priority = priority;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    // ---- Links ----

    public async Task<IReadOnlyList<TicketLinkDto>> GetLinksAsync(Guid ticketId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);
        return await LoadLinksAsync(ticketId, ct);
    }

    public async Task<IReadOnlyList<TicketLinkDto>> AddLinkAsync(
        Guid ticketId, CreateTicketLinkRequest request, CancellationToken ct = default)
    {
        if (ticketId == request.TargetTicketId)
            throw new BadRequestException("A ticket cannot be linked to itself.");

        await EnsureTicketExistsAsync(ticketId, ct);

        // The far end is scope-checked too: linking must not become a way to learn that a
        // ticket in another department exists.
        await EnsureTicketExistsAsync(request.TargetTicketId, ct);

        var alreadyLinked = await db.TicketLinks.AnyAsync(
            l => l.Type == request.Type &&
                 ((l.SourceTicketId == ticketId && l.TargetTicketId == request.TargetTicketId) ||
                  (l.SourceTicketId == request.TargetTicketId && l.TargetTicketId == ticketId)), ct);

        if (alreadyLinked)
            throw new ConflictException("These tickets are already linked in that way.");

        db.TicketLinks.Add(new TicketLink
        {
            SourceTicketId = ticketId,
            TargetTicketId = request.TargetTicketId,
            Type = request.Type
        });

        var now = clock.UtcNow;
        var (source, target) = await ReferencePairAsync(ticketId, request.TargetTicketId, ct);

        // Both sides get a history row, so the change is visible from whichever ticket the
        // reader opens.
        AddHistoryById(ticketId, "Link", null, $"{request.Type} -> {target}", null, now);
        AddHistoryById(request.TargetTicketId, "Link", null, $"{request.Type} <- {source}", null, now);

        await db.SaveChangesAsync(ct);
        return await LoadLinksAsync(ticketId, ct);
    }

    public async Task<IReadOnlyList<TicketLinkDto>> RemoveLinkAsync(
        Guid ticketId, Guid linkId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        var link = await db.TicketLinks
            .FirstOrDefaultAsync(l => l.Id == linkId &&
                                      (l.SourceTicketId == ticketId || l.TargetTicketId == ticketId), ct)
            ?? throw new NotFoundException(nameof(TicketLink), linkId);

        var now = clock.UtcNow;
        var (source, target) = await ReferencePairAsync(link.SourceTicketId, link.TargetTicketId, ct);

        link.IsDeleted = true;
        link.DeletedAt = now;
        link.DeletedBy = currentUser.UserId;

        AddHistoryById(link.SourceTicketId, "Link", $"{link.Type} -> {target}", null, null, now);
        AddHistoryById(link.TargetTicketId, "Link", $"{link.Type} <- {source}", null, null, now);

        await db.SaveChangesAsync(ct);
        return await LoadLinksAsync(ticketId, ct);
    }

    // ---- Merge ----

    /// <summary>Folds a duplicate ticket into the one that will be worked.
    ///
    /// Comments, interactions and attachments are re-pointed at the target so the customer
    /// ends up with one thread rather than two half-conversations. The source is closed
    /// with a history entry naming the target, so the trail explains where its content went.</summary>
    public async Task<TicketDetailDto> MergeAsync(
        Guid sourceId, MergeTicketRequest request, CancellationToken ct = default)
    {
        if (sourceId == request.TargetTicketId)
            throw new BadRequestException("A ticket cannot be merged into itself.");

        var source = await db.Tickets.FirstOrDefaultAsync(t => t.Id == sourceId, ct)
            ?? throw new NotFoundException(nameof(Ticket), sourceId);
        scope.EnsureCanAccess(source);

        var target = await db.Tickets.FirstOrDefaultAsync(t => t.Id == request.TargetTicketId, ct)
            ?? throw new NotFoundException(nameof(Ticket), request.TargetTicketId);
        scope.EnsureCanAccess(target);

        if (source.Status == TicketStatus.Closed)
            throw new ConflictException("A closed ticket cannot be merged. Reopen it first.");

        var now = clock.UtcNow;

        // --- Re-home the conversation ---
        await db.TicketComments.Where(c => c.TicketId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.TicketId, target.Id), ct);

        await db.Interactions.Where(i => i.TicketId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.TicketId, target.Id), ct);

        await db.Attachments
            .Where(a => a.OwnerType == AttachmentOwnerType.Ticket && a.OwnerId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.OwnerId, target.Id), ct);

        await MoveWatchersAsync(sourceId, target.Id, ct);
        await MoveTagsAsync(sourceId, target.Id, ct);

        // --- Explain the merge on the target, for whoever picks it up next ---
        db.TicketComments.Add(new TicketComment
        {
            TicketId = target.Id,
            Body = string.IsNullOrWhiteSpace(request.Reason)
                ? $"Merged from {source.Number}."
                : $"Merged from {source.Number}. {request.Reason.Trim()}",
            IsInternal = true,
            AuthorId = currentUser.UserId
        });

        // --- Close the source ---
        // Merging is the one case that closes a ticket regardless of where the workflow
        // would normally allow it from: its content now lives on the target, so leaving it
        // open would double-count the work. The history row records the exception.
        var previousStatus = source.Status;
        source.Status = TicketStatus.Closed;
        source.ClosedAt = now;
        source.ResolvedAt ??= now;

        AddHistory(source, nameof(Ticket.Status), previousStatus.ToString(), TicketStatus.Closed.ToString(),
            $"Merged into {target.Number}.", now);
        AddHistory(source, "Merge", null, target.Number, request.Reason, now);
        AddHistory(target, "Merge", null, source.Number, request.Reason, now);

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(target.Id, ct);
    }

    private async Task MoveWatchersAsync(Guid sourceId, Guid targetId, CancellationToken ct)
    {
        var sourceWatchers = await db.TicketWatchers.Where(w => w.TicketId == sourceId).ToListAsync(ct);
        if (sourceWatchers.Count == 0) return;

        var existing = await db.TicketWatchers
            .Where(w => w.TicketId == targetId)
            .Select(w => w.UserId)
            .ToListAsync(ct);

        foreach (var watcher in sourceWatchers)
        {
            // Deduplicate rather than relying on the unique index to throw.
            if (!existing.Contains(watcher.UserId))
            {
                db.TicketWatchers.Add(new TicketWatcher { TicketId = targetId, UserId = watcher.UserId });
            }

            watcher.IsDeleted = true;
            watcher.DeletedAt = clock.UtcNow;
        }
    }

    private async Task MoveTagsAsync(Guid sourceId, Guid targetId, CancellationToken ct)
    {
        var sourceTags = await db.TicketTags.Where(t => t.TicketId == sourceId).ToListAsync(ct);
        if (sourceTags.Count == 0) return;

        var existing = await db.TicketTags
            .Where(t => t.TicketId == targetId)
            .Select(t => t.TagId)
            .ToListAsync(ct);

        foreach (var link in sourceTags)
        {
            if (!existing.Contains(link.TagId))
            {
                db.TicketTags.Add(new TicketTag { TicketId = targetId, TagId = link.TagId });
            }

            link.IsDeleted = true;
            link.DeletedAt = clock.UtcNow;
        }
    }

    // ---- Watchers ----

    public async Task<IReadOnlyList<WatcherDto>> GetWatchersAsync(Guid ticketId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);
        return await LoadWatchersAsync(ticketId, ct);
    }

    public async Task<IReadOnlyList<WatcherDto>> AddWatcherAsync(
        Guid ticketId, AddWatcherRequest request, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        var callerId = currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

        // Adding yourself is always allowed. Adding a colleague is a supervisory act:
        // it puts a ticket on someone else's radar and, once the SLA story lands, sends
        // them notifications they did not ask for.
        if (request.UserId != callerId && !currentUser.HasPermission(Auth.Permissions.Dashboard.ViewTeam))
            throw new ForbiddenException("You can only add yourself as a watcher.");

        var agents = await identity.GetAgentsAsync(null, ct);
        if (agents.All(a => a.Id != request.UserId))
            throw new BadRequestException("The selected user is not an agent.");

        var already = await db.TicketWatchers.AnyAsync(w => w.TicketId == ticketId && w.UserId == request.UserId, ct);
        if (!already)
        {
            db.TicketWatchers.Add(new TicketWatcher { TicketId = ticketId, UserId = request.UserId });
            AddHistoryById(ticketId, "Watcher", null, request.UserId.ToString(), null, clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        return await LoadWatchersAsync(ticketId, ct);
    }

    public async Task<IReadOnlyList<WatcherDto>> RemoveWatcherAsync(
        Guid ticketId, Guid userId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        var callerId = currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

        if (userId != callerId && !currentUser.HasPermission(Auth.Permissions.Dashboard.ViewTeam))
            throw new ForbiddenException("You can only remove yourself as a watcher.");

        var watcher = await db.TicketWatchers
            .FirstOrDefaultAsync(w => w.TicketId == ticketId && w.UserId == userId, ct);

        if (watcher is not null)
        {
            watcher.IsDeleted = true;
            watcher.DeletedAt = clock.UtcNow;
            watcher.DeletedBy = callerId;

            AddHistoryById(ticketId, "Watcher", userId.ToString(), null, null, clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        return await LoadWatchersAsync(ticketId, ct);
    }

    // ---- Tags ----

    public async Task<IReadOnlyList<TagDto>> GetTagsAsync(Guid ticketId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);
        return await LoadTagsAsync(ticketId, ct);
    }

    public async Task<IReadOnlyList<TagDto>> AddTagAsync(
        Guid ticketId, AddTagRequest request, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        var name = request.Name.Trim();

        // Tags are created lazily on first use: an agent should be able to coin one while
        // working a ticket, not have to visit an admin screen first.
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);

        if (tag is null)
        {
            tag = new Tag { Name = name, ColorHex = request.ColorHex };
            db.Tags.Add(tag);
        }
        else if (!string.IsNullOrWhiteSpace(request.ColorHex) && tag.ColorHex != request.ColorHex)
        {
            tag.ColorHex = request.ColorHex;
        }

        var already = await db.TicketTags.AnyAsync(t => t.TicketId == ticketId && t.TagId == tag.Id, ct);
        if (!already)
        {
            db.TicketTags.Add(new TicketTag { TicketId = ticketId, Tag = tag });
            AddHistoryById(ticketId, "Tag", null, name, null, clock.UtcNow);
        }

        await db.SaveChangesAsync(ct);
        return await LoadTagsAsync(ticketId, ct);
    }

    public async Task<IReadOnlyList<TagDto>> RemoveTagAsync(
        Guid ticketId, Guid tagId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        var link = await db.TicketTags
            .Include(t => t.Tag)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId && t.TagId == tagId, ct);

        if (link is not null)
        {
            link.IsDeleted = true;
            link.DeletedAt = clock.UtcNow;
            link.DeletedBy = currentUser.UserId;

            // The Tag itself survives: other tickets may use it, and a tag that vanishes
            // when its last ticket is untagged would be surprising.
            AddHistoryById(ticketId, "Tag", link.Tag?.Name, null, null, clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        return await LoadTagsAsync(ticketId, ct);
    }

    /// <summary>Tag autocomplete for the list filter and the detail page.</summary>
    public async Task<IReadOnlyList<TagDto>> SearchTagsAsync(string? query, CancellationToken ct = default)
    {
        var q = db.Tags.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(t => t.Name.Contains(term));
        }

        return await q
            .OrderBy(t => t.Name)
            .Take(50)
            .Select(t => new TagDto(t.Id, t.Name, t.ColorHex))
            .ToListAsync(ct);
    }

    // ---- Escalation ----

    public async Task<TicketDetailDto> ChangeEscalationAsync(
        Guid ticketId, ChangeEscalationRequest request, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        scope.EnsureCanAccess(ticket);

        var target = ticket.EscalationLevel + request.Delta;

        if (target < 0)
            throw new ConflictException("The ticket is not escalated.");

        if (target > MaxEscalationLevel)
            throw new ConflictException($"Escalation level cannot exceed {MaxEscalationLevel}.");

        AddHistory(ticket, nameof(Ticket.EscalationLevel),
            ticket.EscalationLevel.ToString(), target.ToString(), request.Reason.Trim(), clock.UtcNow);

        ticket.EscalationLevel = target;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(ticketId, ct);
    }

    // ---- shared loaders ----

    private async Task<IReadOnlyList<TagDto>> LoadTagsAsync(Guid ticketId, CancellationToken ct) =>
        await db.TicketTags.AsNoTracking()
            .Where(t => t.TicketId == ticketId)
            .OrderBy(t => t.Tag!.Name)
            .Select(t => new TagDto(t.TagId, t.Tag!.Name, t.Tag.ColorHex))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<WatcherDto>> LoadWatchersAsync(Guid ticketId, CancellationToken ct)
    {
        var ids = await db.TicketWatchers.AsNoTracking()
            .Where(w => w.TicketId == ticketId)
            .Select(w => w.UserId)
            .ToListAsync(ct);

        if (ids.Count == 0) return [];

        var names = await identity.GetUserDisplayNamesAsync(ids, ct);

        return ids
            .Select(id => new WatcherDto(id, names.GetValueOrDefault(id, id.ToString())))
            .OrderBy(w => w.DisplayName)
            .ToList();
    }

    /// <summary>Loads outgoing and incoming links as one list, each mapped so the DTO's
    /// "other ticket" is the far end from this ticket's viewpoint.</summary>
    private async Task<IReadOnlyList<TicketLinkDto>> LoadLinksAsync(Guid ticketId, CancellationToken ct)
    {
        var outgoing = await db.TicketLinks.AsNoTracking()
            .Where(l => l.SourceTicketId == ticketId)
            .Select(l => new TicketLinkDto(
                l.Id, l.Type, true,
                l.TargetTicketId, l.TargetTicket!.Number, l.TargetTicket.Subject, l.TargetTicket.Status))
            .ToListAsync(ct);

        var incoming = await db.TicketLinks.AsNoTracking()
            .Where(l => l.TargetTicketId == ticketId)
            .Select(l => new TicketLinkDto(
                l.Id, l.Type, false,
                l.SourceTicketId, l.SourceTicket!.Number, l.SourceTicket.Subject, l.SourceTicket.Status))
            .ToListAsync(ct);

        return [.. outgoing, .. incoming];
    }

    private async Task<(string Source, string Target)> ReferencePairAsync(Guid sourceId, Guid targetId, CancellationToken ct)
    {
        var numbers = await db.Tickets.AsNoTracking()
            .Where(t => t.Id == sourceId || t.Id == targetId)
            .Select(t => new { t.Id, t.Number })
            .ToDictionaryAsync(t => t.Id, t => t.Number, ct);

        return (numbers.GetValueOrDefault(sourceId, "?"), numbers.GetValueOrDefault(targetId, "?"));
    }

    /// <summary>History for a ticket that is not loaded into the change tracker.</summary>
    private void AddHistoryById(Guid ticketId, string field, string? oldValue, string? newValue, string? note, DateTimeOffset at)
    {
        db.TicketHistory.Add(new TicketHistory
        {
            TicketId = ticketId,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ChangedBy = currentUser.UserId,
            ChangedAt = at
        });
    }
}

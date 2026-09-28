using System.Text.Json;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla.Dtos;
using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Sla;

/// <summary>In-app alerts (PDF area 5, "Alerts and notifications").
///
/// Every read is scoped to the signed-in user by construction — there is no "user id"
/// parameter on the list or mark-read calls, so there is no way to ask for someone else's
/// notifications by guessing an id.</summary>
public sealed class NotificationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : INotificationService
{
    /// <summary>Upper bound on a single list request. The bell menu shows far fewer; this
    /// only stops a caller asking for the entire history in one go.</summary>
    private const int MaxTake = 200;

    public async Task NotifyAsync(
        IReadOnlyCollection<Guid> userIds,
        NotificationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (userIds.Count == 0) return;

        var now = clock.UtcNow;

        var parameters = request.Parameters is { Count: > 0 }
            ? JsonSerializer.Serialize(request.Parameters)
            : null;

        // Distinct because a user can be both the assignee and a watcher of the same ticket,
        // and should be told once.
        var rows = userIds.Distinct().Select(userId => new Notification
        {
            UserId = userId,
            Kind = request.Kind,
            ParametersJson = parameters,
            TicketId = request.TicketId,
            CreatedAt = now
        });

        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationListDto> ListAsync(bool unreadOnly, int take, CancellationToken ct = default)
    {
        var userId = RequireUser();
        var limit = Math.Clamp(take <= 0 ? 20 : take, 1, MaxTake);

        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        var unreadCount = await mine.CountAsync(n => n.ReadAt == null, ct);

        var query = unreadOnly ? mine.Where(n => n.ReadAt == null) : mine;

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new
            {
                n.Id, n.Kind, n.ParametersJson, n.TicketId,
                TicketNumber = n.Ticket == null ? null : n.Ticket.Number,
                n.CreatedAt, n.ReadAt
            })
            .ToListAsync(ct);

        var items = rows.Select(n => new NotificationDto(
            n.Id, n.Kind, ReadParameters(n.ParametersJson),
            n.TicketId, n.TicketNumber, n.CreatedAt, n.ReadAt)).ToList();

        return new NotificationListDto(items, unreadCount);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUser();

        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            // Scoped by user in the predicate, so someone else's notification reads as
            // missing rather than forbidden — which also avoids confirming it exists.
            ?? throw new NotFoundException(nameof(Notification), id);

        if (notification.ReadAt is not null) return;

        notification.ReadAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var userId = RequireUser();
        var now = clock.UtcNow;

        await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    /// <summary>A row whose parameters cannot be read still renders — the client falls back
    /// to the bare message for its kind. Failing the whole list because one row is malformed
    /// would hide every other notification.</summary>
    private static IReadOnlyDictionary<string, string>? ReadParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new ForbiddenException(
            "Not authenticated.", ErrorCodes.NotAuthenticated);
}

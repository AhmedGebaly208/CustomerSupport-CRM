using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Application.Workspace;

public interface IReminderDispatcher
{
    /// <summary>Notifies the owner of every reminder that has come due, and returns how
    /// many were sent.</summary>
    Task<int> DispatchAsync(CancellationToken ct = default);
}

/// <summary>Turns due reminders into notifications (PDF area 4).
///
/// Marks each reminder as sent rather than deleting it, so the task stays on the agent's
/// board until they deal with it while never notifying twice.</summary>
public sealed class ReminderDispatcher(
    IAppDbContext db,
    INotificationService notifications,
    IClock clock,
    ILogger<ReminderDispatcher> logger) : IReminderDispatcher
{
    private const int BatchSize = 200;

    public async Task<int> DispatchAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var due = await db.AgentTasks
            .Where(t => t.IsReminder
                        && !t.IsDone
                        && t.ReminderSentAt == null
                        && t.DueAt != null
                        && t.DueAt <= now)
            .OrderBy(t => t.DueAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        foreach (var task in due)
        {
            var parameters = new Dictionary<string, string> { ["title"] = task.Title };

            await notifications.NotifyAsync(
                [task.OwnerUserId],
                new NotificationRequest(NotificationKind.TaskDue, parameters, task.TicketId),
                ct);

            task.ReminderSentAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Dispatched {Count} reminder(s).", due.Count);

        return due.Count;
    }
}

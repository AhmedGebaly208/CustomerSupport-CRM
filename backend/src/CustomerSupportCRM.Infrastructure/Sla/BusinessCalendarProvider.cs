using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Domain.Sla;
using CustomerSupportCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Infrastructure.Sla;

/// <summary>Builds the desk calendar from the BusinessHours and Holiday tables.
///
/// Cached through IConfigCache for the same reason branding is: it is read on every SLA
/// calculation and written only when an administrator edits the schedule. The cache key is
/// removed by SystemConfigService when either table changes, so a new holiday takes effect
/// on the next calculation rather than after an expiry.</summary>
public sealed class BusinessCalendarProvider(
    AppDbContext db,
    IConfigCache cache,
    IConfiguration configuration,
    ILogger<BusinessCalendarProvider> logger) : IBusinessCalendarProvider
{
    public Task<BusinessCalendar> GetAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync(SlaCacheKeys.BusinessCalendar, async () =>
        {
            var timeZone = ResolveTimeZone();

            var rows = await db.BusinessHours.AsNoTracking().ToListAsync(ct);
            var holidays = await db.Holidays.AsNoTracking().Select(h => h.Date).ToListAsync(ct);

            var week = new Dictionary<DayOfWeek, WorkingWindow>();

            foreach (var row in rows)
            {
                if (!row.IsWorkingDay || row.OpenAt is not { } open || row.CloseAt is not { } close)
                    continue;

                if (close <= open)
                {
                    // A closing time at or before the opening time would make the day
                    // zero-length and silently stall every SLA that lands on it.
                    logger.LogWarning(
                        "Business hours for {Day} close at or before they open; treating the day as closed.",
                        row.Day);
                    continue;
                }

                week[row.Day] = new WorkingWindow(TimeOnly.FromTimeSpan(open), TimeOnly.FromTimeSpan(close));
            }

            if (week.Count == 0)
            {
                // Nothing configured yet. The Saudi default is a far better answer than a
                // calendar that is closed every day, which would push every due date out to
                // the calculator's walk limit.
                logger.LogInformation("No business hours configured; using the Sunday-Thursday default.");
                return BusinessCalendar.SundayToThursday(timeZone) with
                {
                    Holidays = holidays.ToHashSet()
                };
            }

            return new BusinessCalendar(week, holidays.ToHashSet(), timeZone);
        });

    /// <summary>The zone business hours are expressed in. Configurable because a deployment
    /// outside the Kingdom should not have to edit code, but defaulted so nothing has to be
    /// set for the common case.</summary>
    private TimeZoneInfo ResolveTimeZone()
    {
        var id = configuration["Sla:TimeZone"];
        if (string.IsNullOrWhiteSpace(id)) id = "Arab Standard Time";

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Falling back to UTC keeps due dates being produced; getting them silently
            // wrong by an offset would be worse than a logged warning.
            logger.LogWarning(ex, "Time zone '{TimeZone}' was not found; falling back to UTC.", id);
            return TimeZoneInfo.Utc;
        }
    }
}

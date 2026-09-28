namespace CustomerSupportCRM.Domain.Sla;

/// <summary>Arithmetic on working time: the one place that knows a four-hour SLA target
/// raised at 4pm on a Thursday is not due at 8pm that evening.
///
/// Pure and static by design. It takes the instant it should treat as "now" from the caller
/// rather than reading a clock, so a due date recomputed later lands on the same answer and
/// the escalation evaluator can reason about the past.</summary>
public static class WorkingTimeCalculator
{
    /// <summary>A day is walked minute-window by minute-window; this caps how far the walk
    /// will look for the next open day before giving up. Ten years is far beyond any real
    /// SLA target and still terminates promptly on a calendar that is closed every day —
    /// a misconfiguration that would otherwise loop forever.</summary>
    private const int MaxDaysToWalk = 3660;

    /// <summary>The instant at which <paramref name="minutes"/> working minutes have elapsed
    /// from <paramref name="startUtc"/>.
    ///
    /// A start outside working hours rolls forward to the next opening, so a ticket raised
    /// over the weekend is measured from Sunday morning rather than being due before anyone
    /// is at a desk.</summary>
    public static DateTimeOffset Add(DateTimeOffset startUtc, int minutes, BusinessCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (minutes <= 0) return startUtc;
        if (calendar.IsAlwaysOpen) return startUtc.AddMinutes(minutes);

        var local = TimeZoneInfo.ConvertTime(startUtc, calendar.TimeZone);
        var remaining = TimeSpan.FromMinutes(minutes);
        var date = DateOnly.FromDateTime(local.DateTime);
        var timeOfDay = TimeOnly.FromDateTime(local.DateTime);

        for (var walked = 0; walked <= MaxDaysToWalk; walked++)
        {
            var window = calendar.WindowFor(date);

            if (window is not null)
            {
                // Start from the later of "now" and the day's opening: a mid-morning start
                // counts from that moment, an overnight start counts from opening.
                var from = timeOfDay > window.Opens ? timeOfDay : window.Opens;

                if (from < window.Closes)
                {
                    var available = window.Closes - from;

                    if (remaining <= available)
                        return ToUtc(date, from.Add(remaining), calendar);

                    remaining -= available;
                }
            }

            date = date.AddDays(1);
            // Every day after the first is entered at its own opening time.
            timeOfDay = TimeOnly.MinValue;
        }

        // Only reachable when the calendar has no working day at all within the walk. Falling
        // back to plain elapsed time keeps a due date that is late rather than absent, which
        // an operator can see and correct; returning null would silently disable the SLA.
        return startUtc.AddMinutes(minutes);
    }

    /// <summary>Working minutes between two instants, excluding any paused windows.
    ///
    /// Pauses are how "waiting on the customer" stops the clock: a ticket parked in Pending
    /// should not breach because the customer took a week to reply.</summary>
    public static int Elapsed(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        BusinessCalendar calendar,
        IEnumerable<(DateTimeOffset From, DateTimeOffset To)>? paused = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (toUtc <= fromUtc) return 0;

        var gross = GrossWorkingMinutes(fromUtc, toUtc, calendar);
        if (gross == 0) return 0;

        var pausedMinutes = 0;

        foreach (var (pauseFrom, pauseTo) in paused ?? [])
        {
            // Clip each pause to the measured span so a pause that started before the ticket
            // or is still open does not subtract time that was never counted.
            var clippedFrom = pauseFrom > fromUtc ? pauseFrom : fromUtc;
            var clippedTo = pauseTo < toUtc ? pauseTo : toUtc;

            if (clippedTo > clippedFrom)
                pausedMinutes += GrossWorkingMinutes(clippedFrom, clippedTo, calendar);
        }

        return Math.Max(0, gross - pausedMinutes);
    }

    /// <summary>How far through a target the elapsed time is, as a percentage. Used by the
    /// escalation rules, which fire at thresholds like "80% of the target consumed".
    ///
    /// Not capped at 100: a breach reads as 150% rather than looking the same as being
    /// exactly on time.</summary>
    public static int PercentConsumed(int elapsedMinutes, int targetMinutes)
    {
        if (targetMinutes <= 0) return 0;

        return (int)Math.Round(elapsedMinutes * 100.0 / targetMinutes, MidpointRounding.AwayFromZero);
    }

    private static int GrossWorkingMinutes(DateTimeOffset fromUtc, DateTimeOffset toUtc, BusinessCalendar calendar)
    {
        if (calendar.IsAlwaysOpen)
            return (int)Math.Min(int.MaxValue, (toUtc - fromUtc).TotalMinutes);

        var localFrom = TimeZoneInfo.ConvertTime(fromUtc, calendar.TimeZone);
        var localTo = TimeZoneInfo.ConvertTime(toUtc, calendar.TimeZone);

        var date = DateOnly.FromDateTime(localFrom.DateTime);
        var lastDate = DateOnly.FromDateTime(localTo.DateTime);

        var startTime = TimeOnly.FromDateTime(localFrom.DateTime);
        var endTime = TimeOnly.FromDateTime(localTo.DateTime);

        var total = 0.0;

        for (var walked = 0; date <= lastDate && walked <= MaxDaysToWalk; walked++, date = date.AddDays(1))
        {
            var window = calendar.WindowFor(date);
            if (window is null) continue;

            // Clamp the day's open period to the slice being measured.
            var dayFrom = date == DateOnly.FromDateTime(localFrom.DateTime) && startTime > window.Opens
                ? startTime
                : window.Opens;

            var dayTo = date == lastDate && endTime < window.Closes
                ? endTime
                : window.Closes;

            if (dayTo > dayFrom) total += (dayTo - dayFrom).TotalMinutes;
        }

        return (int)Math.Min(int.MaxValue, total);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, BusinessCalendar calendar)
    {
        var unspecified = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var offset = calendar.TimeZone.GetUtcOffset(unspecified);

        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}

namespace CustomerSupportCRM.Domain.Sla;

/// <summary>The open period of a single working day, in the calendar's local time.</summary>
///
/// Named WorkingWindow rather than BusinessHours because that name is already taken by the
/// configuration entity this is built from. The entity is a stored row; this is the value
/// the arithmetic runs on.
public sealed record WorkingWindow(TimeOnly Opens, TimeOnly Closes)
{
    public bool IsEmpty => Closes <= Opens;
}

/// <summary>When a support desk is open: a weekly pattern, the dates that override it, and
/// the time zone both are expressed in.
///
/// SLA targets are quoted in working minutes ("four business hours to first reply"), so
/// every due date depends on this. It is a value type with no persistence concerns — the
/// Application layer builds one from the BusinessHours and Holiday tables.</summary>
public sealed record BusinessCalendar(
    IReadOnlyDictionary<DayOfWeek, WorkingWindow> Week,
    IReadOnlySet<DateOnly> Holidays,
    TimeZoneInfo TimeZone)
{
    /// <summary>A calendar that never closes. Used by policies that count elapsed time
    /// literally — a critical incident does not stop breaching at 5pm.</summary>
    public static BusinessCalendar TwentyFourSeven(TimeZoneInfo timeZone) =>
        new(
            Enum.GetValues<DayOfWeek>().ToDictionary(
                day => day,
                _ => new WorkingWindow(TimeOnly.MinValue, TimeOnly.MaxValue)),
            new HashSet<DateOnly>(),
            timeZone);

    /// <summary>The Saudi default: Sunday to Thursday, 09:00–17:00, weekend Friday and
    /// Saturday. Used for the seed and as the fallback when nothing is configured.</summary>
    public static BusinessCalendar SundayToThursday(TimeZoneInfo timeZone) =>
        new(
            new Dictionary<DayOfWeek, WorkingWindow>
            {
                [DayOfWeek.Sunday] = new(new TimeOnly(9, 0), new TimeOnly(17, 0)),
                [DayOfWeek.Monday] = new(new TimeOnly(9, 0), new TimeOnly(17, 0)),
                [DayOfWeek.Tuesday] = new(new TimeOnly(9, 0), new TimeOnly(17, 0)),
                [DayOfWeek.Wednesday] = new(new TimeOnly(9, 0), new TimeOnly(17, 0)),
                [DayOfWeek.Thursday] = new(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            },
            new HashSet<DateOnly>(),
            timeZone);

    /// <summary>True when the desk is open for at least part of the given local date.</summary>
    public bool IsWorkingDay(DateOnly date) =>
        !Holidays.Contains(date)
        && Week.TryGetValue(date.DayOfWeek, out var window)
        && !window.IsEmpty;

    /// <summary>The open period on a date, or null when the desk is closed that day.</summary>
    public WorkingWindow? WindowFor(DateOnly date) =>
        IsWorkingDay(date) && Week.TryGetValue(date.DayOfWeek, out var window) ? window : null;

    /// <summary>True when this calendar is open continuously, which lets the calculator skip
    /// the day-by-day walk entirely.</summary>
    public bool IsAlwaysOpen =>
        Holidays.Count == 0
        && Enum.GetValues<DayOfWeek>().All(day =>
            Week.TryGetValue(day, out var window)
            && window.Opens == TimeOnly.MinValue
            && window.Closes == TimeOnly.MaxValue);
}

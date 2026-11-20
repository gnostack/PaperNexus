using Cronos;
using CronExpressionDescriptor;

namespace PaperNexus.Core;

// The numbers an interval unit allows: a range typed into a number box, or, when FixedValues is
// set, only those values, picked from a list (the phone's minutes: 15, 20 and 30, because every
// other minute count either changes faster than every 15 minutes or does not keep its spacing
// across the hour, issue #203 step 5).
public sealed record IntervalRule(IntervalType Type, int Minimum, int Maximum, IReadOnlyList<int>? FixedValues = null)
{
    public bool HasFixedValues => FixedValues is { Count: > 0 };
}

// The rules for the slideshow's schedule, written once for every screen that edits it: the
// cron expression an interval stands for, whether a typed expression is valid, and the sentence
// that describes a schedule.
public static class SlideshowSchedule
{
    // Every interval unit with the range the desktop offers, in the order the unit picker lists
    // them. A device offers these or its own (IDeviceSettingRules.IntervalTypes).
    public static readonly IReadOnlyList<IntervalRule> IntervalRules = new[]
    {
        new IntervalRule(IntervalType.Seconds, 1, 59),
        new IntervalRule(IntervalType.Minutes, 1, 59),
        new IntervalRule(IntervalType.Hours,   1, 23),
        new IntervalRule(IntervalType.Days,    1, 28),
        new IntervalRule(IntervalType.Weeks,   1,  4),
        new IntervalRule(IntervalType.Months,  1, 12),
        new IntervalRule(IntervalType.Years,   1,  1),
    };

    public const string EmptyCronText = "Enter a cron expression, e.g. 0 9 * * 1-5";
    public const string InvalidCronText = "Invalid  ·  Fields: minute  hour  day  month  weekday\nExample: 0 9 * * 1-5  (9:00 AM on weekdays)";

    // The expression the slideshow runs on: the one an interval stands for in interval mode,
    // the typed one in cron mode.
    public static string EffectiveCron(SlideshowScheduleMode mode, int interval, IntervalType type, string typedCron)
    {
        if (mode == SlideshowScheduleMode.Interval)
            return BuildIntervalCron(interval, type);
        return typedCron ?? string.Empty;
    }

    // Converts a numeric interval and unit type to a valid cron expression.
    // All field values are clamped to their legal ranges.
    public static string BuildIntervalCron(int interval, IntervalType type)
    {
        var n = Math.Max(1, interval);
        return type switch
        {
            IntervalType.Seconds => $"*/{Math.Min(n, 59)} * * * * *",
            IntervalType.Minutes => $"*/{Math.Min(n, 59)} * * * *",
            IntervalType.Hours => $"0 */{Math.Min(n, 23)} * * *",
            IntervalType.Days => $"0 0 */{Math.Min(n, 28)} * *",
            IntervalType.Weeks => $"0 0 */{Math.Min(n * 7, 28)} * *",
            IntervalType.Months => $"0 0 1 */{Math.Min(n, 12)} *",
            _ => "0 0 1 1 *", // Years
        };
    }

    // Whether a typed expression parses, as five fields or six with seconds first.
    public static bool IsValidCron(string expression)
    {
        try
        {
            Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Parses a cron expression, auto-detecting 5-field (standard) or 6-field (with seconds) format.
    public static CronExpression Parse(string expression)
    {
        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        return CronExpression.Parse(expression, format);
    }

    // Where and how far the shortest gap between changes is looked for: four years and a day
    // from the start of a leap year, so February 29 and every pair of consecutive days a
    // schedule's day fields allow are inside it.
    private static readonly DateTimeOffset GapSearchStart = new(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static readonly TimeSpan GapSearchLength = TimeSpan.FromDays((4 * 365) + 1 + 1);

    // Whether two consecutive changes on expression can come less than gap apart. A device's
    // fastest slideshow (the phone's 15 minutes, issue #203 step 5) refuses such a schedule. An
    // expression that does not parse is not faster: its own check refuses it.
    public static bool IsFasterThan(string expression, TimeSpan gap)
    {
        if (gap <= TimeSpan.Zero)
            return false;
        var shortest = ShortestGap(expression, stopBelow: gap);
        return shortest is not null && shortest.Value < gap;
    }

    // The shortest time between two consecutive occurrences of expression, in wall-clock time:
    // the occurrences are counted in UTC, so a daylight-saving change never shortens or
    // lengthens a gap here. Looks over GapSearchLength and stops at the first gap shorter than
    // stopBelow. Null when the expression does not parse or occurs less than twice in that time.
    public static TimeSpan? ShortestGap(string expression, TimeSpan? stopBelow = null)
    {
        CronExpression cron;
        try
        {
            cron = Parse(expression);
        }
        catch (CronFormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        var end = GapSearchStart + GapSearchLength;
        var occurrences = cron.GetOccurrences(GapSearchStart, end, TimeZoneInfo.Utc, fromInclusive: true, toInclusive: false);
        TimeSpan? shortest = null;
        DateTimeOffset? previous = null;
        foreach (var occurrence in occurrences)
        {
            if (previous is { } last)
            {
                var gap = occurrence - last;
                if (shortest is null || gap < shortest.Value)
                    shortest = gap;
                if (stopBelow is { } limit && gap < limit)
                    return shortest;
            }
            previous = occurrence;
        }
        return shortest;
    }

    // When the slideshow next changes: the first occurrence of cron at or after the later of now
    // and the last change plus shortestGap, so two changes are never closer than the device's
    // fastest slideshow, whatever is stored (a stored schedule that is too fast runs at that
    // floor). Without a floor still ahead, an occurrence exactly at now is not taken: it is the
    // change being made. Null when the schedule has no further occurrence.
    public static DateTimeOffset? NextChange(CronExpression cron, DateTimeOffset now, DateTimeOffset? lastChange, TimeSpan shortestGap, TimeZoneInfo zone)
    {
        cron.ThrowIfNull();
        zone.ThrowIfNull();
        if (lastChange is { } last)
        {
            var floor = last + shortestGap;
            if (floor > now)
                return cron.GetNextOccurrence(floor, zone, inclusive: true);
        }
        return cron.GetNextOccurrence(now, zone, inclusive: false);
    }

    // The sentence under the schedule: a prompt when cron mode has nothing typed, the plain
    // description of the schedule, or the field guide when it cannot be read.
    public static string Describe(SlideshowScheduleMode mode, string effectiveCron)
    {
        if (mode == SlideshowScheduleMode.CronExpression && string.IsNullOrWhiteSpace(effectiveCron))
            return EmptyCronText;

        try
        {
            return ExpressionDescriptor.GetDescription(effectiveCron);
        }
        catch
        {
            return InvalidCronText;
        }
    }
}

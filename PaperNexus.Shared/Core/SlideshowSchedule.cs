using Cronos;
using CronExpressionDescriptor;

namespace PaperNexus.Core;

// The rules for the slideshow's schedule, written once for every screen that edits it: the
// cron expression an interval stands for, whether a typed expression is valid, and the sentence
// that describes a schedule.
public static class SlideshowSchedule
{
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

using Cronos;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// Tests for WallpaperConfigViewModel.BuildIntervalCron.
// Verifies that generated expressions are correct, clamped to valid field ranges,
// and parseable by Cronos without throwing.
public class CronBuilderTests
{
    // --- Expected expressions per type ---

    [Theory]
    [InlineData(1, IntervalType.Seconds, "*/1 * * * * *")]
    [InlineData(30, IntervalType.Seconds, "*/30 * * * * *")]
    [InlineData(59, IntervalType.Seconds, "*/59 * * * * *")]
    [InlineData(1, IntervalType.Minutes, "*/1 * * * *")]
    [InlineData(30, IntervalType.Minutes, "*/30 * * * *")]
    [InlineData(59, IntervalType.Minutes, "*/59 * * * *")]
    [InlineData(1, IntervalType.Hours, "0 */1 * * *")]
    [InlineData(6, IntervalType.Hours, "0 */6 * * *")]
    [InlineData(23, IntervalType.Hours, "0 */23 * * *")]
    [InlineData(1, IntervalType.Days, "0 0 */1 * *")]
    [InlineData(7, IntervalType.Days, "0 0 */7 * *")]
    [InlineData(28, IntervalType.Days, "0 0 */28 * *")]
    [InlineData(1, IntervalType.Weeks, "0 0 */7 * *")]
    [InlineData(2, IntervalType.Weeks, "0 0 */14 * *")]
    [InlineData(4, IntervalType.Weeks, "0 0 */28 * *")]
    [InlineData(1, IntervalType.Months, "0 0 1 */1 *")]
    [InlineData(3, IntervalType.Months, "0 0 1 */3 *")]
    [InlineData(12, IntervalType.Months, "0 0 1 */12 *")]
    [InlineData(1, IntervalType.Years, "0 0 1 1 *")]
    public void BuildIntervalCron_ReturnsExpectedExpression(int interval, IntervalType type, string expected)
    {
        var result = WallpaperConfigViewModel.BuildIntervalCron(interval, type);
        Assert.Equal(expected, result);
    }

    // --- Clamping: values at or below zero clamp to 1, values above max clamp to max ---

    [Theory]
    [InlineData(0, IntervalType.Seconds)]  // clamps to 1
    [InlineData(-5, IntervalType.Seconds)]  // clamps to 1
    [InlineData(100, IntervalType.Seconds)]  // clamps to 59
    [InlineData(0, IntervalType.Minutes)]  // clamps to 1
    [InlineData(100, IntervalType.Minutes)]  // clamps to 59
    [InlineData(0, IntervalType.Hours)]    // clamps to 1
    [InlineData(50, IntervalType.Hours)]    // clamps to 23
    [InlineData(0, IntervalType.Days)]     // clamps to 1
    [InlineData(99, IntervalType.Weeks)]    // weeks * 7 clamped to 28
    [InlineData(0, IntervalType.Months)]   // clamps to 1
    [InlineData(99, IntervalType.Months)]   // clamps to 12
    public void BuildIntervalCron_ClampsToValidRange(int interval, IntervalType type)
    {
        var result = WallpaperConfigViewModel.BuildIntervalCron(interval, type);
        var fields = result.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        var ex = Record.Exception(() => CronExpression.Parse(result, format));
        Assert.Null(ex);
    }

    // --- Cronos parseability: every valid input must parse without throwing ---

    [Theory]
    [InlineData(1, IntervalType.Seconds)]
    [InlineData(30, IntervalType.Seconds)]
    [InlineData(59, IntervalType.Seconds)]
    [InlineData(1, IntervalType.Minutes)]
    [InlineData(30, IntervalType.Minutes)]
    [InlineData(59, IntervalType.Minutes)]
    [InlineData(1, IntervalType.Hours)]
    [InlineData(12, IntervalType.Hours)]
    [InlineData(23, IntervalType.Hours)]
    [InlineData(1, IntervalType.Days)]
    [InlineData(14, IntervalType.Days)]
    [InlineData(28, IntervalType.Days)]
    [InlineData(1, IntervalType.Weeks)]
    [InlineData(4, IntervalType.Weeks)]
    [InlineData(1, IntervalType.Months)]
    [InlineData(6, IntervalType.Months)]
    [InlineData(12, IntervalType.Months)]
    [InlineData(1, IntervalType.Years)]
    public void BuildIntervalCron_AllOutputs_ParseableByChronos(int interval, IntervalType type)
    {
        var expression = WallpaperConfigViewModel.BuildIntervalCron(interval, type);
        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        var ex = Record.Exception(() => CronExpression.Parse(expression, format));
        Assert.Null(ex);
    }
}

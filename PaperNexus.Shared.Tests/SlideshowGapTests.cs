using System.Diagnostics;
using Cronos;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The rules a device's fastest slideshow rests on (issue #203, step 5): the shortest gap between
// two consecutive changes of a schedule, and when the slideshow next changes with the floor of
// the last change plus that gap.
public class SlideshowGapTests
{
    private static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);

    [Theory]
    [InlineData("*/15 * * * *", false)]
    [InlineData("*/20 * * * *", false)]
    [InlineData("*/30 * * * *", false)]
    [InlineData("0 */2 * * *", false)]
    [InlineData("0 9 * * 1-5", false)]
    [InlineData("0 */15 * * * *", false)]   // six fields, seconds first, still 15 minutes apart
    [InlineData("*/5 * * * *", true)]
    [InlineData("*/16 * * * *", true)]      // :48 to the next :00 is 12 minutes
    [InlineData("*/50 * * * *", true)]      // :50 to the next :00 is 10 minutes
    [InlineData("0,10 * * * *", true)]
    [InlineData("*/30 * * * * *", true)]    // every 30 seconds
    [InlineData("0,59 0,23 * * *", true)]   // 23:59 to 00:00 the next day
    public void IsFasterThan_FifteenMinutes(string expression, bool expected)
    {
        Assert.Equal(expected, SlideshowSchedule.IsFasterThan(expression, FifteenMinutes));
    }

    [Fact]
    public void IsFasterThan_TheLeapDay_IsInsideTheSearch()
    {
        // 23:59 on February 28 and 00:00 on February 29 are a minute apart only in a leap year
        Assert.True(SlideshowSchedule.IsFasterThan("0,59 0,23 28,29 2 *", FifteenMinutes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cron")]
    [InlineData("61 * * * *")]
    public void IsFasterThan_AnExpressionThatDoesNotParse_IsNotFaster(string expression)
    {
        Assert.False(SlideshowSchedule.IsFasterThan(expression, FifteenMinutes));
    }

    [Fact]
    public void IsFasterThan_NoGap_IsNeverFaster()
    {
        // The desktop has no fastest slideshow
        Assert.False(SlideshowSchedule.IsFasterThan("*/1 * * * * *", TimeSpan.Zero));
    }

    [Theory]
    [InlineData("*/16 * * * *", 12)]
    [InlineData("*/15 * * * *", 15)]
    [InlineData("*/45 * * * *", 15)]
    [InlineData("0 9 * * 1-5", 24 * 60)]
    public void ShortestGap_IsTheSmallestDistanceBetweenConsecutiveChanges(string expression, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), SlideshowSchedule.ShortestGap(expression));
    }

    [Fact]
    public void ShortestGap_OnceInTheWholeSearch_HasNoGap()
    {
        // February 29 comes once in the four years searched
        Assert.Null(SlideshowSchedule.ShortestGap("0 0 29 2 *"));
    }

    [Fact]
    public void IsFasterThan_AScheduleThatPasses_IsCheckedQuickly()
    {
        // A schedule that passes is read over the whole search, 140,000 changes for every 15
        // minutes; the settings screen checks it as the person types
        var watch = Stopwatch.StartNew();
        var faster = SlideshowSchedule.IsFasterThan("*/15 * * * *", FifteenMinutes);
        watch.Stop();

        Assert.False(faster);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Took {watch.Elapsed}.");
    }

    // --- When the slideshow next changes ---

    private static readonly CronExpression EveryFifteen = CronExpression.Parse("*/15 * * * *");

    private static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new(2026, 11, 19, hour, minute, second, TimeSpan.Zero);

    [Fact]
    public void NextChange_NoChangeYet_IsTheNextOccurrence()
    {
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(12, 7), null, FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 15), next);
    }

    [Fact]
    public void NextChange_ExactlyOnAnOccurrence_IsTheOneAfter()
    {
        // The occurrence at now is the change being made
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(12, 15), null, FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 30), next);
    }

    [Fact]
    public void NextChange_RightAfterAChange_IsTheFloor()
    {
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(12, 15, 2), At(12, 15), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 30), next);
    }

    // The floor is counted between the times the changes were due (issue #203, step 7): a
    // change for 12:15 that ran a moment later is followed by the one for 12:30. Counted from
    // when it ran (12:15:00.5 + 15 minutes), 12:30 fell inside the floor and every 15 minutes
    // became every 30.
    [Fact]
    public void NextChange_AChangeThatRanAMomentAfterItsTime_IsFollowedByTheNextOccurrence()
    {
        var ranAt = At(12, 15).AddMilliseconds(500);

        var next = SlideshowSchedule.NextChange(EveryFifteen, ranAt, At(12, 15), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 30), next);
    }

    // Android delivers a window alarm up to 10 minutes late. The change for 12:15 that came at
    // 12:25 is followed by the one for 12:30, five minutes later: the 15 minutes hold between the
    // scheduled times, not between when Android delivered them.
    [Fact]
    public void NextChange_AChangeDeliveredAFullWindowLate_IsFollowedByTheNextOccurrence()
    {
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(12, 25, 1), At(12, 15), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 30), next);
    }

    // Delivered later than the next occurrence (the phone was asleep): that occurrence has
    // passed, so the next change is the first one after now.
    [Fact]
    public void NextChange_AChangeDeliveredAfterTheNextOccurrence_IsTheFirstOccurrenceAfterNow()
    {
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(12, 31), At(12, 15), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 45), next);
    }

    [Theory]
    [InlineData("*/15 * * * *", 15, 30)]
    [InlineData("0,15,30,45 * * * *", 15, 30)]
    [InlineData("*/20 * * * *", 20, 40)]
    [InlineData("*/30 * * * *", 30, 60)]
    [InlineData("0 */15 * * * *", 15, 30)]   // six fields, seconds first
    public void NextChange_EachScheduleTheyCanPick_KeepsItsSpacing(string expression, int dueMinute, int nextMinute)
    {
        var cron = SlideshowSchedule.Parse(expression);
        var due = At(12, 0).AddMinutes(dueMinute);

        var next = SlideshowSchedule.NextChange(cron, due.AddSeconds(1), due, FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 0).AddMinutes(nextMinute), next);
    }

    // The clock moved forward a little after a change: the floor still holds from the time the
    // change was due
    [Fact]
    public void NextChange_TheClockMovedForwardAfterAChange_KeepsTheFloor()
    {
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(12, 22), At(12, 15), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 30), next);
    }

    [Fact]
    public void NextChange_AStoredScheduleTooFast_RunsAtTheFloor()
    {
        var everyFive = CronExpression.Parse("*/5 * * * *");

        var next = SlideshowSchedule.NextChange(everyFive, At(12, 0, 1), At(12, 0), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 15), next);
    }

    [Fact]
    public void NextChange_ALongAgoChange_IsTheNextOccurrenceAfterNow()
    {
        // A phone that was off overnight changes at the next scheduled time, not at once
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(7, 3), At(0, 0), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(7, 15), next);
    }

    [Fact]
    public void NextChange_ALastChangeAfterNow_SetsNoFloor()
    {
        // The clock was set back two hours since the last change
        var next = SlideshowSchedule.NextChange(EveryFifteen, At(12, 7), At(14, 0), FifteenMinutes, TimeZoneInfo.Utc);

        Assert.Equal(At(12, 15), next);
    }

    [Fact]
    public void NextChange_ReadsTheExpressionInTheGivenZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var nineInTheMorning = CronExpression.Parse("0 9 * * *");

        var next = SlideshowSchedule.NextChange(nineInTheMorning, At(12, 0), null, FifteenMinutes, zone);

        // 9:00 in Chicago on November 19 (standard time, UTC-6) is 15:00 UTC
        Assert.Equal(At(15, 0), next?.ToUniversalTime());
    }
}

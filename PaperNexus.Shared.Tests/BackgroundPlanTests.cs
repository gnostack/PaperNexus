using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// What a device that schedules its own background work arms next (issue #203, step 5): the
// slideshow's next change and whether on the minute, and the scheduled download's next run,
// all derived from the settings.
public class BackgroundPlanTests
{
    private static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 7, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute) => new(2026, 11, 19, hour, minute, 0, TimeSpan.Zero);

    private static WallpaperNexusSettings Settings(bool slideshowOn = true, string cron = "*/15 * * * *", params WallpaperSource[] sources)
    {
        var settings = new WallpaperNexusSettings
        {
            Slideshow = new SlideshowSettings { Enabled = slideshowOn, CronExpression = cron },
            Download = new DownloadSettings { Folder = "/pictures" },
            Sources = [.. sources],
        };
        return settings;
    }

    private static WallpaperSource Source(string name, string cron = "0 */8 * * *", DateTimeOffset? lastDownload = null, bool enabled = true) => new()
    {
        Name = name,
        CronExpression = cron,
        LastDownloadUtc = lastDownload,
        IsEnabled = enabled,
    };

    private static BackgroundPlan Plan(WallpaperNexusSettings settings, DateTimeOffset? lastChange = null, DateTimeOffset? lastDownloadTry = null, bool exact = false) =>
        BackgroundPlan.For(settings, Now, lastChange, lastDownloadTry, exact, FifteenMinutes, TimeZoneInfo.Utc);

    // --- The slideshow ---

    [Fact]
    public void SlideshowOn_ArmsTheNextChange()
    {
        var plan = Plan(Settings());

        Assert.Equal(At(12, 15), plan.SlideshowAt);
        Assert.False(plan.SlideshowExact);
    }

    [Fact]
    public void SlideshowOff_ArmsNoChange()
    {
        var plan = Plan(Settings(slideshowOn: false), exact: true);

        Assert.Null(plan.SlideshowAt);
        Assert.False(plan.SlideshowExact);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cron")]
    public void AStoredScheduleThatDoesNotParse_ArmsNoChange(string cron)
    {
        var plan = Plan(Settings(cron: cron));

        Assert.Null(plan.SlideshowAt);
    }

    [Fact]
    public void ExactTiming_IsCarriedWithTheChange()
    {
        var plan = Plan(Settings(), exact: true);

        Assert.Equal(At(12, 15), plan.SlideshowAt);
        Assert.True(plan.SlideshowExact);
    }

    [Fact]
    public void TheLastChange_SetsTheFloor()
    {
        var plan = Plan(Settings(cron: "*/5 * * * *"), lastChange: At(12, 5));

        Assert.Equal(At(12, 20), plan.SlideshowAt);
    }

    // --- The scheduled download ---

    [Fact]
    public void ASourceNeverDownloaded_IsDueNow()
    {
        var plan = Plan(Settings(sources: Source("Bing")));

        Assert.Equal(Now, plan.DownloadAt);
    }

    [Fact]
    public void ASourceWhoseLastDownloadIsLaterThanNow_IsDueNow()
    {
        // The clock was set back after a download (#228): the stored time says nothing
        var plan = Plan(Settings(sources: Source("Bing", lastDownload: Now.AddDays(1))));

        Assert.Equal(Now, plan.DownloadAt);
    }

    [Fact]
    public void ASourceNeverDownloaded_AlreadyTried_WaitsForItsNextOccurrence()
    {
        var plan = Plan(Settings(sources: Source("Bing")), lastDownloadTry: At(11, 0));

        Assert.Equal(At(16, 0), plan.DownloadAt);
    }

    [Fact]
    public void ARecentlyDownloadedSource_IsDueAtItsNextOccurrence()
    {
        var plan = Plan(Settings(sources: Source("Bing", lastDownload: At(9, 0))));

        Assert.Equal(At(16, 0), plan.DownloadAt);
    }

    [Fact]
    public void AnOverdueSource_NotTriedSince_IsDueNow()
    {
        // Downloaded at 7:00 on an every-8-hours schedule: due at 8:00, last tried at 7:30
        var plan = Plan(Settings(sources: Source("Bing", lastDownload: At(7, 0))), lastDownloadTry: At(7, 30));

        Assert.Equal(Now, plan.DownloadAt);
    }

    [Fact]
    public void AnOverdueSource_TriedSince_WaitsForItsNextOccurrence()
    {
        // Due at 8:00 and tried at 8:01 (its feed failed): the next try is at 16:00
        var plan = Plan(Settings(sources: Source("Bing", lastDownload: At(7, 0))), lastDownloadTry: At(8, 1));

        Assert.Equal(At(16, 0), plan.DownloadAt);
    }

    [Fact]
    public void TheSoonestEnabledSource_SetsTheRun()
    {
        var plan = Plan(Settings(sources:
        [
            Source("Daily", cron: "0 0 * * *", lastDownload: At(0, 0)),
            Source("Hourly", cron: "0 * * * *", lastDownload: At(12, 0)),
            Source("Off", cron: "*/30 * * * *", lastDownload: null, enabled: false),
        ]));

        Assert.Equal(At(13, 0), plan.DownloadAt);
    }

    [Fact]
    public void ASourceWhoseScheduleDoesNotParse_IsLeftOut()
    {
        var plan = Plan(Settings(sources:
        [
            Source("Broken", cron: "not a cron"),
            Source("Bing", lastDownload: At(9, 0)),
        ]));

        Assert.Equal(At(16, 0), plan.DownloadAt);
    }

    [Fact]
    public void NoEnabledSource_ArmsNoDownload()
    {
        var plan = Plan(Settings(sources: Source("Off", enabled: false)));

        Assert.Null(plan.DownloadAt);
    }

    [Fact]
    public void NoDownloadFolder_ArmsNoDownload()
    {
        var settings = Settings(sources: Source("Bing"));
        settings.Download.Folder = string.Empty;

        var plan = Plan(settings);

        Assert.Null(plan.DownloadAt);
    }
}

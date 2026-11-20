using Cronos;

namespace PaperNexus;

// The desktop's download schedule: a timer inside the app's own process that runs the scheduled
// download (IDownloadWallpapers.DownloadDueAsync) at start and then at the soonest next cron
// occurrence across the enabled sources. Bootstrapper.AddInProcessDownloadSchedule registers it;
// the desktop gets it through AddInProcessSchedules. The phone uses it only until Android's own
// scheduling runs its downloads (issue #203, step 5, change 3), because Android can stop the
// phone's process at any time.
//
// Until issue #203 step 5 this loop was part of DownloadWallpapers itself; it keeps that job's
// name, so its entry in timers.json stays the same.
internal sealed class DownloadSchedule : ScheduledJobService
{
    private readonly IDownloadWallpapers _downloader;
    private readonly ISettingsStore _settingsStore;

    public DownloadSchedule(ILogger<DownloadSchedule> logger, IDownloadWallpapers downloader, ISettingsStore settingsStore, JobTimerStore timerStore)
        : base(logger, timerStore)
    {
        _downloader = downloader.ThrowIfNull();
        _settingsStore = settingsStore.ThrowIfNull();
        JobName = typeof(DownloadWallpapers).FullName;
        ExecuteOnStartup = true;
    }

    // Returns the soonest upcoming cron occurrence across all enabled sources so the
    // legacy scheduler wakes up at the right time. Falls back to 1 hour if no source
    // has a next occurrence within that window. Sources with an invalid cron expression
    // are skipped rather than crashing the scheduler into a 1-minute error loop.
    protected override async Task<DateTimeOffset> GetNextExecutionAsync(JobExecutionContext context)
    {
        var settings = await _settingsStore.LoadAsync();
        var now = DateTimeOffset.UtcNow;
        var earliest = NextRun(settings.Sources, now, TimeZoneInfo.Local, source =>
            Logger.LogWarning("Source '{Source}' has invalid cron expression '{Expression}' - skipping for next-execution calculation.", source.Name, source.CronExpression));
        return earliest;
    }

    protected override Task Execute() => _downloader.DownloadDueAsync();

    // The soonest next cron occurrence after now across the enabled sources, or an hour from
    // now when none comes sooner. invalidCron is told about each source whose expression does
    // not parse; those sources are left out.
    internal static DateTimeOffset NextRun(IEnumerable<WallpaperSource> sources, DateTimeOffset now, TimeZoneInfo zone, Action<WallpaperSource> invalidCron)
    {
        // In the zone's offset, as the occurrences are, so the logged time reads as local time
        var earliest = TimeZoneInfo.ConvertTime(now.AddHours(1), zone);
        foreach (var source in sources.Where(s => s.IsEnabled))
        {
            try
            {
                var expression = CronExpression.Parse(source.CronExpression);
                var next = expression.GetNextOccurrence(now, zone);
                if (next.HasValue && next.Value < earliest)
                    earliest = next.Value;
            }
            catch (CronFormatException)
            {
                invalidCron(source);
            }
        }
        return earliest;
    }
}

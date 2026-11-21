using Cronos;

namespace PaperNexus.Core;

// What a device that schedules its own background work (the phone, issue #203 step 5) arms
// next: when the slideshow changes the wallpaper and whether on the minute, and when the
// scheduled download runs. Null means nothing is armed for it. It is derived from the settings
// every time, so the settings stay the only record of the schedule.
public sealed record BackgroundPlan(DateTimeOffset? SlideshowAt, bool SlideshowExact, DateTimeOffset? DownloadAt)
{
    // The plan for settings at now.
    //   lastChangeFor: the time the slideshow's last change was due (not when it ran), for the
    //     device's floor, which is counted between scheduled changes.
    //   lastDownloadTry: when the scheduled download last ran, so a source that failed waits
    //     for its next occurrence instead of being tried again at once.
    //   exact: whether the change is made on the minute (Exact timing on and allowed).
    //   shortestGap: the device's fastest slideshow.
    //   zone: the time zone the cron expressions are read in.
    public static BackgroundPlan For(
        WallpaperNexusSettings settings,
        DateTimeOffset now,
        DateTimeOffset? lastChangeFor,
        DateTimeOffset? lastDownloadTry,
        bool exact,
        TimeSpan shortestGap,
        TimeZoneInfo zone)
    {
        settings.ThrowIfNull();
        zone.ThrowIfNull();
        var slideshowAt = NextSlideshowChange(settings.Slideshow, now, lastChangeFor, shortestGap, zone);
        var downloadAt = NextDownload(settings, now, lastDownloadTry, zone);
        var plan = new BackgroundPlan(slideshowAt, slideshowAt is not null && exact, downloadAt);
        return plan;
    }

    // The next change, or null when the slideshow is off or its stored expression does not
    // parse (the desktop's slideshow job treats an invalid expression as off too).
    internal static DateTimeOffset? NextSlideshowChange(SlideshowSettings slideshow, DateTimeOffset now, DateTimeOffset? lastChangeFor, TimeSpan shortestGap, TimeZoneInfo zone)
    {
        if (!slideshow.Enabled
            || string.IsNullOrWhiteSpace(slideshow.CronExpression)
            || !SlideshowSchedule.IsValidCron(slideshow.CronExpression))
            return null;
        var cron = SlideshowSchedule.Parse(slideshow.CronExpression);
        var next = SlideshowSchedule.NextChange(cron, now, lastChangeFor, shortestGap, zone);
        return next;
    }

    // When the scheduled download next runs: the soonest time any enabled source is due.
    // A source is due at the next occurrence of its cron after its last download, and at once
    // when it has never been downloaded. A due source is tried once: when the scheduled download
    // already ran at or after the time it became due (it failed, or the run was stopped), it
    // waits for its next occurrence after now, as the desktop's schedule does. Sources whose
    // expression does not parse are left out. Null when there is no download folder or no
    // enabled source with a time.
    internal static DateTimeOffset? NextDownload(WallpaperNexusSettings settings, DateTimeOffset now, DateTimeOffset? lastDownloadTry, TimeZoneInfo zone)
    {
        if (!settings.IsConfigured)
            return null;

        DateTimeOffset? earliest = null;
        foreach (var source in settings.Sources.Where(s => s.IsEnabled))
        {
            CronExpression cron;
            try
            {
                cron = CronExpression.Parse(source.CronExpression);
            }
            catch (CronFormatException)
            {
                continue;
            }

            // The one rule for when a source is due (a last download later than now counts as none)
            var due = DownloadWallpapers.DueAt(source, cron, now, zone);
            if (due is null)
                continue;

            DateTimeOffset? at = due.Value;
            if (due.Value <= now)
            {
                var alreadyTried = lastDownloadTry is { } tried && tried >= due.Value;
                at = alreadyTried ? cron.GetNextOccurrence(now, zone) : now;
            }

            if (at is { } time && (earliest is null || time < earliest.Value))
                earliest = time;
        }
        return earliest;
    }
}

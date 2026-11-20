namespace PaperNexus.Core;

// How a device that schedules its own background work (the phone, issue #203 step 5) arms it
// with the operating system, so the app's process is started for each change and each download
// run even when the app is closed. BackgroundWork decides the times (BackgroundPlan) and calls
// it; the host only passes them on. Optional: the desktop runs its schedules on timers inside its
// own process (AddInProcessSchedules) and registers none.
public interface IBackgroundScheduler
{
    // Arms the next slideshow change for at, replacing the one armed before; null cancels it.
    // exact asks for a change on the minute (Exact timing, issue #203 step 5 change 4).
    public void ArmSlideshow(DateTimeOffset? at, bool exact);

    // Arms the next scheduled download run for at (at the earliest; it waits for a network),
    // replacing the one armed before; null cancels it.
    public void ArmDownloads(DateTimeOffset? at);
}

// Whether this device lets the app change the wallpaper on the minute (Android's "Alarms &
// reminders" special access, issue #203 step 5), and asking for it. Optional: the phone only.
public interface IExactTimingPermission
{
    // True while the device allows exact timing now.
    public bool IsAllowed { get; }

    // Answers true when exact timing is allowed. When it is not, opens the device's own screen
    // for the person to allow it and answers once they come back, with what they chose.
    public Task<bool> AskAsync();
}

// What the scheduled background work last left wrong, for the tab where that work lives to say
// so the next time it opens: the Current tab for a slideshow change, the Sources tab for a
// scheduled download.
public interface IScheduledWorkFailures
{
    // Raised, on a background thread, when either fact changes.
    public event Action? Changed;

    // True when the last background slideshow change failed and no change has worked since.
    public Task<bool> SlideshowChangeFailedAsync();

    // The last scheduled download run when some of its sources failed, or null when the last
    // run worked (or none ran).
    public Task<DownloadOutcome?> ScheduledDownloadFailureAsync();

    // The person's "Download now" finished with outcome. It downloads every enabled source, so
    // when none failed it supersedes a failed scheduled run, whose line then goes.
    public Task RecordDownloadNowAsync(DownloadOutcome outcome);
}

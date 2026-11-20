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
}

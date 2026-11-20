namespace PaperNexus;

// The phone's background slideshow and downloads (issue #203, step 5): what to arm with the
// operating system and what to do when it starts the app's process for a change or a download
// run. The schedule is always derived from the settings (BackgroundPlan.For); this class only
// keeps what the work itself did, in timers.json, through JobTimerStore:
//   - the last background slideshow change: when it ran, the time it was scheduled for (so a
//     second alarm for the same time changes nothing), and whether it failed;
//   - the last scheduled download run: when it ran (a due source is tried once, then waits for
//     its next occurrence) and how many sources it tried and how many failed.
// The Current and Sources tabs read the two failures through IScheduledWorkFailures.
//
// It plans when the process starts (StartAsync), after every settings save (ISettingsStore.Saved:
// a schedule edited, the slideshow switched off, a source's download time), and after every
// change and download run. On the desktop no IBackgroundScheduler is registered and it does
// nothing: the desktop's slideshow and downloads run on timers inside its own process.
public sealed class BackgroundWork : IHostedService, IScheduledWorkFailures, IAddHostedSingleton<IScheduledWorkFailures>
{
    // Their entries in timers.json
    internal const string SlideshowJobName = "PaperNexus.BackgroundSlideshow";
    internal const string DownloadsJobName = "PaperNexus.BackgroundDownloads";

    private readonly ISettingsStore _settingsStore;
    private readonly JobTimerStore _timerStore;
    private readonly ISwitchWallpaper _switchWallpaper;
    private readonly IDownloadWallpapers _downloadWallpapers;
    private readonly IDeviceSettingRules _deviceRules;
    private readonly ILogger<BackgroundWork> _logger;
    private readonly IBackgroundScheduler? _scheduler;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;

    // One plan at a time, so two saves close together cannot arm in the wrong order
    private readonly SemaphoreSlim _planLock = new(1);
    private bool _slideshowArmed;
    private (DateTimeOffset? At, bool Exact) _armedSlideshow;
    private bool _downloadsArmed;
    private DateTimeOffset? _armedDownloads;
    // While a download run is in progress its job is not armed again: arming the job the
    // operating system is running stops it
    private int _downloadsRunning;

    public BackgroundWork(
        ISettingsStore settingsStore,
        JobTimerStore timerStore,
        ISwitchWallpaper switchWallpaper,
        IDownloadWallpapers downloadWallpapers,
        IDeviceSettingRules deviceRules,
        ILogger<BackgroundWork> logger,
        IBackgroundScheduler? scheduler = null)
        : this(settingsStore, timerStore, switchWallpaper, downloadWallpapers, deviceRules, logger, scheduler, TimeProvider.System, TimeZoneInfo.Local)
    {
    }

    // Tests set the clock and the time zone.
    internal BackgroundWork(
        ISettingsStore settingsStore,
        JobTimerStore timerStore,
        ISwitchWallpaper switchWallpaper,
        IDownloadWallpapers downloadWallpapers,
        IDeviceSettingRules deviceRules,
        ILogger<BackgroundWork> logger,
        IBackgroundScheduler? scheduler,
        TimeProvider clock,
        TimeZoneInfo zone)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _timerStore = timerStore.ThrowIfNull();
        _switchWallpaper = switchWallpaper.ThrowIfNull();
        _downloadWallpapers = downloadWallpapers.ThrowIfNull();
        _deviceRules = deviceRules.ThrowIfNull();
        _logger = logger.ThrowIfNull();
        _scheduler = scheduler;
        _clock = clock.ThrowIfNull();
        _zone = zone.ThrowIfNull();
    }

    public event Action? Changed;

    // Plans as the process starts, then follows every save and every wallpaper change.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_scheduler is null)
            return;
        _settingsStore.Saved += OnSettingsSaved;
        _switchWallpaper.WallpaperChanged += OnWallpaperChanged;
        await PlanAsync().ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settingsStore.Saved -= OnSettingsSaved;
        _switchWallpaper.WallpaperChanged -= OnWallpaperChanged;
        return Task.CompletedTask;
    }

    // Derives the next slideshow change and download run from the settings and what the work
    // last did, and arms each that differs from what is armed now. Never throws: a plan that
    // fails is logged, and the next save or process start plans again.
    public async Task PlanAsync()
    {
        if (_scheduler is null)
            return;
        await _planLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
            var slideshow = await _timerStore.LoadContextAsync(SlideshowJobName).ConfigureAwait(false);
            var downloads = await _timerStore.LoadContextAsync(DownloadsJobName).ConfigureAwait(false);
            var now = _clock.GetUtcNow();
            // Exact timing arrives with issue #203 step 5, change 4
            var plan = BackgroundPlan.For(settings, now, slideshow.StartedAt, downloads.StartedAt, exact: false, _deviceRules.ShortestSlideshowGap, _zone);

            var slideshowKey = (plan.SlideshowAt, plan.SlideshowExact);
            if (!_slideshowArmed || slideshowKey != _armedSlideshow)
            {
                _scheduler.ArmSlideshow(plan.SlideshowAt, plan.SlideshowExact);
                _armedSlideshow = slideshowKey;
                _slideshowArmed = true;
                if (plan.SlideshowAt is null)
                    _logger.LogInformation("No slideshow change armed: the slideshow is off or its schedule does not parse.");
                else
                    _logger.LogInformation("Slideshow armed for {At:O}.", plan.SlideshowAt);
            }

            // A run that is due now stays the same run however often the plan is made while
            // it waits, so it is not armed again each time
            var downloadsKey = plan.DownloadAt is { } at && at <= now ? DateTimeOffset.MinValue : plan.DownloadAt;
            if (Volatile.Read(ref _downloadsRunning) == 0 && (!_downloadsArmed || downloadsKey != _armedDownloads))
            {
                _scheduler.ArmDownloads(plan.DownloadAt);
                _armedDownloads = downloadsKey;
                _downloadsArmed = true;
                if (plan.DownloadAt is null)
                    _logger.LogInformation("No scheduled download armed: no enabled source with a schedule.");
                else
                    _logger.LogInformation("Scheduled download armed for {At:O}.", plan.DownloadAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Planning the background slideshow and downloads failed.");
        }
        finally
        {
            _planLock.Release();
        }
    }

    // A slideshow change the operating system started, for the change scheduled at occurrence.
    // Changes to the next picture in slideshow order, as the desktop's slideshow does; records
    // when it ran and whether it failed; then plans the next. A second alarm for an occurrence
    // already handled, or one that arrives with the slideshow off, changes nothing.
    public async Task RunSlideshowAsync(DateTimeOffset? occurrence)
    {
        // The alarm that started this is spent: whatever the next plan is, it is armed
        await ForgetArmedAsync(slideshow: true).ConfigureAwait(false);
        try
        {
            var last = await _timerStore.LoadContextAsync(SlideshowJobName).ConfigureAwait(false);
            var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
            if (occurrence is not null && last.ScheduledFor == occurrence)
            {
                _logger.LogInformation("The slideshow change for {Occurrence:O} was already made.", occurrence);
            }
            else if (!settings.Slideshow.Enabled)
            {
                _logger.LogInformation("The slideshow is off; no change.");
            }
            else
            {
                await ChangeAsync(occurrence, last).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The background slideshow change could not be recorded.");
        }
        await PlanAsync().ConfigureAwait(false);
    }

    private async Task ChangeAsync(DateTimeOffset? occurrence, JobExecutionContext last)
    {
        var startedAt = _clock.GetUtcNow();
        var watch = Stopwatch.StartNew();
        string? error = null;
        try
        {
            var path = await _switchWallpaper.SwitchToNextAsync().ConfigureAwait(false);
            if (path is null)
                _logger.LogInformation("No wallpapers to change to; the Current tab downloads them when it opens.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The background slideshow change failed.");
            error = ex.Message;
        }

        var context = new JobExecutionContext
        {
            StartedAt = startedAt,
            FinishedAt = _clock.GetUtcNow(),
            Duration = watch.Elapsed,
            LastExecutionSucceeded = error is null,
            ErrorMessage = error,
            ScheduledFor = occurrence,
        };
        await _timerStore.SaveContextAsync(SlideshowJobName, context).ConfigureAwait(false);
        if (last.LastExecutionSucceeded == (error is null) && last.StartedAt is not null)
            return;
        Changed?.Invoke();
    }

    // A scheduled download run the operating system started: downloads the sources that are
    // due, records when it ran and what failed, calls jobFinished (the job is done before the
    // next run is armed, since arming the running job stops it), then plans the next. A run in
    // which no source was due records nothing.
    public async Task RunDownloadsAsync(Action jobFinished)
    {
        jobFinished.ThrowIfNull();
        Interlocked.Increment(ref _downloadsRunning);
        try
        {
            var startedAt = _clock.GetUtcNow();
            var watch = Stopwatch.StartNew();
            var outcome = await _downloadWallpapers.DownloadDueAsync().ConfigureAwait(false);
            _logger.LogInformation("Scheduled download: {Tried} source(s) tried, {Failed} failed.", outcome.SourcesTried, outcome.SourcesFailed);
            if (outcome.SourcesTried > 0)
            {
                var last = await _timerStore.LoadContextAsync(DownloadsJobName).ConfigureAwait(false);
                var context = new JobExecutionContext
                {
                    StartedAt = startedAt,
                    FinishedAt = _clock.GetUtcNow(),
                    Duration = watch.Elapsed,
                    LastExecutionSucceeded = outcome.SourcesFailed == 0,
                    SourcesTried = outcome.SourcesTried,
                    SourcesFailed = outcome.SourcesFailed,
                };
                await _timerStore.SaveContextAsync(DownloadsJobName, context).ConfigureAwait(false);
                var failedBefore = last.StartedAt is not null && !last.LastExecutionSucceeded;
                if (failedBefore || !context.LastExecutionSucceeded)
                    Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The scheduled download run failed.");
        }
        finally
        {
            jobFinished();
            Interlocked.Decrement(ref _downloadsRunning);
        }
        // The finished job is spent: whatever the next plan is, it is armed
        await ForgetArmedAsync(slideshow: false).ConfigureAwait(false);
        await PlanAsync().ConfigureAwait(false);
    }

    // Forgets what was armed for the slideshow or the downloads, once Android has used it up.
    private async Task ForgetArmedAsync(bool slideshow)
    {
        await _planLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (slideshow)
                _slideshowArmed = false;
            else
                _downloadsArmed = false;
        }
        finally
        {
            _planLock.Release();
        }
    }

    public async Task<bool> SlideshowChangeFailedAsync()
    {
        var last = await _timerStore.LoadContextAsync(SlideshowJobName).ConfigureAwait(false);
        var failed = last.StartedAt is not null && !last.LastExecutionSucceeded;
        return failed;
    }

    public async Task<DownloadOutcome?> ScheduledDownloadFailureAsync()
    {
        var last = await _timerStore.LoadContextAsync(DownloadsJobName).ConfigureAwait(false);
        if (last.StartedAt is null || last.LastExecutionSucceeded || last.SourcesTried is not { } tried)
            return null;
        var outcome = new DownloadOutcome(tried, last.SourcesFailed ?? 0);
        return outcome;
    }

    private void OnSettingsSaved(WallpaperNexusSettings settings) => _ = PlanAsync();

    // Any wallpaper change that worked (Next, Random, set as current, a background change)
    // clears a failed background change.
    private void OnWallpaperChanged(string path) => _ = ClearSlideshowFailureAsync();

    private async Task ClearSlideshowFailureAsync()
    {
        try
        {
            var last = await _timerStore.LoadContextAsync(SlideshowJobName).ConfigureAwait(false);
            if (last.StartedAt is null || last.LastExecutionSucceeded)
                return;
            var cleared = last with { LastExecutionSucceeded = true, ErrorMessage = string.Empty };
            await _timerStore.SaveContextAsync(SlideshowJobName, cleared).ConfigureAwait(false);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clearing the failed slideshow change failed.");
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The phone's background slideshow and downloads (issue #203, step 5): what BackgroundWork arms
// when the process starts and after each save, what a change and a download run do and record,
// and the two failures the Current and Sources tabs show.
public class BackgroundWorkTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 7, 0, TimeSpan.Zero);

    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();
    private readonly string _timerDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_BackgroundTimers_{Guid.NewGuid():N}");
    private readonly RecordingScheduler _scheduler = new();
    private readonly ScriptedSwitcher _switcher = new();
    private readonly ScriptedDownloader _downloader = new();
    private readonly SettableClock _clock = new(Now);

    public BackgroundWorkTests()
    {
        Directory.CreateDirectory(_timerDir);
    }

    public void Dispose()
    {
        TestHelpers.DeleteSettingsStore(_store);
        try { Directory.Delete(_timerDir, true); } catch { }
    }

    private JobTimerStore Timers() => new(_timerDir);

    private readonly SettablePermission _exactTiming = new();
    private readonly FakeWallpaperApplier _applier = new();

    private BackgroundWork Create(IBackgroundScheduler? scheduler = null, JobTimerStore? timers = null) =>
        new(_store, timers ?? Timers(), _switcher, _downloader, TestSettingRules.Phone, _applier,
            NullLogger<BackgroundWork>.Instance, scheduler ?? _scheduler, _clock, TimeZoneInfo.Utc, _exactTiming);

    // --- Exact timing ---

    [Fact]
    public async Task ExactTimingOnAndAllowed_ArmsTheChangeOnTheMinute()
    {
        await _store.UpdateAsync(s => s.Slideshow.ExactTiming = true);
        _exactTiming.IsAllowed = true;
        var work = Create();

        await work.StartAsync(CancellationToken.None);

        Assert.Equal([(At(12, 30), true)], _scheduler.Slideshow);
    }

    [Fact]
    public async Task ExactTimingOn_ButNotAllowed_ArmsTheWindowAlarmOnly_UntilItIsAllowed()
    {
        await _store.UpdateAsync(s => s.Slideshow.ExactTiming = true);
        var work = Create();
        await work.StartAsync(CancellationToken.None);
        Assert.Equal((At(12, 30), false), _scheduler.Slideshow[^1]);

        // The person allows it outside the app: the permission broadcast plans again
        _exactTiming.IsAllowed = true;
        await work.PlanAsync();

        Assert.Equal((At(12, 30), true), _scheduler.Slideshow[^1]);
    }

    [Fact]
    public async Task ExactTimingOff_IsNeverOnTheMinute()
    {
        _exactTiming.IsAllowed = true;
        var work = Create();

        await work.StartAsync(CancellationToken.None);

        Assert.Equal([(At(12, 30), false)], _scheduler.Slideshow);
    }

    [Fact]
    public async Task TheExactAlarmAndItsBackupTogether_ChangeTheWallpaperOnce()
    {
        var work = Create();
        var gate = new TaskCompletionSource();
        _switcher.Gate = gate;
        _clock.Now = At(12, 30);

        var exact = work.RunSlideshowAsync(At(12, 30));
        var backup = work.RunSlideshowAsync(At(12, 30));
        gate.SetResult();
        await Task.WhenAll(exact, backup);

        Assert.Equal(1, _switcher.Nexts);
    }

    // --- Download now ---

    [Fact]
    public async Task ADownloadNowThatWorked_ClearsAFailedScheduledRun()
    {
        var work = Create();
        _downloader.Outcome = new DownloadOutcome(2, 1);
        await work.RunDownloadsAsync(() => { });
        var changes = 0;
        work.Changed += () => changes++;

        await work.RecordDownloadNowAsync(new DownloadOutcome(2, 0));

        Assert.Null(await work.ScheduledDownloadFailureAsync());
        Assert.Equal(1, changes);
        // When the scheduled run last ran stays, for its own schedule
        var recorded = await Timers().LoadContextAsync(BackgroundWork.DownloadsJobName);
        Assert.Equal(Now, recorded.StartedAt);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(0, 0)]
    public async Task ADownloadNowThatDidNotWork_LeavesTheFailure(int tried, int failed)
    {
        var work = Create();
        _downloader.Outcome = new DownloadOutcome(2, 2);
        await work.RunDownloadsAsync(() => { });

        await work.RecordDownloadNowAsync(new DownloadOutcome(tried, failed));

        Assert.Equal(new DownloadOutcome(2, 2), await work.ScheduledDownloadFailureAsync());
    }

    private sealed class SettablePermission : IExactTimingPermission
    {
        public bool IsAllowed { get; set; }

        public Task<bool> AskAsync() => Task.FromResult(IsAllowed);
    }

    private static DateTimeOffset At(int hour, int minute) => new(2026, 11, 19, hour, minute, 0, TimeSpan.Zero);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not reached.");
            await Task.Delay(10);
        }
    }

    // --- Planning ---

    [Fact]
    public async Task Start_ArmsTheNextChangeAndTheFirstDownload()
    {
        var work = Create();

        await work.StartAsync(CancellationToken.None);

        Assert.Equal([(At(12, 30), false)], _scheduler.Slideshow);
        // The default sources were never downloaded: their run is due now
        Assert.Equal([Now], _scheduler.Downloads);
    }

    [Fact]
    public async Task WithNoScheduler_NothingIsArmedOrRecorded()
    {
        // The desktop registers none: its slideshow and downloads run in its own process
        var work = new BackgroundWork(_store, Timers(), _switcher, _downloader, TestSettingRules.Desktop, _applier,
            NullLogger<BackgroundWork>.Instance, null, _clock, TimeZoneInfo.Utc);

        await work.StartAsync(CancellationToken.None);
        await _store.UpdateAsync(s => s.Slideshow.Enabled = false);
        await work.PlanAsync();

        Assert.Empty(_scheduler.Slideshow);
        Assert.False(File.Exists(Path.Combine(_timerDir, JobTimerStore.FileName)));
    }

    [Fact]
    public async Task SwitchingTheSlideshowOff_CancelsTheChange()
    {
        var work = Create();
        await work.StartAsync(CancellationToken.None);

        await _store.UpdateAsync(s => s.Slideshow.Enabled = false);

        await WaitUntil(() => _scheduler.Slideshow.Count == 2);
        Assert.Equal((null, false), _scheduler.Slideshow[1]);
    }

    [Fact]
    public async Task EditingTheSchedule_ArmsTheNewTime()
    {
        var work = Create();
        await work.StartAsync(CancellationToken.None);

        await _store.UpdateAsync(s => s.Slideshow.CronExpression = "0 */2 * * *");

        await WaitUntil(() => _scheduler.Slideshow.Count == 2);
        Assert.Equal((At(14, 0), false), _scheduler.Slideshow[1]);
    }

    [Fact]
    public async Task ASaveThatChangesNoTime_ArmsNothingAgain()
    {
        var work = Create();
        await work.StartAsync(CancellationToken.None);

        await _store.UpdateAsync(s => s.FavoriteWallpapers.Add("/pictures/lake.png"));
        await work.PlanAsync();

        Assert.Single(_scheduler.Slideshow);
        Assert.Single(_scheduler.Downloads);
    }

    [Fact]
    public async Task AFinishedRun_IsFollowedByAJobArmedAgain_EvenForTheSameTime()
    {
        // Android removes a one-shot job once it has run, so the next one is always armed
        await _store.UpdateAsync(s => s.Sources.ForEach(source => source.LastDownloadUtc = Now.AddHours(-1)));
        var work = Create();
        await work.StartAsync(CancellationToken.None);
        var armedBefore = _scheduler.Downloads.Count;
        _downloader.Outcome = new DownloadOutcome(0, 0);

        await work.RunDownloadsAsync(() => { });

        Assert.Equal(armedBefore + 1, _scheduler.Downloads.Count);
        Assert.Equal(_scheduler.Downloads[armedBefore - 1], _scheduler.Downloads[^1]);
    }

    [Fact]
    public async Task AnAlarmThatChangesNothing_IsFollowedByAChangeArmedAgain()
    {
        // A second alarm for a time already handled leaves the plan where it was; the alarm
        // is still spent, so it is armed again
        var work = Create();
        await work.StartAsync(CancellationToken.None);
        _clock.Now = At(12, 30).AddSeconds(5);
        await work.RunSlideshowAsync(At(12, 30));
        var armedBefore = _scheduler.Slideshow.Count;

        await work.RunSlideshowAsync(At(12, 30));

        Assert.Equal(armedBefore + 1, _scheduler.Slideshow.Count);
        Assert.Equal(_scheduler.Slideshow[armedBefore - 1], _scheduler.Slideshow[^1]);
    }

    // --- A background change ---

    [Fact]
    public async Task AChange_ChangesTheWallpaper_RecordsIt_AndArmsTheNext()
    {
        var work = Create();
        await work.StartAsync(CancellationToken.None);
        _clock.Now = At(12, 30).AddSeconds(5);

        await work.RunSlideshowAsync(At(12, 30));

        Assert.Equal(1, _switcher.Nexts);
        Assert.False(await work.SlideshowChangeFailedAsync());
        var recorded = await Timers().LoadContextAsync(BackgroundWork.SlideshowJobName);
        Assert.Equal(At(12, 30), recorded.ScheduledFor);
        Assert.Equal((At(13, 0), false), _scheduler.Slideshow[^1]);
    }

    // The floor (no two changes closer than the phone's 15 minutes) is counted between the
    // times the changes were scheduled for: a change for 12:15 that ran a moment later is
    // followed by the one for 12:30, not 12:45.
    [Fact]
    public async Task EveryFifteenMinutes_AChangeThatRanJustAfterItsTime_IsFollowedByTheNextOccurrence()
    {
        await _store.UpdateAsync(s => s.Slideshow.CronExpression = "*/15 * * * *");
        var work = Create();
        _clock.Now = At(12, 15).AddMilliseconds(500);

        await work.RunSlideshowAsync(At(12, 15));

        Assert.Equal((At(12, 30), false), _scheduler.Slideshow[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryFifteenMinutes_ThreeChangesInARow_AreFifteenMinutesApart_ExactTimingOnOrOff(bool exact)
    {
        await _store.UpdateAsync(s =>
        {
            s.Slideshow.CronExpression = "*/15 * * * *";
            s.Slideshow.ExactTiming = exact;
        });
        _exactTiming.IsAllowed = true;
        var work = Create();

        foreach (var minute in new[] { 15, 30, 45 })
        {
            // Each one delivered a little after its time, the window alarm up to minutes late
            _clock.Now = At(12, minute).AddSeconds(exact ? 0.4 : 290);
            await work.RunSlideshowAsync(At(12, minute));
            Assert.Equal((At(12, minute).AddMinutes(15), exact), _scheduler.Slideshow[^1]);
        }
        Assert.Equal(3, _switcher.Nexts);
    }

    // A change made by hand (Next in the app, a long-press menu entry) is the person's own act:
    // it does not move the slideshow's schedule. The 12:15 change still comes a minute later.
    [Fact]
    public async Task AChangeByHandShortlyBeforeAnOccurrence_LeavesTheScheduleAlone()
    {
        await _store.UpdateAsync(s => s.Slideshow.CronExpression = "*/15 * * * *");
        var work = Create();
        _clock.Now = At(12, 0).AddSeconds(1);
        await work.RunSlideshowAsync(At(12, 0));
        Assert.Equal((At(12, 15), false), _scheduler.Slideshow[^1]);

        _clock.Now = At(12, 14);
        await work.RunShortcutChangeAsync(random: false);
        await work.PlanAsync();
        Assert.Equal((At(12, 15), false), _scheduler.Slideshow[^1]);

        _clock.Now = At(12, 15).AddSeconds(1);
        await work.RunSlideshowAsync(At(12, 15));
        Assert.Equal(3, _switcher.Nexts);
        Assert.Equal((At(12, 30), false), _scheduler.Slideshow[^1]);
    }

    // The clock set back after a change: the change's time is in the future, says nothing about
    // how long ago the wallpaper changed, and sets no floor
    [Fact]
    public async Task TheClockSetBackAfterAChange_ArmsTheNextOccurrenceAfterTheNewTime()
    {
        await _store.UpdateAsync(s => s.Slideshow.CronExpression = "*/15 * * * *");
        var work = Create();
        _clock.Now = At(12, 15).AddSeconds(1);
        await work.RunSlideshowAsync(At(12, 15));

        _clock.Now = At(11, 2);
        await work.PlanAsync();

        Assert.Equal((At(11, 15), false), _scheduler.Slideshow[^1]);
    }

    // An entry written without the time its change was due counts from when it ran
    [Fact]
    public async Task AnEntryWithoutItsScheduledTime_CountsTheFloorFromWhenItRan()
    {
        await _store.UpdateAsync(s => s.Slideshow.CronExpression = "*/5 * * * *");
        var timers = Timers();
        await timers.SaveContextAsync(BackgroundWork.SlideshowJobName, new JobExecutionContext
        {
            StartedAt = At(12, 3),
            FinishedAt = At(12, 3),
            LastExecutionSucceeded = true,
        });
        var work = Create(timers: timers);

        await work.StartAsync(CancellationToken.None);

        Assert.Equal((At(12, 20), false), _scheduler.Slideshow[^1]);
    }

    [Fact]
    public async Task ASecondAlarmForTheSameTime_ChangesNothing()
    {
        var timers = Timers();
        var work = Create(timers: timers);
        _clock.Now = At(12, 30).AddSeconds(5);

        await work.RunSlideshowAsync(At(12, 30));
        await work.RunSlideshowAsync(At(12, 30));

        Assert.Equal(1, _switcher.Nexts);
    }

    [Fact]
    public async Task AnAlarmWithTheSlideshowOff_ChangesNothing()
    {
        await _store.UpdateAsync(s => s.Slideshow.Enabled = false);
        var work = Create();

        await work.RunSlideshowAsync(At(12, 30));

        Assert.Equal(0, _switcher.Nexts);
    }

    [Fact]
    public async Task ARefusedChange_IsAFailure_UntilAChangeWorks()
    {
        var work = Create();
        await work.StartAsync(CancellationToken.None);
        var changes = 0;
        work.Changed += () => Interlocked.Increment(ref changes);
        _switcher.Refuses = true;

        await work.RunSlideshowAsync(At(12, 30));

        Assert.True(await work.SlideshowChangeFailedAsync());
        Assert.Equal(1, changes);

        // The person taps Next and it works
        _switcher.Refuses = false;
        await _switcher.SwitchToNextAsync();

        await WaitUntil(() => Volatile.Read(ref changes) == 2);
        Assert.False(await work.SlideshowChangeFailedAsync());
    }

    [Fact]
    public async Task NoPictures_IsNoFailure()
    {
        var work = Create();
        _switcher.HasPictures = false;

        await work.RunSlideshowAsync(At(12, 30));

        Assert.False(await work.SlideshowChangeFailedAsync());
    }

    // --- A scheduled download run ---

    [Fact]
    public async Task ADownloadRunWithFailures_IsRecorded_UntilARunWorks()
    {
        var work = Create();
        var finished = 0;
        _downloader.Outcome = new DownloadOutcome(2, 1);

        await work.RunDownloadsAsync(() => finished++);

        Assert.Equal(1, finished);
        Assert.Equal(new DownloadOutcome(2, 1), await work.ScheduledDownloadFailureAsync());

        _downloader.Outcome = new DownloadOutcome(2, 0);
        await work.RunDownloadsAsync(() => finished++);

        Assert.Equal(2, finished);
        Assert.Null(await work.ScheduledDownloadFailureAsync());
    }

    [Fact]
    public async Task ARunWithNothingDue_RecordsNothing()
    {
        var work = Create();
        _downloader.Outcome = new DownloadOutcome(2, 2);
        await work.RunDownloadsAsync(() => { });

        _downloader.Outcome = new DownloadOutcome(0, 0);
        await work.RunDownloadsAsync(() => { });

        Assert.Equal(new DownloadOutcome(2, 2), await work.ScheduledDownloadFailureAsync());
    }

    [Fact]
    public async Task AFailedSource_WaitsForItsNextOccurrence()
    {
        var work = Create();
        await work.StartAsync(CancellationToken.None);
        _downloader.Outcome = new DownloadOutcome(2, 2);

        await work.RunDownloadsAsync(() => { });

        // The default sources download every 8 hours: tried at 12:07, next at 16:00
        Assert.Equal(At(16, 0), _scheduler.Downloads[^1]);
    }

    [Fact]
    public async Task WhileARunIsInProgress_ItsJobIsNotArmedAgain_AndIsFinishedBeforeTheNextIsArmed()
    {
        var work = Create();
        await work.StartAsync(CancellationToken.None);
        var gate = new TaskCompletionSource();
        _downloader.Gate = gate;
        _downloader.Outcome = new DownloadOutcome(2, 0);
        var events = new List<string>();
        _scheduler.OnDownloads = at => { lock (events) events.Add("armed"); };

        var run = work.RunDownloadsAsync(() => { lock (events) events.Add("finished"); });
        await WaitUntil(() => _downloader.Started);
        // A save while the run downloads (the person hearts a picture)
        await _store.UpdateAsync(s => s.Slideshow.CronExpression = "0 */2 * * *");
        await work.PlanAsync();
        gate.SetResult();
        await run;

        Assert.Equal(["finished", "armed"], events);
    }

    // --- A change from the long-press menu (issue #203, step 6) ---

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task AMenuEntry_ChangesTheWallpaper_NextOrRandom(bool random, int randoms)
    {
        var work = Create();

        var outcome = await work.RunShortcutChangeAsync(random);

        Assert.Equal(ShortcutChangeOutcome.Changed, outcome);
        Assert.Equal(1, _switcher.Nexts);
        Assert.Equal(randoms, _switcher.Randoms);
    }

    [Fact]
    public async Task AMenuEntryTheDeviceRefuses_IsAFailure_AndRecordsNothing()
    {
        _switcher.Refuses = true;
        var work = Create();

        var outcome = await work.RunShortcutChangeAsync(random: false);

        Assert.Equal(ShortcutChangeOutcome.Failed, outcome);
        Assert.Equal("Could not change the wallpaper.", ShortcutSentence.For(outcome));
        // The pop-up says it at once; no screen reads it later
        Assert.False(File.Exists(Path.Combine(_timerDir, JobTimerStore.FileName)));
    }

    [Fact]
    public async Task AMenuEntry_OnAPhoneThatForbidsChanges_MakesNoChange()
    {
        _applier.CanSetWallpaper = false;
        var work = Create();

        var outcome = await work.RunShortcutChangeAsync(random: true);

        Assert.Equal(ShortcutChangeOutcome.NotAllowed, outcome);
        Assert.Equal("This phone does not allow apps to change the wallpaper.", ShortcutSentence.For(outcome));
        Assert.Equal(0, _switcher.Nexts);
    }

    [Fact]
    public async Task AMenuEntry_WithNoPictures_OpensTheApp_AndMakesNoChange()
    {
        _switcher.HasPictures = false;
        var work = Create();

        var outcome = await work.RunShortcutChangeAsync(random: false);

        Assert.Equal(ShortcutChangeOutcome.NoPictures, outcome);
        Assert.Null(ShortcutSentence.For(outcome));
        Assert.Equal(0, _switcher.Nexts);
    }

    [Fact]
    public async Task AMenuEntry_WhileAnotherChangeRuns_MakesNoSecondChange()
    {
        // The slideshow or the app's own buttons are changing the wallpaper
        _switcher.OtherChangeRunning = true;
        var work = Create();

        var outcome = await work.RunShortcutChangeAsync(random: false);

        Assert.Equal(ShortcutChangeOutcome.AlreadyRunning, outcome);
        Assert.Null(ShortcutSentence.For(outcome));
        Assert.Equal(0, _switcher.Nexts);
    }

    [Fact]
    public async Task TwoMenuTapsTogether_MakeOneChange_AndTheSecondWaitsForIt()
    {
        var work = Create();
        var gate = new TaskCompletionSource();
        _switcher.Gate = gate;

        var first = work.RunShortcutChangeAsync(random: false);
        var second = work.RunShortcutChangeAsync(random: true);
        Assert.False(second.IsCompleted);
        gate.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Equal([ShortcutChangeOutcome.Changed, ShortcutChangeOutcome.AlreadyRunning], outcomes);
        Assert.Equal(1, _switcher.Nexts);
        Assert.Equal(0, _switcher.Randoms);
    }

    [Fact]
    public async Task AMenuTapAfterTheLastOneEnded_MakesANewChange()
    {
        var work = Create();

        await work.RunShortcutChangeAsync(random: false);
        var outcome = await work.RunShortcutChangeAsync(random: false);

        Assert.Equal(ShortcutChangeOutcome.Changed, outcome);
        Assert.Equal(2, _switcher.Nexts);
    }

    // --- Fakes ---

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingScheduler : IBackgroundScheduler
    {
        public List<(DateTimeOffset? At, bool Exact)> Slideshow { get; } = [];
        public List<DateTimeOffset?> Downloads { get; } = [];
        public Action<DateTimeOffset?>? OnDownloads { get; set; }

        public void ArmSlideshow(DateTimeOffset? at, bool exact)
        {
            lock (Slideshow)
                Slideshow.Add((at, exact));
        }

        public void ArmDownloads(DateTimeOffset? at)
        {
            lock (Downloads)
                Downloads.Add(at);
            OnDownloads?.Invoke(at);
        }
    }

    private sealed class ScriptedSwitcher : ISwitchWallpaper
    {
        public event Action<string>? WallpaperChanged;

        public bool Refuses { get; set; }
        public bool HasPictures { get; set; } = true;
        // How many switches were asked for (Next, Random and set as current), and how many of
        // them were Random
        public int Nexts { get; private set; }
        public int Randoms { get; private set; }

        public TaskCompletionSource? Gate { get; set; }

        // True while a switch waits at the gate, or when a test says another change is running
        public bool IsSwitching => Volatile.Read(ref _waiting) > 0 || OtherChangeRunning;
        public bool OtherChangeRunning { get; set; }
        private int _waiting;

        public async Task<string?> SwitchToNextAsync()
        {
            Interlocked.Increment(ref _waiting);
            try
            {
                if (Gate is not null)
                    await Gate.Task;
                return Switch();
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }
        }

        private string? Switch()
        {
            Nexts++;
            if (!HasPictures)
                return null;
            if (Refuses)
                throw new WallpaperRefusedException("/pictures/lake.png");
            WallpaperChanged?.Invoke("/pictures/lake.png");
            return "/pictures/lake.png";
        }

        public Task<string?> SwitchToRandomAsync()
        {
            Randoms++;
            return SwitchToNextAsync();
        }

        public Task<string?> SwitchToSpecificAsync(string path) => SwitchToNextAsync();

        public Task<bool> HasWallpapersAsync() => Task.FromResult(HasPictures);
    }

    private sealed class ScriptedDownloader : IDownloadWallpapers
    {
        public event Action? WallpapersDownloaded
        {
            add { }
            remove { }
        }

        public DownloadOutcome Outcome { get; set; } = new(0, 0);
        public TaskCompletionSource? Gate { get; set; }
        public bool Started { get; private set; }

        public Task<DownloadOutcome> DownloadAllAsync() => throw new NotSupportedException("Download now is not part of these tests.");

        public async Task<DownloadOutcome> DownloadDueAsync()
        {
            Started = true;
            if (Gate is not null)
                await Gate.Task;
            return Outcome;
        }
    }
}

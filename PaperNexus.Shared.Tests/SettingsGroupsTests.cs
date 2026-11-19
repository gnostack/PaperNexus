using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaperNexus.Core;
using PaperNexus.Core.Imaging;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The settings groups both devices' settings screens use: each shows and saves only its own
// settings, applies the desktop's rules for them, and reports each save for the status line.
public class SettingsGroupsTests : IDisposable
{
    private readonly SettingsStore _store;

    public SettingsGroupsTests()
    {
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.DeleteSettingsStore(_store);
    }

    // Settings no group shown on the phone owns, set away from their defaults, so a group that
    // wrote them would be caught.
    private async Task WriteOthersAsync()
    {
        await _store.UpdateAsync(latest =>
        {
            latest.RunOnStartup = false;
            latest.MinimizeToTray = false;
            latest.AutoUpdatesEnabled = false;
            latest.Download.Folder = "/kept/folder";
            latest.FavoriteWallpapers.Add("/kept/favorite.png");
            latest.BannedWallpapers.Add("/kept/banned.png");
            latest.CurrentWallpaperPath = "/kept/current.png";
            latest.DiscoveredEasterEggs.Add("konami");
            latest.WindowX = 40;
        });
    }

    private static void AssertOthersKept(WallpaperNexusSettings saved)
    {
        Assert.False(saved.RunOnStartup);
        Assert.False(saved.MinimizeToTray);
        Assert.False(saved.AutoUpdatesEnabled);
        Assert.Equal("/kept/folder", saved.Download.Folder);
        Assert.Equal(["/kept/favorite.png"], saved.FavoriteWallpapers);
        Assert.Equal(["/kept/banned.png"], saved.BannedWallpapers);
        Assert.Equal("/kept/current.png", saved.CurrentWallpaperPath);
        Assert.Equal(["konami"], saved.DiscoveredEasterEggs);
        Assert.Equal(40, saved.WindowX);
    }

    private DebugSettingsViewModel CreateDebug(LogLevelSwitch? logLevels = null, RecordingFactoryReset? reset = null, bool startedInDebugMode = false)
    {
        var debug = new DebugSettingsViewModel(
            _store,
            new TestAppInfo(startedInDebugMode),
            logLevels ?? new LogLevelSwitch(false),
            reset ?? new RecordingFactoryReset());
        return debug;
    }

    // --- Every group writes only its own settings ---

    [Fact]
    public async Task DownloadGroup_WritesOnlyItsOwnSettings()
    {
        await WriteOthersAsync();
        var group = new DownloadSettingsViewModel(_store);
        await group.LoadAsync();

        group.SelectedResolution = DownloadSettingsViewModel.ResolutionOptions[4];
        group.RetentionDays = 90;
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(3840, saved.Download.ResolutionWidth);
        Assert.Equal(2160, saved.Download.ResolutionHeight);
        Assert.Equal(90, saved.Download.RetentionDays);
        AssertOthersKept(saved);
    }

    [Fact]
    public async Task SlideshowGroup_WritesOnlyItsOwnSettings()
    {
        await WriteOthersAsync();
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Phone);
        await group.LoadAsync();

        group.Enabled = false;
        group.SelectedIntervalType = SlideshowSettingsViewModel.IntervalTypeOptions[2]; // Hours
        group.Interval = 6;
        group.SelectedOrder = SlideshowSettingsViewModel.SlideshowOrderOptions[3]; // Random
        group.FavoritePriorityEnabled = true;
        group.FavoritePriorityWeight = 7;
        group.SelectedFillStyle = group.FillStyleOptions[1]; // Fit
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.False(saved.Slideshow.Enabled);
        Assert.Equal(IntervalType.Hours, saved.Slideshow.IntervalType);
        Assert.Equal(6, saved.Slideshow.Interval);
        Assert.Equal("0 */6 * * *", saved.Slideshow.CronExpression);
        Assert.Equal(SlideshowOrder.Random, saved.Slideshow.Order);
        Assert.True(saved.Slideshow.FavoritePriorityEnabled);
        Assert.Equal(7, saved.Slideshow.FavoritePriorityWeight);
        Assert.Equal(WallpaperFillStyle.Fit, saved.Slideshow.FillStyle);
        AssertOthersKept(saved);
    }

    [Fact]
    public async Task AnnotationGroup_WritesOnlyItsOwnSettings()
    {
        await WriteOthersAsync();
        var group = new AnnotationSettingsViewModel(_store, new EasterEggRequests());
        await group.LoadAsync();

        group.AnnotateWallpaper = false;
        group.FontSize = 40;
        group.Color = "#112233";
        group.OutlineEnabled = false;
        group.SelectedPosition = AnnotationSettingsViewModel.AnnotationPositionOptions[3];
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.False(saved.AnnotateWallpaper);
        Assert.Equal(40, saved.Annotation.FontSize);
        Assert.Equal("#112233", saved.Annotation.Color);
        Assert.False(saved.Annotation.OutlineEnabled);
        Assert.Equal(AnnotationPosition.BottomRight, saved.Annotation.Position);
        AssertOthersKept(saved);
    }

    [Fact]
    public async Task DebugGroup_WritesOnlyDebugMode()
    {
        await WriteOthersAsync();
        await _store.UpdateAsync(latest => latest.DebugMode = true);
        var group = CreateDebug();
        await group.LoadAsync();

        group.RequestDebugMode(false);
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.False(saved.DebugMode);
        AssertOthersKept(saved);
    }

    // --- Saving and reporting ---

    [Fact]
    public async Task Change_IsSavedHalfASecondLater_AndReported()
    {
        var group = new DownloadSettingsViewModel(_store);
        await group.LoadAsync();
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;

        group.RetentionDays = 30;
        Assert.Equal(365, (await _store.LoadAsync()).Download.RetentionDays);
        var saved = await TestHelpers.WaitForSavedAsync(_store, s => s.Download.RetentionDays == 30);

        Assert.Equal(30, saved.Download.RetentionDays);
        await Task.Delay(100);
        var report = Assert.Single(reports);
        Assert.Equal(SettingsGroupViewModel.SavedText, report.Text);
        Assert.Equal(StatusTone.Success, report.Tone);
    }

    [Fact]
    public async Task Loading_SavesNothing()
    {
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;
        var before = File.Exists(_store.FilePath);

        await group.LoadAsync();
        await Task.Delay(SettingsGroupViewModel.SaveDelayMs + 300);

        Assert.Empty(reports);
        Assert.Equal(before, File.Exists(_store.FilePath));
    }

    [Fact]
    public async Task Flush_SavesAWaitingChangeAtOnce()
    {
        var group = new DownloadSettingsViewModel(_store);
        await group.LoadAsync();

        group.RetentionDays = 45;
        group.Flush();
        var saved = await TestHelpers.WaitForSavedAsync(_store, s => s.Download.RetentionDays == 45);

        Assert.Equal(45, saved.Download.RetentionDays);
    }

    // --- Download rules ---

    [Fact]
    public async Task Download_EmptiedRetentionSavesTheDefault()
    {
        var group = new DownloadSettingsViewModel(_store);
        await group.LoadAsync();

        group.RetentionDays = null;
        await group.SaveNowAsync();

        Assert.Equal(DownloadSettingsViewModel.DefaultRetentionDays, (await _store.LoadAsync()).Download.RetentionDays);
    }

    [Fact]
    public async Task Download_AnUnlistedSizeShowsAsNative()
    {
        await _store.UpdateAsync(latest =>
        {
            latest.Download.ResolutionWidth = 1234;
            latest.Download.ResolutionHeight = 567;
        });
        var group = new DownloadSettingsViewModel(_store);
        await group.LoadAsync();

        Assert.Equal("Native", group.SelectedResolution.Label);
    }

    // --- Slideshow rules ---

    [Fact]
    public async Task Slideshow_AnInvalidCronHoldsBackOnlyTheSchedule()
    {
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);
        await group.LoadAsync();
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;

        group.ScheduleMode = SlideshowScheduleMode.CronExpression;
        group.CronExpression = "not a schedule";
        group.Enabled = false;
        group.SelectedOrder = SlideshowSettingsViewModel.SlideshowOrderOptions[3];
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(SlideshowScheduleMode.Interval, saved.Slideshow.ScheduleMode);
        Assert.Equal("*/30 * * * *", saved.Slideshow.CronExpression);
        Assert.False(saved.Slideshow.Enabled);
        Assert.Equal(SlideshowOrder.Random, saved.Slideshow.Order);
        var refusal = Assert.Single(reports);
        Assert.Equal(SlideshowSettingsViewModel.InvalidCronSentence, refusal.Text);
        Assert.Equal(StatusTone.Error, refusal.Tone);
    }

    [Fact]
    public async Task Slideshow_AValidCronIsSavedAsTyped()
    {
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);
        await group.LoadAsync();

        group.ScheduleMode = SlideshowScheduleMode.CronExpression;
        group.CronExpression = "0 9 * * 1-5";
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(SlideshowScheduleMode.CronExpression, saved.Slideshow.ScheduleMode);
        Assert.Equal("0 9 * * 1-5", saved.Slideshow.CronExpression);
    }

    [Fact]
    public async Task Slideshow_ChangingTheUnitKeepsTheNumberInItsRange()
    {
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);
        await group.LoadAsync();

        group.Interval = 45;
        group.SelectedIntervalType = SlideshowSettingsViewModel.IntervalTypeOptions[2]; // Hours, 1-23

        Assert.Equal(23, group.Interval);
        Assert.Equal(1m, group.IntervalMinimum);
        Assert.Equal(23m, group.IntervalMaximum);
    }

    [Fact]
    public async Task Slideshow_AnEmptiedNumberBecomesTheUnitsMinimum()
    {
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);
        await group.LoadAsync();

        group.Interval = null;

        Assert.Equal(1, group.Interval);
    }

    [Fact]
    public async Task Slideshow_AnUnknownOrderShowsAsNewestFirst()
    {
        // An order this version does not know (a newer one's) reads as the model's default.
        File.WriteAllText(_store.FilePath, "{ \"Slideshow\": { \"Order\": 99 } }");
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);
        await group.LoadAsync();

        Assert.Equal(SlideshowOrder.NewestFirst, group.SelectedOrder.Order);
    }

    [Fact]
    public void Slideshow_ThePhoneOffersFiveFillStylesAndTheDesktopSix()
    {
        var phone = new SlideshowSettingsViewModel(_store, TestSettingRules.Phone);
        var desktop = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);

        Assert.Equal(["Fill", "Fit", "Stretch", "Tile", "Center"], phone.FillStyleOptions.Select(o => o.Label));
        Assert.Equal(["Fill", "Fit", "Stretch", "Tile", "Center", "Span"], desktop.FillStyleOptions.Select(o => o.Label));
    }

    [Fact]
    public async Task Slideshow_AStoredSpanShowsAsFillOnThePhoneAndStaysUntilAStyleIsPicked()
    {
        await _store.UpdateAsync(latest => latest.Slideshow.FillStyle = WallpaperFillStyle.Span);
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Phone);
        await group.LoadAsync();

        Assert.Equal(WallpaperFillStyle.Fill, group.SelectedFillStyle.Style);

        // Another slideshow change leaves the stored Span alone
        group.SelectedOrder = SlideshowSettingsViewModel.SlideshowOrderOptions[0];
        await group.SaveNowAsync();
        Assert.Equal(WallpaperFillStyle.Span, (await _store.LoadAsync()).Slideshow.FillStyle);

        // Picking a style saves it
        group.SelectedFillStyle = group.FillStyleOptions[2];
        await group.SaveNowAsync();
        Assert.Equal(WallpaperFillStyle.Stretch, (await _store.LoadAsync()).Slideshow.FillStyle);
    }

    [Fact]
    public async Task Slideshow_PickingFillOverAStoredSpanSavesFill()
    {
        await _store.UpdateAsync(latest => latest.Slideshow.FillStyle = WallpaperFillStyle.Span);
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Phone);
        await group.LoadAsync();

        group.SelectedFillStyle = group.FillStyleOptions[1];
        group.SelectedFillStyle = group.FillStyleOptions[0];
        await group.SaveNowAsync();

        Assert.Equal(WallpaperFillStyle.Fill, (await _store.LoadAsync()).Slideshow.FillStyle);
    }

    [Theory]
    [InlineData(SlideshowScheduleMode.CronExpression, "", SlideshowSchedule.EmptyCronText)]
    [InlineData(SlideshowScheduleMode.CronExpression, "nonsense", SlideshowSchedule.InvalidCronText)]
    [InlineData(SlideshowScheduleMode.Interval, "*/30 * * * *", "Every 30 minutes")]
    public void Schedule_DescriptionSentences(SlideshowScheduleMode mode, string cron, string expected)
    {
        Assert.Equal(expected, SlideshowSchedule.Describe(mode, cron));
    }

    [Theory]
    [InlineData("0 9 * * 1-5", true)]
    [InlineData("*/10 * * * * *", true)]
    [InlineData("0 9 * *", false)]
    [InlineData("", false)]
    public void Schedule_CronValidity(string cron, bool valid)
    {
        Assert.Equal(valid, SlideshowSchedule.IsValidCron(cron));
    }

    // --- Annotation rules ---

    [Fact]
    public async Task Annotation_AWordColourAsksForItsEgg()
    {
        var eggs = new EasterEggRequests();
        var requested = new List<EasterEggShow>();
        eggs.Requested += requested.Add;
        var group = new AnnotationSettingsViewModel(_store, eggs);
        await group.LoadAsync();

        group.Color = "#C0FFEE";

        var show = Assert.Single(requested);
        Assert.Equal(EasterEggCatalog.Colors, show.Id);
    }

    [Fact]
    public async Task Annotation_AStoredWordColourDoesNotPlayItsEggWhenTheScreenOpens()
    {
        await _store.UpdateAsync(latest => latest.Annotation.Color = "#C0FFEE");
        var eggs = new EasterEggRequests();
        var requested = new List<EasterEggShow>();
        eggs.Requested += requested.Add;
        var group = new AnnotationSettingsViewModel(_store, eggs);

        await group.LoadAsync();
        Assert.Equal("#C0FFEE", group.Color);
        Assert.Empty(requested);

        // Typing a word colour still plays it
        group.Color = "#DECADE";
        var show = Assert.Single(requested);
        Assert.Equal(EasterEggCatalog.Colors, show.Id);
    }

    [Fact]
    public async Task Annotation_AStoredFontMissingFromTheListIsStillShownAndKept()
    {
        await _store.UpdateAsync(latest => latest.Annotation.FontFamily = "A Font This Machine Does Not Have");
        var group = new AnnotationSettingsViewModel(_store, new EasterEggRequests());
        await group.LoadAsync();

        Assert.Equal("A Font This Machine Does Not Have", group.FontFamily);
        Assert.Contains("A Font This Machine Does Not Have", group.FontFamilyOptions);

        group.FontSize = 20;
        await group.SaveNowAsync();
        Assert.Equal("A Font This Machine Does Not Have", (await _store.LoadAsync()).Annotation.FontFamily);
    }

    [Fact]
    public void Annotation_EveryListedFontDrawsAsItself()
    {
        // A listed name that resolves to another family drew the title in Cinzel.
        foreach (var name in BundledFonts.InstalledFamilyNames())
        {
            using var typeface = BundledFonts.ResolveTypeface(name);
            Assert.Equal(name, typeface.FamilyName, ignoreCase: true);
        }
    }

    // --- Debug mode and Factory Reset ---

    [Fact]
    public async Task Debug_SeventeenYesAnswersTurnItOn()
    {
        var logLevels = new LogLevelSwitch(false);
        var group = CreateDebug(logLevels);
        await group.LoadAsync();
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;

        group.RequestDebugMode(true);
        for (var i = 0; i < DebugSettingsViewModel.DebugConfirmations.Count; i++)
        {
            Assert.False(group.DebugMode);
            Assert.Equal(DebugSettingsViewModel.DebugQuestion(i), group.PendingQuestion);
            await group.AnswerAsync(true);
        }

        Assert.True(group.DebugMode);
        Assert.Null(group.PendingQuestion);
        Assert.Equal(LogLevel.Debug, logLevels.MinimumLevel);
        var saved = await TestHelpers.WaitForSavedAsync(_store, s => s.DebugMode);
        Assert.True(saved.DebugMode);
        Assert.Contains(reports, r => r.Text == DebugSettingsViewModel.EnabledSentence && r.DurationMs == 5000);
    }

    [Fact]
    public async Task Debug_ANoStopsTheQuestionsAndSaysHowFarTheyGot()
    {
        var logLevels = new LogLevelSwitch(false);
        var group = CreateDebug(logLevels);
        await group.LoadAsync();
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;

        group.RequestDebugMode(true);
        for (var i = 0; i < 4; i++)
            await group.AnswerAsync(true);
        await group.AnswerAsync(false);

        Assert.False(group.DebugMode);
        Assert.Null(group.PendingQuestion);
        Assert.Equal(LogLevel.Information, logLevels.MinimumLevel);
        var report = Assert.Single(reports);
        Assert.Equal("Debug mode cancelled. You only made it through 4 of 17 confirmations.", report.Text);
        Assert.Equal(StatusTone.Neutral, report.Tone);
        await Task.Delay(SettingsGroupViewModel.SaveDelayMs + 300);
        Assert.False(File.Exists(_store.FilePath) && (await _store.LoadAsync()).DebugMode);
    }

    [Fact]
    public async Task Debug_TurningItOffAsksNothingAndLowersTheLogAtOnce()
    {
        await _store.UpdateAsync(latest => latest.DebugMode = true);
        var logLevels = new LogLevelSwitch(false);
        var group = CreateDebug(logLevels);
        await group.LoadAsync();
        Assert.Equal(LogLevel.Debug, logLevels.MinimumLevel);

        group.RequestDebugMode(false);

        Assert.Null(group.PendingQuestion);
        Assert.Equal(LogLevel.Information, logLevels.MinimumLevel);
        var saved = await TestHelpers.WaitForSavedAsync(_store, s => !s.DebugMode);
        Assert.False(saved.DebugMode);
    }

    [Fact]
    public async Task Debug_StartedWithDebugShowsOnWithoutSavingIt()
    {
        var group = CreateDebug(startedInDebugMode: true);
        await group.LoadAsync();
        await Task.Delay(SettingsGroupViewModel.SaveDelayMs + 300);

        Assert.True(group.DebugMode);
        Assert.False((await _store.LoadAsync()).DebugMode);
    }

    [Fact]
    public async Task FactoryReset_AsksTwiceThenResets()
    {
        var reset = new RecordingFactoryReset();
        var group = CreateDebug(reset: reset);

        group.RequestFactoryReset();
        Assert.Equal(DebugSettingsViewModel.FactoryResetTitle, group.PendingQuestion?.Title);
        Assert.Equal(DebugSettingsViewModel.FactoryResetQuestion, group.PendingQuestion?.Message);
        await group.AnswerAsync(true);
        Assert.Equal(DebugSettingsViewModel.FactoryResetFinalTitle, group.PendingQuestion?.Title);
        Assert.Equal(0, reset.Calls);
        await group.AnswerAsync(true);

        Assert.Equal(1, reset.Calls);
        Assert.Null(group.PendingQuestion);
    }

    [Fact]
    public async Task FactoryReset_ANoAtEitherQuestionResetsNothing()
    {
        var reset = new RecordingFactoryReset();
        var group = CreateDebug(reset: reset);

        group.RequestFactoryReset();
        await group.AnswerAsync(false);
        group.RequestFactoryReset();
        await group.AnswerAsync(true);
        await group.AnswerAsync(false);

        Assert.Equal(0, reset.Calls);
        Assert.Null(group.PendingQuestion);
    }

    [Fact]
    public async Task FactoryReset_AFailureIsReported()
    {
        var reset = new RecordingFactoryReset { Failure = new IOException("disk gone") };
        var group = CreateDebug(reset: reset);
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;

        group.RequestFactoryReset();
        await group.AnswerAsync(true);
        await group.AnswerAsync(true);

        var report = Assert.Single(reports);
        Assert.Equal("Factory reset failed: disk gone", report.Text);
        Assert.Equal(5000, report.DurationMs);
    }

    // --- The log level ---

    [Fact]
    public void LogLevel_FollowsDebugModeForTheNextEntry()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_Logs_{Guid.NewGuid():N}");
        var logLevels = new LogLevelSwitch(false);
        using (var provider = new FileLoggerProvider(directory, logLevels))
        {
            var logger = provider.CreateLogger("test");
            Assert.False(logger.IsEnabled(LogLevel.Debug));

            logLevels.SetDebugMode(true);
            Assert.True(logger.IsEnabled(LogLevel.Debug));

            logLevels.SetDebugMode(false);
            Assert.False(logger.IsEnabled(LogLevel.Debug));
            Assert.True(logger.IsEnabled(LogLevel.Information));
        }
        try { Directory.Delete(directory, true); } catch { }
    }

    [Fact]
    public void LogLevel_DebugEntriesFromTheContainersLoggersReachTheFile()
    {
        // The logging factory drops entries below Information unless told otherwise, so a
        // service's debug entry never reached the file, whatever the switch said.
        var directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_Logs_{Guid.NewGuid():N}");
        var logLevels = new LogLevelSwitch(false);
        var provider = new FileLoggerProvider(directory, logLevels);
        var withoutRule = new ServiceCollection().AddLogging(b => b.AddProvider(provider)).BuildServiceProvider();
        var withRule = new ServiceCollection().AddLogging(b => b.AddFileLogger(provider)).BuildServiceProvider();
        try
        {
            logLevels.SetDebugMode(true);
            Assert.False(withoutRule.GetRequiredService<ILogger<SettingsGroupsTests>>().IsEnabled(LogLevel.Debug));
            Assert.True(withRule.GetRequiredService<ILogger<SettingsGroupsTests>>().IsEnabled(LogLevel.Debug));

            logLevels.SetDebugMode(false);
            Assert.False(withRule.GetRequiredService<ILogger<SettingsGroupsTests>>().IsEnabled(LogLevel.Debug));
            Assert.True(withRule.GetRequiredService<ILogger<SettingsGroupsTests>>().IsEnabled(LogLevel.Information));
        }
        finally
        {
            withoutRule.Dispose();
            withRule.Dispose();
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    [Fact]
    public void LogLevel_StartedWithDebugStaysDetailed()
    {
        var logLevels = new LogLevelSwitch(true);
        logLevels.SetDebugMode(false);

        Assert.Equal(LogLevel.Debug, logLevels.MinimumLevel);
    }

    [Fact]
    public async Task LogLevel_TheStoredSettingAppliesWhenTheAppStarts()
    {
        await _store.UpdateAsync(latest => latest.DebugMode = true);
        var logLevels = new LogLevelSwitch(false);
        var startup = new DebugLogLevel(_store, logLevels);

        await startup.StartAsync(CancellationToken.None);

        Assert.Equal(LogLevel.Debug, logLevels.MinimumLevel);
    }

    [Fact]
    public async Task LogLevel_TheStartupServiceResolvesFromTheContainer()
    {
        // DebugLogLevel is registered as its own class; resolving the hosted services once
        // waited forever on a registration that resolved itself, which froze the desktop's
        // start before any window drew.
        var services = new ServiceCollection();
        services.AddSingleton<IAppFolders>(TestHelpers.AppFolders);
        services.AddSingleton<IDeviceSettingRules>(TestSettingRules.Desktop);
        services.AddSingleton(new LogLevelSwitch(false));
        services.AddServicesFrom(typeof(SettingsStore).Assembly);
        using var provider = services.BuildServiceProvider();

        var resolving = Task.Run(() => provider.GetRequiredService<DebugLogLevel>());
        var finished = await Task.WhenAny(resolving, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(resolving, finished);
        var startup = await resolving;
        Assert.NotNull(startup);
        Assert.Same(startup, provider.GetRequiredService<DebugLogLevel>());
    }

    // --- About ---

    [Fact]
    public void About_FiveTapsInARowPlayTheVersionEgg()
    {
        var eggs = new EasterEggRequests();
        var requested = new List<EasterEggShow>();
        eggs.Requested += requested.Add;
        var clock = new ManualClock();
        var about = new AboutViewModel(new TestAppInfo(), new NoOpShellOpener(), eggs, clock);

        for (var i = 0; i < 4; i++)
        {
            about.TapVersion();
            clock.Advance(TimeSpan.FromSeconds(2.9));
        }
        Assert.Empty(requested);
        about.TapVersion();

        var show = Assert.Single(requested);
        Assert.Equal(EasterEggCatalog.Version, show.Id);
    }

    [Fact]
    public void About_APauseOverThreeSecondsStartsTheCountAgain()
    {
        var eggs = new EasterEggRequests();
        var requested = new List<EasterEggShow>();
        eggs.Requested += requested.Add;
        var clock = new ManualClock();
        var about = new AboutViewModel(new TestAppInfo(), new NoOpShellOpener(), eggs, clock);

        for (var i = 0; i < 4; i++)
            about.TapVersion();
        clock.Advance(TimeSpan.FromSeconds(3.1));
        for (var i = 0; i < 4; i++)
            about.TapVersion();

        Assert.Empty(requested);
        about.TapVersion();
        Assert.Single(requested);
    }

    [Fact]
    public void About_ReportBugOpensANewIssueWithTheVersion()
    {
        var opener = new RecordingShellOpener();
        var about = new AboutViewModel(new TestAppInfo(), opener, new EasterEggRequests());

        about.ReportBugCommand.Execute(null);

        var url = Assert.Single(opener.Opened);
        Assert.StartsWith(GitHubRepository.Current.WebUrl + "/issues/new?assignees=claude&labels=bug&title=Bug+Report&body=", url);
        Assert.Contains(Uri.EscapeDataString("**App Version:** v0"), url);
    }

    // --- Secrets ---

    [Fact]
    public async Task Secrets_ListsTheDevicesEggsWithProgress()
    {
        await _store.UpdateAsync(latest => latest.DiscoveredEasterEggs.AddRange(["konami", "splash"]));
        var phoneEggs = EasterEggCatalog.All.Where(e => e.Id != EasterEggCatalog.Splash).ToList();
        var secrets = new SecretsViewModel(_store, new TestDeviceEggs(phoneEggs));

        await secrets.LoadAsync();

        Assert.Equal(4, secrets.Rows.Count);
        Assert.Equal("1 / 4 found", secrets.ProgressText);
        Assert.Equal(0.25, secrets.FoundFraction);
        Assert.True(secrets.Rows[0].IsFound);
        Assert.Equal("■", secrets.Rows[0].Mark);
        Assert.Equal("□", secrets.Rows[1].Mark);
    }

    [Fact]
    public async Task Secrets_AllFoundSaysNothingIsLeft()
    {
        await _store.UpdateAsync(latest => latest.DiscoveredEasterEggs.AddRange(EasterEggCatalog.All.Select(e => e.Id)));
        var secrets = new SecretsViewModel(_store, new TestDeviceEggs(EasterEggCatalog.All));

        await secrets.LoadAsync();

        Assert.Equal("5 / 5 found - all of them. Nothing left to hide.", secrets.ProgressText);
        Assert.Equal(1.0, secrets.FoundFraction);
    }

    // --- The settings store ---

    [Fact]
    public async Task Store_ReadingOverDefaultsKeepsListsAsStored()
    {
        // The file's lists are the lists, not added to the defaults.
        await File.WriteAllTextAsync(_store.FilePath,
            "{ \"Sources\": [ { \"Name\": \"Only\", \"Url\": \"https://only.test\" } ], \"FavoriteWallpapers\": [ \"/f.png\" ] }");

        var settings = await _store.LoadAsync();

        Assert.Equal(["Only"], settings.Sources.Select(s => s.Name));
        Assert.Equal(["/f.png"], settings.FavoriteWallpapers);
    }

    // --- The desktop window's status bar and its host groups ---

    [Fact]
    public async Task Window_AHostGroupLoadsSavesReportsAndFlushesWithTheOthers()
    {
        await _store.UpdateAsync(latest => latest.WindowX = 12);
        var vm = TestHelpers.CreateViewModel(_store);
        var hostGroup = new WindowXGroup(_store);
        vm.AddHostGroup(hostGroup);
        await vm.LoadAsync();
        Assert.Equal(12, hostGroup.WindowX);

        // A change waiting for its save is saved when the window closes, and its line shows
        hostGroup.WindowX = 99;
        vm.Cleanup();
        var saved = await TestHelpers.WaitForSavedAsync(_store, s => s.WindowX == 99);
        Assert.Equal(99, saved.WindowX);
        for (var i = 0; i < 40 && vm.StatusMessage != "✓ Settings saved."; i++)
            await Task.Delay(25);
        Assert.Equal("✓ Settings saved.", vm.StatusMessage);
    }


    [Fact]
    public async Task Window_ShowsAGroupsSaveWithItsTick()
    {
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();

        vm.Download.RetentionDays = 30;
        await TestHelpers.WaitForSavedAsync(_store, s => s.Download.RetentionDays == 30);
        for (var i = 0; i < 40 && vm.StatusMessage != "✓ Settings saved."; i++)
            await Task.Delay(25);

        Assert.Equal("✓ Settings saved.", vm.StatusMessage);
        vm.Cleanup();
    }

    // A host's own group, standing in for the desktop's: it owns WindowX.
    private sealed class WindowXGroup : SettingsGroupViewModel
    {
        private double? _windowX;

        public WindowXGroup(ISettingsStore store)
            : base(store)
        {
        }

        public double? WindowX
        {
            get => _windowX;
            set
            {
                if (SetProperty(ref _windowX, value))
                    SaveSoon();
            }
        }

        protected override void ReadFrom(WallpaperNexusSettings settings) => WindowX = settings.WindowX;

        protected override Action<WallpaperNexusSettings>? CaptureChange()
        {
            var windowX = WindowX;
            return latest => latest.WindowX = windowX;
        }
    }

    // A clock the test moves.
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class RecordingShellOpener : IShellOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string target) => Opened.Add(target);

        public void OpenFolder(string folderPath) => Opened.Add(folderPath);
    }
}

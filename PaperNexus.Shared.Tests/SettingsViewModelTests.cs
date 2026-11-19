using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The phone's Settings tab: its pickers, the Factory Reset button, the line that confirms each
// change, and that the tab never writes a setting it does not show.
public class SettingsViewModelTests : IDisposable
{
    private readonly SettingsStore _store;

    public SettingsViewModelTests()
    {
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.DeleteSettingsStore(_store);
    }

    private readonly EasterEggRequests _eggs = new();

    private SettingsViewModel Create(RecordingFactoryReset? reset = null, LogLevelSwitch? logLevels = null, IShellOpener? shellOpener = null)
    {
        var viewModel = TestHelpers.WithoutSynchronizationContext(() => new SettingsViewModel(
            _store,
            new DownloadSettingsViewModel(_store),
            new SlideshowSettingsViewModel(_store, TestSettingRules.Phone),
            new DebugSettingsViewModel(_store, new TestAppInfo(), logLevels ?? new LogLevelSwitch(false), reset ?? new RecordingFactoryReset()),
            new AboutViewModel(new TestAppInfo(), shellOpener ?? new NoOpShellOpener(), _eggs),
            new SecretsViewModel(_store, new TestDeviceEggs(PhoneEggs)),
            _eggs));
        return viewModel;
    }

    // The phone's three eggs, as AndroidDeviceEasterEggs lists them (without its hints, which
    // live in the Android project).
    private static readonly IReadOnlyList<EasterEggDefinition> PhoneEggs = EasterEggCatalog.All
        .Where(egg => egg.Id is EasterEggCatalog.Konami or EasterEggCatalog.Version or EasterEggCatalog.Favorites)
        .ToList();

    private static readonly KonamiStroke[] KonamiCode =
    [
        KonamiStroke.Up, KonamiStroke.Up, KonamiStroke.Down, KonamiStroke.Down,
        KonamiStroke.Left, KonamiStroke.Right, KonamiStroke.Left, KonamiStroke.Right,
        KonamiStroke.Tap, KonamiStroke.Tap,
    ];

    [Fact]
    public void AddStroke_TheKonamiCodePlaysItsEggOnTheLastTap()
    {
        var viewModel = Create();
        var requested = new List<EasterEggShow>();
        _eggs.Requested += requested.Add;

        foreach (var stroke in KonamiCode[..^1])
            viewModel.AddStroke(stroke);
        Assert.Empty(requested);
        viewModel.AddStroke(KonamiStroke.Tap);

        var show = Assert.Single(requested);
        Assert.Equal(EasterEggCatalog.Konami, show.Id);
    }

    [Fact]
    public async Task OpenSecrets_ListsThePhonesThreeEggsWithWhatWasFound()
    {
        await _store.UpdateAsync(latest => latest.DiscoveredEasterEggs.AddRange([EasterEggCatalog.Version, EasterEggCatalog.Splash]));
        var viewModel = Create();

        await viewModel.OpenSecretsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSecretsOpen);
        Assert.Equal(3, viewModel.Secrets.Rows.Count);
        Assert.Equal("1 / 3 found", viewModel.Secrets.ProgressText);
        Assert.Equal(1.0 / 3, viewModel.Secrets.FoundFraction, 6);
        Assert.Equal([false, true, false], viewModel.Secrets.Rows.Select(r => r.IsFound));

        viewModel.CloseSecretsCommand.Execute(null);
        Assert.False(viewModel.IsSecretsOpen);
    }

    [Fact]
    public async Task OpenSecrets_AllThreeFoundSaysNothingIsLeft()
    {
        await _store.UpdateAsync(latest => latest.DiscoveredEasterEggs.AddRange(PhoneEggs.Select(e => e.Id)));
        var viewModel = Create();

        await viewModel.OpenSecretsCommand.ExecuteAsync(null);

        Assert.Equal("3 / 3 found - all of them. Nothing left to hide.", viewModel.Secrets.ProgressText);
        Assert.Equal(1.0, viewModel.Secrets.FoundFraction);
    }

    [Fact]
    public void About_ALinkThatCannotOpenSaysSoOnTheLine()
    {
        var viewModel = Create(shellOpener: new ThrowingShellOpener());

        viewModel.About.OpenHomepageCommand.Execute(null);

        Assert.Equal("Could not open the browser: No app opens web addresses.", viewModel.Status.Message);
        Assert.True(viewModel.Status.IsError);
    }

    private sealed class ThrowingShellOpener : IShellOpener
    {
        public void Open(string target) => throw new InvalidOperationException("No app opens web addresses.");

        public void OpenFolder(string folderPath) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(SettingsPickerKind.Resolution, "Saved Resolution", 7, "Native")]
    [InlineData(SettingsPickerKind.ScheduleMode, "Schedule", 2, "Interval")]
    [InlineData(SettingsPickerKind.IntervalType, "Interval", 7, "Minutes")]
    [InlineData(SettingsPickerKind.Order, "Order", 4, "Newest first")]
    [InlineData(SettingsPickerKind.FillStyle, "Fill Style", 5, "Fill")]
    public async Task Open_ListsTheChoicesWithTheCurrentOneMarked(SettingsPickerKind kind, string title, int count, string selected)
    {
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();

        viewModel.Open(kind);

        var picker = Assert.IsType<SettingsPicker>(viewModel.Picker);
        Assert.Equal(title, picker.Title);
        Assert.Equal(count, picker.Choices.Count);
        Assert.Equal(selected, Assert.Single(picker.Choices, c => c.IsSelected).Label);
    }

    [Fact]
    public async Task Open_ThePhonesFillStylesHaveNoSpanAndKeepTheirExplanations()
    {
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();

        viewModel.Open(SettingsPickerKind.FillStyle);

        Assert.Equal(["Fill", "Fit", "Stretch", "Tile", "Center"], viewModel.Picker!.Choices.Select(c => c.Label));
        Assert.All(viewModel.Picker.Choices, c => Assert.True(c.HasDescription));
    }

    [Fact]
    public async Task Choose_SetsTheValueClosesThePickerAndSaves()
    {
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();

        viewModel.Open(SettingsPickerKind.Resolution);
        viewModel.Choose(4);
        viewModel.Open(SettingsPickerKind.Order);
        viewModel.Choose(3);
        viewModel.Open(SettingsPickerKind.FillStyle);
        viewModel.Choose(3);

        Assert.Null(viewModel.Picker);
        var saved = await TestHelpers.WaitForSavedAsync(_store, s => s.Slideshow.FillStyle == WallpaperFillStyle.Tile && s.Download.ResolutionWidth == 3840);
        Assert.Equal(3840, saved.Download.ResolutionWidth);
        Assert.Equal(SlideshowOrder.Random, saved.Slideshow.Order);
        Assert.Equal(WallpaperFillStyle.Tile, saved.Slideshow.FillStyle);
    }

    [Fact]
    public async Task Choose_TheScheduleAndUnitPickersChangeTheSchedule()
    {
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();

        viewModel.Open(SettingsPickerKind.IntervalType);
        viewModel.Choose(2); // Hours
        viewModel.Open(SettingsPickerKind.ScheduleMode);
        viewModel.Choose(1); // Cron expression

        Assert.Equal(IntervalType.Hours, viewModel.Slideshow.SelectedIntervalType.Type);
        Assert.True(viewModel.Slideshow.IsCronMode);
    }

    [Fact]
    public async Task ClosePicker_ChangesNothing()
    {
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();

        viewModel.Open(SettingsPickerKind.Order);
        viewModel.ClosePicker();
        await Task.Delay(SettingsGroupViewModel.SaveDelayMs + 300);

        Assert.Null(viewModel.Picker);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public async Task EveryChange_SavesOnlyWhatTheTabShows()
    {
        await _store.UpdateAsync(latest =>
        {
            latest.RunOnStartup = false;
            latest.MinimizeToTray = false;
            latest.AutoUpdatesEnabled = false;
            latest.Download.Folder = "/kept/folder";
            latest.AnnotateWallpaper = false;
            latest.Annotation.FontFamily = "Kept Font";
            latest.Annotation.FontSize = 33;
            latest.Annotation.Color = "#123456";
            latest.Annotation.Position = AnnotationPosition.BottomRight;
            latest.Annotation.OutlineEnabled = false;
        });
        var before = await File.ReadAllTextAsync(_store.FilePath);
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();

        viewModel.Download.RetentionDays = 30;
        viewModel.Slideshow.Enabled = false;
        viewModel.Slideshow.FavoritePriorityEnabled = true;
        viewModel.Slideshow.FavoritePriorityWeight = 9;
        viewModel.Open(SettingsPickerKind.FillStyle);
        viewModel.Choose(1);
        viewModel.Debug.RequestDebugMode(false);
        viewModel.Flush();
        var saved = await TestHelpers.WaitForSavedAsync(_store, s => s.Download.RetentionDays == 30 && s.Slideshow.FavoritePriorityWeight == 9);

        Assert.Equal(30, saved.Download.RetentionDays);
        Assert.False(saved.RunOnStartup);
        Assert.False(saved.MinimizeToTray);
        Assert.False(saved.AutoUpdatesEnabled);
        Assert.Equal("/kept/folder", saved.Download.Folder);
        Assert.False(saved.AnnotateWallpaper);
        Assert.Equal("Kept Font", saved.Annotation.FontFamily);
        Assert.Equal(33, saved.Annotation.FontSize);
        Assert.Equal("#123456", saved.Annotation.Color);
        Assert.Equal(AnnotationPosition.BottomRight, saved.Annotation.Position);
        Assert.False(saved.Annotation.OutlineEnabled);
        Assert.NotEqual(before, await File.ReadAllTextAsync(_store.FilePath));
    }

    [Fact]
    public async Task FactoryReset_AsksTheTwoQuestions()
    {
        var reset = new RecordingFactoryReset();
        var viewModel = Create(reset);
        await viewModel.EnsureLoadedAsync();

        viewModel.FactoryResetCommand.Execute(null);
        await viewModel.Debug.AnswerAsync(true);
        await viewModel.Debug.AnswerAsync(true);

        Assert.Equal(1, reset.Calls);
    }

    [Fact]
    public async Task Status_ShowsEachGroupsLineWithItsTone()
    {
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();

        viewModel.Debug.RequestDebugMode(true);
        await viewModel.Debug.AnswerAsync(false);

        Assert.Equal("Debug mode cancelled. You only made it through 0 of 17 confirmations.", viewModel.Status.Message);
        Assert.Equal(StatusTone.Neutral, viewModel.Status.Tone);

        viewModel.Slideshow.ScheduleMode = SlideshowScheduleMode.CronExpression;
        viewModel.Slideshow.CronExpression = "not a schedule";
        await viewModel.Slideshow.SaveNowAsync();
        Assert.Equal(SlideshowSettingsViewModel.InvalidCronSentence, viewModel.Status.Message);
        Assert.True(viewModel.Status.IsError);

        viewModel.Download.RetentionDays = 45;
        await viewModel.Download.SaveNowAsync();
        Assert.Equal(SettingsGroupViewModel.SavedText, viewModel.Status.Message);
        Assert.True(viewModel.Status.IsSuccess);
    }

    [Fact]
    public async Task EnsureLoaded_ReadsTheSettingsOnce()
    {
        await _store.UpdateAsync(latest => latest.Download.RetentionDays = 77);
        var viewModel = Create();
        await viewModel.EnsureLoadedAsync();
        await _store.UpdateAsync(latest => latest.Download.RetentionDays = 88);

        await viewModel.EnsureLoadedAsync();

        Assert.Equal(77, viewModel.Download.RetentionDays);
    }
}

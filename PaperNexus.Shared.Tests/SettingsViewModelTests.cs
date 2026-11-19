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

    private SettingsViewModel Create(RecordingFactoryReset? reset = null, LogLevelSwitch? logLevels = null)
    {
        var viewModel = TestHelpers.WithoutSynchronizationContext(() => new SettingsViewModel(
            _store,
            new DownloadSettingsViewModel(_store),
            new SlideshowSettingsViewModel(_store, TestSettingRules.Phone),
            new DebugSettingsViewModel(_store, new TestAppInfo(), logLevels ?? new LogLevelSwitch(false), reset ?? new RecordingFactoryReset())));
        return viewModel;
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

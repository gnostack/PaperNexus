using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The phone's fastest slideshow in its settings (issue #203, step 5): no seconds, minutes picked
// from 15, 20 and 30, a schedule faster than every 15 minutes refused when typed and shown with a
// sentence when stored, nothing rewritten; the desktop keeps seconds, its ranges and any cron.
public class FifteenMinuteRuleTests : IDisposable
{
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();

    public void Dispose() => TestHelpers.DeleteSettingsStore(_store);

    private async Task<(SlideshowSettingsViewModel Group, List<SettingsStatus> Reports)> LoadAsync(IDeviceSettingRules rules)
    {
        var group = new SlideshowSettingsViewModel(_store, rules);
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;
        await group.LoadAsync();
        return (group, reports);
    }

    // The phone's Settings tab on the test store
    private SettingsViewModel CreatePhoneSettings()
    {
        var eggs = new EasterEggRequests();
        var viewModel = TestHelpers.WithoutSynchronizationContext(() => new SettingsViewModel(
            _store,
            new DownloadSettingsViewModel(_store),
            new SlideshowSettingsViewModel(_store, TestSettingRules.Phone),
            new DebugSettingsViewModel(_store, new TestAppInfo(), new LogLevelSwitch(false), new RecordingFactoryReset()),
            new AboutViewModel(new TestAppInfo(), new NoOpShellOpener(), eggs),
            new SecretsViewModel(_store, new TestDeviceEggs(EasterEggCatalog.All)),
            eggs));
        return viewModel;
    }

    private Task StoreScheduleAsync(SlideshowScheduleMode mode, double interval, IntervalType type, string cron) =>
        _store.UpdateAsync(latest =>
        {
            latest.Slideshow.ScheduleMode = mode;
            latest.Slideshow.Interval = interval;
            latest.Slideshow.IntervalType = type;
            latest.Slideshow.CronExpression = cron;
        });

    // --- The units each device offers ---

    [Fact]
    public void Phone_OffersMinutesToYears_WithMinutesPickedFromAList()
    {
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Phone);

        Assert.Equal(
            [IntervalType.Minutes, IntervalType.Hours, IntervalType.Days, IntervalType.Weeks, IntervalType.Months, IntervalType.Years],
            group.IntervalTypeChoices.Select(o => o.Type));
        var minutes = group.IntervalTypeChoices[0];
        Assert.Equal("Minutes", minutes.Label);
        Assert.Equal([15, 20, 30], minutes.FixedValues);
        Assert.All(group.IntervalTypeChoices.Skip(1), o => Assert.False(o.HasFixedValues));
        Assert.Equal((1d, 23d), (group.IntervalTypeChoices[1].Minimum, group.IntervalTypeChoices[1].Maximum));
        Assert.Equal((1d, 28d), (group.IntervalTypeChoices[2].Minimum, group.IntervalTypeChoices[2].Maximum));
    }

    [Fact]
    public void Desktop_OffersEveryUnitWithItsRanges_AsTheWindowLists()
    {
        var group = new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop);

        Assert.Equal(SlideshowSettingsViewModel.IntervalTypeOptions, group.IntervalTypeChoices);
        Assert.Equal(IntervalType.Seconds, group.IntervalTypeChoices[0].Type);
        Assert.Equal((1d, 59d), (group.IntervalTypeChoices[1].Minimum, group.IntervalTypeChoices[1].Maximum));
        Assert.DoesNotContain(group.IntervalTypeChoices, o => o.HasFixedValues);
        Assert.False(group.HasFixedIntervals);
    }

    // --- The phone's default and its minute choices ---

    [Fact]
    public async Task Phone_TheDefaultEveryThirtyMinutes_ShowsInTheListWithNoSentence()
    {
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        Assert.Equal(IntervalType.Minutes, group.SelectedIntervalType.Type);
        Assert.True(group.HasFixedIntervals);
        Assert.Equal([15, 20, 30], group.FixedIntervals);
        Assert.Equal("30", group.IntervalText);
        Assert.Empty(reports);
    }

    [Theory]
    [InlineData(15, "*/15 * * * *")]
    [InlineData(20, "*/20 * * * *")]
    [InlineData(30, "*/30 * * * *")]
    public async Task Phone_EachMinuteChoice_IsSavedWithItsCron(int minutes, string cron)
    {
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        group.Interval = minutes;
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(minutes, saved.Slideshow.Interval);
        Assert.Equal(IntervalType.Minutes, saved.Slideshow.IntervalType);
        Assert.Equal(cron, saved.Slideshow.CronExpression);
        Assert.Equal(SettingsGroupViewModel.SavedText, reports.Last().Text);
    }

    [Theory]
    [InlineData(2, 15)]
    [InlineData(23, 20)]
    public async Task Phone_ChoosingMinutes_MovesTheNumberToTheNearestChoice(int hours, int minutes)
    {
        var (group, _) = await LoadAsync(TestSettingRules.Phone);
        group.SelectedIntervalType = group.IntervalTypeChoices.Single(o => o.Type == IntervalType.Hours);
        group.Interval = hours;

        group.SelectedIntervalType = group.IntervalTypeChoices.Single(o => o.Type == IntervalType.Minutes);

        Assert.Equal(minutes, group.Interval);
    }

    [Fact]
    public async Task Phone_ThePickerListsTheChoices_WithTheCurrentOneMarked_AndSavesTheChosenOne()
    {
        var viewModel = CreatePhoneSettings();
        await viewModel.EnsureLoadedAsync();

        viewModel.Open(SettingsPickerKind.IntervalValue);

        var picker = Assert.IsType<SettingsPicker>(viewModel.Picker);
        Assert.Equal("Minutes", picker.Title);
        Assert.Equal(["15", "20", "30"], picker.Choices.Select(c => c.Label));
        Assert.Equal(["30"], picker.Choices.Where(c => c.IsSelected).Select(c => c.Label));
        Assert.All(picker.Choices, c => Assert.False(c.HasDescription));

        viewModel.Choose(0);
        await viewModel.Slideshow.SaveNowAsync();

        Assert.Null(viewModel.Picker);
        var saved = await _store.LoadAsync();
        Assert.Equal("*/15 * * * *", saved.Slideshow.CronExpression);
    }

    // --- A typed cron ---

    [Fact]
    public async Task Phone_ACronFasterThanFifteenMinutes_HoldsBackOnlyTheSchedule()
    {
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        group.ScheduleMode = SlideshowScheduleMode.CronExpression;
        group.CronExpression = "*/5 * * * *";
        group.SelectedOrder = SlideshowSettingsViewModel.SlideshowOrderOptions[3];  // Random
        group.FavoritePriorityEnabled = true;
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(SlideshowScheduleMode.Interval, saved.Slideshow.ScheduleMode);
        Assert.Equal("*/30 * * * *", saved.Slideshow.CronExpression);
        Assert.Equal(SlideshowOrder.Random, saved.Slideshow.Order);
        Assert.True(saved.Slideshow.FavoritePriorityEnabled);
        var refusal = Assert.Single(reports);
        Assert.Equal("The phone can change the wallpaper at most every 15 minutes.", refusal.Text);
        Assert.Equal(StatusTone.Error, refusal.Tone);
    }

    [Fact]
    public async Task Phone_ACronEveryFifteenMinutes_IsSaved()
    {
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        group.ScheduleMode = SlideshowScheduleMode.CronExpression;
        group.CronExpression = "*/15 * * * *";
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(SlideshowScheduleMode.CronExpression, saved.Slideshow.ScheduleMode);
        Assert.Equal("*/15 * * * *", saved.Slideshow.CronExpression);
        Assert.Equal(SettingsGroupViewModel.SavedText, Assert.Single(reports).Text);
    }

    [Fact]
    public async Task Phone_AnInvalidCron_StillSaysItIsInvalid()
    {
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        group.ScheduleMode = SlideshowScheduleMode.CronExpression;
        group.CronExpression = "not a schedule";
        await group.SaveNowAsync();

        Assert.Equal(SlideshowSettingsViewModel.InvalidCronSentence, Assert.Single(reports).Text);
    }

    // --- The desktop is unchanged ---

    [Fact]
    public async Task Desktop_ACronEveryFiveMinutes_IsSaved()
    {
        var (group, reports) = await LoadAsync(TestSettingRules.Desktop);

        group.ScheduleMode = SlideshowScheduleMode.CronExpression;
        group.CronExpression = "*/5 * * * *";
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal("*/5 * * * *", saved.Slideshow.CronExpression);
        Assert.Equal(SettingsGroupViewModel.SavedText, Assert.Single(reports).Text);
    }

    [Fact]
    public async Task Desktop_ASecondsInterval_IsSaved()
    {
        var (group, _) = await LoadAsync(TestSettingRules.Desktop);

        group.SelectedIntervalType = SlideshowSettingsViewModel.IntervalTypeOptions[0];  // Seconds
        group.Interval = 10;
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(IntervalType.Seconds, saved.Slideshow.IntervalType);
        Assert.Equal("*/10 * * * * *", saved.Slideshow.CronExpression);
    }

    [Fact]
    public async Task Desktop_TypedMinutes_KeepTheirRange()
    {
        var (group, _) = await LoadAsync(TestSettingRules.Desktop);

        group.Interval = 7;
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal("*/7 * * * *", saved.Slideshow.CronExpression);
        Assert.Equal(59m, group.IntervalMaximum);
    }

    [Fact]
    public async Task Desktop_AStoredFastSchedule_ShowsNoSentence()
    {
        await StoreScheduleAsync(SlideshowScheduleMode.CronExpression, 30, IntervalType.Minutes, "*/5 * * * *");

        var (_, reports) = await LoadAsync(TestSettingRules.Desktop);

        Assert.Empty(reports);
    }

    // --- A stored schedule that is too fast, on the phone (development phones only) ---

    [Fact]
    public async Task Phone_AStoredCronTooFast_ShowsAsItIs_WithTheSentence_AndIsNotRewritten()
    {
        await StoreScheduleAsync(SlideshowScheduleMode.CronExpression, 30, IntervalType.Minutes, "*/5 * * * *");
        var before = await File.ReadAllTextAsync(_store.FilePath);

        var (group, reports) = await LoadAsync(TestSettingRules.Phone);
        await Task.Delay(SettingsGroupViewModel.SaveDelayMs + 300);

        Assert.True(group.IsCronMode);
        Assert.Equal("*/5 * * * *", group.CronExpression);
        var line = Assert.Single(reports);
        Assert.Equal(SlideshowSettingsViewModel.TooFastSentence, line.Text);
        Assert.Equal(StatusTone.Error, line.Tone);
        Assert.True(line.Holds);
        Assert.Equal(before, await File.ReadAllTextAsync(_store.FilePath));
    }

    [Fact]
    public async Task Phone_StoredSeconds_ShowAsTheyAre_AndTheOtherSettingsStillSave()
    {
        await StoreScheduleAsync(SlideshowScheduleMode.Interval, 10, IntervalType.Seconds, "*/10 * * * * *");

        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        Assert.Equal(IntervalType.Seconds, group.SelectedIntervalType.Type);
        Assert.Equal(10, group.Interval);
        Assert.False(group.HasFixedIntervals);
        Assert.Equal(SlideshowSettingsViewModel.TooFastSentence, Assert.Single(reports).Text);

        group.SelectedOrder = SlideshowSettingsViewModel.SlideshowOrderOptions[0];  // Alphabetical
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(SlideshowOrder.Alphabetical, saved.Slideshow.Order);
        Assert.Equal(IntervalType.Seconds, saved.Slideshow.IntervalType);
        Assert.Equal(10, saved.Slideshow.Interval);
        Assert.Equal("*/10 * * * * *", saved.Slideshow.CronExpression);
    }

    [Fact]
    public async Task Phone_AStoredFiftyMinutes_ShowsAsItIs_WithTheSentence()
    {
        // Every 50 minutes changes at :00 and :50, ten minutes apart across the hour
        await StoreScheduleAsync(SlideshowScheduleMode.Interval, 50, IntervalType.Minutes, "*/50 * * * *");

        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        Assert.Equal("50", group.IntervalText);
        Assert.Equal(SlideshowSettingsViewModel.TooFastSentence, Assert.Single(reports).Text);
    }

    [Fact]
    public async Task Phone_AStoredFortyFiveMinutes_ShowsAsItIs_AndStaysWhenOtherSettingsSave()
    {
        // 45 is not in the list but never changes faster than every 15 minutes
        await StoreScheduleAsync(SlideshowScheduleMode.Interval, 45, IntervalType.Minutes, "*/45 * * * *");

        var viewModel = CreatePhoneSettings();
        await viewModel.EnsureLoadedAsync();
        var group = viewModel.Slideshow;

        Assert.Equal("45", group.IntervalText);
        Assert.Null(viewModel.Status.Message);
        viewModel.Open(SettingsPickerKind.IntervalValue);
        Assert.DoesNotContain(viewModel.Picker!.Choices, c => c.IsSelected);
        viewModel.ClosePicker();

        group.Enabled = false;
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.False(saved.Slideshow.Enabled);
        Assert.Equal(45, saved.Slideshow.Interval);
        Assert.Equal("*/45 * * * *", saved.Slideshow.CronExpression);
    }

    [Fact]
    public async Task Phone_AStoredFastScheduleWithTheSlideshowOff_ShowsNoSentence()
    {
        await StoreScheduleAsync(SlideshowScheduleMode.CronExpression, 30, IntervalType.Minutes, "*/5 * * * *");
        await _store.UpdateAsync(latest => latest.Slideshow.Enabled = false);

        var (_, reports) = await LoadAsync(TestSettingRules.Phone);

        Assert.Empty(reports);
    }
}

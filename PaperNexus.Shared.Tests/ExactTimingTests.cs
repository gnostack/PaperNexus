using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The phone's "Exact timing" switch (issue #203, step 5): it shows the saved value and only asks
// to change it; turning it on asks the phone's permission when it is not allowed yet, a refusal
// says why and saves nothing, and a permission taken away while the app was closed shows the
// switch off with that sentence and saves off. The desktop never shows or writes it.
public class ExactTimingTests : IDisposable
{
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();
    private readonly FakePermission _permission = new();

    public void Dispose() => TestHelpers.DeleteSettingsStore(_store);

    private async Task<(SlideshowSettingsViewModel Group, List<SettingsStatus> Reports)> LoadAsync(IDeviceSettingRules rules)
    {
        var group = new SlideshowSettingsViewModel(_store, rules, _permission);
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;
        await group.LoadAsync();
        return (group, reports);
    }

    private async Task<bool> SavedExactTimingAsync() => (await _store.LoadAsync()).Slideshow.ExactTiming;

    [Fact]
    public void OnlyThePhoneOffersIt()
    {
        Assert.True(new SlideshowSettingsViewModel(_store, TestSettingRules.Phone).OffersExactTiming);
        Assert.False(new SlideshowSettingsViewModel(_store, TestSettingRules.Desktop).OffersExactTiming);
    }

    [Fact]
    public async Task ItIsOffByDefault()
    {
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        Assert.False(group.ExactTiming);
        Assert.Empty(reports);
        Assert.False(new SlideshowSettings().ExactTiming);
    }

    [Fact]
    public async Task TurningItOn_WhenAllowed_SavesOn_WithoutAsking()
    {
        // Android 12 and 13 allow exact alarms from the start
        _permission.IsAllowed = true;
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        await group.RequestExactTimingAsync(true);
        await group.SaveNowAsync();

        Assert.True(group.ExactTiming);
        Assert.Equal(0, _permission.Asks);
        Assert.True(await SavedExactTimingAsync());
        Assert.Equal(SettingsGroupViewModel.SavedText, reports.Last().Text);
    }

    [Fact]
    public async Task TurningItOn_AsksThePhone_AndSavesOnWhenAllowed()
    {
        _permission.AnswersAllowed = true;
        var (group, _) = await LoadAsync(TestSettingRules.Phone);

        await group.RequestExactTimingAsync(true);
        await group.SaveNowAsync();

        Assert.Equal(1, _permission.Asks);
        Assert.True(group.ExactTiming);
        Assert.True(await SavedExactTimingAsync());
    }

    [Fact]
    public async Task TurningItOn_Refused_StaysOff_SaysWhy_AndSavesNothing()
    {
        _permission.AnswersAllowed = false;
        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        await group.RequestExactTimingAsync(true);
        await Task.Delay(SettingsGroupViewModel.SaveDelayMs + 300);

        Assert.Equal(1, _permission.Asks);
        Assert.False(group.ExactTiming);
        var line = Assert.Single(reports);
        Assert.Equal("Exact timing needs the phone's Alarms & reminders permission.", line.Text);
        Assert.Equal(StatusTone.Error, line.Tone);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public async Task TurningItOff_SavesOff()
    {
        _permission.IsAllowed = true;
        await _store.UpdateAsync(s => s.Slideshow.ExactTiming = true);
        var (group, _) = await LoadAsync(TestSettingRules.Phone);
        Assert.True(group.ExactTiming);

        await group.RequestExactTimingAsync(false);
        await group.SaveNowAsync();

        Assert.False(group.ExactTiming);
        Assert.False(await SavedExactTimingAsync());
    }

    [Fact]
    public async Task SavedOn_ButNoLongerAllowed_ShowsOff_SaysWhy_AndSavesOff()
    {
        // The permission was taken away while the app was closed
        await _store.UpdateAsync(s => s.Slideshow.ExactTiming = true);
        _permission.IsAllowed = false;

        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        Assert.False(group.ExactTiming);
        var line = Assert.Single(reports);
        Assert.Equal(SlideshowSettingsViewModel.ExactTimingNeedsPermissionSentence, line.Text);
        Assert.True(line.Holds);
        await TestHelpers.WaitUntilAsync(async () => !await SavedExactTimingAsync());
    }

    [Fact]
    public async Task SavedOn_AndAllowed_ShowsOn()
    {
        await _store.UpdateAsync(s => s.Slideshow.ExactTiming = true);
        _permission.IsAllowed = true;

        var (group, reports) = await LoadAsync(TestSettingRules.Phone);

        Assert.True(group.ExactTiming);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task TheDesktop_NeverWritesIt()
    {
        await _store.UpdateAsync(s => s.Slideshow.ExactTiming = true);
        var (group, _) = await LoadAsync(TestSettingRules.Desktop);

        await group.RequestExactTimingAsync(false);
        group.SelectedOrder = SlideshowSettingsViewModel.SlideshowOrderOptions[0];
        await group.SaveNowAsync();

        Assert.True(await SavedExactTimingAsync());
        Assert.Equal(SlideshowOrder.Alphabetical, (await _store.LoadAsync()).Slideshow.Order);
    }

    private sealed class FakePermission : IExactTimingPermission
    {
        public bool IsAllowed { get; set; }
        public bool AnswersAllowed { get; set; }
        public int Asks { get; private set; }

        public Task<bool> AskAsync()
        {
            Asks++;
            IsAllowed = AnswersAllowed;
            return Task.FromResult(AnswersAllowed);
        }
    }
}

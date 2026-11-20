using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The desktop settings window's Wallpaper Sources tab: rows from IWallpaperSources, each change
// saved at once with the window's own status sentences, and the window's settings save never
// writing sources.
public class WallpaperConfigSourcesTests : IDisposable
{
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();

    public void Dispose()
    {
        TestHelpers.DeleteSettingsStore(_store);
        TestHelpers.Cleanup();
    }

    private async Task<(WallpaperConfigViewModel Vm, WallpaperSources Sources)> CreateAsync(IDownloadWallpapers? downloader = null)
    {
        await _store.WriteAsync(new WallpaperNexusSettings
        {
            Slideshow = new SlideshowSettings { Enabled = false },
            Sources =
            [
                new WallpaperSource { Name = "One", Url = "https://one.test/feed", CronExpression = "0 1 * * *" },
                new WallpaperSource { Name = "Two", Url = "https://two.test/feed", IsEnabled = false },
            ],
        });
        var sources = TestHelpers.CreateWallpaperSources(_store, new FeedHandler(FeedHandler.TwoPictures));
        var vm = TestHelpers.CreateViewModel(_store, sources, downloader);
        await vm.LoadAsync();
        return (vm, sources);
    }

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

    [Fact]
    public async Task Load_ShowsEveryRowWithItsFields()
    {
        var (vm, _) = await CreateAsync();

        Assert.Equal(["One", "Two"], vm.Sources.Select(s => s.Name));
        Assert.Equal("https://one.test/feed", vm.Sources[0].Url);
        Assert.Equal("0 1 * * *", vm.Sources[0].CronExpression);
        Assert.False(vm.Sources[1].IsEnabled);
        vm.Cleanup();
    }

    [Fact]
    public async Task Checkbox_SavesAtOnce_AndSaysSettingsSaved()
    {
        var (vm, _) = await CreateAsync();

        vm.Sources[1].IsEnabled = true;
        await WaitUntil(() => vm.StatusMessage == "✓ Settings saved.");

        Assert.True((await _store.LoadAsync()).Sources[1].IsEnabled);
        vm.Cleanup();
    }

    [Fact]
    public async Task RowDelete_DeletesAtOnce()
    {
        var (vm, _) = await CreateAsync();

        await vm.Sources[0].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["Two"], vm.Sources.Select(s => s.Name));
        Assert.Equal(["Two"], (await _store.LoadAsync()).Sources.Select(s => s.Name));
        Assert.Equal("✓ Settings saved.", vm.StatusMessage);
        vm.Cleanup();
    }

    [Fact]
    public async Task RowEdit_AsksTheWindowToOpenTheDialog_FilledWithTheRow()
    {
        var (vm, _) = await CreateAsync();
        SourceItem? requested = null;
        vm.EditSourceRequested += item => requested = item;

        vm.Sources[1].EditCommand.Execute(null);
        var form = vm.CreateSourceForm(requested);

        Assert.Same(vm.Sources[1], requested);
        Assert.Equal("Edit Wallpaper Source", form.Title);
        Assert.Equal("Two", form.Name);
        vm.Cleanup();
    }

    [Fact]
    public async Task DialogSave_SelectsTheSavedRow_AndSaysSettingsSaved()
    {
        var (vm, _) = await CreateAsync();
        var form = vm.CreateSourceForm(null);
        form.Name = "Lakes";
        form.Url = "https://lakes.test/feed";

        await form.SaveCommand.ExecuteAsync(null);
        await vm.SourceSavedAsync(form);

        Assert.Equal(["One", "Two", "Lakes"], vm.Sources.Select(s => s.Name));
        Assert.Same(vm.Sources[2], vm.SelectedSource);
        Assert.Equal("✓ Settings saved.", vm.StatusMessage);
        vm.Cleanup();
    }

    // The window's settings save writes only its own settings: a source added and a download
    // time recorded after the window loaded both survive it.
    [Fact]
    public async Task WindowSave_LeavesTheSourcesAlone()
    {
        var (vm, sources) = await CreateAsync();
        var downloaded = DateTimeOffset.UtcNow.AddMinutes(-3);
        await sources.AddAsync(new SourceFields("Lakes", "https://lakes.test/feed", "$[*].imageUrl", "$[*].title", "", true));
        await _store.UpdateAsync(latest => latest.Sources[0].LastDownloadUtc = downloaded);

        vm.Download.RetentionDays = 30;
        for (var i = 0; i < 100 && (await _store.LoadAsync()).Download.RetentionDays != 30; i++)
            await Task.Delay(50);

        var saved = await _store.LoadAsync();
        Assert.Equal(30, saved.Download.RetentionDays);
        Assert.Equal(["One", "Two", "Lakes"], saved.Sources.Select(s => s.Name));
        Assert.Equal(downloaded, saved.Sources[0].LastDownloadUtc);
        vm.Cleanup();
    }

    // An older file with "Twin" and "twin": the second row's delete, checkbox and edit change the
    // second source only, and the dialog's save selects the row that was edited.
    [Fact]
    public async Task Twins_TheSecondRowActsOnTheSecondSource()
    {
        await _store.WriteAsync(new WallpaperNexusSettings
        {
            Slideshow = new SlideshowSettings { Enabled = false },
            Sources =
            [
                new WallpaperSource { Name = "Twin", Url = "https://first.test/feed" },
                new WallpaperSource { Name = "twin", Url = "https://second.test/feed" },
                new WallpaperSource { Name = "TWIN", Url = "https://third.test/feed" },
            ],
        });
        var sources = TestHelpers.CreateWallpaperSources(_store);
        var vm = TestHelpers.CreateViewModel(_store, sources);
        await vm.LoadAsync();

        vm.Sources[1].IsEnabled = false;
        await WaitUntil(() => vm.StatusMessage == "✓ Settings saved.");
        var form = vm.CreateSourceForm(vm.Sources[2]);
        form.Url = "https://edited.test/feed";
        await form.SaveCommand.ExecuteAsync(null);
        await vm.SourceSavedAsync(form);
        Assert.Same(vm.Sources[2], vm.SelectedSource);
        await vm.Sources[1].DeleteCommand.ExecuteAsync(null);

        var saved = (await _store.LoadAsync()).Sources;
        Assert.Equal(["https://first.test/feed", "https://edited.test/feed"], saved.Select(s => s.Url));
        Assert.True(saved[0].IsEnabled);
        await WaitUntil(() => vm.Sources.Count == 2);
        Assert.Equal(["https://first.test/feed", "https://edited.test/feed"], vm.Sources.Select(s => s.Url));
        vm.Cleanup();
    }

    [Theory]
    [InlineData(2, 0, "✓ Download complete.")]
    [InlineData(2, 1, "✗ Download failed for 1 of 2 sources.")]
    [InlineData(1, 1, "✗ Download failed: check your connection.")]
    [InlineData(0, 0, "No enabled sources to download from.")]
    public async Task DownloadNow_SaysWhatHappened(int tried, int failed, string expected)
    {
        var downloader = new OutcomeDownloader(new DownloadOutcome(tried, failed));
        var (vm, _) = await CreateAsync(downloader);

        var download = vm.DownloadNowCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.StatusMessage == expected);

        Assert.Equal(expected, vm.StatusMessage);
        vm.Cleanup();
        await Task.WhenAny(download, Task.Delay(100));
    }

    private sealed class OutcomeDownloader(DownloadOutcome outcome) : IDownloadWallpapers
    {
        // No test here runs the scheduled download
        public Task<DownloadOutcome> DownloadDueAsync() =>
            throw new NotSupportedException("The scheduled download is not part of this test.");

        public event Action? WallpapersDownloaded { add { } remove { } }

        public Task<DownloadOutcome> DownloadAllAsync() => Task.FromResult(outcome);
    }
}

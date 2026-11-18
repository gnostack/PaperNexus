using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The phone's Current tab: what it shows on opening, after a download, and around Next,
// Random and the heart. The switcher and downloader are stand-ins that act on a real folder
// and a real settings store, so the tab reads the same files it would on the phone.
public class CurrentWallpaperViewModelTests : IDisposable
{
    private readonly string _wallpaperDir;
    private readonly SettingsStore _store;
    private readonly FakeSwitcher _switcher;
    private readonly FakeDownloader _downloader;
    private readonly FakeWallpaperApplier _applier = new();

    public CurrentWallpaperViewModelTests()
    {
        _wallpaperDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Current_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_wallpaperDir);
        _store = TestHelpers.CreateSettingsStore();
        _switcher = new FakeSwitcher(_store, _wallpaperDir);
        _downloader = new FakeDownloader(_wallpaperDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_wallpaperDir)) Directory.Delete(_wallpaperDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    private CurrentWallpaperViewModel CreateViewModel()
    {
        var viewModel = new CurrentWallpaperViewModel(_store, _switcher, _downloader, _applier, NullLogger<CurrentWallpaperViewModel>.Instance);
        return viewModel;
    }

    private string AddPicture(string name)
    {
        var path = Path.Combine(_wallpaperDir, name);
        TestHelpers.CreateSmallPng(path);
        return path;
    }

    // Opening with a current wallpaper shows it, its title and its heart.
    [Fact]
    public async Task Load_WithACurrentWallpaper_ShowsIt()
    {
        var current = AddPicture("Mountain Lake - abc123.png");
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: current);
        var settings = await _store.LoadAsync();
        settings.FavoriteWallpapers.Add(current);
        await _store.WriteAsync(settings);
        using var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        Assert.Equal(CurrentWallpaperState.Showing, viewModel.State);
        Assert.Equal(current, viewModel.PicturePath);
        Assert.Equal("Mountain Lake", viewModel.Title);
        Assert.True(viewModel.IsFavorite);
        Assert.True(viewModel.CanSwitch);
        Assert.True(viewModel.CanFavorite);
        Assert.False(viewModel.HasError);
        Assert.Equal(0, _switcher.Switches);
    }

    // First open with no pictures: the tab says it is downloading, with Next and Random dimmed,
    // and once pictures arrive it sets the next one in slideshow order without a tap.
    [Fact]
    public async Task Load_NoPictures_DownloadsThenSetsTheFirstWallpaper()
    {
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        var gate = new TaskCompletionSource();
        _downloader.Gate = gate;
        _downloader.PicturesToAdd = ["First - a.png", "Second - b.png"];
        using var viewModel = CreateViewModel();

        var loading = viewModel.LoadAsync();
        await WaitUntil(() => viewModel.State == CurrentWallpaperState.Downloading);

        Assert.Equal(CurrentWallpaperViewModel.DownloadingText, viewModel.PlaceholderText);
        Assert.False(viewModel.CanSwitch);
        Assert.False(viewModel.CanFavorite);
        gate.SetResult();
        await loading;

        Assert.Equal(CurrentWallpaperState.Showing, viewModel.State);
        Assert.Equal(1, _switcher.NextSwitches);
        Assert.Equal(Path.Combine(_wallpaperDir, "First - a.png"), viewModel.PicturePath);
        Assert.Equal("First", viewModel.Title);
        Assert.True(viewModel.CanSwitch);
    }

    // A download that brings no pictures shows the failure sentence with Try again; Try again
    // downloads and then sets the first wallpaper.
    [Fact]
    public async Task Load_DownloadBringsNothing_ShowsTheFailureAndTryAgainRecovers()
    {
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        _downloader.Fails = true;
        using var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        Assert.Equal(CurrentWallpaperState.DownloadFailed, viewModel.State);
        Assert.True(viewModel.IsDownloadFailed);
        Assert.Equal(CurrentWallpaperViewModel.DownloadFailedText, viewModel.PlaceholderText);
        Assert.False(viewModel.CanSwitch);

        _downloader.Fails = false;
        _downloader.PicturesToAdd = ["Arrived - a.png"];
        await viewModel.TryAgainCommand.ExecuteAsync(null);

        Assert.Equal(2, _downloader.Downloads);
        Assert.Equal(CurrentWallpaperState.Showing, viewModel.State);
        Assert.Equal("Arrived", viewModel.Title);
    }

    // Pictures but no current wallpaper (the current one was deleted): the next one is set.
    [Fact]
    public async Task Load_PicturesButNoCurrent_SetsTheNextWithoutDownloading()
    {
        AddPicture("Only - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: Path.Combine(_wallpaperDir, "gone.png"));
        using var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        Assert.Equal(0, _downloader.Downloads);
        Assert.Equal(1, _switcher.NextSwitches);
        Assert.Equal(CurrentWallpaperState.Showing, viewModel.State);
    }

    // Next and Random both dim while either works, then the new wallpaper is shown.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NextOrRandom_DimsBothWhileWorking_ThenShowsTheNewWallpaper(bool next)
    {
        var first = AddPicture("One - a.png");
        AddPicture("Two - b.png");
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: first);
        using var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        var gate = new TaskCompletionSource();
        _switcher.Gate = gate;

        var command = next ? viewModel.NextCommand.ExecuteAsync(null) : viewModel.RandomCommand.ExecuteAsync(null);
        await WaitUntil(() => viewModel.IsWorking);

        Assert.False(viewModel.CanSwitch);
        Assert.Equal("One", viewModel.Title);
        gate.SetResult();
        await command;

        Assert.True(viewModel.CanSwitch);
        Assert.Equal("Two", viewModel.Title);
        Assert.Equal(CurrentWallpaperState.Showing, viewModel.State);
    }

    // A change the phone does not take shows the red line, keeps the current wallpaper shown,
    // and the buttons work again at once.
    [Fact]
    public async Task Next_Fails_ShowsTheRedLineAndTheButtonsWorkAgain()
    {
        var first = AddPicture("One - a.png");
        AddPicture("Two - b.png");
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: first);
        using var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        _switcher.Refuses = true;

        await viewModel.NextCommand.ExecuteAsync(null);

        Assert.Equal(CurrentWallpaperViewModel.SwitchFailedText, viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
        Assert.Equal("One", viewModel.Title);
        Assert.True(viewModel.CanSwitch);

        // The next change that works clears the red line
        _switcher.Refuses = false;
        await viewModel.NextCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasError);
        Assert.Equal("Two", viewModel.Title);
    }

    // A phone that forbids changing the wallpaper shows the red line and keeps Next and
    // Random dimmed; the first wallpaper is not attempted.
    [Fact]
    public async Task Load_ChangesNotAllowed_ShowsTheRedLineAndKeepsTheButtonsDimmed()
    {
        AddPicture("One - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        _applier.CanSetWallpaper = false;
        using var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        Assert.Equal(CurrentWallpaperViewModel.NotAllowedText, viewModel.ErrorMessage);
        Assert.False(viewModel.CanSwitch);
        Assert.Equal(0, _switcher.Switches);
        Assert.Equal(CurrentWallpaperState.NoWallpaper, viewModel.State);
    }

    // The heart fills in and saves the favorite; tapping again clears both.
    [Fact]
    public async Task ToggleFavorite_FillsAndClearsTheHeartAndTheSavedFavorite()
    {
        var current = AddPicture("One - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: current);
        using var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        await viewModel.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsFavorite);
        Assert.Equal("Remove from favorites", viewModel.FavoriteToolTip);
        Assert.Contains(current, (await _store.LoadAsync()).FavoriteWallpapers);

        await viewModel.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsFavorite);
        Assert.DoesNotContain(current, (await _store.LoadAsync()).FavoriteWallpapers);
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

    // Switches in alphabetical order through the folder's pictures, recording the current one
    // in the settings as the real switcher does.
    private sealed class FakeSwitcher : ISwitchWallpaper
    {
        private readonly ISettingsStore _store;
        private readonly string _folder;

        public FakeSwitcher(ISettingsStore store, string folder)
        {
            _store = store;
            _folder = folder;
        }

        public event Action<string>? WallpaperChanged;

        public TaskCompletionSource? Gate { get; set; }
        public bool Refuses { get; set; }
        public int Switches { get; private set; }
        public int NextSwitches { get; private set; }

        public Task<string?> SwitchToNextAsync()
        {
            NextSwitches++;
            return SwitchAsync();
        }

        public Task<string?> SwitchToRandomAsync() => SwitchAsync();

        public Task<string?> SwitchToSpecificAsync(string path) => SwitchAsync();

        public Task<bool> HasWallpapersAsync() => Task.FromResult(Pictures().Count > 0);

        private List<string> Pictures() => Directory.GetFiles(_folder, "*.png").Order().ToList();

        private async Task<string?> SwitchAsync()
        {
            Switches++;
            if (Gate is not null)
                await Gate.Task;
            var pictures = Pictures();
            if (pictures.Count == 0)
                return null;
            var settings = await _store.LoadAsync();
            var index = pictures.IndexOf(settings.CurrentWallpaperPath);
            var next = pictures[(index + 1) % pictures.Count];
            if (Refuses)
                throw new WallpaperRefusedException(next);
            await _store.UpdateAsync(latest => latest.CurrentWallpaperPath = next);
            WallpaperChanged?.Invoke(next);
            return next;
        }
    }

    // Adds the given pictures to the folder when a download runs, or adds nothing when it
    // fails.
    private sealed class FakeDownloader : IDownloadWallpapers
    {
        private readonly string _folder;

        public FakeDownloader(string folder)
        {
            _folder = folder;
        }

        public TaskCompletionSource? Gate { get; set; }
        public bool Fails { get; set; }
        public string[] PicturesToAdd { get; set; } = [];
        public int Downloads { get; private set; }

        public async Task DownloadAllAsync()
        {
            Downloads++;
            if (Gate is not null)
                await Gate.Task;
            if (Fails)
                return;
            foreach (var name in PicturesToAdd)
                TestHelpers.CreateSmallPng(Path.Combine(_folder, name));
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The phone's Gallery and Favorites tabs: what each lists, the line each tap shows, and how a
// change made on one tab shows on the other at once. Built on the real library, switcher and
// settings store, with a wallpaper applier that changes nothing on the machine.
[Collection("Wallpaper")]
public class PictureListViewModelTests : IDisposable
{
    private readonly string _folder;
    private readonly SettingsStore _store;
    private readonly GatedSwitcher _switcher;
    private readonly WallpaperLibrary _library;
    private readonly AllowingApplier _applier = new();

    public PictureListViewModelTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"PaperNexus_Lists_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        _store = TestHelpers.CreateSettingsStore();
        var realSwitcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store, TestHelpers.AppFolders, TestSettingRules.Desktop);
        _switcher = new GatedSwitcher(realSwitcher);
        _library = TestHelpers.CreateLibrary(_store, _switcher);
    }

    public void Dispose()
    {
        TestHelpers.Cleanup();
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    private string AddPicture(string name, int minutesAgo = 0)
    {
        var path = Path.Combine(_folder, name);
        TestHelpers.CreateSmallPng(path);
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-minutesAgo));
        return path;
    }

    private GalleryViewModel CreateGallery(ISettingsStore? store = null)
    {
        var library = store is null ? _library : TestHelpers.CreateLibrary(store, _switcher);
        return TestHelpers.WithoutSynchronizationContext(
            () => new GalleryViewModel(library, _switcher, _applier, NullLogger<GalleryViewModel>.Instance));
    }

    private FavoritesViewModel CreateFavorites()
    {
        return TestHelpers.WithoutSynchronizationContext(
            () => new FavoritesViewModel(_library, _switcher, _applier, NullLogger<FavoritesViewModel>.Instance));
    }

    // Three pictures, B the newest and current.
    private async Task<(string A, string B, string C)> ThreePicturesAsync()
    {
        var a = AddPicture("A - a.png", minutesAgo: 10);
        var b = AddPicture("B - b.png", minutesAgo: 1);
        var c = AddPicture("C - c.png", minutesAgo: 20);
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: b);
        return (a, b, c);
    }

    [Fact]
    public async Task Gallery_ListsNewestFirst_WithTitlesAndMarks()
    {
        var (a, b, c) = await ThreePicturesAsync();
        await _store.UpdateAsync(latest => latest.BannedWallpapers.Add(c));
        using var gallery = CreateGallery();

        Assert.False(gallery.IsEmpty);
        await gallery.LoadAsync();

        Assert.Equal([b, a, c], gallery.Items.Select(i => i.FilePath));
        Assert.Equal("B", gallery.Items[0].DisplayName);
        Assert.True(gallery.Items[2].IsBanned);
        Assert.False(gallery.IsEmpty);
        Assert.False(gallery.Status.IsShown);
    }

    // Before the first download the folder does not exist yet; the tab just has nothing.
    [Fact]
    public async Task Gallery_NoPictures_SaysNoWallpapersYet()
    {
        await TestHelpers.WriteSettingsAsync(_store, Path.Combine(_folder, "not yet"));
        using var gallery = CreateGallery();

        await gallery.LoadAsync();

        Assert.True(gallery.IsEmpty);
        Assert.Equal("No wallpapers yet.", gallery.EmptyText);
        Assert.False(gallery.Status.IsShown);
    }

    [Fact]
    public async Task Gallery_Heart_SavesAndConfirms_ThenRemoves()
    {
        var (a, _, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.LoadAsync();
        var card = gallery.Items.Single(i => i.FilePath == a);

        await card.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.True(card.IsFavorite);
        Assert.Equal("Remove from favorites", card.FavoriteToolTip);
        Assert.Equal("Added to favorites.", gallery.Status.Message);
        Assert.True(gallery.Status.IsSuccess);
        Assert.Equal([a], (await _store.LoadAsync()).FavoriteWallpapers);

        await card.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.False(card.IsFavorite);
        Assert.Equal("Removed from favorites.", gallery.Status.Message);
        Assert.Empty((await _store.LoadAsync()).FavoriteWallpapers);
    }

    [Fact]
    public async Task Gallery_Ban_TintsAndConfirms_ThenLetsBackIn()
    {
        var (a, _, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.LoadAsync();
        var card = gallery.Items.Single(i => i.FilePath == a);

        await card.ToggleBanCommand.ExecuteAsync(null);
        Assert.True(card.IsBanned);
        Assert.Equal("Banned from rotation.", gallery.Status.Message);
        Assert.Equal([a], (await _store.LoadAsync()).BannedWallpapers);

        await card.ToggleBanCommand.ExecuteAsync(null);
        Assert.False(card.IsBanned);
        Assert.Equal("Back in rotation.", gallery.Status.Message);
        Assert.Empty((await _store.LoadAsync()).BannedWallpapers);
    }

    [Fact]
    public async Task Gallery_DeleteAPictureNotCurrent_RemovesTheCard_SaysDeleted()
    {
        var (a, b, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.LoadAsync();

        await gallery.Items.Single(i => i.FilePath == a).DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(gallery.Items, i => i.FilePath == a);
        Assert.False(File.Exists(a));
        Assert.Equal("Wallpaper deleted.", gallery.Status.Message);
        Assert.Equal(b, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    [Fact]
    public async Task Gallery_DeleteTheCurrent_SetsTheNext_AndSaysWhich()
    {
        var (a, b, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.LoadAsync();

        await gallery.Items.Single(i => i.FilePath == b).DeleteCommand.ExecuteAsync(null);

        // Slideshow order is alphabetical in these settings, so A comes first
        Assert.Equal("Deleted and switched to: A", gallery.Status.Message);
        Assert.Equal(a, (await _store.LoadAsync()).CurrentWallpaperPath);
        Assert.Equal(2, gallery.Items.Count);
    }

    // Set as current waits behind any running switch: the card's buttons dim and the line says
    // so meanwhile, then it says which picture is the wallpaper.
    [Fact]
    public async Task Gallery_SetAsCurrent_DimsTheCardWhileSwitching_ThenConfirms()
    {
        var (a, _, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.LoadAsync();
        var card = gallery.Items.Single(i => i.FilePath == a);
        _switcher.Gate = new TaskCompletionSource();

        var running = card.SetAsCurrentCommand.ExecuteAsync(null);
        Assert.True(card.IsBusy);
        Assert.False(card.CanAct);
        Assert.False(card.CanSetAsCurrent);
        Assert.Equal("Switching wallpaper...", gallery.Status.Message);
        Assert.False(gallery.Status.IsSuccess);

        _switcher.Gate.SetResult();
        await running;

        Assert.False(card.IsBusy);
        Assert.Equal("Switched to: A", gallery.Status.Message);
        Assert.Equal(a, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    // A banned picture can still be set by hand, as on the desktop.
    [Fact]
    public async Task Gallery_SetAsCurrent_ABannedPicture_IsAllowed()
    {
        var (a, _, _) = await ThreePicturesAsync();
        await _store.UpdateAsync(latest => latest.BannedWallpapers.Add(a));
        using var gallery = CreateGallery();
        await gallery.LoadAsync();

        await gallery.Items.Single(i => i.FilePath == a).SetAsCurrentCommand.ExecuteAsync(null);

        Assert.Equal("Switched to: A", gallery.Status.Message);
        Assert.Equal(a, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    [Fact]
    public async Task Gallery_SetAsCurrent_FileGone_SaysNotFound()
    {
        var (a, _, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.LoadAsync();
        File.Delete(a);

        await gallery.Items.Single(i => i.FilePath == a).SetAsCurrentCommand.ExecuteAsync(null);

        Assert.Equal("Wallpaper file not found.", gallery.Status.Message);
        Assert.True(gallery.Status.IsError);
    }

    [Fact]
    public async Task Gallery_SaveFails_ShowsTheDesktopsErrorSentence()
    {
        var (a, _, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery(new FailingUpdateStore(_store));
        await gallery.LoadAsync();
        var card = gallery.Items.Single(i => i.FilePath == a);

        await card.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.Equal("Error updating favorites: The disk is full.", gallery.Status.Message);
        Assert.True(gallery.Status.IsError);
        Assert.False(card.IsFavorite);

        await card.ToggleBanCommand.ExecuteAsync(null);
        Assert.Equal("Error updating ban list: The disk is full.", gallery.Status.Message);
        Assert.False(card.IsBanned);
    }

    // On a phone that forbids changing the wallpaper, set as current stays dimmed.
    [Fact]
    public async Task Gallery_PhoneForbidsWallpaperChanges_SetAsCurrentStaysDimmed()
    {
        await ThreePicturesAsync();
        _applier.Allowed = false;
        using var gallery = CreateGallery();
        await gallery.LoadAsync();

        Assert.All(gallery.Items, i => Assert.False(i.CanSetAsCurrent));
        Assert.All(gallery.Items, i => Assert.True(i.CanAct));
    }

    // A finished download adds its pictures to an open Gallery, keeping the cards it had.
    [Fact]
    public async Task Gallery_DownloadFinished_AddsTheNewPictures()
    {
        var (_, b, _) = await ThreePicturesAsync();
        var downloader = new EventDownloader();
        var library = new WallpaperLibrary(_store, _switcher, downloader);
        using var gallery = TestHelpers.WithoutSynchronizationContext(
            () => new GalleryViewModel(library, _switcher, _applier, NullLogger<GalleryViewModel>.Instance));
        await gallery.LoadAsync();
        var keptCard = gallery.Items.Single(i => i.FilePath == b);

        var fresh = AddPicture("Fresh - d.png");
        downloader.Finish();
        await WaitUntil(() => gallery.Items.Count == 4);

        Assert.Equal(fresh, gallery.Items[0].FilePath);
        Assert.Same(keptCard, gallery.Items.Single(i => i.FilePath == b));
    }

    [Fact]
    public async Task Favorites_ListedInHeartedOrder_WithTheEmptySentence()
    {
        var (a, b, c) = await ThreePicturesAsync();
        using var favorites = CreateFavorites();
        await favorites.LoadAsync();
        Assert.True(favorites.IsEmpty);
        Assert.Equal("No favorites yet. Tap the heart on a wallpaper to add one.", favorites.EmptyText);

        await _store.UpdateAsync(latest => latest.FavoriteWallpapers = [c, a, b]);
        await favorites.LoadAsync();

        Assert.Equal([c, a, b], favorites.Items.Select(i => i.FilePath));
        Assert.All(favorites.Items, i => Assert.True(i.IsFavorite));
    }

    [Fact]
    public async Task Favorites_Remove_DropsTheCardAndTheFavorite()
    {
        var (a, b, _) = await ThreePicturesAsync();
        await _store.UpdateAsync(latest => latest.FavoriteWallpapers = [a, b]);
        using var favorites = CreateFavorites();
        await favorites.LoadAsync();

        await favorites.Items[0].ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.Equal([b], favorites.Items.Select(i => i.FilePath));
        Assert.Equal("Removed from favorites.", favorites.Status.Message);
        Assert.Equal([b], (await _store.LoadAsync()).FavoriteWallpapers);
    }

    // Hearting, deleting and removing on one tab shows on the other at once.
    [Fact]
    public async Task ChangesOnOneTab_ShowOnTheOther()
    {
        var (a, b, c) = await ThreePicturesAsync();
        await _store.UpdateAsync(latest => latest.FavoriteWallpapers = [c]);
        using var gallery = CreateGallery();
        using var favorites = CreateFavorites();
        await gallery.LoadAsync();
        await favorites.LoadAsync();

        // A heart in the Gallery adds the picture at the end of Favorites
        await gallery.Items.Single(i => i.FilePath == a).ToggleFavoriteCommand.ExecuteAsync(null);
        await WaitUntil(() => favorites.Items.Count == 2);
        Assert.Equal([c, a], favorites.Items.Select(i => i.FilePath));

        // Removing it on Favorites clears the Gallery's heart
        await favorites.Items.Single(i => i.FilePath == a).ToggleFavoriteCommand.ExecuteAsync(null);
        await WaitUntil(() => !gallery.Items.Single(i => i.FilePath == a).IsFavorite);

        // Deleting a favorite in the Gallery removes it from Favorites
        await gallery.Items.Single(i => i.FilePath == c).DeleteCommand.ExecuteAsync(null);
        await WaitUntil(() => favorites.Items.Count == 0);
        Assert.True(favorites.IsEmpty);
        Assert.DoesNotContain(gallery.Items, i => i.FilePath == c);
    }

    // The view model outlives the screen: a view built for a recreated screen finds the same
    // cards (not listed again), the confirmation line still within its three seconds, and the
    // place in the list.
    [Fact]
    public async Task ASecondViewAttaching_FindsTheCardsTheLineAndThePlace()
    {
        var (a, _, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.EnsureLoadedAsync();
        var cards = gallery.Items.ToList();
        await gallery.Items.Single(i => i.FilePath == a).ToggleFavoriteCommand.ExecuteAsync(null);
        gallery.ScrollOffset = 1234;
        // A picture added since is not listed by the second view; a reload would list it
        AddPicture("Late - d.png");

        await gallery.EnsureLoadedAsync();

        Assert.Equal(cards, gallery.Items);
        Assert.Equal("Added to favorites.", gallery.Status.Message);
        Assert.Equal(1234, gallery.ScrollOffset);
    }

    // The line still goes after its three seconds, whichever view shows it.
    [Fact]
    public async Task TheConfirmationLine_GoesAfterThreeSeconds()
    {
        var (a, _, _) = await ThreePicturesAsync();
        using var gallery = CreateGallery();
        await gallery.EnsureLoadedAsync();

        await gallery.Items.Single(i => i.FilePath == a).ToggleBanCommand.ExecuteAsync(null);
        Assert.True(gallery.Status.IsShown);

        await WaitUntil(() => !gallery.Status.IsShown);
    }

    // A closed tab stops following the library.
    [Fact]
    public async Task Dispose_StopsFollowingChanges()
    {
        var (a, _, _) = await ThreePicturesAsync();
        var gallery = CreateGallery();
        await gallery.LoadAsync();
        gallery.Dispose();

        await _library.SetBannedAsync(a, true);

        Assert.False(gallery.Items.Single(i => i.FilePath == a).IsBanned);
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

    // The real switcher, held at a gate a test opens, so a switch can be watched while it runs.
    private sealed class GatedSwitcher : ISwitchWallpaper
    {
        private readonly ISwitchWallpaper _inner;

        public GatedSwitcher(ISwitchWallpaper inner)
        {
            _inner = inner;
        }

        public TaskCompletionSource? Gate { get; set; }

        public event Action<string>? WallpaperChanged
        {
            add => _inner.WallpaperChanged += value;
            remove => _inner.WallpaperChanged -= value;
        }

        public Task<string?> SwitchToNextAsync() => _inner.SwitchToNextAsync();

        public Task<string?> SwitchToRandomAsync() => _inner.SwitchToRandomAsync();

        public async Task<string?> SwitchToSpecificAsync(string path)
        {
            if (Gate is not null)
                await Gate.Task;
            return await _inner.SwitchToSpecificAsync(path);
        }

        public Task<bool> HasWallpapersAsync() => _inner.HasWallpapersAsync();
    }

    // Reads as the real store does; every change fails as a full disk would.
    private sealed class FailingUpdateStore : ISettingsStore
    {
        private readonly ISettingsStore _inner;

        public FailingUpdateStore(ISettingsStore inner)
        {
            _inner = inner;
        }

        public Task<WallpaperNexusSettings> LoadAsync() => _inner.LoadAsync();

        public Task<WallpaperNexusSettings> UpdateAsync(Action<WallpaperNexusSettings> change) =>
            throw new IOException("The disk is full.");
    }

    private sealed class AllowingApplier : IWallpaperApplier
    {
        public bool Allowed { get; set; } = true;
        public bool CanSetWallpaper => Allowed;
        public WallpaperCanvas? GetCanvas() => null;
        public bool SetWallpaper(string wallpaperPath) => true;
        public void ApplyFillStyle(WallpaperFillStyle style) { }
    }

    private sealed class EventDownloader : IDownloadWallpapers
    {
        public event Action? WallpapersDownloaded;

        public Task<DownloadOutcome> DownloadAllAsync() => Task.FromResult(new DownloadOutcome(0, 0));

        public void Finish() => WallpapersDownloaded?.Invoke();
    }
}

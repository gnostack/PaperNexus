using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The picture library's rules, which both devices use: listing the gallery and the favorites,
// hearting, banning, deleting, and the change notices every open screen follows.
[Collection("Wallpaper")]
public class WallpaperLibraryTests : IDisposable
{
    private readonly string _folder;
    private readonly SettingsStore _store;

    public WallpaperLibraryTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"PaperNexus_Library_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.Cleanup();
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    // A picture written minutesAgo minutes ago, so tests control "newest first".
    private string AddPicture(string name, int minutesAgo = 0)
    {
        var path = Path.Combine(_folder, name);
        TestHelpers.CreateSmallPng(path);
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-minutesAgo));
        return path;
    }

    private WallpaperLibrary CreateLibrary(IWallpaperApplier? applier = null, IDownloadWallpapers? downloader = null)
    {
        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, applier ?? NoOpWallpaperApplier.Instance, _store, TestHelpers.AppFolders, TestSettingRules.Desktop);
        return new WallpaperLibrary(_store, switcher, downloader);
    }

    private List<LibraryChange> Record(WallpaperLibrary library)
    {
        var changes = new List<LibraryChange>();
        library.Changed += change => { lock (changes) changes.Add(change); };
        return changes;
    }

    [Fact]
    public async Task ListGallery_PicturesOnly_NewestFirstByLastWrite_WithTheirMarks()
    {
        var old = AddPicture("Old - a.png", minutesAgo: 30);
        var newest = AddPicture("Newest - b.jpg", minutesAgo: 1);
        var middle = AddPicture("Middle - c.jpeg", minutesAgo: 10);
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not a picture");
        await TestHelpers.WriteSettingsAsync(_store, _folder);
        await _store.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers.Add(middle.ToUpperInvariant());
            latest.BannedWallpapers.Add(old);
        });

        var listing = await CreateLibrary().ListGalleryAsync();

        Assert.True(listing.FolderFound);
        Assert.Equal([newest, middle, old], listing.Pictures.Select(p => p.Path));
        Assert.Equal("Newest", listing.Pictures[0].Title);
        Assert.True(listing.Pictures[1].IsFavorite);
        Assert.False(listing.Pictures[1].IsBanned);
        Assert.True(listing.Pictures[2].IsBanned);
    }

    [Fact]
    public async Task ListGallery_FolderMissing_IsNotFoundAndEmpty()
    {
        await TestHelpers.WriteSettingsAsync(_store, Path.Combine(_folder, "missing"));

        var listing = await CreateLibrary().ListGalleryAsync();

        Assert.False(listing.FolderFound);
        Assert.Empty(listing.Pictures);
    }

    // Favorites keep the order they were hearted, and one whose file is gone is still listed.
    [Fact]
    public async Task ListFavorites_HeartedOrder_IncludingMissingFiles()
    {
        var first = AddPicture("First - a.png", minutesAgo: 1);
        var second = AddPicture("Second - b.png", minutesAgo: 20);
        var gone = Path.Combine(_folder, "Gone - c.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder);
        await _store.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers = [second, gone, first];
            latest.BannedWallpapers = [first];
        });

        var favorites = await CreateLibrary().ListFavoritesAsync();

        Assert.Equal([second, gone, first], favorites.Select(f => f.Path));
        Assert.All(favorites, f => Assert.True(f.IsFavorite));
        Assert.True(favorites[2].IsBanned);
    }

    [Fact]
    public async Task SetFavorite_AddsOnce_RemovesEveryEntry_CountsAndNotifies()
    {
        var picture = AddPicture("Lake - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder);
        var library = CreateLibrary();
        var changes = Record(library);

        var afterAdd = await library.SetFavoriteAsync(picture, true);
        var afterRepeat = await library.SetFavoriteAsync(picture, true);
        var saved = await _store.LoadAsync();
        Assert.Equal(1, afterAdd);
        Assert.Equal(1, afterRepeat);
        Assert.Equal([picture], saved.FavoriteWallpapers);

        // A differently cased duplicate written by an older version is removed too
        await _store.UpdateAsync(latest => latest.FavoriteWallpapers.Add(picture.ToUpperInvariant()));
        var afterRemove = await library.SetFavoriteAsync(picture, false);
        Assert.Equal(0, afterRemove);
        Assert.Empty((await _store.LoadAsync()).FavoriteWallpapers);
        Assert.Equal(
            [LibraryChangeKind.Favorited, LibraryChangeKind.Favorited, LibraryChangeKind.Unfavorited],
            changes.Select(c => c.Kind));
        Assert.All(changes, c => Assert.Equal(picture, c.Path));
    }

    [Fact]
    public async Task SetBanned_AddsOnce_Removes_Notifies()
    {
        var picture = AddPicture("Lake - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder);
        var library = CreateLibrary();
        var changes = Record(library);

        await library.SetBannedAsync(picture, true);
        await library.SetBannedAsync(picture, true);
        Assert.Equal([picture], (await _store.LoadAsync()).BannedWallpapers);
        Assert.True((await library.GetPictureAsync(picture)).IsBanned);

        await library.SetBannedAsync(picture, false);
        Assert.Empty((await _store.LoadAsync()).BannedWallpapers);
        Assert.Equal(
            [LibraryChangeKind.Banned, LibraryChangeKind.Banned, LibraryChangeKind.Unbanned],
            changes.Select(c => c.Kind));
    }

    // One delete rule everywhere: the file, its favorite and its ban all go. A picture that is
    // not the current wallpaper leaves the wallpaper alone.
    [Fact]
    public async Task Delete_NotCurrent_RemovesFileAndMarks_DoesNotSwitch()
    {
        var current = AddPicture("Current - a.png");
        var other = AddPicture("Other - b.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: current);
        await _store.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers.Add(other);
            latest.BannedWallpapers.Add(other);
        });
        var library = CreateLibrary();
        var changes = Record(library);

        var outcome = await library.DeleteAsync(other);

        var saved = await _store.LoadAsync();
        Assert.Equal(new DeleteOutcome(false, null), outcome);
        Assert.False(File.Exists(other));
        Assert.Empty(saved.FavoriteWallpapers);
        Assert.Empty(saved.BannedWallpapers);
        Assert.Equal(current, saved.CurrentWallpaperPath);
        Assert.Equal(new LibraryChange(LibraryChangeKind.Deleted, other), Assert.Single(changes));
    }

    // Deleting the current wallpaper sets the next one. The deleted path is no longer in the
    // folder, so "next" starts again from the first picture in slideshow order.
    [Fact]
    public async Task Delete_Current_SetsTheFirstPictureInSlideshowOrder()
    {
        var a = AddPicture("A - a.png");
        var b = AddPicture("B - b.png");
        var c = AddPicture("C - c.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: b);

        var outcome = await CreateLibrary().DeleteAsync(b);

        Assert.Equal(new DeleteOutcome(true, a), outcome);
        Assert.False(File.Exists(b));
        Assert.True(File.Exists(c));
        Assert.Equal(a, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    // With nothing left there is no next wallpaper, and the saved current still names the
    // deleted file, as on the desktop.
    [Fact]
    public async Task Delete_TheLastPicture_SwitchesToNothing()
    {
        var only = AddPicture("Only - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: only);

        var outcome = await CreateLibrary().DeleteAsync(only);

        Assert.Equal(new DeleteOutcome(true, null), outcome);
        Assert.False(File.Exists(only));
        Assert.Equal(only, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    // A device that refuses the next wallpaper fails the delete after the picture is gone; the
    // failure reaches the caller, who shows the error.
    [Fact]
    public async Task Delete_Current_DeviceRefusesTheNext_Throws()
    {
        var a = AddPicture("A - a.png");
        var b = AddPicture("B - b.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: b);
        var library = CreateLibrary(new RefusingWallpaperApplier());

        await Assert.ThrowsAsync<WallpaperRefusedException>(() => library.DeleteAsync(b));

        Assert.False(File.Exists(b));
        Assert.True(File.Exists(a));
    }

    // A finished download run tells every screen that pictures may have changed.
    [Fact]
    public void DownloadFinished_IsReportedAsPicturesChanged()
    {
        var downloader = new EventDownloader();
        var library = CreateLibrary(downloader: downloader);
        var changes = Record(library);

        downloader.Finish();

        Assert.Equal(new LibraryChange(LibraryChangeKind.PicturesChanged, null), Assert.Single(changes));
    }

    private sealed class RefusingWallpaperApplier : IWallpaperApplier
    {
        public bool CanSetWallpaper => true;
        public WallpaperCanvas? GetCanvas() => null;
        public bool SetWallpaper(string wallpaperPath) => false;
        public void ApplyFillStyle(WallpaperFillStyle style) { }
    }

    private sealed class EventDownloader : IDownloadWallpapers
    {
        public event Action? WallpapersDownloaded;

        public Task<DownloadOutcome> DownloadAllAsync() => Task.FromResult(new DownloadOutcome(0, 0));

        public void Finish() => WallpapersDownloaded?.Invoke();
    }
}

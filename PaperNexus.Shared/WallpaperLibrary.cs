using PaperNexus.ViewModels;

namespace PaperNexus;

// The downloaded pictures and what the person has marked on them: which are favorites and
// which are banned from rotation. Every screen on either device lists, hearts, bans and
// deletes pictures through this one service, so the rules are written once. Which picture is
// the wallpaper belongs to ISwitchWallpaper, which also sets one as current.
public interface IWallpaperLibrary
{
    // Raised after every change the library saved, and after a download run, so each open
    // screen can show it without knowing about the others. Raised on a background thread.
    public event Action<LibraryChange>? Changed;

    // Every picture in the download folder, newest first by the file's last write time.
    // FolderFound is false when no folder is set or it does not exist; the list is then empty.
    public Task<GalleryListing> ListGalleryAsync();

    // Every favorite in the order it was hearted, including one whose file is gone.
    public Task<IReadOnlyList<LibraryPicture>> ListFavoritesAsync();

    // One picture's marks. The file need not exist.
    public Task<LibraryPicture> GetPictureAsync(string path);

    // Makes the picture a favorite or not, and returns how many favorites there are now. A
    // favorite is never added twice. The file need not exist.
    public Task<int> SetFavoriteAsync(string path, bool isFavorite);

    // Bans the picture from rotation or lets it back in. A ban is never added twice.
    public Task SetBannedAsync(string path, bool isBanned);

    // Deletes the picture from disk and from the favorites and bans. When it was the current
    // wallpaper, the next picture in slideshow order is set; SwitchedTo is null when there was
    // none left. A failure (the file cannot be deleted, the device refuses the next
    // wallpaper) reaches the caller.
    public Task<DeleteOutcome> DeleteAsync(string path);
}

// A picture and its marks. Title is the name a person sees.
public sealed record LibraryPicture(string Path, string Title, bool IsFavorite, bool IsBanned);

// The gallery's pictures, and whether the download folder exists.
public sealed record GalleryListing(bool FolderFound, IReadOnlyList<LibraryPicture> Pictures);

// What a delete did: WasCurrent says the picture was the current wallpaper, and SwitchedTo
// names the picture set in its place, or null when there was none.
public sealed record DeleteOutcome(bool WasCurrent, string? SwitchedTo);

public enum LibraryChangeKind
{
    Favorited,
    Unfavorited,
    Banned,
    Unbanned,
    Deleted,

    // A download run finished: pictures may have been added or removed. Path is null.
    PicturesChanged,
}

// One change: what happened, to which picture.
public sealed record LibraryChange(LibraryChangeKind Kind, string? Path);

internal sealed class WallpaperLibrary : IWallpaperLibrary, IAddSingleton<IWallpaperLibrary>
{
    // The picture files the gallery lists, as the switcher chooses from them.
    private static readonly HashSet<string> PictureExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

    private readonly ISettingsStore _settingsStore;
    private readonly ISwitchWallpaper _switchWallpaper;

    public event Action<LibraryChange>? Changed;

    // The downloader is optional so a test can build the library without one; the container
    // always supplies it, and its finished runs become PicturesChanged.
    public WallpaperLibrary(ISettingsStore settingsStore, ISwitchWallpaper switchWallpaper, IDownloadWallpapers? downloadWallpapers = null)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _switchWallpaper = switchWallpaper.ThrowIfNull();
        if (downloadWallpapers is not null)
            downloadWallpapers.WallpapersDownloaded += OnWallpapersDownloaded;
    }

    public async Task<GalleryListing> ListGalleryAsync()
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        var folder = settings.Download.Folder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return new GalleryListing(false, []);

        var favorites = new HashSet<string>(settings.FavoriteWallpapers, StringComparer.OrdinalIgnoreCase);
        var banned = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);
        // Newest first, so recently downloaded wallpapers appear at the top
        var pictures = Directory.EnumerateFiles(folder)
            .Where(f => PictureExtensions.Contains(Path.GetExtension(f)))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .Select(f => CreatePicture(f, favorites.Contains(f), banned.Contains(f)))
            .ToList();
        return new GalleryListing(true, pictures);
    }

    public async Task<IReadOnlyList<LibraryPicture>> ListFavoritesAsync()
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        var banned = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);
        var favorites = settings.FavoriteWallpapers
            .Select(f => CreatePicture(f, true, banned.Contains(f)))
            .ToList();
        return favorites;
    }

    public async Task<LibraryPicture> GetPictureAsync(string path)
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        var isFavorite = settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase);
        var isBanned = settings.BannedWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase);
        return CreatePicture(path, isFavorite, isBanned);
    }

    public async Task<int> SetFavoriteAsync(string path, bool isFavorite)
    {
        var saved = await _settingsStore.UpdateAsync(latest => SetMark(latest.FavoriteWallpapers, path, isFavorite)).ConfigureAwait(false);
        var count = saved.FavoriteWallpapers.Count;
        Raise(isFavorite ? LibraryChangeKind.Favorited : LibraryChangeKind.Unfavorited, path);
        return count;
    }

    public async Task SetBannedAsync(string path, bool isBanned)
    {
        await _settingsStore.UpdateAsync(latest => SetMark(latest.BannedWallpapers, path, isBanned)).ConfigureAwait(false);
        Raise(isBanned ? LibraryChangeKind.Banned : LibraryChangeKind.Unbanned, path);
    }

    // The file goes first: when it cannot be deleted nothing else changes. The marks go
    // next, so no favorite or ban names a missing file. Screens hear of the delete before the
    // next wallpaper is set, so the picture leaves every list at once.
    public async Task<DeleteOutcome> DeleteAsync(string path)
    {
        if (File.Exists(path))
            File.Delete(path);

        var wasCurrent = false;
        await _settingsStore.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
            latest.BannedWallpapers.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
            wasCurrent = latest.CurrentWallpaperPath.Equals(path, StringComparison.OrdinalIgnoreCase);
        }).ConfigureAwait(false);
        Raise(LibraryChangeKind.Deleted, path);

        if (!wasCurrent)
            return new DeleteOutcome(false, null);

        // The deleted path is no longer in the folder, so "next" starts again from the first
        // picture in slideshow order, as it always has.
        var next = await _switchWallpaper.SwitchToNextAsync().ConfigureAwait(false);
        return new DeleteOutcome(true, next);
    }

    // Adds the path to a mark list once, or removes every entry for it.
    private static void SetMark(List<string> marks, string path, bool marked)
    {
        var present = marks.Contains(path, StringComparer.OrdinalIgnoreCase);
        if (marked && !present)
            marks.Add(path);
        else if (!marked)
            marks.RemoveAll(m => m.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    private static LibraryPicture CreatePicture(string path, bool isFavorite, bool isBanned)
    {
        var picture = new LibraryPicture(path, WallpaperDisplayName.FromPath(path), isFavorite, isBanned);
        return picture;
    }

    private void OnWallpapersDownloaded() => Raise(LibraryChangeKind.PicturesChanged, null);

    private void Raise(LibraryChangeKind kind, string? path)
    {
        Changed?.Invoke(new LibraryChange(kind, path));
    }
}

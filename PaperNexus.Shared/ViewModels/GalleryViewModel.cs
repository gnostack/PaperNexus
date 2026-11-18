namespace PaperNexus.ViewModels;

// The phone's Gallery tab: every downloaded picture, newest first, each with set as current,
// heart, ban and delete. A banned picture is tinted red. A finished download adds its
// pictures while the tab is open.
public sealed class GalleryViewModel : PictureListViewModel
{
    public const string EmptySentence = "No wallpapers yet.";
    public const string AddedText = "Added to favorites.";
    public const string RemovedText = "Removed from favorites.";
    public const string BannedText = "Banned from rotation.";
    public const string UnbannedText = "Back in rotation.";
    public const string DeletedText = "Wallpaper deleted.";

    public GalleryViewModel(
        IWallpaperLibrary library,
        ISwitchWallpaper switchWallpaper,
        IWallpaperApplier wallpaperApplier,
        ILogger<GalleryViewModel> logger)
        : base(library, switchWallpaper, wallpaperApplier, logger)
    {
    }

    public override string EmptyText => EmptySentence;

    // A missing download folder is a gallery with nothing in it yet.
    protected override async Task<IReadOnlyList<LibraryPicture>> ListAsync()
    {
        var listing = await Library.ListGalleryAsync();
        return listing.Pictures;
    }

    protected override string LoadFailedText(Exception ex) => $"Error loading gallery: {ex.Message}";

    protected override GalleryItem CreateItem(LibraryPicture picture)
    {
        var item = new GalleryItem(
            picture.Path,
            picture.IsFavorite,
            picture.IsBanned,
            SetAsCurrentAsync,
            ToggleFavoriteAsync,
            ToggleBanAsync,
            DeleteAsync)
        {
            AllowsSetAsCurrent = AllowsSetAsCurrent,
        };
        return item;
    }

    protected override async Task ApplyChangeAsync(LibraryChange change)
    {
        switch (change.Kind)
        {
            case LibraryChangeKind.Favorited:
            case LibraryChangeKind.Unfavorited:
                var hearted = FindItem(change.Path);
                if (hearted is not null)
                    hearted.IsFavorite = change.Kind == LibraryChangeKind.Favorited;
                break;
            case LibraryChangeKind.Banned:
            case LibraryChangeKind.Unbanned:
                var banned = FindItem(change.Path);
                if (banned is not null)
                    banned.IsBanned = change.Kind == LibraryChangeKind.Banned;
                break;
            case LibraryChangeKind.Deleted:
                RemoveItem(change.Path);
                break;
            case LibraryChangeKind.PicturesChanged:
                await LoadAsync();
                break;
        }
    }

    private async Task ToggleFavoriteAsync(GalleryItem item)
    {
        var makeFavorite = !item.IsFavorite;
        try
        {
            await Library.SetFavoriteAsync(item.FilePath, makeFavorite);
            item.IsFavorite = makeFavorite;
            ShowStatus(makeFavorite ? AddedText : RemovedText, StatusTone.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Updating favorites for {Path} failed.", item.FilePath);
            ShowStatus($"Error updating favorites: {ex.Message}", StatusTone.Error);
        }
    }

    private async Task ToggleBanAsync(GalleryItem item)
    {
        var ban = !item.IsBanned;
        try
        {
            await Library.SetBannedAsync(item.FilePath, ban);
            item.IsBanned = ban;
            ShowStatus(ban ? BannedText : UnbannedText, StatusTone.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Updating the ban list for {Path} failed.", item.FilePath);
            ShowStatus($"Error updating ban list: {ex.Message}", StatusTone.Error);
        }
    }

    // Deletes without asking, as the desktop's gallery does. Deleting the current wallpaper
    // sets the next one.
    private async Task DeleteAsync(GalleryItem item)
    {
        try
        {
            var outcome = await Library.DeleteAsync(item.FilePath);
            RemoveItem(item.FilePath);
            var message = outcome.SwitchedTo is not null
                ? $"Deleted and switched to: {WallpaperDisplayName.FromPath(outcome.SwitchedTo)}"
                : DeletedText;
            ShowStatus(message, StatusTone.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Deleting {Path} failed.", item.FilePath);
            ShowStatus($"Error deleting wallpaper: {ex.Message}", StatusTone.Error);
        }
    }
}

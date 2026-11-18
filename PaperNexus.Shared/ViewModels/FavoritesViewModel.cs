namespace PaperNexus.ViewModels;

// The phone's Favorites tab: only the favorites, in the order they were hearted, each with set
// as current and remove from favorites. A picture hearted on another tab appears here at once,
// and one removed or deleted anywhere leaves.
public sealed class FavoritesViewModel : PictureListViewModel
{
    public const string EmptySentence = "No favorites yet. Tap the heart on a wallpaper to add one.";
    public const string RemovedText = "Removed from favorites.";

    public FavoritesViewModel(
        IWallpaperLibrary library,
        ISwitchWallpaper switchWallpaper,
        IWallpaperApplier wallpaperApplier,
        ILogger<FavoritesViewModel> logger)
        : base(library, switchWallpaper, wallpaperApplier, logger)
    {
    }

    public override string EmptyText => EmptySentence;

    protected override Task<IReadOnlyList<LibraryPicture>> ListAsync() => Library.ListFavoritesAsync();

    // Listing the favorites only reads the settings, and reading the settings never fails (a
    // missing or damaged file reads as the defaults), so there is no sentence for it.
    protected override string? LoadFailedText(Exception ex) => null;

    // The card's heart action is its "Remove from favorites" button; it has no ban or delete.
    protected override GalleryItem CreateItem(LibraryPicture picture)
    {
        var item = new GalleryItem(
            picture.Path,
            true,
            picture.IsBanned,
            SetAsCurrentAsync,
            RemoveAsync,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask)
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
                // A new favorite goes at the end, in hearted order; listing again places it
                if (FindItem(change.Path) is null)
                    await LoadAsync();
                break;
            case LibraryChangeKind.Unfavorited:
            case LibraryChangeKind.Deleted:
                RemoveItem(change.Path);
                break;
            case LibraryChangeKind.Banned:
            case LibraryChangeKind.Unbanned:
                var banned = FindItem(change.Path);
                if (banned is not null)
                    banned.IsBanned = change.Kind == LibraryChangeKind.Banned;
                break;
            case LibraryChangeKind.PicturesChanged:
                // The download's retention pass drops favorites whose file is gone
                await LoadAsync();
                break;
        }
    }

    private async Task RemoveAsync(GalleryItem item)
    {
        try
        {
            await Library.SetFavoriteAsync(item.FilePath, false);
            RemoveItem(item.FilePath);
            ShowStatus(RemovedText, StatusTone.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Removing the favorite {Path} failed.", item.FilePath);
            ShowStatus($"Error removing favorite: {ex.Message}", StatusTone.Error);
        }
    }
}

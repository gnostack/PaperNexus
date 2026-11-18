using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// The part the phone's Gallery and Favorites tabs share: one card per picture, the short line
// that confirms each tap, setting a picture as the wallpaper, and following changes made on
// any other screen. Each tab says which pictures it lists, which buttons a card has, and what
// a change does to its cards.
//
// The cards are loaded the first time the tab is shown. After that the library's Changed
// event keeps them current, applied on the thread that created the view model (the screen's
// thread), so a heart tapped on one tab shows on the others at once.
//
// On the phone the view model lives as long as the process, not the screen: Android destroys
// and recreates its screen (a new font size, language or display size), and the new screen's
// view attaches to this same view model, so the cards, the confirmation line and the place in
// the list are still there. It never holds a view.
public abstract partial class PictureListViewModel : ObservableObject, IScrollPlace, IDisposable
{
    public const string SwitchingText = "Switching wallpaper...";
    public const string FileNotFoundText = "Wallpaper file not found.";

    private readonly ISwitchWallpaper _switchWallpaper;
    private readonly IWallpaperApplier _wallpaperApplier;
    private readonly SynchronizationContext? _screenContext;
    private bool _isDisposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    protected PictureListViewModel(
        IWallpaperLibrary library,
        ISwitchWallpaper switchWallpaper,
        IWallpaperApplier wallpaperApplier,
        ILogger logger)
    {
        Library = library.ThrowIfNull();
        _switchWallpaper = switchWallpaper.ThrowIfNull();
        _wallpaperApplier = wallpaperApplier.ThrowIfNull();
        Logger = logger.ThrowIfNull();
        _screenContext = SynchronizationContext.Current;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Library.Changed += OnLibraryChanged;
    }

    protected IWallpaperLibrary Library { get; }

    protected ILogger Logger { get; }

    public ObservableCollection<GalleryItem> Items { get; } = [];

    // The tab's sentence when it has no pictures, shown only once it has looked.
    public abstract string EmptyText { get; }

    public bool IsEmpty => IsLoaded && Items.Count == 0;

    // The line that confirms each tap.
    public StatusLine Status { get; } = new();

    // How far down the list was scrolled, in the view's units, kept here so a view built for
    // a recreated screen can return to the same place.
    public double ScrollOffset { get; set; }

    // Lists the pictures the first time a view shows the tab; a later view (after the screen
    // was recreated) finds them already listed and kept current.
    public Task EnsureLoadedAsync() => IsLoaded ? Task.CompletedTask : LoadAsync();

    // Lists the tab's pictures. Kept cards (and their thumbnails) are reused and moved into
    // the new order; new pictures get new cards; cards for pictures no longer listed go.
    public async Task LoadAsync()
    {
        try
        {
            var pictures = await ListAsync();
            Reconcile(pictures);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Listing the pictures failed.");
            var sentence = LoadFailedText(ex);
            if (sentence is not null)
                ShowStatus(sentence, StatusTone.Error);
        }
        finally
        {
            IsLoaded = true;
        }
    }

    // The pictures this tab shows, in its order.
    protected abstract Task<IReadOnlyList<LibraryPicture>> ListAsync();

    // A new card for a picture, wired to this tab's actions.
    protected abstract GalleryItem CreateItem(LibraryPicture picture);

    // What a change made elsewhere (or here) does to this tab's cards. Applying a change the
    // tab already made itself changes nothing.
    protected abstract Task ApplyChangeAsync(LibraryChange change);

    // The sentence when listing failed, or null for a tab whose listing cannot fail.
    protected abstract string? LoadFailedText(Exception ex);

    // Whether this device lets the app change the wallpaper; cards are created with it.
    protected bool AllowsSetAsCurrent => _wallpaperApplier.CanSetWallpaper;

    // Sets the card's picture as the wallpaper. It waits behind any switch already running;
    // the line says so meanwhile, and then says what happened.
    protected async Task SetAsCurrentAsync(GalleryItem item)
    {
        ShowStatus(SwitchingText, StatusTone.Neutral, hold: true);
        string? result;
        try
        {
            result = await Task.Run(() => _switchWallpaper.SwitchToSpecificAsync(item.FilePath));
        }
        catch (Exception ex)
        {
            // A refused or unreadable wallpaper leaves the current one in place
            Logger.LogError(ex, "Setting {Path} as the wallpaper failed.", item.FilePath);
            ShowStatus($"Error switching wallpaper: {ex.Message}", StatusTone.Error);
            return;
        }

        if (result is null)
        {
            ShowStatus(FileNotFoundText, StatusTone.Error);
            return;
        }

        ShowStatus($"Switched to: {item.DisplayName}", StatusTone.Success);
    }

    // Shows a line for three seconds, replacing any earlier one; hold keeps it until the next.
    protected void ShowStatus(string message, StatusTone tone, bool hold = false) => Status.Show(message, tone, hold);

    // Finds the card for a path, or null.
    protected GalleryItem? FindItem(string? path)
    {
        if (path is null)
            return null;
        var item = Items.FirstOrDefault(i => i.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        return item;
    }

    // Removes a card and frees its thumbnail.
    protected void RemoveItem(string? path)
    {
        var item = FindItem(path);
        if (item is null)
            return;
        Items.Remove(item);
        item.DisposeThumbnail();
    }

    // Brings the cards into the order of pictures, reusing the card already shown for a path.
    private void Reconcile(IReadOnlyList<LibraryPicture> pictures)
    {
        for (var index = 0; index < pictures.Count; index++)
        {
            var picture = pictures[index];
            var existing = FindItem(picture.Path);
            if (existing is null)
            {
                Items.Insert(index, CreateItem(picture));
                continue;
            }

            existing.IsFavorite = picture.IsFavorite;
            existing.IsBanned = picture.IsBanned;
            var at = Items.IndexOf(existing);
            if (at != index)
                Items.Move(at, index);
        }

        while (Items.Count > pictures.Count)
        {
            var last = Items[^1];
            Items.RemoveAt(Items.Count - 1);
            last.DisposeThumbnail();
        }
    }

    // Raised on a background thread; applied on the screen's thread. Ignored until the tab
    // has loaded, since its first load reads everything anyway.
    private void OnLibraryChanged(LibraryChange change)
    {
        if (_screenContext is null)
        {
            _ = ApplyIfLoadedAsync(change);
            return;
        }

        _screenContext.Post(_ => _ = ApplyIfLoadedAsync(change), null);
    }

    private async Task ApplyIfLoadedAsync(LibraryChange change)
    {
        if (!IsLoaded || _isDisposed)
            return;
        try
        {
            await ApplyChangeAsync(change);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Showing a change to {Path} failed.", change.Path);
        }
    }

    // Stops following the library, which outlives the screen, and frees every thumbnail.
    public void Dispose()
    {
        _isDisposed = true;
        Library.Changed -= OnLibraryChanged;
        Status.Dispose();
        foreach (var item in Items)
            item.DisposeThumbnail();
    }
}

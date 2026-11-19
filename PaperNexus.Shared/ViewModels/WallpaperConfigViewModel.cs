using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaperNexus.Core;

namespace PaperNexus.ViewModels;

// The desktop's settings window. Its settings are shown and saved by the settings groups both
// devices share (Download, Slideshow, Annotation and Debug) and by any group the host adds for
// settings only it has (AddHostGroup), each writing only the settings it owns; About holds the
// version and the links. The window adds the current wallpaper, the
// sources, the gallery and favorites, and the status bar every group reports to.
public partial class WallpaperConfigViewModel : ObservableObject
{
    private readonly ISwitchWallpaper? _switchWallpaper;
    private readonly ICheckForUpdates? _checkForUpdates;
    private readonly IDownloadWallpapers? _downloadWallpapers;
    private readonly IAppInfo _appInfo;
    private readonly IShellOpener _shellOpener;
    private readonly IAppFolders _appFolders;
    private readonly EasterEggRequests _easterEggs;
    private readonly IWallpaperLibrary _library;
    private readonly IWallpaperSources _wallpaperSources;
    private readonly SynchronizationContext? _windowContext;

    [ObservableProperty]
    private ObservableCollection<GalleryItem> _galleryItems = [];

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private IBrush _statusForeground;

    [ObservableProperty]
    private string _currentWallpaperPath;

    [ObservableProperty]
    private string _currentWallpaperName;

    // One row per wallpaper source. Every change to a source is saved at once through
    // IWallpaperSources, never by the window's settings save.
    public ObservableCollection<SourceItem> Sources { get; } = [];

    [ObservableProperty]
    private SourceItem? _selectedSource;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private bool _isCurrentWallpaperFavorited;

    // One card per favorite, shown on the Favorites tab with the same thumbnail as the Gallery.
    [ObservableProperty]
    private ObservableCollection<GalleryItem> _favoriteWallpapers = [];

    // Every settings read in the window goes through this store; every settings write goes
    // through a group's update.
    private readonly ISettingsStore _settingsStore;

    private CancellationTokenSource _statusCts = new();

    // Guards the _statusCts swap. Status messages are raised from the UI thread and from
    // background save/download continuations, so two calls can interleave mid-swap.
    private readonly object _statusLock = new();

    // Created per window from the container, which supplies the settings store, the host's
    // app facts, a fresh set of settings groups, and whichever background services the host
    // registers. A service the host does not have stays null and its command reports it as
    // unavailable. The WallpaperChanged event is subscribed here so the preview updates
    // automatically when the slideshow switches, the library's Changed event so the cards
    // follow hearts, bans and deletes made anywhere, the sources' Changed event so the source
    // rows show every saved change, and each group's reports so the status bar shows them.
    public WallpaperConfigViewModel(
        ISettingsStore settingsStore,
        IAppInfo appInfo,
        IShellOpener shellOpener,
        IAppFolders appFolders,
        IWallpaperLibrary library,
        IWallpaperSources wallpaperSources,
        EasterEggRequests easterEggs,
        DownloadSettingsViewModel download,
        SlideshowSettingsViewModel slideshow,
        AnnotationSettingsViewModel annotation,
        DebugSettingsViewModel debug,
        AboutViewModel about,
        ISwitchWallpaper? switchWallpaper = null,
        IDownloadWallpapers? downloadWallpapers = null,
        ICheckForUpdates? checkForUpdates = null)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _appInfo = appInfo.ThrowIfNull();
        _shellOpener = shellOpener.ThrowIfNull();
        _appFolders = appFolders.ThrowIfNull();
        _library = library.ThrowIfNull();
        _wallpaperSources = wallpaperSources.ThrowIfNull();
        _easterEggs = easterEggs.ThrowIfNull();
        Download = download.ThrowIfNull();
        Slideshow = slideshow.ThrowIfNull();
        Annotation = annotation.ThrowIfNull();
        Debug = debug.ThrowIfNull();
        About = about.ThrowIfNull();
        _windowContext = SynchronizationContext.Current;
        _switchWallpaper = switchWallpaper;
        _downloadWallpapers = downloadWallpapers;
        _checkForUpdates = checkForUpdates;
        _statusMessage = string.Empty;
        _statusForeground = Brushes.White;
        _currentWallpaperPath = string.Empty;
        _currentWallpaperName = string.Empty;

        if (_switchWallpaper is not null)
            _switchWallpaper.WallpaperChanged += OnWallpaperChanged;
        _library.Changed += OnLibraryChanged;
        _wallpaperSources.Changed += OnWallpaperSourcesChanged;
        foreach (var group in Groups)
            group.StatusReported += OnGroupStatusReported;
        About.StatusReported += OnGroupStatusReported;
    }

    public DownloadSettingsViewModel Download { get; }

    public SlideshowSettingsViewModel Slideshow { get; }

    public AnnotationSettingsViewModel Annotation { get; }

    public DebugSettingsViewModel Debug { get; }

    public AboutViewModel About { get; }

    // Groups for settings only the host has (the desktop's folder, run on startup, minimize to
    // tray and automatic updates), which live in the host. The window adds them before it
    // loads; they load, save, report and flush like the shared groups.
    private readonly List<SettingsGroupViewModel> _hostGroups = [];

    private IEnumerable<SettingsGroupViewModel> Groups => [Download, Slideshow, Annotation, Debug, .. _hostGroups];

    // Adds a group for settings only the host has. Called once per group, before LoadAsync.
    public void AddHostGroup(SettingsGroupViewModel group)
    {
        group.ThrowIfNull();
        _hostGroups.Add(group);
        group.StatusReported += OnGroupStatusReported;
    }

    // A group's line on the status bar, marked as the window has always marked them: a tick
    // for success, a cross for an error, nothing for a neutral line.
    private void OnGroupStatusReported(SettingsStatus status)
    {
        var mark = status.Tone switch
        {
            StatusTone.Success => "✓ ",
            StatusTone.Error => "✗ ",
            _ => string.Empty,
        };
        var message = mark + status.Text;
        if (status.Holds)
        {
            StatusMessage = message;
            return;
        }
        _ = ShowTransientStatusAsync(message, status.DurationMs);
    }

    // A heart, ban or delete saved by any screen (this window's own commands included, whose
    // effect is already shown, so applying it again changes nothing). Raised on a background
    // thread; applied on the window's thread. A finished download does not reload the
    // gallery: the window reloads it when its tab is chosen or Refresh is pressed, as before.
    private void OnLibraryChanged(LibraryChange change)
    {
        if (_windowContext is null)
        {
            ApplyLibraryChange(change);
            return;
        }

        _windowContext.Post(_ => ApplyLibraryChange(change), null);
    }

    private void ApplyLibraryChange(LibraryChange change)
    {
        var path = change.Path;
        if (path is null)
            return;
        var galleryItem = GalleryItems.FirstOrDefault(g => g.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        var isCurrent = CurrentWallpaperPath.Equals(path, StringComparison.OrdinalIgnoreCase);
        switch (change.Kind)
        {
            case LibraryChangeKind.Favorited:
                AddFavoriteItem(path);
                if (galleryItem is not null)
                    galleryItem.IsFavorite = true;
                if (isCurrent)
                    IsCurrentWallpaperFavorited = true;
                break;
            case LibraryChangeKind.Unfavorited:
                RemoveFavoriteItem(path);
                if (galleryItem is not null)
                    galleryItem.IsFavorite = false;
                if (isCurrent)
                    IsCurrentWallpaperFavorited = false;
                break;
            case LibraryChangeKind.Banned:
            case LibraryChangeKind.Unbanned:
                if (galleryItem is not null)
                    galleryItem.IsBanned = change.Kind == LibraryChangeKind.Banned;
                break;
            case LibraryChangeKind.Deleted:
                RemoveFavoriteItem(path);
                if (galleryItem is not null)
                {
                    GalleryItems.Remove(galleryItem);
                    galleryItem.DisposeThumbnail();
                }
                break;
        }
    }

    // Called from a background thread when the wallpaper changes; marshals all UI
    // updates to the UI thread so Avalonia bindings stay consistent.
    private void OnWallpaperChanged(string path)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CurrentWallpaperPath = path;
            CurrentWallpaperName = GetDisplayName(path);
            RefreshPreviewImage();
            RefreshFavoriteState();
        });
    }

    // A source saved by this window (or anywhere else in the process). Raised on a background
    // thread; applied on the window's thread.
    private void OnWallpaperSourcesChanged()
    {
        if (_windowContext is null)
        {
            _ = ReloadSourcesAsync();
            return;
        }

        _windowContext.Post(_ => _ = ReloadSourcesAsync(), null);
    }

    // Brings the rows up to the saved list, keeping the selected row when it is still there.
    // Listing reads the settings store, which answers defaults rather than failing. Only the
    // listing started last is shown, so an older one finishing late cannot undo a newer change.
    private async Task ReloadSourcesAsync()
    {
        var listing = Interlocked.Increment(ref _sourceListing);
        var sources = await _wallpaperSources.ListAsync();
        if (listing == Volatile.Read(ref _sourceListing))
            SourceItem.Reconcile(Sources, sources, CreateSourceItem);
    }

    // Counts the source listings started, so only the newest is shown.
    private int _sourceListing;

    // Raised when a row's edit button is pressed; the window opens the source dialog, which
    // only a window can own.
    public event Action<SourceItem>? EditSourceRequested;

    private SourceItem CreateSourceItem(ListedSource source)
    {
        var item = new SourceItem(source, RequestEditSource, DeleteSourceAsync, SetSourceEnabledAsync);
        return item;
    }

    private void RequestEditSource(SourceItem item) => EditSourceRequested?.Invoke(item);

    // A form for a new source, or one filled with the row's source, for the source dialog.
    public SourceFormViewModel CreateSourceForm(SourceItem? editing)
    {
        var form = editing is null
            ? new SourceFormViewModel(_wallpaperSources)
            : new SourceFormViewModel(_wallpaperSources, editing.Key, editing.Fields);
        return form;
    }

    // The dialog saved its source: the rows are brought up to date, the saved source is
    // selected, and the status bar confirms it. The confirmations are not awaited, so a row is
    // not held busy while its sentence shows.
    public async Task SourceSavedAsync(SourceFormViewModel form)
    {
        await ReloadSourcesAsync();
        var savedKey = form.SavedKey;
        SelectedSource = savedKey is null ? null : Sources.FirstOrDefault(s => s.Key.IsSameSource(savedKey));
        _ = ShowTransientStatusAsync("✓ Settings saved.");
    }

    // The row's delete button deletes at once, without asking.
    private async Task DeleteSourceAsync(SourceItem item)
    {
        try
        {
            await _wallpaperSources.DeleteAsync(item.Key);
            Sources.Remove(item);
            _ = ShowTransientStatusAsync("✓ Settings saved.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error saving settings: {ex.Message}";
        }
    }

    // The row's checkbox saves at once; a failed save puts it back.
    private async Task SetSourceEnabledAsync(SourceItem item, bool isEnabled)
    {
        try
        {
            await _wallpaperSources.SetEnabledAsync(item.Key, isEnabled);
            item.Apply(item.Fields with { IsEnabled = isEnabled });
            _ = ShowTransientStatusAsync("✓ Settings saved.");
        }
        catch (Exception ex)
        {
            item.Apply(item.Fields);
            StatusMessage = $"✗ Error saving settings: {ex.Message}";
        }
    }

    // Derives the status bar text colour from the message prefix:
    // ✓ = green (success), ✗ = red (error), anything else = white (neutral/info).
    partial void OnStatusMessageChanged(string value)
    {
        StatusForeground = value.StartsWith("✓") ? new SolidColorBrush(Color.Parse("#4ADE80"))
            : value.StartsWith("✗") ? new SolidColorBrush(Color.Parse("#F87171"))
            : Brushes.White;
    }

    // Fills every group and the window's own state from the saved settings, read once. The
    // groups load without saving; the sources and favorites are listed after them.
    public async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        foreach (var group in Groups)
            group.Load(settings);

        await ReloadSourcesAsync();

        var path = settings.CurrentWallpaperPath;
        CurrentWallpaperPath = path;
        CurrentWallpaperName = string.IsNullOrEmpty(path) ? "(none)" : GetDisplayName(path);
        RefreshPreviewImage();
        var favorites = await _library.ListFavoritesAsync();
        IsCurrentWallpaperFavorited = !string.IsNullOrEmpty(path)
            && favorites.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        // Dispose old thumbnails before replacing the cards to release bitmap memory
        foreach (var item in FavoriteWallpapers)
            item.DisposeThumbnail();
        var favoriteItems = favorites.Select(f => CreateFavoriteItem(f.Path));
        FavoriteWallpapers = new ObservableCollection<GalleryItem>(favoriteItems);
    }

    [RelayCommand]
    private async Task DownloadNow()
    {
        if (_downloadWallpapers is null)
        {
            await ShowTransientStatusAsync("✗ Download service not available.");
            return;
        }

        try
        {
            StatusMessage = DownloadSentence.RunningText;
            var outcome = await Task.Run(_downloadWallpapers.DownloadAllAsync);
            var (text, tone) = DownloadSentence.For(outcome);
            var mark = tone switch
            {
                StatusTone.Success => "✓ ",
                StatusTone.Error => "✗ ",
                _ => string.Empty,
            };
            await ShowTransientStatusAsync(mark + text);
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync("✗ " + DownloadSentence.ErrorText(ex));
        }
    }

    [RelayCommand]
    private async Task NextWallpaper()
    {
        try
        {
            if (_switchWallpaper is null)
            {
                StatusMessage = "✗ Wallpaper switcher not available.";
                return;
            }

            StatusMessage = "Switching wallpaper...";
            var next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
            // If the folder is empty, trigger a download and retry before giving up
            if (next is null && _downloadWallpapers is not null)
            {
                StatusMessage = "No wallpapers found. Downloading...";
                await Task.Run(_downloadWallpapers.DownloadAllAsync);
                next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
            }
            if (next is null)
            {
                await ShowTransientStatusAsync("✗ No wallpapers found. Check your wallpapers folder setting.");
                return;
            }
            CurrentWallpaperPath = next;
            CurrentWallpaperName = GetDisplayName(next);
            RefreshPreviewImage();
            RefreshFavoriteState();
            await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RandomWallpaper()
    {
        try
        {
            if (_switchWallpaper is null)
            {
                StatusMessage = "✗ Wallpaper switcher not available.";
                return;
            }

            StatusMessage = "Picking random wallpaper...";
            var next = await Task.Run(_switchWallpaper.SwitchToRandomAsync);
            if (next is null && _downloadWallpapers is not null)
            {
                StatusMessage = "No wallpapers found. Downloading...";
                await Task.Run(_downloadWallpapers.DownloadAllAsync);
                next = await Task.Run(_switchWallpaper.SwitchToRandomAsync);
            }
            if (next is null)
            {
                await ShowTransientStatusAsync("✗ No wallpapers found. Check your wallpapers folder setting.");
                return;
            }
            CurrentWallpaperPath = next;
            CurrentWallpaperName = GetDisplayName(next);
            RefreshPreviewImage();
            RefreshFavoriteState();
            await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
        }
    }

    // Deletes the current wallpaper file from disk (and from favorites and bans, the one
    // delete rule), then automatically advances to the next available wallpaper so the
    // desktop is never left showing a missing file. The window has already asked.
    [RelayCommand]
    private async Task DeleteCurrentWallpaper()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var outcome = await Task.Run(() => _library.DeleteAsync(path));
            RemoveFavoriteItem(path);
            RemoveGalleryItem(path);

            CurrentWallpaperPath = string.Empty;
            CurrentWallpaperName = "(none)";

            if (!outcome.WasCurrent)
            {
                await ShowTransientStatusAsync("✓ Wallpaper deleted.");
                return;
            }

            var next = outcome.SwitchedTo;
            if (next is null)
            {
                await ShowTransientStatusAsync("✓ Wallpaper deleted. No more wallpapers in folder.");
                return;
            }

            CurrentWallpaperPath = next;
            CurrentWallpaperName = GetDisplayName(next);
            RefreshPreviewImage();
            RefreshFavoriteState();
            await ShowTransientStatusAsync($"✓ Deleted and switched to: {CurrentWallpaperName}");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error deleting wallpaper: {ex.Message}");
        }
    }

    // Toggles the current wallpaper in/out of the favorites list, as its heart shows it, and
    // keeps the FavoriteWallpapers observable collection in step.
    [RelayCommand]
    private async Task ToggleFavorite()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var makeFavorite = !IsCurrentWallpaperFavorited;
            var favoriteCount = await _library.SetFavoriteAsync(path, makeFavorite);
            if (!makeFavorite)
            {
                IsCurrentWallpaperFavorited = false;
                RemoveFavoriteItem(path);
                await ShowTransientStatusAsync("✓ Removed from favorites.");
            }
            else
            {
                IsCurrentWallpaperFavorited = true;
                AddFavoriteItem(path);
                // Round numbers of favorites play an overlay instead of the usual confirmation.
                var milestone = EasterEggShows.Favorite(favoriteCount);
                if (milestone is not null)
                    _easterEggs.Request(milestone);
                else
                    await ShowTransientStatusAsync("✓ Added to favorites.");
            }
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating favorites: {ex.Message}");
        }
    }

    // Builds the Favorites tab card for a path. Ban and delete stay Gallery-only, so the
    // card's template never shows them; its favorite action removes the card.
    private GalleryItem CreateFavoriteItem(string path)
    {
        var item = new GalleryItem(
            path,
            true,
            false,
            GallerySetAsCurrent,
            RemoveFavoriteItemAsync,
            GalleryToggleBan,
            GalleryDelete);
        return item;
    }

    // Adds a card for a newly favorited path unless one is already shown.
    private void AddFavoriteItem(string path)
    {
        var alreadyShown = FavoriteWallpapers.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (!alreadyShown)
            FavoriteWallpapers.Add(CreateFavoriteItem(path));
    }

    // Removes the card for a path that is no longer a favorite and frees its thumbnail.
    private void RemoveFavoriteItem(string path)
    {
        var toRemove = FavoriteWallpapers.FirstOrDefault(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (toRemove is null)
            return;
        FavoriteWallpapers.Remove(toRemove);
        toRemove.DisposeThumbnail();
    }

    // Removes a favorite from its own card on the Favorites tab: drops the path from the
    // favorites, then the card, and clears the heart when it was the active wallpaper.
    private async Task RemoveFavoriteItemAsync(GalleryItem item)
    {
        var path = item.FilePath;
        try
        {
            await _library.SetFavoriteAsync(path, false);
            RemoveFavoriteItem(path);
            if (CurrentWallpaperPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                IsCurrentWallpaperFavorited = false;
            await ShowTransientStatusAsync("✓ Removed from favorites.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error removing favorite: {ex.Message}");
        }
    }

    // Removes the gallery card for a deleted path and frees its thumbnail.
    private void RemoveGalleryItem(string path)
    {
        var toRemove = GalleryItems.FirstOrDefault(g => g.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (toRemove is null)
            return;
        GalleryItems.Remove(toRemove);
        toRemove.DisposeThumbnail();
    }

    // Reloads the preview thumbnail from the processed current.png/current.jpg
    // and disposes the previous bitmap to avoid leaking unmanaged memory.
    public void RefreshPreviewImage()
    {
        try
        {
            var oldImage = PreviewImage;

            // Load from the processed current.png/current.jpg in the host's data folder.
            // PNG is preferred; fall back to JPEG if the PNG version does not exist
            var previewPath = SwitchWallpaper.FindCurrentImage(_appFolders.DataFolder);

            if (previewPath is not null)
            {
                using var stream = File.OpenRead(previewPath);
                PreviewImage = new Avalonia.Media.Imaging.Bitmap(stream);
            }
            else
            {
                PreviewImage = null;
            }

            // Dispose the old bitmap after the new one is assigned to avoid a blank-frame flash
            oldImage?.Dispose();
        }
        catch
        {
            PreviewImage = null;
        }
    }

    private async void RefreshFavoriteState()
    {
        try
        {
            var path = CurrentWallpaperPath;
            if (string.IsNullOrEmpty(path))
            {
                IsCurrentWallpaperFavorited = false;
                return;
            }
            var picture = await _library.GetPictureAsync(path);
            IsCurrentWallpaperFavorited = picture.IsFavorite;
        }
        catch
        {
            IsCurrentWallpaperFavorited = false;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (_checkForUpdates is null)
        {
            await ShowTransientStatusAsync("✗ Update service not available.");
            return;
        }

        var progress = new Progress<string>(msg => StatusMessage = msg);
        try
        {
            await Task.Run(() => _checkForUpdates.CheckAsync(forceUpdate: false, progress: progress));
            await ShowTransientStatusAsync($"✓ Already up to date ({_appInfo.Version}).");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Update check failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesForce()
    {
        if (_checkForUpdates is null)
        {
            await ShowTransientStatusAsync("✗ Update service not available.");
            return;
        }

        var progress = new Progress<string>(msg => StatusMessage = msg);
        try
        {
            await Task.Run(() => _checkForUpdates.CheckAsync(forceUpdate: true, progress: progress));
            await ShowTransientStatusAsync($"✓ Already on latest version ({_appInfo.Version}).");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Update check failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenCurrentWallpaper()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        try
        {
            _shellOpener.Open(path);
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Could not open wallpaper: {ex.Message}");
        }
    }

    private static string GetDisplayName(string path) => WallpaperDisplayName.FromPath(path);

    // Releases all managed resources held by the ViewModel: event subscriptions, cancellation
    // tokens, and unmanaged Avalonia Bitmap objects. Called when the settings window closes.
    public void Cleanup()
    {
        if (_switchWallpaper is not null)
            _switchWallpaper.WallpaperChanged -= OnWallpaperChanged;
        _library.Changed -= OnLibraryChanged;
        _wallpaperSources.Changed -= OnWallpaperSourcesChanged;

        // Cancel any pending status-clear or gallery-load operations, then hand out a fresh
        // source so a group's save still in flight can still raise a status message. The old
        // source is cancelled but not disposed here: a call may still be awaiting its token,
        // and disposing it underneath that call is what previously threw
        // ObjectDisposedException. Its owner disposes it once superseded.
        lock (_statusLock)
        {
            _statusCts.Cancel();
            _statusCts = new CancellationTokenSource();
        }

        // A change still waiting for its save is saved now, so changes made just before the
        // window closes are not silently discarded.
        foreach (var group in Groups)
            group.Flush();

        // Free the preview bitmap's unmanaged memory
        var image = PreviewImage;
        PreviewImage = null;
        image?.Dispose();

        foreach (var item in GalleryItems)
            item.DisposeThumbnail();
        GalleryItems.Clear();

        foreach (var item in FavoriteWallpapers)
            item.DisposeThumbnail();
        FavoriteWallpapers.Clear();
    }

    // Displays a status message for durationMs milliseconds, then clears it.
    // Cancels any previously running transient message so overlapping calls don't race.
    public async Task ShowTransientStatusAsync(string message, int durationMs = 3000)
    {
        // Cancel the previous delay so the old message doesn't clear the new one prematurely.
        // Swapping under a lock, and cancelling rather than disposing, fixes a race that
        // threw ObjectDisposedException out of Task.Delay below: the previous code disposed
        // the superseded source immediately, while the call that owned it was still awaiting
        // its token. Superseding a message must only cancel it - the owner disposes it.
        CancellationTokenSource cts;
        lock (_statusLock)
        {
            _statusCts.Cancel();
            cts = new CancellationTokenSource();
            _statusCts = cts;
        }

        StatusMessage = message;
        try
        {
            await Task.Delay(durationMs, cts.Token);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Dispose only once superseded. While this source is still the current one, a
            // later call will Cancel() it, which would throw on a disposed instance.
            var superseded = false;
            lock (_statusLock)
                superseded = !ReferenceEquals(_statusCts, cts);
            if (superseded)
                cts.Dispose();
        }
    }

    // Rebuilds the gallery from the wallpapers folder, newest first. GalleryItem objects are
    // created synchronously; thumbnails are loaded asynchronously in the background.
    [RelayCommand]
    private async Task LoadGallery()
    {
        try
        {
            var listing = await _library.ListGalleryAsync();
            if (!listing.FolderFound)
            {
                await ShowTransientStatusAsync("✗ Wallpapers folder not configured or not found.");
                return;
            }

            // Dispose old thumbnails before clearing to release bitmap memory
            foreach (var item in GalleryItems)
                item.DisposeThumbnail();
            GalleryItems.Clear();

            foreach (var picture in listing.Pictures)
            {
                GalleryItems.Add(new GalleryItem(
                    picture.Path,
                    picture.IsFavorite,
                    picture.IsBanned,
                    GallerySetAsCurrent,
                    GalleryToggleFavorite,
                    GalleryToggleBan,
                    GalleryDelete));
            }

            var count = listing.Pictures.Count;
            await ShowTransientStatusAsync($"✓ {count} image{(count == 1 ? "" : "s")} loaded.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error loading gallery: {ex.Message}");
        }
    }

    private async Task GallerySetAsCurrent(GalleryItem item)
    {
        if (_switchWallpaper is null)
        {
            await ShowTransientStatusAsync("✗ Wallpaper switcher not available.");
            return;
        }
        StatusMessage = "Switching wallpaper...";
        string? result;
        try
        {
            result = await Task.Run(() => _switchWallpaper.SwitchToSpecificAsync(item.FilePath));
        }
        catch (Exception ex)
        {
            // A refused or unreadable wallpaper leaves the current one in place
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
            return;
        }
        if (result is null)
        {
            await ShowTransientStatusAsync("✗ Wallpaper file not found.");
            return;
        }
        CurrentWallpaperPath = result;
        CurrentWallpaperName = GetDisplayName(result);
        RefreshPreviewImage();
        RefreshFavoriteState();
        await ShowTransientStatusAsync($"✓ Switched to: {CurrentWallpaperName}");
    }

    // Toggles a gallery item's favorite state, as its heart shows it, and mirrors the change in
    // the FavoriteWallpapers observable collection. Also updates IsCurrentWallpaperFavorited
    // if the toggled item happens to be the active wallpaper.
    private async Task GalleryToggleFavorite(GalleryItem item)
    {
        try
        {
            var makeFavorite = !item.IsFavorite;
            var favoriteCount = await _library.SetFavoriteAsync(item.FilePath, makeFavorite);
            item.IsFavorite = makeFavorite;
            var isCurrent = CurrentWallpaperPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase);
            if (!makeFavorite)
            {
                RemoveFavoriteItem(item.FilePath);
                if (isCurrent)
                    IsCurrentWallpaperFavorited = false;
                return;
            }

            AddFavoriteItem(item.FilePath);
            if (isCurrent)
                IsCurrentWallpaperFavorited = true;
            var milestone = EasterEggShows.Favorite(favoriteCount);
            if (milestone is not null)
                _easterEggs.Request(milestone);
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating favorites: {ex.Message}");
        }
    }

    private async Task GalleryToggleBan(GalleryItem item)
    {
        try
        {
            var ban = !item.IsBanned;
            await _library.SetBannedAsync(item.FilePath, ban);
            item.IsBanned = ban;
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating ban list: {ex.Message}");
        }
    }

    // Deletes a wallpaper from disk, from the favorites/ban lists and from the gallery
    // collection. If the deleted file was the active wallpaper, the library advances to the
    // next available image so the desktop is never left showing a file that no longer exists.
    private async Task GalleryDelete(GalleryItem item)
    {
        try
        {
            var outcome = await Task.Run(() => _library.DeleteAsync(item.FilePath));
            RemoveFavoriteItem(item.FilePath);

            if (outcome.WasCurrent)
            {
                // The deleted file can no longer be a favorite - clear the heart indicator so
                // the UI does not show an empty-path wallpaper as favorited.
                IsCurrentWallpaperFavorited = false;

                var next = outcome.SwitchedTo;
                if (next is not null)
                {
                    CurrentWallpaperPath = next;
                    CurrentWallpaperName = GetDisplayName(next);
                    RefreshPreviewImage();
                    RefreshFavoriteState();
                    RemoveGalleryItem(item.FilePath);
                    await ShowTransientStatusAsync($"✓ Deleted and switched to: {CurrentWallpaperName}");
                    return;
                }

                // No remaining wallpapers - clear the display
                CurrentWallpaperPath = string.Empty;
                CurrentWallpaperName = "(none)";
                RefreshPreviewImage();
            }

            RemoveGalleryItem(item.FilePath);
            await ShowTransientStatusAsync("✓ Wallpaper deleted.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error deleting wallpaper: {ex.Message}");
        }
    }
}

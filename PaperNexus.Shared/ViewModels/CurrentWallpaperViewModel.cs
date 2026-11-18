using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

// What the phone's Current tab shows in place of the picture.
public enum CurrentWallpaperState
{
    // There is a current wallpaper; its picture, title and heart are shown.
    Showing,

    // There are no pictures yet and a download is running.
    Downloading,

    // A download finished and there are still no pictures.
    DownloadFailed,

    // There are pictures but none is the wallpaper: on first open while the first one is
    // being set, or when setting it failed or is not allowed.
    NoWallpaper,
}

// The phone's Current tab: the current wallpaper, its title, the heart, and the Next and
// Random buttons, which change the wallpaper on both the home screen and the lock screen.
//
// On opening it shows the current wallpaper. When there is none, it makes one: with no
// pictures it downloads them, and with pictures it sets the next one in slideshow order, the
// same rule "Next" uses, without a tap. Every change of wallpaper made elsewhere (another copy
// of the tab after Android recreates the screen, the Gallery's set-as-current) arrives through
// WallpaperChanged, and a heart or delete made on another tab through the library's Changed.
//
// Delete removes the current wallpaper's picture from the phone (the view asks first) and
// sets the next one; after the last picture it downloads new ones.
//
// It lives as long as the process: a view built for a recreated screen attaches to it and
// finds the wallpaper and the confirmation line as they were (EnsureLoadedAsync does the
// first-open work once).
public partial class CurrentWallpaperViewModel : ObservableObject, IDisposable
{
    public const string DownloadingText = "No wallpapers yet. Downloading…";
    public const string DownloadFailedText = "Could not download wallpapers. Check your connection.";
    public const string NotAllowedText = "This phone does not allow apps to change the wallpaper.";
    public const string SwitchFailedText = "Could not change the wallpaper. Try again.";
    public const string AddedText = "Added to favorites.";
    public const string RemovedText = "Removed from favorites.";
    public const string DeleteQuestion = "Delete this wallpaper file from disk?";
    public const string DeletedText = "Wallpaper deleted.";
    public const string DeletedNoMoreText = "Wallpaper deleted. No more wallpapers.";

    private readonly ISettingsStore _settingsStore;
    private readonly IWallpaperLibrary _library;
    private readonly ISwitchWallpaper _switchWallpaper;
    private readonly IDownloadWallpapers _downloadWallpapers;
    private readonly IWallpaperApplier _wallpaperApplier;
    private readonly ILogger<CurrentWallpaperViewModel> _logger;
    private readonly SynchronizationContext? _screenContext;
    private CancellationTokenSource _statusCts = new();
    private readonly object _statusLock = new();

    // True while this tab deletes its own picture, so the library's report of that delete
    // is not handled a second time.
    private bool _deleting;

    // True from creation until LoadAsync has decided what to show, and while a download or a
    // switch runs. Next and Random are dimmed meanwhile.
    private bool _isWorking = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowing))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadFailed))]
    [NotifyPropertyChangedFor(nameof(PlaceholderText))]
    [NotifyPropertyChangedFor(nameof(CanSwitch))]
    [NotifyPropertyChangedFor(nameof(CanFavorite))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private CurrentWallpaperState _state = CurrentWallpaperState.NoWallpaper;

    // The source picture of the current wallpaper (the downloaded file, not the processed one).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFavorite))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private string? _picturePath;

    // The short line under the buttons that confirms a heart or a delete, for three seconds.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusSuccess))]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    private StatusTone _statusTone;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteToolTip))]
    private bool _isFavorite;

    // False when the phone forbids changing the wallpaper; Next and Random then stay dimmed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitch))]
    private bool _isSwitchingAllowed = true;

    // The red line under the buttons, or null when there is nothing wrong.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public CurrentWallpaperViewModel(
        ISettingsStore settingsStore,
        IWallpaperLibrary library,
        ISwitchWallpaper switchWallpaper,
        IDownloadWallpapers downloadWallpapers,
        IWallpaperApplier wallpaperApplier,
        ILogger<CurrentWallpaperViewModel> logger)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _library = library.ThrowIfNull();
        _switchWallpaper = switchWallpaper.ThrowIfNull();
        _downloadWallpapers = downloadWallpapers.ThrowIfNull();
        _wallpaperApplier = wallpaperApplier.ThrowIfNull();
        _logger = logger.ThrowIfNull();
        _screenContext = SynchronizationContext.Current;
        _switchWallpaper.WallpaperChanged += OnWallpaperChanged;
        _library.Changed += OnLibraryChanged;
    }

    public bool IsShowing => State == CurrentWallpaperState.Showing;

    public bool IsDownloading => State == CurrentWallpaperState.Downloading;

    public bool IsDownloadFailed => State == CurrentWallpaperState.DownloadFailed;

    // The sentence in the picture's place while there is no picture.
    public string PlaceholderText => State switch
    {
        CurrentWallpaperState.Downloading => DownloadingText,
        CurrentWallpaperState.DownloadFailed => DownloadFailedText,
        _ => string.Empty,
    };

    // Next and Random work only with pictures to choose from, when the phone allows changing
    // the wallpaper, and while nothing else is running.
    public bool CanSwitch => IsWorking is false
        && IsSwitchingAllowed
        && State is CurrentWallpaperState.Showing or CurrentWallpaperState.NoWallpaper;

    // The heart acts on the current wallpaper, so it needs one.
    public bool CanFavorite => State == CurrentWallpaperState.Showing && !string.IsNullOrEmpty(PicturePath);

    public bool HasError => ErrorMessage is not null;

    // Delete acts on the current wallpaper, and waits while a switch or download runs.
    public bool CanDelete => CanFavorite && !IsWorking;

    public bool HasStatus => StatusMessage is not null;

    public bool IsStatusSuccess => StatusTone == StatusTone.Success;

    public bool IsStatusError => StatusTone == StatusTone.Error;

    public string FavoriteToolTip => IsFavorite ? "Remove from favorites" : "Add to favorites";

    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (SetProperty(ref _isWorking, value))
            {
                OnPropertyChanged(nameof(CanSwitch));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    // The first-open work, once per view model: a view attaching again after the screen was
    // recreated finds the tab already showing what it should.
    public Task EnsureLoadedAsync()
    {
        _loadTask ??= LoadAsync();
        return _loadTask;
    }

    private Task? _loadTask;

    // Decides what the tab shows.
    public async Task LoadAsync()
    {
        IsWorking = true;
        try
        {
            IsSwitchingAllowed = _wallpaperApplier.CanSetWallpaper;
            if (!IsSwitchingAllowed)
                ErrorMessage = NotAllowedText;

            var settings = await _settingsStore.LoadAsync();
            var current = settings.CurrentWallpaperPath;
            if (!string.IsNullOrEmpty(current) && File.Exists(current))
            {
                await ShowWallpaperAsync(current);
                return;
            }

            await MakeAWallpaperAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the current wallpaper.");
            State = CurrentWallpaperState.NoWallpaper;
        }
        finally
        {
            IsWorking = false;
        }
    }

    // Changes the wallpaper to the next one in slideshow order.
    [RelayCommand]
    private Task Next() => SwitchAsync(_switchWallpaper.SwitchToNextAsync);

    // Changes the wallpaper to a random one.
    [RelayCommand]
    private Task Random() => SwitchAsync(_switchWallpaper.SwitchToRandomAsync);

    // Downloads again after a download that brought no pictures.
    [RelayCommand]
    private async Task TryAgain()
    {
        if (IsWorking)
            return;

        IsWorking = true;
        try
        {
            await MakeAWallpaperAsync();
        }
        finally
        {
            IsWorking = false;
        }
    }

    // Makes the current wallpaper a favorite, or clears it when it already is one, as the
    // heart shows it, and says which in the line under the buttons.
    [RelayCommand]
    private async Task ToggleFavorite()
    {
        var path = PicturePath;
        if (!CanFavorite || path is null)
            return;

        var makeFavorite = !IsFavorite;
        try
        {
            await _library.SetFavoriteAsync(path, makeFavorite);
            IsFavorite = makeFavorite;
            ShowStatus(makeFavorite ? AddedText : RemovedText, StatusTone.Success);
        }
        catch (Exception ex)
        {
            // The heart keeps showing what is saved
            _logger.LogError(ex, "Failed to update favorites for {Path}.", path);
            ShowStatus($"Error updating favorites: {ex.Message}", StatusTone.Error);
        }
    }

    // Deletes the current wallpaper's picture and sets the next one, as the desktop does.
    // The view has already asked. After the last picture the tab downloads new ones.
    [RelayCommand]
    private async Task Delete()
    {
        var path = PicturePath;
        if (!CanDelete || path is null)
            return;

        IsWorking = true;
        _deleting = true;
        try
        {
            var outcome = await _library.DeleteAsync(path);
            if (outcome.SwitchedTo is not null)
            {
                await ShowWallpaperAsync(outcome.SwitchedTo);
                ShowStatus($"Deleted and switched to: {Title}", StatusTone.Success);
                return;
            }

            // The shown picture was not the saved current one (or nothing was left): set the
            // next picture when there is one, otherwise download new ones
            if (await _switchWallpaper.HasWallpapersAsync())
            {
                ShowStatus(DeletedText, StatusTone.Success);
                await SetWallpaperAsync(_switchWallpaper.SwitchToNextAsync);
                return;
            }

            ShowStatus(DeletedNoMoreText, StatusTone.Success);
            PicturePath = null;
            Title = string.Empty;
            IsFavorite = false;
            await MakeAWallpaperAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting the wallpaper {Path} failed.", path);
            ShowStatus($"Error deleting wallpaper: {ex.Message}", StatusTone.Error);
        }
        finally
        {
            _deleting = false;
            IsWorking = false;
        }
    }

    // With no current wallpaper: downloads when there are no pictures, then sets the next
    // picture in slideshow order as the wallpaper. The tab shows the download sentences
    // while it has no pictures.
    private async Task MakeAWallpaperAsync()
    {
        var hasPictures = await _switchWallpaper.HasWallpapersAsync();
        if (!hasPictures)
        {
            State = CurrentWallpaperState.Downloading;
            try
            {
                await Task.Run(_downloadWallpapers.DownloadAllAsync);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Downloading the first wallpapers failed.");
            }

            hasPictures = await _switchWallpaper.HasWallpapersAsync();
            if (!hasPictures)
            {
                State = CurrentWallpaperState.DownloadFailed;
                return;
            }
        }

        State = CurrentWallpaperState.NoWallpaper;
        if (IsSwitchingAllowed)
            await SetWallpaperAsync(_switchWallpaper.SwitchToNextAsync);
    }

    // Runs one switch from a button: Next and Random dim while it works, and the red line
    // says when the phone did not take the new wallpaper.
    private async Task SwitchAsync(Func<Task<string?>> switchOperation)
    {
        if (!CanSwitch)
            return;

        IsWorking = true;
        try
        {
            var switched = await SetWallpaperAsync(switchOperation);
            // The pictures are gone (deleted by retention, or by hand): fetch new ones
            if (!switched && ErrorMessage is null)
                await MakeAWallpaperAsync();
        }
        finally
        {
            IsWorking = false;
        }
    }

    // Sets a wallpaper with switchOperation and shows it. Returns false when there was
    // nothing to set; a failure shows the red line and leaves the current wallpaper shown.
    private async Task<bool> SetWallpaperAsync(Func<Task<string?>> switchOperation)
    {
        if (IsSwitchingAllowed)
            ErrorMessage = null;
        try
        {
            var path = await Task.Run(switchOperation);
            if (path is null)
                return false;

            await ShowWallpaperAsync(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Changing the wallpaper failed.");
            ErrorMessage = SwitchFailedText;
            return true;
        }
    }

    // Shows the picture, with its heart as the library has it.
    private async Task ShowWallpaperAsync(string path)
    {
        var picture = await _library.GetPictureAsync(path);
        PicturePath = path;
        Title = picture.Title;
        IsFavorite = picture.IsFavorite;
        State = CurrentWallpaperState.Showing;
    }

    // Shows a line for three seconds, replacing any earlier one.
    private void ShowStatus(string message, StatusTone tone)
    {
        CancellationTokenSource cts;
        lock (_statusLock)
        {
            _statusCts.Cancel();
            _statusCts.Dispose();
            cts = new CancellationTokenSource();
            _statusCts = cts;
        }

        StatusTone = tone;
        StatusMessage = message;
        _ = ClearStatusAfterAsync(cts.Token);
    }

    private async Task ClearStatusAfterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(PictureListViewModel.StatusDurationMs, token);
            StatusMessage = null;
        }
        catch (OperationCanceledException) { }
    }

    // A wallpaper set anywhere in the app (another copy of this tab, after Android recreated
    // the screen) is shown here too. Raised on a background thread.
    private void OnWallpaperChanged(string path)
    {
        Dispatcher.UIThread.Post(() => _ = ShowChangedWallpaperAsync(path));
    }

    private async Task ShowChangedWallpaperAsync(string path)
    {
        try
        {
            await ShowWallpaperAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show the changed wallpaper {Path}.", path);
        }
    }

    // A heart or delete made on another tab. A heart on this picture changes this heart. The
    // picture deleted elsewhere is replaced by the library's switch to the next one, which
    // arrives through WallpaperChanged; when no picture is left, the tab downloads new ones.
    // Raised on a background thread; applied on the screen's thread.
    private void OnLibraryChanged(LibraryChange change)
    {
        if (_screenContext is null)
        {
            _ = ApplyLibraryChangeAsync(change);
            return;
        }

        _screenContext.Post(_ => _ = ApplyLibraryChangeAsync(change), null);
    }

    private async Task ApplyLibraryChangeAsync(LibraryChange change)
    {
        var isThisPicture = change.Path is not null && PicturePath is not null
            && change.Path.Equals(PicturePath, StringComparison.OrdinalIgnoreCase);
        if (!isThisPicture)
            return;

        try
        {
            switch (change.Kind)
            {
                case LibraryChangeKind.Favorited:
                case LibraryChangeKind.Unfavorited:
                    IsFavorite = change.Kind == LibraryChangeKind.Favorited;
                    break;
                case LibraryChangeKind.Deleted when !_deleting && !IsWorking:
                    if (await _switchWallpaper.HasWallpapersAsync())
                        return;
                    IsWorking = true;
                    try
                    {
                        PicturePath = null;
                        Title = string.Empty;
                        IsFavorite = false;
                        await MakeAWallpaperAsync();
                    }
                    finally
                    {
                        IsWorking = false;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Showing a change to {Path} failed.", change.Path);
        }
    }

    // Stops listening to the switcher and the library, which outlive this tab.
    public void Dispose()
    {
        _switchWallpaper.WallpaperChanged -= OnWallpaperChanged;
        _library.Changed -= OnLibraryChanged;
        lock (_statusLock)
            _statusCts.Cancel();
    }
}

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
// of the tab after Android recreates the screen) arrives through WallpaperChanged.
public partial class CurrentWallpaperViewModel : ObservableObject, IDisposable
{
    public const string DownloadingText = "No wallpapers yet. Downloading…";
    public const string DownloadFailedText = "Could not download wallpapers. Check your connection.";
    public const string NotAllowedText = "This phone does not allow apps to change the wallpaper.";
    public const string SwitchFailedText = "Could not change the wallpaper. Try again.";

    private readonly ISettingsStore _settingsStore;
    private readonly ISwitchWallpaper _switchWallpaper;
    private readonly IDownloadWallpapers _downloadWallpapers;
    private readonly IWallpaperApplier _wallpaperApplier;
    private readonly ILogger<CurrentWallpaperViewModel> _logger;

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
    private CurrentWallpaperState _state = CurrentWallpaperState.NoWallpaper;

    // The source picture of the current wallpaper (the downloaded file, not the processed one).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFavorite))]
    private string? _picturePath;

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
        ISwitchWallpaper switchWallpaper,
        IDownloadWallpapers downloadWallpapers,
        IWallpaperApplier wallpaperApplier,
        ILogger<CurrentWallpaperViewModel> logger)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _switchWallpaper = switchWallpaper.ThrowIfNull();
        _downloadWallpapers = downloadWallpapers.ThrowIfNull();
        _wallpaperApplier = wallpaperApplier.ThrowIfNull();
        _logger = logger.ThrowIfNull();
        _switchWallpaper.WallpaperChanged += OnWallpaperChanged;
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

    public string FavoriteToolTip => IsFavorite ? "Remove from favorites" : "Add to favorites";

    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (SetProperty(ref _isWorking, value))
                OnPropertyChanged(nameof(CanSwitch));
        }
    }

    // Decides what the tab shows. Called once when the tab appears.
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
                ShowWallpaper(current, settings.FavoriteWallpapers);
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

    // Makes the current wallpaper a favorite, or clears it when it already is one.
    [RelayCommand]
    private async Task ToggleFavorite()
    {
        var path = PicturePath;
        if (!CanFavorite || path is null)
            return;

        try
        {
            var settings = await _settingsStore.LoadAsync();
            var wasFavorite = settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase);
            if (wasFavorite)
                settings.FavoriteWallpapers.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
            else
                settings.FavoriteWallpapers.Add(path);
            await _settingsStore.SaveAsync(settings);
            IsFavorite = !wasFavorite;
        }
        catch (Exception ex)
        {
            // The heart keeps showing what is saved
            _logger.LogError(ex, "Failed to update favorites for {Path}.", path);
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

            var settings = await _settingsStore.LoadAsync();
            ShowWallpaper(path, settings.FavoriteWallpapers);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Changing the wallpaper failed.");
            ErrorMessage = SwitchFailedText;
            return true;
        }
    }

    private void ShowWallpaper(string path, IEnumerable<string> favorites)
    {
        PicturePath = path;
        Title = WallpaperDisplayName.FromPath(path);
        IsFavorite = favorites.Contains(path, StringComparer.OrdinalIgnoreCase);
        State = CurrentWallpaperState.Showing;
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
            var settings = await _settingsStore.LoadAsync();
            ShowWallpaper(path, settings.FavoriteWallpapers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show the changed wallpaper {Path}.", path);
        }
    }

    // Stops listening to the switcher, which outlives this tab.
    public void Dispose()
    {
        _switchWallpaper.WallpaperChanged -= OnWallpaperChanged;
    }
}

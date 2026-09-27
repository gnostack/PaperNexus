using System.Collections.ObjectModel;
using System.Linq;
using CronExpressionDescriptor;
using Avalonia;
using Cronos;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PaperNexus.Core;
using BundledFonts = PaperNexus.Core.Imaging.BundledFonts;

namespace PaperNexus.ViewModels;

public record ResolutionOption(string Label, string Description, int Width, int Height)
{
    public override string ToString() => Label;
}

public record FillStyleOption(string Label, string Description, WallpaperFillStyle Style)
{
    public override string ToString() => Label;
}

public record SlideshowOrderOption(string Label, string Description, SlideshowOrder Order)
{
    public override string ToString() => Label;
}

public record AnnotationPositionOption(string Label, string Description, AnnotationPosition Position)
{
    public override string ToString() => Label;
}

public record ScheduleModeOption(string Label, SlideshowScheduleMode Mode)
{
    public override string ToString() => Label;
}

public record IntervalTypeOption(string Label, IntervalType Type, double Minimum, double Maximum)
{
    public override string ToString() => Label;
}

public partial class WallpaperConfigViewModel : ObservableObject
{
    public static readonly IReadOnlyList<ResolutionOption> ResolutionOptions = new[]
    {
        new ResolutionOption("Native",                 "Use the image's original resolution, no resampling",              0,    0),
        new ResolutionOption("HD (1280×720)",          "Low resolution, good for slow connections or limited storage", 1280,  720),
        new ResolutionOption("Full HD (1920×1080)",    "Standard HD, suitable for most 1080p displays",               1920, 1080),
        new ResolutionOption("2K (2560×1440)",         "Quad HD, ideal for 1440p displays",                           2560, 1440),
        new ResolutionOption("4K (3840×2160)",         "Ultra HD, best for large 4K displays",                        3840, 2160),
        new ResolutionOption("5K (5120×2880)",         "5K, for high-DPI displays and Apple Studio Display",          5120, 2880),
        new ResolutionOption("8K (7680×4320)",         "8K, maximum quality, larger file sizes",                      7680, 4320),
    };

    private readonly ISwitchWallpaper? _switchWallpaper;
    private readonly ICheckForUpdates? _checkForUpdates;
    private readonly IDownloadWallpapers? _downloadWallpapers;

    public static readonly IReadOnlyList<FillStyleOption> FillStyleOptions = new[]
    {
        new FillStyleOption("Fill",    "Crop and scale to fill the screen, maintaining aspect ratio",         WallpaperFillStyle.Fill),
        new FillStyleOption("Fit",     "Scale to fit within the screen, adding black bars as needed",          WallpaperFillStyle.Fit),
        new FillStyleOption("Stretch", "Stretch to fill the screen, ignoring aspect ratio",                    WallpaperFillStyle.Stretch),
        new FillStyleOption("Tile",    "Repeat the image in a grid pattern to fill the screen",                WallpaperFillStyle.Tile),
        new FillStyleOption("Center",  "Display at original size, centered, no scaling applied",              WallpaperFillStyle.Center),
        new FillStyleOption("Span",    "Stretch a single image across all monitors as one continuous display", WallpaperFillStyle.Span),
    };

    public static readonly IReadOnlyList<SlideshowOrderOption> SlideshowOrderOptions = new[]
    {
        new SlideshowOrderOption("Alphabetical", "Cycle through wallpapers in A→Z order by filename",                   SlideshowOrder.Alphabetical),
        new SlideshowOrderOption("Oldest first", "Cycle from oldest to newest by file date",                            SlideshowOrder.OldestFirst),
        new SlideshowOrderOption("Newest first", "Cycle from newest to oldest by file date",                            SlideshowOrder.NewestFirst),
        new SlideshowOrderOption("Random",       "Pick a random wallpaper each time, favoring favorites if prioritized", SlideshowOrder.Random),
    };

    public static readonly IReadOnlyList<AnnotationPositionOption> AnnotationPositionOptions = new[]
    {
        new AnnotationPositionOption("Top Left",     "Show annotation in the top-left corner of the wallpaper",     AnnotationPosition.TopLeft),
        new AnnotationPositionOption("Top Right",    "Show annotation in the top-right corner of the wallpaper",    AnnotationPosition.TopRight),
        new AnnotationPositionOption("Bottom Left",  "Show annotation in the bottom-left corner of the wallpaper",  AnnotationPosition.BottomLeft),
        new AnnotationPositionOption("Bottom Right", "Show annotation in the bottom-right corner of the wallpaper", AnnotationPosition.BottomRight),
    };

    public static readonly IReadOnlyList<ScheduleModeOption> ScheduleModeOptions = new[]
    {
        new ScheduleModeOption("Interval",        SlideshowScheduleMode.Interval),
        new ScheduleModeOption("Cron expression", SlideshowScheduleMode.CronExpression),
    };

    public static readonly IReadOnlyList<IntervalTypeOption> IntervalTypeOptions = new[]
    {
        new IntervalTypeOption("Seconds", IntervalType.Seconds,  1, 59),
        new IntervalTypeOption("Minutes", IntervalType.Minutes,  1, 59),
        new IntervalTypeOption("Hours",   IntervalType.Hours,    1, 23),
        new IntervalTypeOption("Days",    IntervalType.Days,     1, 28),
        new IntervalTypeOption("Weeks",   IntervalType.Weeks,    1,  4),
        new IntervalTypeOption("Months",  IntervalType.Months,   1, 12),
        new IntervalTypeOption("Years",   IntervalType.Years,    1,  1),
    };

    // Raised when an easter egg should play. The ViewModel decides *whether* an egg fires;
    // the window owns the overlay and decides how it is drawn.
    public event Action<EasterEggShow>? EasterEggTriggered;

    public static readonly IReadOnlyList<string> FontFamilyOptions = BuildFontFamilyOptions();

    // Builds the font picker list: bundled fonts first, then every font family installed
    // on this machine.
    //
    // This used to probe a hardcoded list of eleven Windows font names (Arial, Segoe UI,
    // MS Gothic, and so on) and keep the ones that resolved. On Linux none of those exist,
    // so the picker collapsed to the single bundled family and the user could not choose a
    // font at all. Enumerating the installed families instead means the list reflects the
    // machine rather than a guess about which fonts a machine ought to have.
    private static List<string> BuildFontFamilyOptions()
    {
        var fonts = new List<string>(BundledFonts.Names);

        var systemFamilies = BundledFonts.InstalledFamilyNames()
            .Where(name => !fonts.Contains(name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);

        fonts.AddRange(systemFamilies);
        return fonts;
    }

    [ObservableProperty]
    private string _folder;

    [ObservableProperty]
    private string _slideshowCronExpression;

    [ObservableProperty]
    private double? _slideshowInterval;

    private IntervalTypeOption _selectedIntervalType;
    private SlideshowScheduleMode _slideshowScheduleMode;

    // Drives both the dropdown selection and the conditional input panels beneath it.
    public SlideshowScheduleMode SlideshowScheduleMode
    {
        get => _slideshowScheduleMode;
        set
        {
            if (SetProperty(ref _slideshowScheduleMode, value))
            {
                OnPropertyChanged(nameof(SelectedScheduleMode));
                OnPropertyChanged(nameof(IsIntervalMode));
                OnPropertyChanged(nameof(IsCronMode));
                OnPropertyChanged(nameof(ScheduleDescription));
                TriggerSave();
            }
        }
    }

    public ScheduleModeOption SelectedScheduleMode
    {
        get => ScheduleModeOptions.FirstOrDefault(o => o.Mode == _slideshowScheduleMode) ?? ScheduleModeOptions[0];
        set { if (value is not null) SlideshowScheduleMode = value.Mode; }
    }

    public IntervalTypeOption SelectedIntervalType
    {
        get => _selectedIntervalType;
        set
        {
            if (SetProperty(ref _selectedIntervalType, value) && value is not null)
            {
                // Clamp current value to the new type's range
                if (SlideshowInterval is null || SlideshowInterval < value.Minimum) SlideshowInterval = value.Minimum;
                else if (SlideshowInterval > value.Maximum) SlideshowInterval = value.Maximum;
                OnPropertyChanged(nameof(IntervalMinimum));
                OnPropertyChanged(nameof(IntervalMaximum));
                OnPropertyChanged(nameof(ScheduleDescription));
                TriggerSave();
            }
        }
    }

    public decimal IntervalMinimum => (decimal)(_selectedIntervalType?.Minimum ?? 1);
    public decimal IntervalMaximum => (decimal)(_selectedIntervalType?.Maximum ?? 59);

    public bool IsIntervalMode => _slideshowScheduleMode == SlideshowScheduleMode.Interval;
    public bool IsCronMode => _slideshowScheduleMode == SlideshowScheduleMode.CronExpression;

    // Shows a human-readable description of the generated cron expression for both interval and cron modes.
    public string ScheduleDescription
    {
        get
        {
            if (_slideshowScheduleMode == SlideshowScheduleMode.CronExpression
                && string.IsNullOrWhiteSpace(SlideshowCronExpression))
                return "Enter a cron expression, e.g. 0 9 * * 1-5";

            try
            {
                return ExpressionDescriptor.GetDescription(BuildSlideshowCronExpression());
            }
            catch
            {
                return "Invalid  ·  Fields: minute  hour  day  month  weekday\nExample: 0 9 * * 1-5  (9:00 AM on weekdays)";
            }
        }
    }

    [ObservableProperty]
    private ResolutionOption _selectedResolution;

    [ObservableProperty]
    private FillStyleOption _selectedFillStyle;

    [ObservableProperty]
    private SlideshowOrderOption _selectedSlideshowOrder;

    [ObservableProperty]
    private int? _retentionDays;

    [ObservableProperty]
    private bool _annotateWallpaper = true;

    [ObservableProperty]
    private string _annotationFontFamily = BundledFonts.DefaultFontFamily;

    [ObservableProperty]
    private int _annotationFontSize = 18;

    [ObservableProperty]
    private string _annotationColor = "#F5F5F5";

    [ObservableProperty]
    private bool _annotationOutlineEnabled = true;

    [ObservableProperty]
    private AnnotationPositionOption _selectedAnnotationPosition;

    [ObservableProperty]
    private bool _runOnStartup = true;

    [ObservableProperty]
    private bool _autoUpdatesEnabled = true;

    [ObservableProperty]
    private bool _slideshowEnabled = true;

    [ObservableProperty]
    private bool _debugMode;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _favoritePriorityEnabled;

    [ObservableProperty]
    private int _favoritePriorityWeight = 3;

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

    [ObservableProperty]
    private ObservableCollection<WallpaperSource> _sources = [];

    [ObservableProperty]
    private WallpaperSource? _selectedSource;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private bool _isCurrentWallpaperFavorited;

    [ObservableProperty]
    private ObservableCollection<string> _favoriteWallpapers = [];

    [ObservableProperty]
    private string? _selectedFavorite;

    // Every settings read and write in the window goes through this store.
    private readonly ISettingsStore _settingsStore;

    private bool _isLoading;
    private bool _hasPendingSave;
    private CancellationTokenSource _statusCts = new();

    // Guards the _statusCts swap. Status messages are raised from the UI thread and from
    // background save/download continuations, so two calls can interleave mid-swap.
    private readonly object _statusLock = new();
    private CancellationTokenSource _saveCts = new();


    // Initialises all observable properties to sensible defaults and resolves
    // background services from the App DI container. The WallpaperChanged event is
    // subscribed here so the preview updates automatically when the slideshow switches.
    // Created per window from the container, which supplies the settings store.
    public WallpaperConfigViewModel(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _folder = string.Empty;
        _slideshowCronExpression = string.Empty;
        _slideshowInterval = 30;
        _selectedIntervalType = IntervalTypeOptions[1]; // Minutes
        _slideshowScheduleMode = SlideshowScheduleMode.Interval;
        _statusMessage = string.Empty;
        _statusForeground = Brushes.White;
        _currentWallpaperPath = string.Empty;
        _currentWallpaperName = string.Empty;
        _selectedResolution = ResolutionOptions[0];
        // Services are resolved lazily from the App's background host rather than injected,
        // because the ViewModel is constructed on the UI thread before the host is fully started.
        _switchWallpaper = (Application.Current as App)?.Services?.GetService<ISwitchWallpaper>();
        _checkForUpdates = (Application.Current as App)?.Services?.GetService<ICheckForUpdates>();
        _downloadWallpapers = (Application.Current as App)?.Services?.GetService<IDownloadWallpapers>();
        _selectedFillStyle = FillStyleOptions[0];
        _selectedAnnotationPosition = AnnotationPositionOptions[0];
        _selectedSlideshowOrder = SlideshowOrderOptions.First(o => o.Order == SlideshowOrder.NewestFirst);
        _sources.CollectionChanged += OnSourcesCollectionChanged;

        if (_switchWallpaper is not null)
            _switchWallpaper.WallpaperChanged += OnWallpaperChanged;
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

    // Unhook property-change listeners from the old collection before replacing it,
    // so the old sources don't continue triggering saves after they are discarded.
    partial void OnSourcesChanging(ObservableCollection<WallpaperSource> value)
    {
        _sources.CollectionChanged -= OnSourcesCollectionChanged;
        foreach (var src in _sources)
            src.PropertyChanged -= OnSourcePropertyChanged;
    }

    // Hook property-change listeners onto the new collection and all its initial items.
    partial void OnSourcesChanged(ObservableCollection<WallpaperSource> value)
    {
        value.CollectionChanged += OnSourcesCollectionChanged;
        foreach (var src in value)
            src.PropertyChanged += OnSourcePropertyChanged;
    }

    // Maintains per-item property listeners when items are added or removed from the sources list,
    // so toggling IsEnabled on a source triggers an auto-save.
    private void OnSourcesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (WallpaperSource src in e.OldItems)
                src.PropertyChanged -= OnSourcePropertyChanged;

        if (e.NewItems is not null)
            foreach (WallpaperSource src in e.NewItems)
                src.PropertyChanged += OnSourcePropertyChanged;

        TriggerSave();
    }

    private void OnSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        TriggerSave();
    }

    // Derives the status bar text colour from the message prefix:
    // ✓ = green (success), ✗ = red (error), anything else = white (neutral/info).
    partial void OnStatusMessageChanged(string value)
    {
        StatusForeground = value.StartsWith("✓") ? new SolidColorBrush(Color.Parse("#4ADE80"))
            : value.StartsWith("✗") ? new SolidColorBrush(Color.Parse("#F87171"))
            : Brushes.White;
    }

    partial void OnFolderChanged(string value) => TriggerSave();
    partial void OnSlideshowCronExpressionChanged(string value)
    {
        OnPropertyChanged(nameof(ScheduleDescription));
        TriggerSave();
    }

    partial void OnSlideshowIntervalChanged(double? value)
    {
        if (value is null)
        {
            SlideshowInterval = _selectedIntervalType?.Minimum ?? 1;
            return;
        }
        OnPropertyChanged(nameof(ScheduleDescription));
        TriggerSave();
    }
    partial void OnSelectedResolutionChanged(ResolutionOption value) => TriggerSave();
    partial void OnSelectedFillStyleChanged(FillStyleOption value) => TriggerSave();
    partial void OnSelectedSlideshowOrderChanged(SlideshowOrderOption value) => TriggerSave();
    partial void OnRetentionDaysChanged(int? value) => TriggerSave();
    partial void OnAnnotateWallpaperChanged(bool value) => TriggerSave();
    partial void OnAnnotationFontFamilyChanged(string value) => TriggerSave();
    partial void OnAnnotationFontSizeChanged(int value) => TriggerSave();
    partial void OnAnnotationColorChanged(string value)
    {
        TriggerSave();
        // A few hex values spell words and are a reasonable thing to try in a colour box.
        var show = EasterEggShows.MagicColor(value);
        if (show is not null)
            EasterEggTriggered?.Invoke(show);
    }
    partial void OnAnnotationOutlineEnabledChanged(bool value) => TriggerSave();
    partial void OnSelectedAnnotationPositionChanged(AnnotationPositionOption value) => TriggerSave();
    partial void OnAutoUpdatesEnabledChanged(bool value) => TriggerSave();
    partial void OnSlideshowEnabledChanged(bool value) => TriggerSave();
    partial void OnDebugModeChanged(bool value) => TriggerSave();
    partial void OnMinimizeToTrayChanged(bool value) => TriggerSave();
    partial void OnFavoritePriorityEnabledChanged(bool value) => TriggerSave();
    partial void OnFavoritePriorityWeightChanged(int value) => TriggerSave();

    partial void OnRunOnStartupChanged(bool value)
    {
        try
        {
            StartupRegistration.Update(value);
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Failed to update startup registration: {ex.Message}");
        }
        TriggerSave();
    }

    // Guards against property-change callbacks firing during LoadAsync (which sets many
    // properties at once) and causing a flood of redundant save operations. Outside of
    // loading, debounces rapid bursts of property changes (e.g. typing a path, adjusting
    // a slider) into a single save after a brief quiet period, reducing disk I/O and
    // preventing the "✓ Settings saved." toast from firing on every keystroke.
    private void TriggerSave()
    {
        if (_isLoading)
            return;
        _ = DebouncedSaveAsync();
    }

    private const int SaveDebounceMs = 500;

    // Cancels any pending debounce timer and restarts it. Only the call that survives
    // the full delay actually writes to disk, so rapid changes coalesce into one save.
    private async Task DebouncedSaveAsync()
    {
        var oldCts = _saveCts;
        oldCts.Cancel();
        _saveCts = new CancellationTokenSource();
        var cts = _saveCts;
        oldCts.Dispose();

        _hasPendingSave = true;
        try
        {
            await Task.Delay(SaveDebounceMs, cts.Token);
            _hasPendingSave = false;
            await SaveSettingsAsync();
        }
        catch (OperationCanceledException) { }
    }

    // Populates all ViewModel properties from persisted settings without triggering
    // auto-save. The _isLoading flag suppresses TriggerSave during the batch assignment.
    // Gallery loading is deferred until after the flag is cleared so it can save independently.
    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsStore.LoadAsync();
            Folder = settings.Download.Folder;
            // Set the backing field directly to avoid triggering TriggerSave via the setter,
            // then raise property-changed for all derived mode flags manually.
            _slideshowScheduleMode = settings.Slideshow.ScheduleMode;
            OnPropertyChanged(nameof(SlideshowScheduleMode));
            OnPropertyChanged(nameof(SelectedScheduleMode));
            OnPropertyChanged(nameof(IsIntervalMode));
            OnPropertyChanged(nameof(IsCronMode));
            _selectedIntervalType = IntervalTypeOptions.FirstOrDefault(o => o.Type == settings.Slideshow.IntervalType)
                ?? IntervalTypeOptions[1]; // Minutes
            OnPropertyChanged(nameof(SelectedIntervalType));
            OnPropertyChanged(nameof(IntervalMinimum));
            OnPropertyChanged(nameof(IntervalMaximum));
            SlideshowInterval = settings.Slideshow.Interval > 0 ? settings.Slideshow.Interval : 30;
            SlideshowCronExpression = settings.Slideshow.CronExpression;
            // Match the stored resolution/fill/order values to the corresponding option objects;
            // fall back to index 0 if no matching option is found (e.g. unknown enum value after upgrade).
            SelectedResolution = ResolutionOptions.FirstOrDefault(
                r => r.Width == settings.Download.ResolutionWidth && r.Height == settings.Download.ResolutionHeight)
                ?? ResolutionOptions[0];
            SelectedFillStyle = FillStyleOptions.FirstOrDefault(f => f.Style == settings.Slideshow.FillStyle)
                ?? FillStyleOptions[0];
            SelectedSlideshowOrder = SlideshowOrderOptions.FirstOrDefault(o => o.Order == settings.Slideshow.Order)
                ?? SlideshowOrderOptions[0];
            RetentionDays = settings.Download.RetentionDays;
            AnnotateWallpaper = settings.AnnotateWallpaper;
            AnnotationFontFamily = settings.Annotation.FontFamily;
            AnnotationFontSize = settings.Annotation.FontSize;
            AnnotationColor = settings.Annotation.Color;
            AnnotationOutlineEnabled = settings.Annotation.OutlineEnabled;
            SelectedAnnotationPosition = AnnotationPositionOptions.FirstOrDefault(
                p => p.Position == settings.Annotation.Position) ?? AnnotationPositionOptions[0];
            RunOnStartup = settings.RunOnStartup;
            AutoUpdatesEnabled = settings.AutoUpdatesEnabled;
            SlideshowEnabled = settings.Slideshow.Enabled;
            DebugMode = settings.DebugMode || Program.IsDebugMode;
            MinimizeToTray = settings.MinimizeToTray;
            FavoritePriorityEnabled = settings.Slideshow.FavoritePriorityEnabled;
            FavoritePriorityWeight = settings.Slideshow.FavoritePriorityWeight > 0 ? settings.Slideshow.FavoritePriorityWeight : 3;

            Sources = new ObservableCollection<WallpaperSource>(settings.Sources);

            var path = settings.CurrentWallpaperPath;
            CurrentWallpaperPath = path;
            CurrentWallpaperName = string.IsNullOrEmpty(path) ? "(none)" : GetDisplayName(path);
            RefreshPreviewImage();
            IsCurrentWallpaperFavorited = !string.IsNullOrEmpty(path)
                && settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase);
            FavoriteWallpapers = new ObservableCollection<string>(settings.FavoriteWallpapers);
        }
        finally
        {
            _isLoading = false;
        }

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
            StatusMessage = "Downloading wallpapers...";
            await Task.Run(_downloadWallpapers.DownloadAllAsync);
            await ShowTransientStatusAsync("✓ Download complete.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Download failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteSource()
    {
        if (SelectedSource is not null)
            Sources.Remove(SelectedSource);
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

    // Deletes the current wallpaper file from disk, then automatically advances to the
    // next available wallpaper so the desktop is never left showing a missing file.
    [RelayCommand]
    private async Task DeleteCurrentWallpaper()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);

            CurrentWallpaperPath = string.Empty;
            CurrentWallpaperName = "(none)";

            if (_switchWallpaper is null)
            {
                await ShowTransientStatusAsync("✓ Wallpaper deleted.");
                return;
            }

            var next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
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

    // Toggles the current wallpaper in/out of the favorites list and keeps
    // the FavoriteWallpapers observable collection in sync with the persisted settings.
    [RelayCommand]
    private async Task ToggleFavorite()
    {
        var path = CurrentWallpaperPath;
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var settings = await _settingsStore.LoadAsync();
            if (settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                settings.FavoriteWallpapers.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
                IsCurrentWallpaperFavorited = false;
                await _settingsStore.SaveAsync(settings);
                var toRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (toRemove is not null) FavoriteWallpapers.Remove(toRemove);
                await ShowTransientStatusAsync("✓ Removed from favorites.");
            }
            else
            {
                settings.FavoriteWallpapers.Add(path);
                IsCurrentWallpaperFavorited = true;
                await _settingsStore.SaveAsync(settings);
                if (!FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase))
                    FavoriteWallpapers.Add(path);
                // Round numbers of favorites play an overlay instead of the usual confirmation.
                var milestone = EasterEggShows.Favorite(settings.FavoriteWallpapers.Count);
                if (milestone is not null)
                    EasterEggTriggered?.Invoke(milestone);
                else
                    await ShowTransientStatusAsync("✓ Added to favorites.");
            }
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating favorites: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveFavorite()
    {
        var path = SelectedFavorite;
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            var settings = await _settingsStore.LoadAsync();
            settings.FavoriteWallpapers.RemoveAll(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
            await _settingsStore.SaveAsync(settings);
            var toRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (toRemove is not null) FavoriteWallpapers.Remove(toRemove);
            if (CurrentWallpaperPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                IsCurrentWallpaperFavorited = false;
            await ShowTransientStatusAsync("✓ Removed from favorites.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error removing favorite: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SetFavoriteAsCurrent()
    {
        var path = SelectedFavorite;
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (_switchWallpaper is null)
            {
                await ShowTransientStatusAsync("✗ Wallpaper switcher not available.");
                return;
            }

            StatusMessage = "Switching wallpaper...";
            var result = await Task.Run(() => _switchWallpaper.SwitchToSpecificAsync(path));
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
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
        }
    }

    // Reloads the preview thumbnail from the processed current.png/current.jpg
    // and disposes the previous bitmap to avoid leaking unmanaged memory.
    internal void RefreshPreviewImage()
    {
        try
        {
            var oldImage = PreviewImage;

            // Load from the processed current.png/current.jpg in the app directory
            var pngPath = Path.Combine(AppContext.BaseDirectory, "current.png");
            var jpgPath = Path.Combine(AppContext.BaseDirectory, "current.jpg");
            // PNG is preferred; fall back to JPEG if the PNG version does not exist
            var previewPath = File.Exists(pngPath) ? pngPath : File.Exists(jpgPath) ? jpgPath : null;

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
            var settings = await _settingsStore.LoadAsync();
            IsCurrentWallpaperFavorited = settings.FavoriteWallpapers.Contains(path, StringComparer.OrdinalIgnoreCase);
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
            await ShowTransientStatusAsync($"✓ Already up to date ({App.AppVersion}).");
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
            await ShowTransientStatusAsync($"✓ Already on latest version ({App.AppVersion}).");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Update check failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Folder;
        if (string.IsNullOrEmpty(folder))
            return;
        try
        {
            ShellOpener.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Could not open folder: {ex.Message}");
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
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Could not open wallpaper: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ReportBug()
    {
        var version = App.AppVersion;
        var body = $"**App Version:** {version}\n\n**Describe the bug:**\n\n\n**Steps to reproduce:**\n\n\n**Expected behavior:**\n\n";
        var url = "https://github.com/0Keith/PaperNexus/issues/new"
                + "?assignees=claude&labels=bug&title=Bug+Report"
                + "&body=" + Uri.EscapeDataString(body);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        Process.Start(new ProcessStartInfo("https://github.com/0Keith/PaperNexus") { UseShellExecute = true });
    }

    // Maps all ViewModel properties back onto a WallpaperNexusSettings instance and persists it.
    // For interval-based schedule modes the cron expression is synthesised from the numeric value;
    // for manual cron mode the raw expression is validated before saving.
    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            settings.Download.Folder = Folder;
            settings.Slideshow.ScheduleMode = SlideshowScheduleMode;
            settings.Slideshow.Interval = SlideshowInterval ?? _selectedIntervalType?.Minimum ?? 1;
            settings.Slideshow.IntervalType = _selectedIntervalType?.Type ?? IntervalType.Minutes;
            var cronExpression = BuildSlideshowCronExpression();
            // Only validate the expression when the user typed it directly - synthesised expressions are always valid
            if (SlideshowScheduleMode == SlideshowScheduleMode.CronExpression)
            {
                try
                {
                    ParseCronExpression(cronExpression);
                }
                catch (CronFormatException)
                {
                    await ShowTransientStatusAsync("✗ Invalid cron expression.");
                    return;
                }
            }
            settings.Slideshow.CronExpression = cronExpression;
            settings.Download.ResolutionWidth = SelectedResolution.Width;
            settings.Download.ResolutionHeight = SelectedResolution.Height;
            settings.Download.RetentionDays = RetentionDays ?? 365;
            settings.Slideshow.FillStyle = SelectedFillStyle.Style;
            settings.Slideshow.Order = SelectedSlideshowOrder.Order;
            settings.Slideshow.Enabled = SlideshowEnabled;
            settings.AnnotateWallpaper = AnnotateWallpaper;
            settings.Annotation.FontFamily = AnnotationFontFamily;
            settings.Annotation.FontSize = AnnotationFontSize;
            settings.Annotation.Color = AnnotationColor;
            settings.Annotation.OutlineEnabled = AnnotationOutlineEnabled;
            settings.Annotation.Position = SelectedAnnotationPosition.Position;
            settings.RunOnStartup = RunOnStartup;
            settings.AutoUpdatesEnabled = AutoUpdatesEnabled;
            settings.DebugMode = DebugMode;
            settings.MinimizeToTray = MinimizeToTray;
            settings.Slideshow.FavoritePriorityEnabled = FavoritePriorityEnabled;
            settings.Slideshow.FavoritePriorityWeight = FavoritePriorityWeight;
            // Preserve LastDownloadUtc from the freshly-loaded settings for each source so that
            // background download timestamps (written by DownloadWallpapers while the settings
            // window is open) are not silently overwritten with the ViewModel's stale copies.
            // Always keep the later of the two timestamps: the persisted value wins if a download
            // completed after LoadAsync ran; the ViewModel value wins if it is somehow newer.
            // Match on source name; unmatched sources (new/renamed) keep no timestamp.
            var persistedTimestamps = settings.Sources
                .Where(s => s.LastDownloadUtc.HasValue)
                .ToDictionary(s => s.Name, s => s.LastDownloadUtc!.Value, StringComparer.OrdinalIgnoreCase);
            var vmSources = Sources.ToList();
            foreach (var src in vmSources)
            {
                if (persistedTimestamps.TryGetValue(src.Name, out var persistedTs))
                    src.LastDownloadUtc = src.LastDownloadUtc.HasValue
                        ? (DateTimeOffset?)src.LastDownloadUtc.Value.Max(persistedTs)
                        : persistedTs;
            }
            settings.Sources = vmSources;
            await _settingsStore.SaveAsync(settings);
            await ShowTransientStatusAsync("✓ Settings saved.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error saving settings: {ex.Message}";
        }
    }

    private static string GetDisplayName(string path)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var lastSep = nameWithoutExt.LastIndexOf(" - ", StringComparison.Ordinal);
        return lastSep > 0 ? nameWithoutExt[..lastSep] : nameWithoutExt;
    }

    // Releases all managed resources held by the ViewModel: event subscriptions, cancellation
    // tokens, and unmanaged Avalonia Bitmap objects. Called when the settings window closes.
    internal void Cleanup()
    {
        if (_switchWallpaper is not null)
            _switchWallpaper.WallpaperChanged -= OnWallpaperChanged;

        // Cancel any pending status-clear or gallery-load operations, then hand out a fresh
        // source so an in-flight SaveSettingsAsync can still raise a status message. The old
        // source is cancelled but not disposed here: a call may still be awaiting its token,
        // and disposing it underneath that call is what previously threw
        // ObjectDisposedException. Its owner disposes it once superseded.
        lock (_statusLock)
        {
            _statusCts.Cancel();
            _statusCts = new CancellationTokenSource();
        }

        // If a debounced save is pending, flush it immediately before cancelling so that
        // changes made just before the window closes are not silently discarded.
        if (_hasPendingSave)
        {
            _hasPendingSave = false;
            _ = SaveSettingsAsync();
        }
        _saveCts.Cancel();
        _saveCts.Dispose();

        // Free the preview bitmap's unmanaged memory
        var image = PreviewImage;
        PreviewImage = null;
        image?.Dispose();

        foreach (var item in GalleryItems)
            item.DisposeThumbnail();
        GalleryItems.Clear();

        foreach (var src in Sources)
            src.PropertyChanged -= OnSourcePropertyChanged;
        Sources.CollectionChanged -= OnSourcesCollectionChanged;
    }

    // Displays a status message for durationMs milliseconds, then clears it.
    // Cancels any previously running transient message so overlapping calls don't race.
    internal async Task ShowTransientStatusAsync(string message, int durationMs = 3000)
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

    // Rebuilds the gallery from the wallpapers folder. Cancels any in-progress gallery
    // load (e.g. from a previous folder change) before starting a new one. GalleryItem
    // objects are created synchronously; thumbnails are loaded asynchronously in the background.
    [RelayCommand]
    private async Task LoadGallery()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            var folder = settings.Download.Folder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                await ShowTransientStatusAsync("✗ Wallpapers folder not configured or not found.");
                return;
            }

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
            // Sort newest-first so recently downloaded wallpapers appear at the top
            var files = Directory.EnumerateFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            var favorites = new HashSet<string>(settings.FavoriteWallpapers, StringComparer.OrdinalIgnoreCase);
            var banned = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);

            // Dispose old thumbnails before clearing to release bitmap memory
            foreach (var item in GalleryItems)
                item.DisposeThumbnail();
            GalleryItems.Clear();

            foreach (var file in files)
            {
                GalleryItems.Add(new GalleryItem(
                    file,
                    favorites.Contains(file),
                    banned.Contains(file),
                    GallerySetAsCurrent,
                    GalleryToggleFavorite,
                    GalleryToggleBan,
                    GalleryDelete));
            }

            await ShowTransientStatusAsync($"✓ {files.Count} image{(files.Count == 1 ? "" : "s")} loaded.");
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
        var result = await Task.Run(() => _switchWallpaper.SwitchToSpecificAsync(item.FilePath));
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

    // Toggles a gallery item's favorite state and mirrors the change in both the persisted
    // settings and the FavoriteWallpapers observable collection. Also updates
    // IsCurrentWallpaperFavorited if the toggled item happens to be the active wallpaper.
    private async Task GalleryToggleFavorite(GalleryItem item)
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            if (item.IsFavorite)
            {
                settings.FavoriteWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                item.IsFavorite = false;
                var toRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                if (toRemove is not null) FavoriteWallpapers.Remove(toRemove);
                if (CurrentWallpaperPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
                    IsCurrentWallpaperFavorited = false;
            }
            else
            {
                settings.FavoriteWallpapers.Add(item.FilePath);
                item.IsFavorite = true;
                if (!FavoriteWallpapers.Contains(item.FilePath, StringComparer.OrdinalIgnoreCase))
                    FavoriteWallpapers.Add(item.FilePath);
                if (CurrentWallpaperPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
                    IsCurrentWallpaperFavorited = true;

                var milestone = EasterEggShows.Favorite(settings.FavoriteWallpapers.Count);
                if (milestone is not null)
                    EasterEggTriggered?.Invoke(milestone);
            }
            await _settingsStore.SaveAsync(settings);
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
            var settings = await _settingsStore.LoadAsync();
            if (item.IsBanned)
            {
                settings.BannedWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                item.IsBanned = false;
            }
            else
            {
                settings.BannedWallpapers.Add(item.FilePath);
                item.IsBanned = true;
            }
            await _settingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error updating ban list: {ex.Message}");
        }
    }

    // Deletes a wallpaper from disk, removes it from the favorites/ban lists and the
    // gallery collection. If the deleted file was the active wallpaper, advances to the
    // next available image so the desktop is never left showing a file that no longer exists.
    private async Task GalleryDelete(GalleryItem item)
    {
        try
        {
            if (File.Exists(item.FilePath))
                File.Delete(item.FilePath);

            // Remove the path from both special lists so stale references don't persist in settings
            var settings = await _settingsStore.LoadAsync();
            settings.FavoriteWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            settings.BannedWallpapers.RemoveAll(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            await _settingsStore.SaveAsync(settings);

            var favToRemove = FavoriteWallpapers.FirstOrDefault(f => f.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            if (favToRemove is not null) FavoriteWallpapers.Remove(favToRemove);

            var wasActive = CurrentWallpaperPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase);
            if (wasActive)
            {
                // The deleted file can no longer be a favorite - clear the heart indicator so
                // the UI does not show an empty-path wallpaper as favorited.
                IsCurrentWallpaperFavorited = false;

                // Advance to the next wallpaper so the desktop does not remain on a deleted file.
                // Mirror the same advance-or-clear logic used by DeleteCurrentWallpaper.
                if (_switchWallpaper is not null)
                {
                    var next = await Task.Run(_switchWallpaper.SwitchToNextAsync);
                    if (next is not null)
                    {
                        CurrentWallpaperPath = next;
                        CurrentWallpaperName = GetDisplayName(next);
                        RefreshPreviewImage();
                        RefreshFavoriteState();
                        item.DisposeThumbnail();
                        GalleryItems.Remove(item);
                        await ShowTransientStatusAsync($"✓ Deleted and switched to: {CurrentWallpaperName}");
                        return;
                    }
                }

                // No switcher available or no remaining wallpapers - clear the display
                CurrentWallpaperPath = string.Empty;
                CurrentWallpaperName = "(none)";
                RefreshPreviewImage();
            }

            item.DisposeThumbnail();
            GalleryItems.Remove(item);
            await ShowTransientStatusAsync("✓ Wallpaper deleted.");
        }
        catch (Exception ex)
        {
            await ShowTransientStatusAsync($"✗ Error deleting wallpaper: {ex.Message}");
        }
    }

    // Derives a cron expression from the current schedule mode, interval, and interval type.
    private string BuildSlideshowCronExpression()
    {
        if (_slideshowScheduleMode == SlideshowScheduleMode.Interval)
            return BuildIntervalCron((int)(SlideshowInterval ?? _selectedIntervalType?.Minimum ?? 1), _selectedIntervalType?.Type ?? IntervalType.Minutes);

        return SlideshowCronExpression;
    }

    // Converts a numeric interval and unit type to a valid cron expression.
    // All field values are clamped to their legal ranges.
    internal static string BuildIntervalCron(int interval, IntervalType type)
    {
        var n = Math.Max(1, interval);
        return type switch
        {
            IntervalType.Seconds => $"*/{Math.Min(n, 59)} * * * * *",
            IntervalType.Minutes => $"*/{Math.Min(n, 59)} * * * *",
            IntervalType.Hours => $"0 */{Math.Min(n, 23)} * * *",
            IntervalType.Days => $"0 0 */{Math.Min(n, 28)} * *",
            IntervalType.Weeks => $"0 0 */{Math.Min(n * 7, 28)} * *",
            IntervalType.Months => $"0 0 1 */{Math.Min(n, 12)} *",
            _ => "0 0 1 1 *", // Years
        };
    }

    // Parses a cron expression, auto-detecting 5-field (standard) or 6-field (with seconds) format.
    private static CronExpression ParseCronExpression(string expression)
    {
        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        return CronExpression.Parse(expression, format);
    }
}

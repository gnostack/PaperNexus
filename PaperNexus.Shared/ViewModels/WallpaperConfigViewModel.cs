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
    private readonly IStartupRegistration? _startupRegistration;
    private readonly IAppInfo _appInfo;
    private readonly IShellOpener _shellOpener;
    private readonly IAppFolders _appFolders;
    private readonly IWallpaperLibrary _library;
    private readonly IWallpaperSources _wallpaperSources;
    private readonly SynchronizationContext? _windowContext;

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

    // Every settings read and write in the window goes through this store.
    private readonly ISettingsStore _settingsStore;

    private bool _isLoading;
    private bool _hasPendingSave;
    private CancellationTokenSource _statusCts = new();

    // Guards the _statusCts swap. Status messages are raised from the UI thread and from
    // background save/download continuations, so two calls can interleave mid-swap.
    private readonly object _statusLock = new();
    private CancellationTokenSource _saveCts = new();


    // Initialises all observable properties to sensible defaults. Created per window from
    // the container, which supplies the settings store, the host's app facts and shell
    // opener, and whichever background services the host registers. A service the host
    // does not have stays null and its command reports it as unavailable; a host with no
    // run-at-sign-in setting registers no IStartupRegistration. The WallpaperChanged event is
    // subscribed here so the preview updates automatically when the slideshow switches, and
    // the library's Changed event so the cards follow hearts, bans and deletes made anywhere,
    // and the sources' Changed event so the source rows show every saved change.
    public WallpaperConfigViewModel(
        ISettingsStore settingsStore,
        IAppInfo appInfo,
        IShellOpener shellOpener,
        IAppFolders appFolders,
        IWallpaperLibrary library,
        IWallpaperSources wallpaperSources,
        ISwitchWallpaper? switchWallpaper = null,
        IDownloadWallpapers? downloadWallpapers = null,
        ICheckForUpdates? checkForUpdates = null,
        IStartupRegistration? startupRegistration = null)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _appInfo = appInfo.ThrowIfNull();
        _shellOpener = shellOpener.ThrowIfNull();
        _appFolders = appFolders.ThrowIfNull();
        _library = library.ThrowIfNull();
        _wallpaperSources = wallpaperSources.ThrowIfNull();
        _windowContext = SynchronizationContext.Current;
        _switchWallpaper = switchWallpaper;
        _downloadWallpapers = downloadWallpapers;
        _checkForUpdates = checkForUpdates;
        _startupRegistration = startupRegistration;
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
        _selectedFillStyle = FillStyleOptions[0];
        _selectedAnnotationPosition = AnnotationPositionOptions[0];
        _selectedSlideshowOrder = SlideshowOrderOptions.First(o => o.Order == SlideshowOrder.NewestFirst);

        if (_switchWallpaper is not null)
            _switchWallpaper.WallpaperChanged += OnWallpaperChanged;
        _library.Changed += OnLibraryChanged;
        _wallpaperSources.Changed += OnWallpaperSourcesChanged;
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

    private SourceItem CreateSourceItem(SourceFields fields)
    {
        var item = new SourceItem(fields, RequestEditSource, DeleteSourceAsync, SetSourceEnabledAsync);
        return item;
    }

    private void RequestEditSource(SourceItem item) => EditSourceRequested?.Invoke(item);

    // A form for a new source, or one filled with the row's source, for the source dialog.
    public SourceFormViewModel CreateSourceForm(SourceItem? editing)
    {
        var form = editing is null
            ? new SourceFormViewModel(_wallpaperSources)
            : new SourceFormViewModel(_wallpaperSources, editing.Fields);
        return form;
    }

    // The dialog saved its source: the rows are brought up to date, the saved source is
    // selected, and the status bar confirms it. The confirmations are not awaited, so a row is
    // not held busy while its sentence shows.
    public async Task SourceSavedAsync(SourceFormViewModel form)
    {
        await ReloadSourcesAsync();
        SelectedSource = Sources.FirstOrDefault(s => string.Equals(s.Name, form.SavedName, StringComparison.OrdinalIgnoreCase));
        _ = ShowTransientStatusAsync("✓ Settings saved.");
    }

    // The row's delete button deletes at once, without asking.
    private async Task DeleteSourceAsync(SourceItem item)
    {
        try
        {
            await _wallpaperSources.DeleteAsync(item.Name);
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
            await _wallpaperSources.SetEnabledAsync(item.Name, isEnabled);
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
            _startupRegistration?.Update(value);
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
            DebugMode = settings.DebugMode || _appInfo.StartedInDebugMode;
            MinimizeToTray = settings.MinimizeToTray;
            FavoritePriorityEnabled = settings.Slideshow.FavoritePriorityEnabled;
            FavoritePriorityWeight = settings.Slideshow.FavoritePriorityWeight > 0 ? settings.Slideshow.FavoritePriorityWeight : 3;

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
    private void OpenFolder()
    {
        var folder = Folder;
        if (string.IsNullOrEmpty(folder))
            return;
        try
        {
            _shellOpener.OpenFolder(folder);
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
            _shellOpener.Open(path);
        }
        catch (Exception ex)
        {
            _ = ShowTransientStatusAsync($"✗ Could not open wallpaper: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ReportBug()
    {
        var version = _appInfo.Version;
        var body = $"**App Version:** {version}\n\n**Describe the bug:**\n\n\n**Steps to reproduce:**\n\n\n**Expected behavior:**\n\n";
        var url = GitHubRepository.Current.WebUrl + "/issues/new"
                + "?assignees=claude&labels=bug&title=Bug+Report"
                + "&body=" + Uri.EscapeDataString(body);
        _shellOpener.Open(url);
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        var homepage = GitHubRepository.Current.WebUrl;
        _shellOpener.Open(homepage);
    }

    // Writes the settings this window edits onto the latest saved settings. For interval-based
    // schedule modes the cron expression is synthesised from the numeric value; for manual cron
    // mode the raw expression is validated before anything is written.
    //
    // The window owns: the download folder, resolution and retention; every slideshow field;
    // the annotation; and run at startup, automatic updates, debug mode and minimize to tray. It
    // must NOT overwrite what other writers own, so it never assigns them: the current
    // wallpaper (the switcher), favorites and bans (their own commands, the downloader's
    // retention pass), the found easter eggs, the window geometry, and the wallpaper sources
    // (IWallpaperSources, and each source's last download time, the downloader).
    private async Task SaveSettingsAsync()
    {
        try
        {
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

            // Read every value from the window here, on its own thread; the update below may
            // run on another one.
            var folder = Folder;
            var scheduleMode = SlideshowScheduleMode;
            var interval = SlideshowInterval ?? _selectedIntervalType?.Minimum ?? 1;
            var intervalType = _selectedIntervalType?.Type ?? IntervalType.Minutes;
            var resolution = SelectedResolution;
            var retentionDays = RetentionDays ?? 365;
            var fillStyle = SelectedFillStyle.Style;
            var order = SelectedSlideshowOrder.Order;
            var slideshowEnabled = SlideshowEnabled;
            var annotateWallpaper = AnnotateWallpaper;
            var fontFamily = AnnotationFontFamily;
            var fontSize = AnnotationFontSize;
            var color = AnnotationColor;
            var outlineEnabled = AnnotationOutlineEnabled;
            var position = SelectedAnnotationPosition.Position;
            var runOnStartup = RunOnStartup;
            var autoUpdatesEnabled = AutoUpdatesEnabled;
            var debugMode = DebugMode;
            var minimizeToTray = MinimizeToTray;
            var favoritePriorityEnabled = FavoritePriorityEnabled;
            var favoritePriorityWeight = FavoritePriorityWeight;

            await _settingsStore.UpdateAsync(latest =>
            {
                latest.Download.Folder = folder;
                latest.Slideshow.ScheduleMode = scheduleMode;
                latest.Slideshow.Interval = interval;
                latest.Slideshow.IntervalType = intervalType;
                latest.Slideshow.CronExpression = cronExpression;
                latest.Download.ResolutionWidth = resolution.Width;
                latest.Download.ResolutionHeight = resolution.Height;
                latest.Download.RetentionDays = retentionDays;
                latest.Slideshow.FillStyle = fillStyle;
                latest.Slideshow.Order = order;
                latest.Slideshow.Enabled = slideshowEnabled;
                latest.AnnotateWallpaper = annotateWallpaper;
                latest.Annotation.FontFamily = fontFamily;
                latest.Annotation.FontSize = fontSize;
                latest.Annotation.Color = color;
                latest.Annotation.OutlineEnabled = outlineEnabled;
                latest.Annotation.Position = position;
                latest.RunOnStartup = runOnStartup;
                latest.AutoUpdatesEnabled = autoUpdatesEnabled;
                latest.DebugMode = debugMode;
                latest.MinimizeToTray = minimizeToTray;
                latest.Slideshow.FavoritePriorityEnabled = favoritePriorityEnabled;
                latest.Slideshow.FavoritePriorityWeight = favoritePriorityWeight;
            });
            await ShowTransientStatusAsync("✓ Settings saved.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error saving settings: {ex.Message}";
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
                EasterEggTriggered?.Invoke(milestone);
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

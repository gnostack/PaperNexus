using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// The slideshow settings both devices show: on or off, the schedule (an interval or a cron
// expression), the order, favorite priority and the fill style. The fill styles listed are the
// ones this device can draw (IDeviceSettingRules).
public sealed partial class SlideshowSettingsViewModel : SettingsGroupViewModel
{
    public const string InvalidCronSentence = "Invalid cron expression.";

    public const int FavoriteWeightMinimum = 2;
    public const int FavoriteWeightMaximum = 20;
    public const int DefaultFavoriteWeight = 3;
    public const double DefaultInterval = 30;

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

    public static readonly IReadOnlyList<SlideshowOrderOption> SlideshowOrderOptions = new[]
    {
        new SlideshowOrderOption("Alphabetical", "Cycle through wallpapers in A→Z order by filename",                   SlideshowOrder.Alphabetical),
        new SlideshowOrderOption("Oldest first", "Cycle from oldest to newest by file date",                            SlideshowOrder.OldestFirst),
        new SlideshowOrderOption("Newest first", "Cycle from newest to oldest by file date",                            SlideshowOrder.NewestFirst),
        new SlideshowOrderOption("Random",       "Pick a random wallpaper each time, favoring favorites if prioritized", SlideshowOrder.Random),
    };

    // Every fill style any device draws; each device lists the ones it offers.
    public static readonly IReadOnlyList<FillStyleOption> AllFillStyleOptions = new[]
    {
        new FillStyleOption("Fill",    "Crop and scale to fill the screen, maintaining aspect ratio",         WallpaperFillStyle.Fill),
        new FillStyleOption("Fit",     "Scale to fit within the screen, adding black bars as needed",          WallpaperFillStyle.Fit),
        new FillStyleOption("Stretch", "Stretch to fill the screen, ignoring aspect ratio",                    WallpaperFillStyle.Stretch),
        new FillStyleOption("Tile",    "Repeat the image in a grid pattern to fill the screen",                WallpaperFillStyle.Tile),
        new FillStyleOption("Center",  "Display at original size, centered, no scaling applied",              WallpaperFillStyle.Center),
        new FillStyleOption("Span",    "Stretch a single image across all monitors as one continuous display", WallpaperFillStyle.Span),
    };

    private static readonly SlideshowOrderOption DefaultOrder =
        SlideshowOrderOptions.First(o => o.Order == SlideshowOrder.NewestFirst);

    private SlideshowScheduleMode _scheduleMode = SlideshowScheduleMode.Interval;
    private IntervalTypeOption _selectedIntervalType = IntervalTypeOptions[1]; // Minutes

    // True once the person picked a fill style. Until then a stored style this device does not
    // offer (a Span on the phone) is left as stored, though it shows as the first one.
    private bool _fillStyleChosen;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private double? _interval = DefaultInterval;

    [ObservableProperty]
    private string _cronExpression = string.Empty;

    [ObservableProperty]
    private SlideshowOrderOption _selectedOrder = DefaultOrder;

    [ObservableProperty]
    private bool _favoritePriorityEnabled;

    [ObservableProperty]
    private int _favoritePriorityWeight = DefaultFavoriteWeight;

    [ObservableProperty]
    private FillStyleOption _selectedFillStyle;

    public SlideshowSettingsViewModel(ISettingsStore settingsStore, IDeviceSettingRules deviceRules)
        : base(settingsStore)
    {
        deviceRules.ThrowIfNull();
        FillStyleOptions = AllFillStyleOptions
            .Where(option => deviceRules.FillStyles.Contains(option.Style))
            .OrderBy(option => IndexOf(deviceRules.FillStyles, option.Style))
            .ToList();
        _selectedFillStyle = FillStyleOptions[0];
    }

    // The fill styles this device offers, in its order.
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; }

    // Drives both the schedule picker and which input shows beneath it.
    public SlideshowScheduleMode ScheduleMode
    {
        get => _scheduleMode;
        set
        {
            if (SetProperty(ref _scheduleMode, value))
            {
                OnPropertyChanged(nameof(SelectedScheduleMode));
                OnPropertyChanged(nameof(IsIntervalMode));
                OnPropertyChanged(nameof(IsCronMode));
                OnPropertyChanged(nameof(ScheduleDescription));
                SaveSoon();
            }
        }
    }

    public ScheduleModeOption SelectedScheduleMode
    {
        get => ScheduleModeOptions.FirstOrDefault(o => o.Mode == _scheduleMode) ?? ScheduleModeOptions[0];
        set
        {
            if (value is not null)
                ScheduleMode = value.Mode;
        }
    }

    // Choosing another unit keeps the number inside the unit's range.
    public IntervalTypeOption SelectedIntervalType
    {
        get => _selectedIntervalType;
        set
        {
            if (value is null || !SetProperty(ref _selectedIntervalType, value))
                return;
            if (Interval is null || Interval < value.Minimum)
                Interval = value.Minimum;
            else if (Interval > value.Maximum)
                Interval = value.Maximum;
            OnPropertyChanged(nameof(IntervalMinimum));
            OnPropertyChanged(nameof(IntervalMaximum));
            OnPropertyChanged(nameof(ScheduleDescription));
            SaveSoon();
        }
    }

    public decimal IntervalMinimum => (decimal)_selectedIntervalType.Minimum;

    public decimal IntervalMaximum => (decimal)_selectedIntervalType.Maximum;

    public bool IsIntervalMode => _scheduleMode == SlideshowScheduleMode.Interval;

    public bool IsCronMode => _scheduleMode == SlideshowScheduleMode.CronExpression;

    // The sentence under the schedule, for interval and cron mode alike.
    public string ScheduleDescription => SlideshowSchedule.Describe(_scheduleMode, EffectiveCron());

    // An emptied number becomes the unit's minimum.
    partial void OnIntervalChanged(double? value)
    {
        if (value is null)
        {
            Interval = _selectedIntervalType.Minimum;
            return;
        }
        OnPropertyChanged(nameof(ScheduleDescription));
        SaveSoon();
    }

    partial void OnCronExpressionChanged(string value)
    {
        OnPropertyChanged(nameof(ScheduleDescription));
        SaveSoon();
    }

    partial void OnEnabledChanged(bool value) => SaveSoon();

    partial void OnSelectedOrderChanged(SlideshowOrderOption value) => SaveSoon();

    partial void OnFavoritePriorityEnabledChanged(bool value) => SaveSoon();

    partial void OnFavoritePriorityWeightChanged(int value) => SaveSoon();

    partial void OnSelectedFillStyleChanged(FillStyleOption value)
    {
        if (!IsLoading)
            _fillStyleChosen = true;
        SaveSoon();
    }

    // Stored values this version does not know show as the defaults: an unknown interval
    // unit as Minutes, an unknown order as Newest first, a fill style this device does not
    // offer as its first one.
    protected override void ReadFrom(WallpaperNexusSettings settings)
    {
        var slideshow = settings.Slideshow;
        ScheduleMode = slideshow.ScheduleMode;
        SelectedIntervalType = IntervalTypeOptions.FirstOrDefault(o => o.Type == slideshow.IntervalType)
            ?? IntervalTypeOptions[1];
        Interval = slideshow.Interval > 0 ? slideshow.Interval : DefaultInterval;
        CronExpression = slideshow.CronExpression;
        SelectedOrder = SlideshowOrderOptions.FirstOrDefault(o => o.Order == slideshow.Order) ?? DefaultOrder;
        SelectedFillStyle = FillStyleOptions.FirstOrDefault(f => f.Style == slideshow.FillStyle) ?? FillStyleOptions[0];
        _fillStyleChosen = false;
        Enabled = slideshow.Enabled;
        FavoritePriorityEnabled = slideshow.FavoritePriorityEnabled;
        FavoritePriorityWeight = slideshow.FavoritePriorityWeight > 0 ? slideshow.FavoritePriorityWeight : DefaultFavoriteWeight;
    }

    // In interval mode the expression the interval stands for is saved with it; in cron mode
    // the typed expression is checked first. While it is invalid the schedule is not saved and
    // the line says why; the group's other settings (on or off, order, favorites, fill style)
    // still are.
    protected override Action<WallpaperNexusSettings>? CaptureChange()
    {
        var cron = EffectiveCron();
        var scheduleMode = _scheduleMode;
        var scheduleIsValid = scheduleMode != SlideshowScheduleMode.CronExpression || SlideshowSchedule.IsValidCron(cron);
        if (!scheduleIsValid)
            ReportRefusal(InvalidCronSentence);

        var interval = Interval ?? _selectedIntervalType.Minimum;
        var intervalType = _selectedIntervalType.Type;
        var order = (SelectedOrder ?? DefaultOrder).Order;
        var fillStyle = (SelectedFillStyle ?? FillStyleOptions[0]).Style;
        var fillStyleChosen = _fillStyleChosen;
        var offeredStyles = FillStyleOptions.Select(f => f.Style).ToList();
        var enabled = Enabled;
        var priorityEnabled = FavoritePriorityEnabled;
        var priorityWeight = FavoritePriorityWeight;
        return latest =>
        {
            var slideshow = latest.Slideshow;
            if (scheduleIsValid)
            {
                slideshow.ScheduleMode = scheduleMode;
                slideshow.Interval = interval;
                slideshow.IntervalType = intervalType;
                slideshow.CronExpression = cron;
            }
            slideshow.Order = order;
            // A stored style this device does not offer stays until the person picks one
            if (fillStyleChosen || offeredStyles.Contains(slideshow.FillStyle))
                slideshow.FillStyle = fillStyle;
            slideshow.Enabled = enabled;
            slideshow.FavoritePriorityEnabled = priorityEnabled;
            slideshow.FavoritePriorityWeight = priorityWeight;
        };
    }

    // The expression the slideshow runs on for what the group shows now.
    private string EffectiveCron()
    {
        var interval = (int)(Interval ?? _selectedIntervalType.Minimum);
        var cron = SlideshowSchedule.EffectiveCron(_scheduleMode, interval, _selectedIntervalType.Type, CronExpression);
        return cron;
    }

    private static int IndexOf(IReadOnlyList<WallpaperFillStyle> styles, WallpaperFillStyle style)
    {
        for (var i = 0; i < styles.Count; i++)
        {
            if (styles[i] == style)
                return i;
        }
        return int.MaxValue;
    }
}

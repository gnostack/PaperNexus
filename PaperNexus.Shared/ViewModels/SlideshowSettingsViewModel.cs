using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// The slideshow settings both devices show: on or off, the schedule (an interval or a cron
// expression), the order, favorite priority and the fill style. The interval units, the numbers
// each allows, the fastest schedule and the fill styles listed are this device's
// (IDeviceSettingRules): the phone has no seconds, picks its minutes from 15, 20 and 30, and
// refuses a schedule that changes faster than every 15 minutes (issue #203, step 5).
public sealed partial class SlideshowSettingsViewModel : SettingsGroupViewModel
{
    public const string InvalidCronSentence = "Invalid cron expression.";

    // The only device with a fastest slideshow is the phone
    public const string TooFastSentence = "The phone can change the wallpaper at most every 15 minutes.";

    public const int FavoriteWeightMinimum = 2;
    public const int FavoriteWeightMaximum = 20;
    public const int DefaultFavoriteWeight = 3;
    public const double DefaultInterval = 30;

    public static readonly IReadOnlyList<ScheduleModeOption> ScheduleModeOptions = new[]
    {
        new ScheduleModeOption("Interval",        SlideshowScheduleMode.Interval),
        new ScheduleModeOption("Cron expression", SlideshowScheduleMode.CronExpression),
    };

    // Every interval unit with the desktop's ranges (SlideshowSchedule.IntervalRules), as the
    // desktop window lists them. IntervalTypeChoices are the ones this device offers.
    public static readonly IReadOnlyList<IntervalTypeOption> IntervalTypeOptions =
        SlideshowSchedule.IntervalRules.Select(OptionFor).ToList();

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

    private readonly TimeSpan _shortestGap;
    private SlideshowScheduleMode _scheduleMode = SlideshowScheduleMode.Interval;
    private IntervalTypeOption _selectedIntervalType;

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
        _shortestGap = deviceRules.ShortestSlideshowGap;
        IntervalTypeChoices = deviceRules.IntervalTypes.Select(ChoiceFor).ToList();
        _selectedIntervalType = IntervalTypeChoices.FirstOrDefault(o => o.Type == IntervalType.Minutes)
            ?? IntervalTypeChoices[0];
        FillStyleOptions = AllFillStyleOptions
            .Where(option => deviceRules.FillStyles.Contains(option.Style))
            .OrderBy(option => IndexOf(deviceRules.FillStyles, option.Style))
            .ToList();
        _selectedFillStyle = FillStyleOptions[0];
    }

    // The fill styles this device offers, in its order.
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; }

    // The interval units this device offers, in its order (the phone's unit picker lists them).
    public IReadOnlyList<IntervalTypeOption> IntervalTypeChoices { get; }

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

    // Choosing another unit keeps the number inside the unit's range, or moves it to the
    // nearest of the unit's fixed values.
    public IntervalTypeOption SelectedIntervalType
    {
        get => _selectedIntervalType;
        set
        {
            if (value is null || !SetProperty(ref _selectedIntervalType, value))
                return;
            if (value.FixedValues is { Count: > 0 } fixedValues)
            {
                var current = Interval ?? value.Minimum;
                if (!fixedValues.Contains((int)current))
                    Interval = Nearest(fixedValues, current);
            }
            else if (Interval is null || Interval < value.Minimum)
                Interval = value.Minimum;
            else if (Interval > value.Maximum)
                Interval = value.Maximum;
            OnPropertyChanged(nameof(IntervalMinimum));
            OnPropertyChanged(nameof(IntervalMaximum));
            OnPropertyChanged(nameof(HasFixedIntervals));
            OnPropertyChanged(nameof(FixedIntervals));
            OnPropertyChanged(nameof(ScheduleDescription));
            SaveSoon();
        }
    }

    // The unit's number is picked from a list (the phone's minutes) rather than typed.
    public bool HasFixedIntervals => _selectedIntervalType.HasFixedValues;

    // The numbers the list offers; empty for a unit typed into the number box.
    public IReadOnlyList<int> FixedIntervals => _selectedIntervalType.FixedValues ?? [];

    // The number as the picker button shows it. A stored number the list does not offer (a
    // stored 45 minutes) shows as it is.
    public string IntervalText => (Interval ?? 0).ToString("0", CultureInfo.InvariantCulture);

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
        OnPropertyChanged(nameof(IntervalText));
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
    // offer as its first one. A known unit this device does not offer (seconds on the phone)
    // and a number its list does not offer show as they are stored, and a stored schedule
    // faster than this device allows shows with the sentence that says so; nothing is
    // rewritten until the person changes the schedule.
    protected override void ReadFrom(WallpaperNexusSettings settings)
    {
        var slideshow = settings.Slideshow;
        ScheduleMode = slideshow.ScheduleMode;
        SelectedIntervalType = IntervalTypeChoices.FirstOrDefault(o => o.Type == slideshow.IntervalType)
            ?? IntervalTypeOptions.FirstOrDefault(o => o.Type == slideshow.IntervalType)
            ?? IntervalTypeChoices.FirstOrDefault(o => o.Type == IntervalType.Minutes)
            ?? IntervalTypeChoices[0];
        Interval = slideshow.Interval > 0 ? slideshow.Interval : DefaultInterval;
        CronExpression = slideshow.CronExpression;
        SelectedOrder = SlideshowOrderOptions.FirstOrDefault(o => o.Order == slideshow.Order) ?? DefaultOrder;
        SelectedFillStyle = FillStyleOptions.FirstOrDefault(f => f.Style == slideshow.FillStyle) ?? FillStyleOptions[0];
        _fillStyleChosen = false;
        Enabled = slideshow.Enabled;
        FavoritePriorityEnabled = slideshow.FavoritePriorityEnabled;
        FavoritePriorityWeight = slideshow.FavoritePriorityWeight > 0 ? slideshow.FavoritePriorityWeight : DefaultFavoriteWeight;

        // Held until the next line: the stored schedule stays too fast until it is changed
        if (Enabled && SlideshowSchedule.IsFasterThan(EffectiveCron(), _shortestGap))
            Report(TooFastSentence, StatusTone.Error, holds: true);
    }

    // In interval mode the expression the interval stands for is saved with it; in cron mode
    // the typed expression is checked first. While it is invalid, or changes faster than this
    // device allows, the schedule is not saved and the line says why; the group's other
    // settings (on or off, order, favorites, fill style) still are.
    protected override Action<WallpaperNexusSettings>? CaptureChange()
    {
        var cron = EffectiveCron();
        var scheduleMode = _scheduleMode;
        var refusal = ScheduleRefusal(scheduleMode, cron);
        var scheduleIsValid = refusal is null;
        if (refusal is not null)
            ReportRefusal(refusal);

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

    // Why the schedule cannot be saved, or null when it can: a typed expression that does not
    // parse, or a schedule that changes faster than this device allows.
    private string? ScheduleRefusal(SlideshowScheduleMode mode, string cron)
    {
        if (mode == SlideshowScheduleMode.CronExpression && !SlideshowSchedule.IsValidCron(cron))
            return InvalidCronSentence;
        if (SlideshowSchedule.IsFasterThan(cron, _shortestGap))
            return TooFastSentence;
        return null;
    }

    // The option for a unit this device offers: the desktop window's own option when the rule
    // is the desktop's, so both lists hold the same objects.
    private static IntervalTypeOption ChoiceFor(IntervalRule rule)
    {
        var desktopOption = IntervalTypeOptions.FirstOrDefault(o =>
            o.Type == rule.Type && o.Minimum == rule.Minimum && o.Maximum == rule.Maximum && !rule.HasFixedValues);
        return desktopOption ?? OptionFor(rule);
    }

    private static IntervalTypeOption OptionFor(IntervalRule rule) =>
        new(LabelOf(rule.Type), rule.Type, rule.Minimum, rule.Maximum, rule.FixedValues);

    private static string LabelOf(IntervalType type) => type switch
    {
        IntervalType.Seconds => "Seconds",
        IntervalType.Minutes => "Minutes",
        IntervalType.Hours => "Hours",
        IntervalType.Days => "Days",
        IntervalType.Weeks => "Weeks",
        IntervalType.Months => "Months",
        _ => "Years",
    };

    // The fixed value closest to value; the smaller one when two are as close.
    private static double Nearest(IReadOnlyList<int> values, double value)
    {
        var nearest = values.OrderBy(v => Math.Abs(v - value)).ThenBy(v => v).First();
        return nearest;
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

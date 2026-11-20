namespace PaperNexus;

// The desktop's answers for the settings that differ between devices: it draws the title on the
// wallpaper, and every fill style is offered, Span included, because the operating
// system fits the picture to one or more monitors itself.
internal sealed class DesktopSettingRules : IDeviceSettingRules, IAddSingleton<IDeviceSettingRules>
{
    private static readonly WallpaperFillStyle[] AllFillStyles =
    [
        WallpaperFillStyle.Fill,
        WallpaperFillStyle.Fit,
        WallpaperFillStyle.Stretch,
        WallpaperFillStyle.Tile,
        WallpaperFillStyle.Center,
        WallpaperFillStyle.Span,
    ];

    public bool DrawsWallpaperTitle => true;

    public IReadOnlyList<WallpaperFillStyle> FillStyles => AllFillStyles;

    // The desktop changes as often as it is told: any interval, seconds included, and any cron
    public TimeSpan ShortestSlideshowGap => TimeSpan.Zero;

    public IReadOnlyList<IntervalRule> IntervalTypes => SlideshowSchedule.IntervalRules;

    public bool OffersExactTiming => false;
}

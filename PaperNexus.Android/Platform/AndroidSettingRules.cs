using PaperNexus.Core;

namespace PaperNexus;

// The phone's answers for the settings that differ between devices (issue #203, steps 4 and 5): it
// never draws the title on the wallpaper (the launcher and the lock screen cover it, and the
// wallpaper cannot know where), and the fill styles are the five a single screen can show. Span stretches one picture across several monitors, which a phone does not have; a
// stored Span shows as Fill and draws as Fill.
internal sealed class AndroidSettingRules : IDeviceSettingRules, IAddSingleton<IDeviceSettingRules>
{
    private static readonly WallpaperFillStyle[] PhoneFillStyles =
    [
        WallpaperFillStyle.Fill,
        WallpaperFillStyle.Fit,
        WallpaperFillStyle.Stretch,
        WallpaperFillStyle.Tile,
        WallpaperFillStyle.Center,
    ];

    public bool DrawsWallpaperTitle => false;

    public IReadOnlyList<WallpaperFillStyle> FillStyles => PhoneFillStyles;

    // The phone changes the wallpaper at most every 15 minutes, with Exact timing on or off
    // (issue #203, step 5): Android's background scheduling runs work no more often than that.
    public TimeSpan ShortestSlideshowGap => FifteenMinutes;

    public IReadOnlyList<IntervalRule> IntervalTypes => PhoneIntervalTypes;

    // The "Exact timing" switch (issue #203, step 5): on the minute with Android's exact alarms
    public bool OffersExactTiming => true;

    private static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);

    // No seconds. Minutes are picked from 15, 20 and 30: the counts that divide the hour and are
    // at least 15, so "every N minutes" stays N minutes apart across the hour (the owner's
    // choice, issue #203 step 5). Hours and longer keep the desktop's ranges.
    private static readonly IntervalRule[] PhoneIntervalTypes =
    [
        new IntervalRule(IntervalType.Minutes, 15, 30, [15, 20, 30]),
        .. SlideshowSchedule.IntervalRules.Where(rule => rule.Type >= IntervalType.Hours),
    ];
}

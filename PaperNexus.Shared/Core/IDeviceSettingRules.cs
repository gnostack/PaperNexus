namespace PaperNexus.Core;

// What this device offers and starts with for the settings whose answer differs between
// devices. The host supplies it, so the settings model and the shared settings screens never
// ask which device they run on.
public interface IDeviceSettingRules
{
    // Whether this device draws the picture's title on the wallpaper at all; when it does not,
    // no title is drawn, whatever the settings say. The desktop does; the phone does not, because
    // its launcher's search bar and dock icons and its lock screen's clock and unlock icon cover
    // a title, and the wallpaper cannot know where they are.
    public bool DrawsWallpaperTitle { get; }

    // The fill styles this device can draw, in the order the picker lists them. A stored
    // style that is not here shows as the first one.
    public IReadOnlyList<WallpaperFillStyle> FillStyles { get; }

    // The shortest time this device allows between two slideshow changes. A schedule that can
    // change faster is refused when typed and shown with a sentence when stored. The desktop has
    // none (zero); the phone's is 15 minutes (issue #203, step 5).
    public TimeSpan ShortestSlideshowGap { get; }

    // The interval units this device offers, in the order the unit picker lists them, each with
    // the numbers it allows (SlideshowSchedule.IntervalRules has the desktop's). A stored unit
    // that is not here still shows as it is.
    public IReadOnlyList<IntervalRule> IntervalTypes { get; }

    // Whether this device has the "Exact timing" setting (SlideshowSettings.ExactTiming): the
    // phone, whose background slideshow is otherwise allowed to come late. The desktop's
    // slideshow runs on a timer in its own process and is always on time.
    public bool OffersExactTiming { get; }
}

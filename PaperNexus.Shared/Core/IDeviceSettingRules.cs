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
}

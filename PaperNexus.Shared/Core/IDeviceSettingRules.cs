namespace PaperNexus.Core;

// What this device offers and starts with for the settings whose answer differs between
// devices. The host supplies it, so the settings model and the shared settings screens never
// ask which device they run on.
public interface IDeviceSettingRules
{
    // Whether a new settings file, or one that lacks the field, shows the title on the
    // wallpaper. The desktop shows it; the phone does not.
    public bool AnnotateWallpaperByDefault { get; }

    // The fill styles this device can draw, in the order the picker lists them. A stored
    // style that is not here shows as the first one.
    public IReadOnlyList<WallpaperFillStyle> FillStyles { get; }
}

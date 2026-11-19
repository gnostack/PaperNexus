using PaperNexus.Core;

namespace PaperNexus;

// The phone's answers for the settings that differ between devices (issue #203, step 4): the
// title is off for a new settings file, and the fill styles are the five a single screen can
// show. Span stretches one picture across several monitors, which a phone does not have; a
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

    public bool AnnotateWallpaperByDefault => false;

    public IReadOnlyList<WallpaperFillStyle> FillStyles => PhoneFillStyles;
}

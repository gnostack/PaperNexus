using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BundledFonts = PaperNexus.Core.Imaging.BundledFonts;

namespace PaperNexus.ViewModels;

// The title drawn on the wallpaper, as both devices show it: whether it is drawn, and its font,
// size, colour, position and outline. A colour that spells a word plays its easter egg.
public sealed partial class AnnotationSettingsViewModel : SettingsGroupViewModel
{
    public const int FontSizeMinimum = 8;
    public const int FontSizeMaximum = 200;
    public const int FontSizeStep = 2;
    public const int DefaultFontSize = 18;
    public const string DefaultColor = "#F5F5F5";

    public static readonly IReadOnlyList<AnnotationPositionOption> AnnotationPositionOptions = new[]
    {
        new AnnotationPositionOption("Top Left",     "Show annotation in the top-left corner of the wallpaper",     AnnotationPosition.TopLeft),
        new AnnotationPositionOption("Top Right",    "Show annotation in the top-right corner of the wallpaper",    AnnotationPosition.TopRight),
        new AnnotationPositionOption("Bottom Left",  "Show annotation in the bottom-left corner of the wallpaper",  AnnotationPosition.BottomLeft),
        new AnnotationPositionOption("Bottom Right", "Show annotation in the bottom-right corner of the wallpaper", AnnotationPosition.BottomRight),
    };

    // The fonts the picker offers: the bundled fonts first, then every family installed on
    // this device that draws as itself, in alphabetical order.
    public static readonly IReadOnlyList<string> InstalledFontOptions = BuildFontOptions();

    private readonly EasterEggRequests _easterEggs;

    [ObservableProperty]
    private bool _annotateWallpaper = true;

    [ObservableProperty]
    private string _fontFamily = BundledFonts.DefaultFontFamily;

    [ObservableProperty]
    private int _fontSize = DefaultFontSize;

    [ObservableProperty]
    private string _color = DefaultColor;

    [ObservableProperty]
    private bool _outlineEnabled = true;

    [ObservableProperty]
    private AnnotationPositionOption _selectedPosition = AnnotationPositionOptions[0];

    public AnnotationSettingsViewModel(ISettingsStore settingsStore, EasterEggRequests easterEggs)
        : base(settingsStore)
    {
        _easterEggs = easterEggs.ThrowIfNull();
        FontFamilyOptions = new ObservableCollection<string>(InstalledFontOptions);
    }

    // The picker's fonts: the installed list, plus a stored font that is not in it (a family
    // removed from the device, or a name an older version listed), so the picker still shows
    // what is saved and the title still draws as it did.
    public ObservableCollection<string> FontFamilyOptions { get; }

    partial void OnAnnotateWallpaperChanged(bool value) => SaveSoon();

    partial void OnFontFamilyChanged(string value) => SaveSoon();

    partial void OnFontSizeChanged(int value) => SaveSoon();

    // A few hex values spell words and are a reasonable thing to try in a colour box. The egg
    // plays when the person types one, not when a stored one is loaded as the screen opens.
    partial void OnColorChanged(string value)
    {
        SaveSoon();
        if (IsLoading)
            return;
        var show = EasterEggShows.MagicColor(value);
        if (show is not null)
            _easterEggs.Request(show);
    }

    partial void OnOutlineEnabledChanged(bool value) => SaveSoon();

    partial void OnSelectedPositionChanged(AnnotationPositionOption value) => SaveSoon();

    // An unknown stored position shows as Top Left.
    protected override void ReadFrom(WallpaperNexusSettings settings)
    {
        var annotation = settings.Annotation;
        AnnotateWallpaper = settings.AnnotateWallpaper;
        var storedFont = annotation.FontFamily;
        if (!FontFamilyOptions.Contains(storedFont, StringComparer.OrdinalIgnoreCase))
            FontFamilyOptions.Add(storedFont);
        FontFamily = FontFamilyOptions.First(name => string.Equals(name, storedFont, StringComparison.OrdinalIgnoreCase));
        FontSize = annotation.FontSize;
        Color = annotation.Color;
        OutlineEnabled = annotation.OutlineEnabled;
        SelectedPosition = AnnotationPositionOptions.FirstOrDefault(p => p.Position == annotation.Position)
            ?? AnnotationPositionOptions[0];
    }

    protected override Action<WallpaperNexusSettings>? CaptureChange()
    {
        var annotateWallpaper = AnnotateWallpaper;
        var fontFamily = FontFamily;
        var fontSize = FontSize;
        var color = Color;
        var outlineEnabled = OutlineEnabled;
        var position = (SelectedPosition ?? AnnotationPositionOptions[0]).Position;
        return latest =>
        {
            latest.AnnotateWallpaper = annotateWallpaper;
            latest.Annotation.FontFamily = fontFamily;
            latest.Annotation.FontSize = fontSize;
            latest.Annotation.Color = color;
            latest.Annotation.OutlineEnabled = outlineEnabled;
            latest.Annotation.Position = position;
        };
    }

    // Builds the font picker list: bundled fonts first, then every font family installed on
    // this machine.
    //
    // This used to probe a hardcoded list of eleven Windows font names (Arial, Segoe UI,
    // MS Gothic, and so on) and keep the ones that resolved. On Linux none of those exist,
    // so the picker collapsed to the single bundled family and the user could not choose a
    // font at all. Enumerating the installed families instead means the list reflects the
    // machine rather than a guess about which fonts a machine ought to have.
    private static List<string> BuildFontOptions()
    {
        var fonts = new List<string>(BundledFonts.Names);

        var systemFamilies = BundledFonts.InstalledFamilyNames()
            .Where(name => !fonts.Contains(name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);

        fonts.AddRange(systemFamilies);
        return fonts;
    }
}

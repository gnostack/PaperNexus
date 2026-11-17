using SkiaSharp;

namespace PaperNexus.Core.Imaging;

// Resolves the typeface used for the wallpaper annotation and lists the families the font
// picker offers.
//
// Cinzel ships inside the assembly so the default annotation renders identically on every
// machine, whatever fonts happen to be installed. Every other family comes from the
// operating system through Skia's font manager (fontconfig on Linux, DirectWrite on
// Windows), so the picker reflects the actual machine rather than a guessed list of names.
internal static class BundledFonts
{
    public const string DefaultFontFamily = "Cinzel";

    private const string CinzelResourceName = "PaperNexus.Cinzel.ttf";

    /// <summary>
    /// Bundled font names available regardless of system-installed fonts.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = [DefaultFontFamily];

    // Every font family installed on this machine, as the operating system names them.
    public static IReadOnlyList<string> InstalledFamilyNames()
    {
        using var fontManager = SKFontManager.CreateDefault();
        var families = fontManager.FontFamilies
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return families;
    }

    // Returns a typeface for familyName that the caller owns and must dispose.
    //
    // A bundled family is loaded from the embedded font file. Any other name is matched
    // against the installed fonts; a name that matches nothing falls back to the bundled
    // default rather than to whatever face the platform substitutes, so a stale or mistyped
    // setting still produces the familiar Cinzel title.
    internal static SKTypeface ResolveTypeface(string familyName)
    {
        var isBundled = Names.Contains(familyName, StringComparer.OrdinalIgnoreCase);
        if (!isBundled)
        {
            var installed = MatchInstalledFamily(familyName);
            if (installed is not null)
                return installed;
        }

        return LoadCinzel();
    }

    // Font managers return the closest face they have for any request, so the match is
    // only accepted when the family it resolved to is the one that was asked for.
    private static SKTypeface? MatchInstalledFamily(string familyName)
    {
        using var fontManager = SKFontManager.CreateDefault();
        var typeface = fontManager.MatchFamily(familyName);
        if (typeface is null)
            return null;

        var isRequestedFamily = string.Equals(typeface.FamilyName, familyName, StringComparison.OrdinalIgnoreCase);
        if (isRequestedFamily)
            return typeface;

        typeface.Dispose();
        return null;
    }

    // Loads a fresh Cinzel typeface from the embedded resource. The font bytes are copied
    // into an SKData so the typeface does not depend on the resource stream staying open.
    private static SKTypeface LoadCinzel()
    {
        var assembly = typeof(BundledFonts).Assembly;
        using var stream = assembly.GetManifestResourceStream(CinzelResourceName)
            ?? throw new InvalidOperationException($"Embedded font '{CinzelResourceName}' is missing.");
        using var data = SKData.Create(stream);
        var typeface = SKTypeface.FromData(data)
            ?? throw new InvalidOperationException($"Embedded font '{CinzelResourceName}' could not be read.");
        return typeface;
    }
}

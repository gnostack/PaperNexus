using PaperNexus.Core.Imaging;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// Covers the two annotation defects: an outline too thin to see, and a font picker that
// listed fonts the machine does not have instead of the ones it does.
public class AnnotationTests
{
    [Fact]
    public void OutlineWidth_IsNeverSubPixel()
    {
        // The original fontSize/36 produced 0.5px at the default 18pt, which antialiased
        // away to nothing - the reason small text appeared to have no outline.
        for (var fontSize = 1; fontSize <= 200; fontSize++)
            Assert.True(WallpaperAnnotator.OutlineWidth(fontSize) >= 1f,
                $"outline at {fontSize}pt would be sub-pixel");
    }

    [Fact]
    public void OutlineWidth_IsVisibleAtTheDefaultFontSize()
    {
        // 18pt is the shipped default and the size the bug was reported at.
        Assert.Equal(1.5f, WallpaperAnnotator.OutlineWidth(18));
    }

    [Fact]
    public void OutlineWidth_GrowsWithTheFont()
    {
        // A fixed width would look heavy on small text and vanish on large text.
        Assert.True(WallpaperAnnotator.OutlineWidth(200) > WallpaperAnnotator.OutlineWidth(72));
        Assert.True(WallpaperAnnotator.OutlineWidth(72) > WallpaperAnnotator.OutlineWidth(18));
    }

    [Fact]
    public void OutlineWidth_StaysLightEnoughToLeaveLetterformsOpen()
    {
        // 1/6 of the font size closed up adjacent glyphs when rendered; stay well under it.
        Assert.True(WallpaperAnnotator.OutlineWidth(72) < 72 / 6f);
    }

    [Fact]
    public void FontFamilyOptions_AlwaysOffersTheBundledFont()
    {
        // Cinzel ships with the app, so the picker is never empty even with no system fonts.
        Assert.Contains("Cinzel", WallpaperConfigViewModel.FontFamilyOptions);
    }

    [Fact]
    public void FontFamilyOptions_ListsFontsInstalledOnThisMachine()
    {
        // The picker used to probe a hardcoded list of Windows font names and keep whichever
        // resolved. On Linux none of them exist, so it collapsed to the single bundled family
        // and the user could not pick a font at all. It must reflect the actual machine.
        var installed = BundledFonts.InstalledFamilyNames();
        if (installed.Count == 0)
            return; // a machine with no system fonts legitimately offers only the bundled one

        Assert.True(WallpaperConfigViewModel.FontFamilyOptions.Count > 1);
        Assert.Contains(WallpaperConfigViewModel.FontFamilyOptions, option => installed.Contains(option));
    }

    [Fact]
    public void FontFamilyOptions_ContainsNoDuplicates()
    {
        // A bundled family that is also installed system-wide must not appear twice.
        var options = WallpaperConfigViewModel.FontFamilyOptions;
        var distinct = options.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(options.Count, distinct);
    }
}

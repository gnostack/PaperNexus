using PaperNexus.Core;
using PaperNexus.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace PaperNexus.Tests;

// The title on a desktop wallpaper is laid out inside the picture less the two side margins: it
// wraps at spaces onto as many lines as it needs, so a long title is never cut off at the side.
public class TitleLayoutTests : IDisposable
{
    private const string LongTitle = "Washington State Ferry at sunset, Mukilteo-Clinton passage, Puget Sound, United States";
    private const float Margin = WallpaperAnnotator.SideMargin;
    private const int Width = 1920;
    private const int Height = 1080;
    private readonly string _directory;

    public TitleLayoutTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_Title_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    public static TheoryData<AnnotationPosition> Positions() => new(Enum.GetValues<AnnotationPosition>());

    // At size 40 the long title is 1888 px, wider than the 1670 px between the margins.
    [Theory]
    [MemberData(nameof(Positions))]
    public void Layout_ALongTitleWrapsInsideTheMargins(AnnotationPosition position)
    {
        const int size = 40;

        var layout = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, size, LongTitle, Width, Height, position);

        // Every word is kept, in order, and no line is wider than the space between the margins
        Assert.True(layout.Lines.Count > 1);
        Assert.Equal(LongTitle, string.Join(" ", layout.Lines.Select(l => l.Text)));
        var rightAligned = position is AnnotationPosition.TopRight or AnnotationPosition.BottomRight;
        foreach (var line in layout.Lines)
        {
            Assert.True(line.Left >= Margin - 0.5f && line.Left + line.Width <= Width - Margin + 0.5f, $"'{line.Text}' spans {line.Left}..{line.Left + line.Width}");
            var edge = rightAligned ? line.Left + line.Width : line.Left;
            Assert.Equal(rightAligned ? Width - Margin : Margin, edge, 0.5f);
        }

        // Top positions go down from the top margin; bottom positions keep the last line's margin
        var isTop = position is AnnotationPosition.TopLeft or AnnotationPosition.TopRight;
        if (isTop)
            Assert.Equal(5f, layout.Lines[0].Top, 0.01f);
        else
            Assert.Equal(Height - size - 10f, layout.Lines[^1].Top, 0.01f);
        for (var i = 1; i < layout.Lines.Count; i++)
            Assert.Equal(layout.Lines[i - 1].Top + layout.LineAdvance, layout.Lines[i].Top, 0.01f);
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void Render_ALongTitleIsDrawnWhollyOnThePicture(AnnotationPosition position)
    {
        var background = new SKColor(40, 60, 80);
        var picturePath = Path.Combine(_directory, $"long-{position}.png");
        TestHelpers.CreateTestPng(picturePath, Width, Height, background.Red, background.Green, background.Blue);
        var request = new AnnotationRequest(LongTitle, null, BundledFonts.DefaultFontFamily, 40, "#F5F5F5", position, OutlineEnabled: true);

        using var encoded = WallpaperRenderer.Render(picturePath, request);
        using var rendered = SKBitmap.Decode(encoded.Data.ToArray());

        var b = ChangedBounds(rendered, background)!.Value;
        // The outline and the glyphs' side bearings may reach a few pixels past the text's box
        Assert.True(b.Left >= Margin - 8 && b.Right <= Width - Margin + 8, $"title spans x {b.Left}..{b.Right}");
        Assert.True(b.Top >= 0 && b.Bottom < Height, $"title spans y {b.Top}..{b.Bottom}");
    }

    [Fact]
    public void Layout_ATitleThatFitsIsOneLineWhereItAlwaysWas()
    {
        var topLeft = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, 32, "A Title", 800, 400, AnnotationPosition.TopLeft);
        var bottomRight = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, 32, "A Title", 800, 400, AnnotationPosition.BottomRight);

        var line = Assert.Single(topLeft.Lines);
        Assert.Equal(Margin, line.Left);
        Assert.Equal(5f, line.Top);
        var right = Assert.Single(bottomRight.Lines);
        Assert.Equal(800 - Margin, right.Left + right.Width, 0.01f);
        Assert.Equal(400 - 32 - 10f, right.Top);
    }

    [Fact]
    public void Layout_AWordWiderThanThePictureGetsItsOwnLineAtTheLeftMargin()
    {
        var word = "Supercalifragilisticexpialidocious-Pneumonoultramicroscopicsilicovolcanoconiosis";

        var layout = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, 80, $"Before {word} after", Width, Height, AnnotationPosition.TopRight);

        Assert.Equal(["Before", word, "after"], layout.Lines.Select(l => l.Text));
        var wide = layout.Lines[1];
        Assert.True(wide.Width > Width - 2 * Margin);
        Assert.Equal(Margin, wide.Left);
    }

    private static SKRectI? ChangedBounds(SKBitmap bitmap, SKColor background)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == background)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? null : new SKRectI(left, top, right, bottom);
    }
}

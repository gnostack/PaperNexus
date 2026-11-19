using PaperNexus.Core;
using PaperNexus.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace PaperNexus.Tests;

// The title on the wallpaper is laid out inside the title area less the two side margins: it
// wraps at spaces onto as many lines as it needs, so it is never cut off, at every position and
// at the phone's text scale as well as the desktop's.
public class TitleLayoutTests : IDisposable
{
    private const string LongTitle = "Washington State Ferry at sunset, Mukilteo-Clinton passage, Puget Sound, United States";
    private const float Margin = WallpaperAnnotator.SideMargin;
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

    // The emulator phone's canvas: 1188 x 2640, title area (54, 256) to (1134, 2394)
    private static readonly WallpaperCanvas Phone = WallpaperCanvas.ForScreen(1080, 2400, 1.1f, 136, 126);

    private static SKRectI TitleArea(WallpaperCanvas canvas) =>
        new(canvas.TitleLeft, canvas.TitleTop, canvas.TitleRight, canvas.TitleBottom);

    public static TheoryData<AnnotationPosition, float> PositionsAndScales()
    {
        var data = new TheoryData<AnnotationPosition, float>();
        foreach (var position in Enum.GetValues<AnnotationPosition>())
        {
            data.Add(position, 1f);
            data.Add(position, 2.625f);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PositionsAndScales))]
    public void Layout_ALongTitleWrapsInsideTheMargins(AnnotationPosition position, float scale)
    {
        var area = TitleArea(Phone);
        var size = (int)Math.Round(18 * scale);

        var layout = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, size, LongTitle, area, position);

        // Every word is kept, in order, and no line is wider than the space between the margins
        Assert.Equal(LongTitle, string.Join(" ", layout.Lines.Select(l => l.Text)));
        foreach (var line in layout.Lines)
        {
            Assert.True(line.Left >= area.Left + Margin - 0.5f, $"'{line.Text}' starts at {line.Left}");
            Assert.True(line.Left + line.Width <= area.Right - Margin + 0.5f, $"'{line.Text}' ends at {line.Left + line.Width}");
        }

        var rightAligned = position is AnnotationPosition.TopRight or AnnotationPosition.BottomRight;
        foreach (var line in layout.Lines)
        {
            var edge = rightAligned ? line.Left + line.Width : line.Left;
            var expected = rightAligned ? area.Right - Margin : area.Left + Margin;
            Assert.Equal(expected, edge, 0.5f);
        }

        // Top positions go down from the top margin; bottom positions keep the last line's margin
        var isTop = position is AnnotationPosition.TopLeft or AnnotationPosition.TopRight;
        if (isTop)
            Assert.Equal(area.Top + 5f, layout.Lines[0].Top, 0.01f);
        else
            Assert.Equal(area.Bottom - size - 10f, layout.Lines[^1].Top, 0.01f);
        for (var i = 1; i < layout.Lines.Count; i++)
            Assert.Equal(layout.Lines[i - 1].Top + layout.LineAdvance, layout.Lines[i].Top, 0.01f);

        // At the phone's scale the title needs more than one line
        if (scale > 1)
            Assert.True(layout.Lines.Count > 1);
    }

    [Theory]
    [MemberData(nameof(PositionsAndScales))]
    public void Render_ALongTitleIsDrawnWhollyInsideTheTitleArea(AnnotationPosition position, float scale)
    {
        var background = new SKColor(40, 60, 80);
        var picturePath = Path.Combine(_directory, $"long-{position}-{scale}.png");
        TestHelpers.CreateTestPng(picturePath, 1920, 1080, background.Red, background.Green, background.Blue);
        var canvas = Phone with { TextScale = scale };
        var request = new AnnotationRequest(LongTitle, null, BundledFonts.DefaultFontFamily, 18, "#F5F5F5", position, OutlineEnabled: true);

        using var encoded = WallpaperRenderer.Render(picturePath, request, canvas, WallpaperFillStyle.Fill);
        using var rendered = SKBitmap.Decode(encoded.Data.ToArray());

        var bounds = ChangedBounds(rendered, background);
        Assert.NotNull(bounds);
        var b = bounds.Value;
        // The outline and the glyphs' side bearings may reach a few pixels past the text's box
        Assert.True(b.Left >= canvas.TitleLeft + Margin - 8 && b.Right <= canvas.TitleRight - Margin + 8, $"title spans x {b.Left}..{b.Right}");
        Assert.True(b.Top >= canvas.TitleTop && b.Bottom < canvas.TitleBottom, $"title spans y {b.Top}..{b.Bottom}");
    }

    [Fact]
    public void Layout_ATitleThatFitsIsOneLineWhereItAlwaysWas()
    {
        var area = new SKRectI(0, 0, 800, 400);

        var topLeft = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, 32, "A Title", area, AnnotationPosition.TopLeft);
        var bottomRight = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, 32, "A Title", area, AnnotationPosition.BottomRight);

        var line = Assert.Single(topLeft.Lines);
        Assert.Equal(Margin, line.Left);
        Assert.Equal(5f, line.Top);
        var right = Assert.Single(bottomRight.Lines);
        Assert.Equal(800 - Margin, right.Left + right.Width, 0.01f);
        Assert.Equal(400 - 32 - 10f, right.Top);
    }

    [Fact]
    public void Layout_AWordWiderThanTheAreaGetsItsOwnLineAtTheLeftMargin()
    {
        var area = TitleArea(Phone);
        var word = "Supercalifragilisticexpialidocious-Pneumonoultramicroscopicsilicovolcanoconiosis";

        var layout = WallpaperAnnotator.Layout(BundledFonts.DefaultFontFamily, 47, $"Before {word} after", area, AnnotationPosition.TopRight);

        Assert.Equal(["Before", word, "after"], layout.Lines.Select(l => l.Text));
        var wide = layout.Lines[1];
        Assert.True(wide.Width > area.Width - 2 * Margin);
        Assert.Equal(area.Left + Margin, wide.Left);
    }

    [Fact]
    public void Render_AWordWiderThanTheAreaIsCutAtTheAreasEdge()
    {
        var background = new SKColor(40, 60, 80);
        var picturePath = Path.Combine(_directory, "word.png");
        TestHelpers.CreateTestPng(picturePath, 1920, 1080, background.Red, background.Green, background.Blue);
        var canvas = Phone with { TextScale = 2.625f };
        var request = new AnnotationRequest("Pneumonoultramicroscopicsilicovolcanoconiosis", null, BundledFonts.DefaultFontFamily, 18, "#F5F5F5", AnnotationPosition.TopLeft, OutlineEnabled: true);

        using var encoded = WallpaperRenderer.Render(picturePath, request, canvas, WallpaperFillStyle.Fill);
        using var rendered = SKBitmap.Decode(encoded.Data.ToArray());

        var b = ChangedBounds(rendered, background)!.Value;
        Assert.True(b.Right < canvas.TitleRight, $"word reaches x {b.Right}");
        Assert.True(b.Right >= canvas.TitleRight - 3, "the word runs to the edge of the area");
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

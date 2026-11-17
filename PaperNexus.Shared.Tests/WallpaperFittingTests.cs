using PaperNexus.Core;
using PaperNexus.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace PaperNexus.Tests;

// The phone's wallpaper: the canvas for a screen, where each fill style puts the picture on
// it, and the title drawn inside the part of the canvas the screen shows.
public class WallpaperFittingTests : IDisposable
{
    private readonly string _directory;

    public WallpaperFittingTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_Fit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        catch { }
    }

    // --- Canvas for a screen ---

    // The emulator measured in step 3: a 1080x2400 screen, wallpaper zoom 1.1, a 136 px status
    // bar and a 126 px navigation bar. The canvas is 1.1 times the screen and the title area is
    // the screen-sized middle without the bars.
    [Fact]
    public void ForScreen_EmulatorPhone_IsTheZoomedScreenWithTheBarsLeftOut()
    {
        var canvas = WallpaperCanvas.ForScreen(1080, 2400, 1.1f, 136, 126);

        Assert.Equal(new WallpaperCanvas(1188, 2640, 54, 256, 1134, 2394), canvas);
    }

    [Fact]
    public void ForScreen_NoZoom_IsTheScreenItself()
    {
        var canvas = WallpaperCanvas.ForScreen(1080, 2400, 1f, 0, 0);

        Assert.Equal(new WallpaperCanvas(1080, 2400, 0, 0, 1080, 2400), canvas);
    }

    // A value below 1 would leave part of the screen without picture; it is treated as none.
    [Fact]
    public void ForScreen_ZoomBelowOne_IsTreatedAsNone()
    {
        var canvas = WallpaperCanvas.ForScreen(1080, 2400, 0.5f, 0, 0);

        Assert.Equal(1080, canvas.Width);
        Assert.Equal(2400, canvas.Height);
    }

    // --- Placement for each fill style ---

    // A wide 4K picture on the emulator's tall canvas.
    [Theory]
    [InlineData(WallpaperFillStyle.Fill, -1752.667, 0, 2940.667, 2640)]
    [InlineData(WallpaperFillStyle.Span, -1752.667, 0, 2940.667, 2640)]
    [InlineData(WallpaperFillStyle.Fit, 0, 985.875, 1188, 1654.125)]
    [InlineData(WallpaperFillStyle.Stretch, 0, 0, 1188, 2640)]
    [InlineData(WallpaperFillStyle.Tile, 0, 0, 3840, 2160)]
    [InlineData(WallpaperFillStyle.Center, -1326, 240, 2514, 2400)]
    public void Placement_WidePictureOnATallCanvas(WallpaperFillStyle style, double left, double top, double right, double bottom)
    {
        var placement = WallpaperFitting.Placement(3840, 2160, 1188, 2640, style);

        Assert.Equal(left, placement.Left, 0.01);
        Assert.Equal(top, placement.Top, 0.01);
        Assert.Equal(right, placement.Right, 0.01);
        Assert.Equal(bottom, placement.Bottom, 0.01);
    }

    // Span has nothing to span on one screen, so it draws exactly as Fill.
    [Fact]
    public void Compose_Span_DrawsTheSameAsFill()
    {
        using var picture = CreateQuarters(300, 100);
        using var fill = WallpaperFitting.Compose(picture, 120, 200, WallpaperFillStyle.Fill);
        using var span = WallpaperFitting.Compose(picture, 120, 200, WallpaperFillStyle.Span);

        Assert.Equal(fill.Bytes, span.Bytes);
    }

    // Fit leaves black bars above and below a wide picture; Fill covers the canvas.
    [Fact]
    public void Compose_FitLeavesBlackBars_FillCoversTheCanvas()
    {
        using var picture = TestHelpers.CreateSolidBitmap(300, 100, new SKColor(200, 30, 30));
        using var fit = WallpaperFitting.Compose(picture, 120, 200, WallpaperFillStyle.Fit);
        using var fill = WallpaperFitting.Compose(picture, 120, 200, WallpaperFillStyle.Fill);

        Assert.Equal(SKColors.Black, fit.GetPixel(60, 5));
        Assert.Equal(SKColors.Black, fit.GetPixel(60, 195));
        Assert.Equal(new SKColor(200, 30, 30), fit.GetPixel(60, 100));
        Assert.Equal(new SKColor(200, 30, 30), fill.GetPixel(60, 5));
        Assert.Equal(new SKColor(200, 30, 30), fill.GetPixel(60, 195));
    }

    // Tile repeats the picture at its own size from the top-left corner.
    [Fact]
    public void Compose_Tile_RepeatsThePictureFromTheTopLeft()
    {
        using var picture = CreateQuarters(40, 40);
        using var tiled = WallpaperFitting.Compose(picture, 100, 100, WallpaperFillStyle.Tile);

        // The top-left quarter of the picture appears at the start of every tile
        Assert.Equal(picture.GetPixel(5, 5), tiled.GetPixel(5, 5));
        Assert.Equal(picture.GetPixel(5, 5), tiled.GetPixel(45, 45));
        Assert.Equal(picture.GetPixel(35, 35), tiled.GetPixel(75, 75));
    }

    // Center keeps the picture's own pixels, centred, with black around a small picture.
    [Fact]
    public void Compose_Center_KeepsThePictureSizeWithBlackAround()
    {
        using var picture = TestHelpers.CreateSolidBitmap(40, 40, new SKColor(10, 200, 10));
        using var centred = WallpaperFitting.Compose(picture, 100, 100, WallpaperFillStyle.Center);

        Assert.Equal(SKColors.Black, centred.GetPixel(25, 50));
        Assert.Equal(new SKColor(10, 200, 10), centred.GetPixel(35, 50));
        Assert.Equal(new SKColor(10, 200, 10), centred.GetPixel(64, 50));
        Assert.Equal(SKColors.Black, centred.GetPixel(75, 50));
    }

    // --- Rendering for a canvas ---

    // With a canvas the processed wallpaper has the canvas's size, and every pixel of the title
    // lies inside the title area: below the status bar, above the navigation bar, and inside the
    // part the zoomed screen shows.
    [Theory]
    [InlineData(AnnotationPosition.TopLeft)]
    [InlineData(AnnotationPosition.TopRight)]
    [InlineData(AnnotationPosition.BottomLeft)]
    [InlineData(AnnotationPosition.BottomRight)]
    public void Render_WithACanvas_DrawsTheTitleInsideTheTitleArea(AnnotationPosition position)
    {
        var background = new SKColor(40, 60, 80);
        var picturePath = Path.Combine(_directory, "wide.png");
        TestHelpers.CreateTestPng(picturePath, 1920, 1080, background.Red, background.Green, background.Blue);
        var canvas = WallpaperCanvas.ForScreen(540, 1200, 1.1f, 68, 63);
        var request = new AnnotationRequest("A Title", null, BundledFonts.DefaultFontFamily, 18, "#F5F5F5", position, OutlineEnabled: true);

        using var encoded = WallpaperRenderer.Render(picturePath, request, canvas, WallpaperFillStyle.Fill);
        using var rendered = SKBitmap.Decode(encoded.Data.ToArray());

        Assert.Equal(canvas.Width, rendered.Width);
        Assert.Equal(canvas.Height, rendered.Height);
        var changed = ChangedBounds(rendered, background);
        Assert.NotNull(changed);
        var bounds = changed.Value;
        Assert.True(bounds.Left >= canvas.TitleLeft && bounds.Right < canvas.TitleRight, $"title spans x {bounds.Left}..{bounds.Right}");
        Assert.True(bounds.Top >= canvas.TitleTop && bounds.Bottom < canvas.TitleBottom, $"title spans y {bounds.Top}..{bounds.Bottom}");
    }

    // Without a canvas (the desktop) the picture keeps its size and the title sits against its
    // own edges, exactly as before canvases existed.
    [Fact]
    public void Render_WithoutACanvas_KeepsThePictureAndDrawsAgainstItsEdges()
    {
        var background = new SKColor(40, 60, 80);
        var picturePath = Path.Combine(_directory, "desktop.png");
        TestHelpers.CreateTestPng(picturePath, 800, 400, background.Red, background.Green, background.Blue);
        var request = new AnnotationRequest("A Title", null, BundledFonts.DefaultFontFamily, 32, "#F5F5F5", AnnotationPosition.TopLeft, OutlineEnabled: true);
        using var expected = TestHelpers.CreateSolidBitmap(800, 400, background);
        WallpaperAnnotator.Draw(expected, request);

        using var encoded = WallpaperRenderer.Render(picturePath, request);
        using var rendered = SKBitmap.Decode(encoded.Data.ToArray());

        Assert.Equal(800, rendered.Width);
        Assert.Equal(400, rendered.Height);
        Assert.Equal(ChangedBounds(expected, background), ChangedBounds(rendered, background));
    }

    // A picture whose four quarters have different colours, so a placement can be told apart.
    private static SKBitmap CreateQuarters(int width, int height)
    {
        var bitmap = TestHelpers.CreateSolidBitmap(width, height, SKColors.Red);
        using var canvas = new SKCanvas(bitmap);
        using var green = new SKPaint { Color = SKColors.Lime };
        using var blue = new SKPaint { Color = SKColors.Blue };
        using var white = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(width / 2, 0, width - width / 2, height / 2, green);
        canvas.DrawRect(0, height / 2, width / 2, height - height / 2, blue);
        canvas.DrawRect(width / 2, height / 2, width - width / 2, height - height / 2, white);
        canvas.Flush();
        return bitmap;
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

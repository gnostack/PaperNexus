using PaperNexus.Core;
using PaperNexus.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace PaperNexus.Tests;

// Covers the imaging layer in Core/Imaging. Everything runs on in-memory bitmaps or
// temporary files; nothing calls into the desktop or any OS API.
public class ImagingTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PaperNexus.ImagingTests." + Guid.NewGuid().ToString("N"));

    public ImagingTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    // --- Resize to fit ---

    [Theory]
    [InlineData(400, 300, 200, 200, 200, 150)] // width-constrained
    [InlineData(300, 400, 200, 200, 150, 200)] // height-constrained
    [InlineData(3840, 2160, 1920, 1080, 1920, 1080)] // exact 16:9 halving
    [InlineData(5000, 1000, 1920, 1080, 1920, 384)] // very wide panorama
    public void FitWithin_PreservesAspectRatioInsideTheBox(int width, int height, int maxWidth, int maxHeight, int expectedWidth, int expectedHeight)
    {
        var fitted = ImageResizing.FitWithin(width, height, maxWidth, maxHeight);

        Assert.Equal((expectedWidth, expectedHeight), fitted);
    }

    [Fact]
    public void ResizeFileToFit_WritesTheFittedSizeAndKeepsJpegFormat()
    {
        var path = Path.Combine(_tempDir, "wide.jpg");
        using (var source = TestHelpers.CreateSolidBitmap(800, 400, SKColors.SteelBlue))
        using (var file = File.Create(path))
            source.Encode(file, SKEncodedImageFormat.Jpeg, 90);

        var resized = ImageResizing.ResizeFileToFit(path, 300, 300);

        Assert.True(resized);
        Assert.Equal((300, 150), TestHelpers.ReadDimensions(path));
        using var codec = SKCodec.Create(path);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
    }

    [Fact]
    public void ResizeFileToFit_ImageInsideTheBox_IsNotRewritten()
    {
        var path = Path.Combine(_tempDir, "small.png");
        TestHelpers.CreateTestPng(path, 100, 50);
        var before = File.ReadAllBytes(path);

        var resized = ImageResizing.ResizeFileToFit(path, 300, 300);

        Assert.False(resized);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // --- Thumbnails and icons ---

    [Theory]
    [InlineData(1200, 800, 400)] // downscale
    [InlineData(300, 150, 300)] // small images are scaled up to the same width
    public void CreateThumbnailPng_Is600WideWithProportionalHeight(int width, int height, int expectedHeight)
    {
        var path = Path.Combine(_tempDir, "thumb-source.png");
        TestHelpers.CreateTestPng(path, width, height);

        var png = ImageResizing.CreateThumbnailPng(path);

        Assert.Equal((ImageResizing.ThumbnailWidth, expectedHeight), TestHelpers.ReadDimensions(png));
    }

    [Fact]
    public void ResizeToSquarePng_ProducesTheRequestedIconSize()
    {
        using var source = TestHelpers.CreateSolidBitmap(512, 512, SKColors.Orange);
        using var encoded = new MemoryStream();
        source.Encode(encoded, SKEncodedImageFormat.Png, 100);
        encoded.Position = 0;

        var png = ImageResizing.ResizeToSquarePng(encoded, 32);

        Assert.Equal((32, 32), TestHelpers.ReadDimensions(png));
    }

    [Fact]
    public void MenuIcons_AreSixteenPixelsSquare()
    {
        byte[][] icons = [MenuIcons.Gear(), MenuIcons.Play(), MenuIcons.Dice(), MenuIcons.Power()];

        foreach (var icon in icons)
            Assert.Equal((16, 16), TestHelpers.ReadDimensions(icon));
    }

    // --- Wallpaper encoding ---

    [Fact]
    public void Encode_UnderTheCeiling_WritesAn8BitRgbPng()
    {
        using var bitmap = TestHelpers.CreateSolidBitmap(64, 64, SKColors.Teal);

        using var encoded = WallpaperRenderer.Encode(bitmap);

        Assert.Equal(WallpaperFileFormat.Png, encoded.Format);
        var bytes = encoded.Data.ToArray();
        // IHDR starts at byte 8; bit depth is at offset 24 and colour type at 25 (2 = RGB, no alpha)
        Assert.Equal(8, bytes[24]);
        Assert.Equal(2, bytes[25]);
    }

    [Fact]
    public void Encode_SourceWithAlpha_StillWritesRgbWithoutAlpha()
    {
        var info = new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        bitmap.Erase(new SKColor(255, 0, 0, 128));

        using var encoded = WallpaperRenderer.Encode(bitmap);

        var bytes = encoded.Data.ToArray();
        Assert.Equal(WallpaperFileFormat.Png, encoded.Format);
        Assert.Equal(2, bytes[25]);
    }

    [Fact]
    public void Encode_PngOverTheCeiling_FallsBackToJpegThatFits()
    {
        // Random noise does not compress, so its 128x128 PNG is ~48 KB; a 20 KB ceiling
        // forces the JPEG path without needing a real 16 MB image.
        using var bitmap = TestHelpers.CreateNoiseBitmap(128, 128, seed: 7);
        const long ceiling = 20_000;

        using var encoded = WallpaperRenderer.Encode(bitmap, ceiling);

        Assert.Equal(WallpaperFileFormat.Jpeg, encoded.Format);
        Assert.True(encoded.Data.Length <= ceiling, $"JPEG was {encoded.Data.Length} bytes");
        Assert.Equal(0, encoded.Data.Position);
        var bytes = encoded.Data.ToArray();
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
    }

    // --- Annotation ---

    [Theory]
    [InlineData(AnnotationPosition.TopLeft)]
    [InlineData(AnnotationPosition.TopRight)]
    [InlineData(AnnotationPosition.BottomLeft)]
    [InlineData(AnnotationPosition.BottomRight)]
    public void Draw_ChangesPixelsOnlyInTheConfiguredCorner(AnnotationPosition position)
    {
        const int width = 800;
        const int height = 400;
        var background = new SKColor(40, 60, 80);
        using var bitmap = TestHelpers.CreateSolidBitmap(width, height, background);
        var request = new AnnotationRequest("Test Title", null, BundledFonts.DefaultFontFamily, 32, "#F5F5F5", position, OutlineEnabled: true);

        WallpaperAnnotator.Draw(bitmap, request);

        var changed = ChangedBounds(bitmap, background);
        Assert.NotNull(changed);
        var bounds = changed.Value;
        var isLeft = position is AnnotationPosition.TopLeft or AnnotationPosition.BottomLeft;
        var isTop = position is AnnotationPosition.TopLeft or AnnotationPosition.TopRight;
        // Every changed pixel lies in the configured quarter of the image
        if (isLeft)
            Assert.True(bounds.Right < width / 2, $"text reaches x={bounds.Right}");
        else
            Assert.True(bounds.Left >= width / 2, $"text starts at x={bounds.Left}");
        if (isTop)
            Assert.True(bounds.Bottom < height / 2, $"text reaches y={bounds.Bottom}");
        else
            Assert.True(bounds.Top >= height / 2, $"text starts at y={bounds.Top}");
        // Text sits against the 125px side margin, never on the image edge
        if (isLeft)
            Assert.True(bounds.Left >= 115, $"text starts at x={bounds.Left}");
        else
            Assert.True(bounds.Right <= width - 115, $"text reaches x={bounds.Right}");
    }

    [Fact]
    public void Draw_OutlineAddsPixelsOfTheContrastingColour()
    {
        var background = new SKColor(128, 128, 128);
        using var plain = TestHelpers.CreateSolidBitmap(400, 200, background);
        using var outlined = TestHelpers.CreateSolidBitmap(400, 200, background);
        var request = new AnnotationRequest("Outline", null, BundledFonts.DefaultFontFamily, 36, "#FFFFFF", AnnotationPosition.TopLeft, OutlineEnabled: false);

        WallpaperAnnotator.Draw(plain, request);
        WallpaperAnnotator.Draw(outlined, request with { OutlineEnabled = true });

        // Light text gets a dark outline, so the outlined render has darker pixels than the plain one
        Assert.True(CountDarkerThan(outlined, 60) > CountDarkerThan(plain, 60));
    }

    [Fact]
    public void ResolveTypeface_UnknownFamily_FallsBackToTheBundledFont()
    {
        // A mistyped or uninstalled family must not silently become whatever face the
        // platform substitutes; it renders in the bundled default instead.
        using var typeface = BundledFonts.ResolveTypeface("No Such Font Family 7f3a");

        Assert.Equal(BundledFonts.DefaultFontFamily, typeface.FamilyName);
    }

    [Theory]
    [InlineData("#F5F5F5", 245, 245, 245, 255)]
    [InlineData("F00", 255, 0, 0, 255)]
    [InlineData("#11223380", 0x11, 0x22, 0x33, 0x80)] // alpha last
    public void TryParseHexColor_ReadsRgbWithAlphaLast(string hex, byte r, byte g, byte b, byte a)
    {
        Assert.True(WallpaperAnnotator.TryParseHexColor(hex, out var color));
        Assert.Equal(new SKColor(r, g, b, a), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("not-a-colour")]
    public void IsValidColor_RejectsMalformedValues(string hex)
    {
        Assert.False(WallpaperAnnotator.IsValidColor(hex));
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

    private static int CountDarkerThan(SKBitmap bitmap, int threshold)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < threshold && pixel.Green < threshold && pixel.Blue < threshold)
                    count++;
            }
        }
        return count;
    }
}

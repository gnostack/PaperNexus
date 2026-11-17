using SkiaSharp;

namespace PaperNexus.Core.Imaging;

// Draws a picture onto a wallpaper canvas the way a fill style places it on a screen. Used
// when the device wants the app to fit the wallpaper itself (the phone); the desktop's
// operating system does the same job from the fill style setting.
internal static class WallpaperFitting
{
    // Where the picture lands on a canvasWidth x canvasHeight canvas, in canvas pixels. For
    // Tile it is the first tile, in the top-left corner; the others repeat it.
    //
    // Fill    - scaled to cover the canvas, centred, the overflow cropped
    // Fit     - scaled to fit inside the canvas, centred, black bars around it
    // Stretch - scaled to the canvas on each axis separately
    // Tile    - at its own pixel size, repeated from the top-left corner
    // Center  - at its own pixel size, centred, cropped or bordered in black
    // Span    - as Fill: a single screen has nothing to span across
    internal static SKRect Placement(int pictureWidth, int pictureHeight, int canvasWidth, int canvasHeight, WallpaperFillStyle style)
    {
        var widthRatio = (float)canvasWidth / pictureWidth;
        var heightRatio = (float)canvasHeight / pictureHeight;
        switch (style)
        {
            case WallpaperFillStyle.Fit:
                return Centred(pictureWidth, pictureHeight, canvasWidth, canvasHeight, Math.Min(widthRatio, heightRatio));
            case WallpaperFillStyle.Stretch:
                return new SKRect(0, 0, canvasWidth, canvasHeight);
            case WallpaperFillStyle.Tile:
                return new SKRect(0, 0, pictureWidth, pictureHeight);
            case WallpaperFillStyle.Center:
                return Centred(pictureWidth, pictureHeight, canvasWidth, canvasHeight, 1f);
            default:
                return Centred(pictureWidth, pictureHeight, canvasWidth, canvasHeight, Math.Max(widthRatio, heightRatio));
        }
    }

    // A new canvasWidth x canvasHeight opaque bitmap with the picture drawn on it for the fill
    // style, black wherever the picture does not reach. The caller owns it.
    internal static SKBitmap Compose(SKBitmap picture, int canvasWidth, int canvasHeight, WallpaperFillStyle style)
    {
        var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var composed = new SKBitmap(info);
        using var canvas = new SKCanvas(composed);
        canvas.Clear(SKColors.Black);
        using var image = SKImage.FromBitmap(picture);

        var placement = Placement(picture.Width, picture.Height, canvasWidth, canvasHeight, style);
        if (style == WallpaperFillStyle.Tile)
        {
            // Every tile is the picture at its own size, so no resampling is needed
            for (var y = 0f; y < canvasHeight; y += placement.Height)
            {
                for (var x = 0f; x < canvasWidth; x += placement.Width)
                    canvas.DrawImage(image, x, y);
            }
        }
        else
        {
            canvas.DrawImage(image, placement, ImageCodec.DownscaleSampling);
        }

        canvas.Flush();
        return composed;
    }

    // The picture scaled by scale and centred on the canvas.
    private static SKRect Centred(int pictureWidth, int pictureHeight, int canvasWidth, int canvasHeight, float scale)
    {
        var width = pictureWidth * scale;
        var height = pictureHeight * scale;
        var left = (canvasWidth - width) / 2f;
        var top = (canvasHeight - height) / 2f;
        var rect = new SKRect(left, top, left + width, top + height);
        return rect;
    }
}

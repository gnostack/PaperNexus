using SkiaSharp;

namespace PaperNexus.Core.Imaging;

// The 16x16 tray menu icons, drawn as simple shapes and returned as PNG bytes so the
// caller can hand them to Avalonia without referencing Skia itself.
internal static class MenuIcons
{
    private const int Size = 16;

    // Eight-pronged star with a white hub, for "Open Settings".
    public static byte[] Gear() => Draw(canvas =>
    {
        using var blue = Fill(SKColors.CornflowerBlue);
        using var white = Fill(SKColors.White);
        using var star = Star(centerX: 8, centerY: 8, prongs: 8, innerRadius: 3.5f, outerRadius: 7f);
        canvas.DrawPath(star, blue);
        canvas.DrawCircle(8, 8, 2.5f, white);
    });

    // Right-pointing triangle, for "Next Wallpaper".
    public static byte[] Play() => Draw(canvas =>
    {
        using var green = Fill(SKColors.LimeGreen);
        using var triangle = new SKPath();
        triangle.MoveTo(4, 2);
        triangle.LineTo(14, 8);
        triangle.LineTo(4, 14);
        triangle.Close();
        canvas.DrawPath(triangle, green);
    });

    // Die face showing three, for "Random Wallpaper".
    public static byte[] Dice() => Draw(canvas =>
    {
        using var orchid = Fill(SKColors.MediumOrchid);
        using var white = Fill(SKColors.White);
        canvas.DrawRect(2, 2, 12, 12, orchid);
        canvas.DrawCircle(5, 5, 1.3f, white);
        canvas.DrawCircle(8, 8, 1.3f, white);
        canvas.DrawCircle(11, 11, 1.3f, white);
    });

    // Power symbol - a ring with a bar through the top, for "Exit".
    public static byte[] Power() => Draw(canvas =>
    {
        using var ring = new SKPaint
        {
            Color = SKColors.Tomato,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
        };
        using var bar = Fill(SKColors.Tomato);
        canvas.DrawCircle(8, 9, 5, ring);
        canvas.DrawRect(7, 2, 2, 7, bar);
    });

    // Renders draw onto a transparent Size x Size canvas and encodes it as PNG.
    private static byte[] Draw(Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            draw(canvas);
            canvas.Flush();
        }
        return ImageCodec.ToPngBytes(bitmap);
    }

    private static SKPaint Fill(SKColor color)
    {
        var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        return paint;
    }

    // A star with the given number of prongs, alternating outer and inner vertices and
    // starting with a prong pointing straight up.
    private static SKPath Star(float centerX, float centerY, int prongs, float innerRadius, float outerRadius)
    {
        var path = new SKPath();
        var vertexCount = prongs * 2;
        for (var i = 0; i < vertexCount; i++)
        {
            var radius = i % 2 == 0 ? outerRadius : innerRadius;
            var angle = (Math.PI * i / prongs) - (Math.PI / 2);
            var x = centerX + (float)(radius * Math.Cos(angle));
            var y = centerY + (float)(radius * Math.Sin(angle));
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }
        path.Close();
        return path;
    }
}

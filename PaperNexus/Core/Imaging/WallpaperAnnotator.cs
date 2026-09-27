using System.Globalization;
using SkiaSharp;

namespace PaperNexus.Core.Imaging;

// What to write on a wallpaper and how. Timestamp is null unless debug mode wants the
// switch time drawn beside the title.
internal sealed record AnnotationRequest(
    string Title,
    string? Timestamp,
    string FontFamily,
    int FontSize,
    string Color,
    AnnotationPosition Position,
    bool OutlineEnabled);

// Draws the title (and the optional debug timestamp) onto a wallpaper bitmap.
internal static class WallpaperAnnotator
{
    // Text colour used when the configured one cannot be parsed (WhiteSmoke).
    public const string DefaultColor = "#F5F5F5";

    // Distance of the text from the left or right edge, and the gap below the text at the
    // bottom positions.
    private const float SideMargin = 125f;
    private const float TopMargin = 5f;
    private const float BottomMargin = 10f;

    // Stroke width for the annotation outline, in pixels.
    //
    // This was previously fontSize/36, which is 0.5px at the default 18pt - a sub-pixel
    // stroke that antialiases away to nothing, so small text had no visible outline at all.
    // 1/12 was chosen by rendering the candidates side by side: clearly visible at 18pt
    // while leaving the letterforms open, where 1/6 was heavy enough to close up adjacent
    // glyphs. The floor guarantees at least one solid pixel however small the font.
    public static float OutlineWidth(int fontSize) => Math.Max(1f, fontSize / 12f);

    public static bool IsValidColor(string hex) => TryParseHexColor(hex, out _);

    // Parses "#RGB", "#RGBA", "#RRGGBB" or "#RRGGBBAA" (leading '#' optional). Alpha comes
    // last, which is the order the settings file has always stored - Skia's own parser
    // expects alpha first, so it is not used here.
    internal static bool TryParseHexColor(string hex, out SKColor color)
    {
        color = SKColors.Empty;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var digits = hex.Trim().TrimStart('#');
        if (digits.Length is 3 or 4)
            digits = string.Concat(digits.Select(c => new string(c, 2)));
        if (digits.Length is not (6 or 8))
            return false;
        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        var rgba = digits.Length == 6 ? (value << 8) | 0xFF : value;
        var r = (byte)(rgba >> 24);
        var g = (byte)(rgba >> 16);
        var b = (byte)(rgba >> 8);
        var a = (byte)rgba;
        color = new SKColor(r, g, b, a);
        return true;
    }

    // Draws the annotation onto bitmap in place. An unparseable colour falls back to
    // DefaultColor; the caller is expected to have logged it via IsValidColor.
    public static void Draw(SKBitmap bitmap, AnnotationRequest request)
    {
        if (!TryParseHexColor(request.Color, out var textColor))
            TryParseHexColor(DefaultColor, out textColor);

        // Dark outline for light text, light outline for dark text
        var brightness = textColor.Red + textColor.Green + textColor.Blue;
        var outlineColor = brightness > 382 ? SKColors.Black : SKColors.White;

        var fontSize = request.FontSize > 0 ? request.FontSize : 18;
        var rightAligned = request.Position is AnnotationPosition.TopRight or AnnotationPosition.BottomRight;
        var align = rightAligned ? SKTextAlign.Right : SKTextAlign.Left;
        var x = rightAligned ? bitmap.Width - SideMargin : SideMargin;
        var isTop = request.Position is AnnotationPosition.TopLeft or AnnotationPosition.TopRight;
        // y is the top edge of the title line
        var y = isTop ? TopMargin : bitmap.Height - fontSize - BottomMargin;

        using var typeface = BundledFonts.ResolveTypeface(request.FontFamily);
        using var canvas = new SKCanvas(bitmap);
        using var fill = new SKPaint { Color = textColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var outline = request.OutlineEnabled
            ? new SKPaint
            {
                Color = outlineColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = OutlineWidth(fontSize),
                StrokeJoin = SKStrokeJoin.Round,
            }
            : null;

        using var titleFont = new SKFont(typeface, fontSize);
        DrawOutlinedText(canvas, request.Title, x, y, align, titleFont, fill, outline);

        // Debug mode adds a smaller timestamp just below a top title or just above a bottom one
        if (request.Timestamp is not null)
        {
            using var timestampFont = new SKFont(typeface, fontSize * 0.75f);
            var timestampY = isTop ? y + fontSize + 4 : y - fontSize;
            DrawOutlinedText(canvas, request.Timestamp, x, timestampY, align, timestampFont, fill, outline);
        }

        canvas.Flush();
    }

    // Draws the outline and the glyph fill as two separate passes, with top as the top
    // edge of the line (Skia positions text by its baseline, so the font's ascent is added).
    //
    // A stroke is centred on the glyph edge, so drawing it over the fill would eat into the
    // letter and visibly thin small text. Stroking first and filling over it keeps the glyph
    // at its full weight and leaves all the contrast outside the letterform, which is what
    // makes the outline readable at small font sizes.
    private static void DrawOutlinedText(
        SKCanvas canvas,
        string text,
        float x,
        float top,
        SKTextAlign align,
        SKFont font,
        SKPaint fill,
        SKPaint? outline)
    {
        var baseline = top - font.Metrics.Ascent;
        if (outline is not null)
            canvas.DrawText(text, x, baseline, align, font, outline);

        canvas.DrawText(text, x, baseline, align, font, fill);
    }
}

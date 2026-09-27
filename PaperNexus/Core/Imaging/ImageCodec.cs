using SkiaSharp;

namespace PaperNexus.Core.Imaging;

// Decoding, resampling and encoding shared by every image operation in this folder.
internal static class ImageCodec
{
    // JPEG quality used when a lossy file has to be re-encoded (resolution cap). High enough
    // that the one extra generation of compression is not visible.
    public const int DownloadJpegQuality = 95;

    // Linear filtering between mipmap levels. Plain bilinear or bicubic sampling only reads
    // the few source pixels nearest each output pixel, which aliases badly when a 4K image is
    // shrunk to a 600px thumbnail or a multi-megabyte logo to a 16px icon; mipmaps average
    // the whole source area first.
    public static readonly SKSamplingOptions DownscaleSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    // Decodes an encoded image into a bitmap the caller owns. Throws rather than returning
    // null so a corrupt download or wallpaper surfaces as an error in the caller's log.
    public static SKBitmap Decode(byte[] encoded)
    {
        var bitmap = SKBitmap.Decode(encoded)
            ?? throw new InvalidDataException("The image could not be decoded.");
        return bitmap;
    }

    public static SKBitmap DecodeFile(string path)
    {
        var encoded = File.ReadAllBytes(path);
        return Decode(encoded);
    }

    // Returns a resampled copy of source at exactly width x height; the caller owns it.
    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        var resized = source.Resize(info, DownscaleSampling)
            ?? throw new InvalidOperationException($"Resizing to {width}x{height} failed.");
        return resized;
    }

    // Returns an opaque copy of source, or null when source is already opaque.
    //
    // Wallpapers are always written without an alpha channel. Skia's PNG encoder writes an
    // RGB (not RGBA) file only when the bitmap is marked opaque, so a source that carries
    // alpha is composited onto black first - the same colour a transparent pixel shows as
    // on the desktop.
    public static SKBitmap? FlattenToOpaque(SKBitmap source)
    {
        if (source.AlphaType == SKAlphaType.Opaque)
            return null;

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var opaque = new SKBitmap(info);
        using var canvas = new SKCanvas(opaque);
        canvas.Clear(SKColors.Black);
        canvas.DrawBitmap(source, 0, 0);
        return opaque;
    }

    // Writes source to destination as an 8-bit RGB PNG (alpha dropped).
    public static void WritePngRgb(SKBitmap source, Stream destination)
    {
        using var opaque = FlattenToOpaque(source);
        var toEncode = opaque ?? source;
        Encode(toEncode, destination, SKEncodedImageFormat.Png, quality: 100);
    }

    // Writes source to destination as a JPEG at the given quality (1-100).
    public static void WriteJpeg(SKBitmap source, Stream destination, int quality)
    {
        using var opaque = FlattenToOpaque(source);
        var toEncode = opaque ?? source;
        Encode(toEncode, destination, SKEncodedImageFormat.Jpeg, quality);
    }

    // PNG bytes of source as-is, alpha preserved. Used for the icons handed to Avalonia,
    // where transparency around the drawn shape is the point.
    public static byte[] ToPngBytes(SKBitmap source)
    {
        using var ms = new MemoryStream();
        Encode(source, ms, SKEncodedImageFormat.Png, quality: 100);
        return ms.ToArray();
    }

    private static void Encode(SKBitmap source, Stream destination, SKEncodedImageFormat format, int quality)
    {
        var encoded = source.Encode(destination, format, quality);
        if (!encoded)
            throw new InvalidOperationException($"Encoding the image as {format} failed.");
    }
}

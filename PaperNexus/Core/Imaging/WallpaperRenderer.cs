using SkiaSharp;

namespace PaperNexus.Core.Imaging;

internal enum WallpaperFileFormat
{
    Png,
    Jpeg,
}

// An encoded wallpaper ready to be written to disk. Owns the buffer holding the bytes.
internal sealed class EncodedWallpaper : IDisposable
{
    public EncodedWallpaper(MemoryStream data, WallpaperFileFormat format)
    {
        Data = data;
        Format = format;
    }

    public MemoryStream Data { get; }
    public WallpaperFileFormat Format { get; }

    public void Dispose() => Data.Dispose();
}

// Produces the processed wallpaper file contents from a source image: optionally draws
// the annotation, then encodes as PNG, falling back to JPEG when the PNG is too large.
internal static class WallpaperRenderer
{
    // Largest encoded wallpaper written to disk (16 MB).
    public const long DefaultSizeCeiling = 1 << 24;

    // Decodes sourcePath, draws annotation onto it when one is given, and encodes the
    // result. The source file is never modified.
    public static EncodedWallpaper Render(string sourcePath, AnnotationRequest? annotation, long sizeCeiling = DefaultSizeCeiling)
    {
        using var bitmap = ImageCodec.DecodeFile(sourcePath);
        if (annotation is not null)
            WallpaperAnnotator.Draw(bitmap, annotation);
        return Encode(bitmap, sizeCeiling);
    }

    // Lossless 8-bit RGB PNG first. When that exceeds sizeCeiling (high-resolution 4K+
    // images), re-encode as JPEG starting at quality 97 and stepping down by 3 until it
    // fits; quality 1 is used if nothing fits.
    public static EncodedWallpaper Encode(SKBitmap bitmap, long sizeCeiling = DefaultSizeCeiling)
    {
        var ms = new MemoryStream();
        try
        {
            // Flatten once here so the JPEG retries below do not each copy the pixels again
            using var opaque = ImageCodec.FlattenToOpaque(bitmap);
            var source = opaque ?? bitmap;

            ImageCodec.WritePngRgb(source, ms);
            if (ms.Length <= sizeCeiling)
                return Rewound(ms, WallpaperFileFormat.Png);

            for (var quality = 97; quality >= 1; quality -= 3)
            {
                ms.SetLength(0);
                ImageCodec.WriteJpeg(source, ms, quality);
                if (ms.Length <= sizeCeiling)
                    break;
            }
            return Rewound(ms, WallpaperFileFormat.Jpeg);
        }
        catch
        {
            ms.Dispose();
            throw;
        }
    }

    private static EncodedWallpaper Rewound(MemoryStream ms, WallpaperFileFormat format)
    {
        ms.Position = 0;
        return new EncodedWallpaper(ms, format);
    }
}

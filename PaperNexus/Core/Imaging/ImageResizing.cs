using SkiaSharp;

namespace PaperNexus.Core.Imaging;

// The resize operations the app performs: capping a downloaded wallpaper's resolution,
// building gallery thumbnails, and scaling the logo for icons.
internal static class ImageResizing
{
    // Width of a gallery thumbnail; height follows the source aspect ratio.
    public const int ThumbnailWidth = 600;

    // Largest size that fits inside maxWidth x maxHeight with the source's aspect ratio.
    // Each side is rounded and never drops below one pixel.
    public static (int Width, int Height) FitWithin(int width, int height, int maxWidth, int maxHeight)
    {
        var widthRatio = (double)maxWidth / width;
        var heightRatio = (double)maxHeight / height;
        var scale = Math.Min(widthRatio, heightRatio);
        var fittedWidth = Math.Max(1, (int)Math.Round(width * scale));
        var fittedHeight = Math.Max(1, (int)Math.Round(height * scale));
        return (fittedWidth, fittedHeight);
    }

    // Shrinks the image file at filePath in place so it fits within maxWidth x maxHeight,
    // preserving the aspect ratio. An image already inside the box is left untouched (never
    // upscaled) and false is returned. The file keeps its format: a .jpg/.jpeg is re-encoded
    // as JPEG, anything else as 8-bit RGB PNG.
    public static bool ResizeFileToFit(string filePath, int maxWidth, int maxHeight)
    {
        using var source = ImageCodec.DecodeFile(filePath);
        if (source.Width <= maxWidth && source.Height <= maxHeight)
            return false;

        var (width, height) = FitWithin(source.Width, source.Height, maxWidth, maxHeight);
        using var resized = ImageCodec.Resize(source, width, height);

        var ext = Path.GetExtension(filePath);
        var isJpeg = ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

        // The source is fully decoded into memory above, so overwriting the file it came
        // from is safe.
        using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        if (isJpeg)
            ImageCodec.WriteJpeg(resized, file, ImageCodec.DownloadJpegQuality);
        else
            ImageCodec.WritePngRgb(resized, file);
        return true;
    }

    // PNG bytes of a ThumbnailWidth-wide copy of the image file at path, height proportional.
    // Smaller images are scaled up to the same width so every gallery card lines up.
    public static byte[] CreateThumbnailPng(string path)
    {
        using var source = ImageCodec.DecodeFile(path);
        var height = Math.Max(1, (int)Math.Round((double)source.Height * ThumbnailWidth / source.Width));
        using var thumbnail = ImageCodec.Resize(source, ThumbnailWidth, height);
        return ImageCodec.ToPngBytes(thumbnail);
    }

    // PNG bytes of the encoded image in source scaled to size x size. Used for the tray
    // icon and the Linux launcher icon, both drawn from the square app logo.
    public static byte[] ResizeToSquarePng(Stream source, int size)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        using var decoded = ImageCodec.Decode(buffer.ToArray());
        using var resized = ImageCodec.Resize(decoded, size, size);
        return ImageCodec.ToPngBytes(resized);
    }
}

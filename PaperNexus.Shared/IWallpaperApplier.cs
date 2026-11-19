namespace PaperNexus;

// Sets the device wallpaper. The host supplies the implementation; tests use a no-op
// double so they never change the real wallpaper.
public interface IWallpaperApplier
{
    // False when this device does not let the app change the wallpaper at all (a phone whose
    // owner or administrator forbids it). The desktop always answers true.
    public bool CanSetWallpaper { get; }

    // The picture the device wants, when the app must fit the wallpaper to the screen itself;
    // null when the device fits the picture (the desktop, through its fill style setting).
    // With a canvas, the switcher draws the picture onto it with the fill style.
    public WallpaperCanvas? GetCanvas();

    // Sets the processed wallpaper file. False when the device refused it.
    public bool SetWallpaper(string wallpaperPath);

    public void ApplyFillStyle(WallpaperFillStyle style);
}

// The picture a device wants for its wallpaper: Width x Height pixels. The switcher draws the
// picture onto it for the fill style; the device shows it as it is.
public sealed record WallpaperCanvas(int Width, int Height)
{
    // The canvas for a phone screen of screenWidth x screenHeight pixels whose system enlarges
    // the wallpaper by zoom (Android shows the middle 1/zoom of it at rest).
    //
    // The canvas is the screen size times the zoom, so the enlarged wallpaper reaches the
    // screen at one canvas pixel per screen pixel and the part the screen shows is the
    // screen-sized rectangle in the middle.
    public static WallpaperCanvas ForScreen(int screenWidth, int screenHeight, float zoom)
    {
        // A zoom below 1 would mean the screen shows more than the picture; treat it as none.
        var scale = Math.Max(1f, zoom);
        var width = (int)Math.Round(screenWidth * scale);
        var height = (int)Math.Round(screenHeight * scale);
        var canvas = new WallpaperCanvas(width, height);
        return canvas;
    }
}

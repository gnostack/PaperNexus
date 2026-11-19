using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Runtime;
using Android.Views;
using PaperNexus.Core;

namespace PaperNexus;

// Sets the phone's wallpaper through Android's WallpaperManager: one picture for the home
// screen and the lock screen together (issue #203, decision 3). Android does not fit a picture
// the way the app wants (a wide picture is scaled to the launcher's own size and its left part
// shown), so the app fits it itself: GetCanvas describes the picture this screen needs.
internal sealed class AndroidWallpaperApplier : IWallpaperApplier, IAddSingleton<IWallpaperApplier>
{
    private readonly Context _context = global::Android.App.Application.Context;

    private WallpaperManager Manager => WallpaperManager.GetInstance(_context)!;

    // False on a phone whose owner or administrator forbids changing the wallpaper
    // (UserManager.DISALLOW_SET_WALLPAPER), or that has no wallpaper at all.
    public bool CanSetWallpaper
    {
        get
        {
            var manager = Manager;
            var allowed = manager.IsWallpaperSupported && manager.IsSetWallpaperAllowed;
            return allowed;
        }
    }

    // The picture for this phone's screen in its natural (upright) orientation: the screen
    // size times the system's wallpaper zoom, with the title kept clear of the status bar at
    // the top and the navigation bar at the bottom, and the title's size in density-independent
    // units (the screen's density, 2.625 on the 420 dpi emulator).
    public WallpaperCanvas? GetCanvas()
    {
        var displayManager = (DisplayManager)_context.GetSystemService(Context.DisplayService)!;
        var display = displayManager.GetDisplay(Display.DefaultDisplay)!;
        // A window context reads the window metrics without an open screen, which the
        // slideshow will need when it runs with the app closed (issue #203, step 5).
        var windowContext = _context.CreateWindowContext(display, (int)WindowManagerTypes.ApplicationOverlay, null);
        var windowManager = windowContext.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>()!;
        var metrics = windowManager.MaximumWindowMetrics;
        var bars = metrics.WindowInsets.GetInsetsIgnoringVisibility(WindowInsets.Type.SystemBars());

        var width = metrics.Bounds.Width();
        var height = metrics.Bounds.Height();
        var topBar = bars.Top;
        var bottomBar = bars.Bottom;
        var turned = display.Rotation is SurfaceOrientation.Rotation90 or SurfaceOrientation.Rotation270;
        if (turned)
        {
            // Turned sideways the navigation bar can sit at a side; the wallpaper is drawn for
            // the upright screen, where it is at the bottom.
            (width, height) = (height, width);
            bottomBar = Math.Max(bars.Bottom, Math.Max(bars.Left, bars.Right));
        }

        var density = windowContext.Resources!.DisplayMetrics!.Density;
        var canvas = WallpaperCanvas.ForScreen(width, height, ReadWallpaperZoom(), topBar, bottomBar, density);
        return canvas;
    }

    // Android enlarges a still wallpaper on both the home screen and the lock screen by the
    // system value config_wallpaperMaxScale, so the screen shows only the middle 1/zoom of it.
    // Measured on the Android 16 emulator (issue #203, step 3): the value reads 1.1, and a
    // screen-sized picture showed 1.1 times enlarged and centred on both screens, while a
    // picture 1.1 times the screen size showed at one pixel per screen pixel. The value is not
    // part of Android's public API, so when it cannot be read the zoom is taken as none (1.0).
    private static float ReadWallpaperZoom()
    {
        try
        {
            var system = global::Android.Content.Res.Resources.System!;
            var id = system.GetIdentifier("config_wallpaperMaxScale", "dimen", "android");
            if (id == 0)
                return 1f;
            var zoom = system.GetFloat(id);
            return zoom >= 1f ? zoom : 1f;
        }
        catch (global::Android.Content.Res.Resources.NotFoundException)
        {
            return 1f;
        }
    }

    // Sets the processed picture on the home screen and the lock screen in one call. Android
    // copies it, and returns the new wallpaper's id, or 0 when it did not take it.
    public bool SetWallpaper(string wallpaperPath)
    {
        using var stream = File.OpenRead(wallpaperPath);
        var screens = WallpaperManagerFlags.System | WallpaperManagerFlags.Lock;
        var id = Manager.SetStream(stream, null, true, screens);
        return id != 0;
    }

    // The fill style is already drawn into the picture (WallpaperRenderer), so there is
    // nothing to tell Android.
    public void ApplyFillStyle(WallpaperFillStyle style)
    {
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PaperNexus.Core;
using PaperNexus.Core.Platform;

namespace PaperNexus;

// Single registered implementation. It owns no platform logic itself - it picks the
// backend for the running OS once and forwards every call to it. Keeping one registered
// type preserves the IAddSingleton auto-discovery contract in Bootstrapper.
internal sealed class WallpaperApplier : IWallpaperApplier, IAddSingleton<IWallpaperApplier>
{
    private readonly IWallpaperBackend _backend;

    // The logger is passed to the Linux backend so a failed wallpaper switch says which
    // desktop was detected and which helper commands were tried. Without it the only symptom
    // is that the wallpaper silently does not change, which is not diagnosable remotely.
    public WallpaperApplier(ILogger<WallpaperApplier> logger)
    {
        _backend = SelectBackend(logger);
    }

    private static IWallpaperBackend SelectBackend(ILogger logger)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsWallpaperBackend();
        if (OperatingSystem.IsLinux())
            return new LinuxWallpaperBackend(logger);
        return new NoOpWallpaperBackend();
    }

    // The desktop never forbids changing the wallpaper.
    public bool CanSetWallpaper => true;

    // The operating system fits the picture to each monitor using the fill style, so the
    // switcher hands it the picture at its own size.
    public WallpaperCanvas? GetCanvas() => null;

    public bool SetWallpaper(string wallpaperPath) => _backend.SetWallpaper(wallpaperPath);

    public void ApplyFillStyle(WallpaperFillStyle style) => _backend.ApplyFillStyle(style);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsWallpaperBackend : IWallpaperBackend
{
    public bool SetWallpaper(string wallpaperPath) => NativeMethods.SetDesktopWallpaper(wallpaperPath);

    public void ApplyFillStyle(WallpaperFillStyle style)
    {
        // WallpaperStyle and TileWallpaper registry values under HKCU\Control Panel\Desktop
        // control how Windows positions the wallpaper image.
        var (wallpaperStyle, tileWallpaper) = style switch
        {
            WallpaperFillStyle.Tile => ("0", "1"),
            WallpaperFillStyle.Center => ("0", "0"),
            WallpaperFillStyle.Stretch => ("2", "0"),
            WallpaperFillStyle.Fit => ("6", "0"),
            WallpaperFillStyle.Fill => ("10", "0"),
            WallpaperFillStyle.Span => ("22", "0"),
            _ => ("10", "0"),
        };

        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        key?.SetValue("WallpaperStyle", wallpaperStyle);
        key?.SetValue("TileWallpaper", tileWallpaper);
    }
}

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    // Tells Windows to apply the image at wallpaperPath as the desktop wallpaper.
    // SPIF_UPDATEINIFILE persists the change to the user profile;
    // SPIF_SENDCHANGE broadcasts WM_SETTINGCHANGE so the shell picks it up immediately.
    // Returns true if the API call succeeded (non-zero return), false otherwise.
    internal static bool SetDesktopWallpaper(string wallpaperPath)
    {
        var result = SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            wallpaperPath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        return result != 0;
    }
}

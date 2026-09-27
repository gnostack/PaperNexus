namespace PaperNexus.Core.Platform;

// Installs the freedesktop application launcher and icon on Linux.
//
// Without a .desktop file in the applications directory, GNOME Shell and the KDE task
// manager have no launcher to pin: they synthesise a temporary entry from the running
// window, which disappears the moment the app exits. That is why a pinned PaperNexus
// shows an icon only while the app is running. Installing a real entry gives the shell a
// permanent launcher, and StartupWMClass lets it match the running window to that entry
// instead of creating a second, duplicate dock item.
//
// Windows takes its icon from the executable's own resources, so this is a no-op there.
public static class DesktopEntry
{
    // Basename shared by the launcher, the icon, and the autostart entry. It must match the
    // window's WM_CLASS ("PaperNexus") for the shell to associate the two.
    public const string EntryName = "PaperNexus";
    public const string WindowClass = "PaperNexus";

    // Icon names are theme lookups, not paths, and are conventionally lowercase.
    private const string IconName = "papernexus";

    // Matches the hicolor size directory the icon is written into.
    private const int IconSize = 256;

    private static string DataHome
    {
        get
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(dataHome))
                return dataHome;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share");
        }
    }

    public static string LauncherPath => Path.Combine(DataHome, "applications", $"{EntryName}.desktop");

    // hicolor is the fallback theme every desktop searches, so an icon placed here is found
    // regardless of which theme the user has selected.
    public static string IconPath =>
        Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", $"{IconName}.png");

    // Writes the launcher and icon, then refreshes the desktop caches. Safe to call on every
    // launch: it rewrites both files so an app that moved on disk keeps a working Exec line,
    // which also repairs installs made before this existed. iconSource supplies the app logo;
    // the caller reads it from the embedded Avalonia asset.
    public static void Install(string exePath, Func<Stream>? iconSource = null)
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            WriteIcon(iconSource);
            WriteLauncher(exePath);
            RefreshCaches();
        }
        catch (IOException) { /* best-effort - a missing launcher does not stop the app running */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    private static void WriteIcon(Func<Stream>? iconSource)
    {
        if (iconSource is null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(IconPath)!);

        // The bundled logo is a multi-megabyte full-resolution PNG. Icon caches load every
        // entry eagerly, so it is downscaled to the 256x256 the directory advertises rather
        // than copied verbatim.
        using var source = iconSource();
        var png = ImageResizing.ResizeToSquarePng(source, IconSize);
        File.WriteAllBytes(IconPath, png);
    }

    private static void WriteLauncher(string exePath)
    {
        // Icon falls back to the absolute file path when the themed icon is missing, so the
        // launcher still shows something if the icon write failed.
        var icon = File.Exists(IconPath) ? IconName : IconPath;

        var contents = $"""
            [Desktop Entry]
            Type=Application
            Version=1.0
            Name=PaperNexus
            GenericName=Wallpaper Rotator
            Comment=Automated wallpaper rotation
            Exec="{exePath}"
            Icon={icon}
            Terminal=false
            Categories=Utility;Graphics;
            Keywords=wallpaper;desktop;background;slideshow;
            StartupWMClass={WindowClass}
            StartupNotify=true
            """;

        Directory.CreateDirectory(Path.GetDirectoryName(LauncherPath)!);
        File.WriteAllText(LauncherPath, contents + Environment.NewLine);
        PlatformPaths.EnsureExecutable(LauncherPath);
    }

    // Both caches are rebuilt lazily by most desktops, but refreshing them makes the entry
    // appear without a logout. Absent tools are simply skipped.
    private static void RefreshCaches()
    {
        var applicationsDir = Path.GetDirectoryName(LauncherPath)!;
        if (LinuxDesktop.CommandExists("update-desktop-database"))
            LinuxDesktop.Run("update-desktop-database", applicationsDir);

        var themeDir = Path.Combine(DataHome, "icons", "hicolor");
        if (LinuxDesktop.CommandExists("gtk-update-icon-cache"))
            LinuxDesktop.Run("gtk-update-icon-cache", "--force", "--quiet", "--ignore-theme-index", themeDir);
    }

}

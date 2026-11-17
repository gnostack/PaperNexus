using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PaperNexus.Core.Platform;

// Registers or removes "launch PaperNexus at login" using each platform's own mechanism:
// the HKCU Run key on Windows, an XDG autostart desktop entry on Linux. Both are per-user
// and need no elevation, which matters on immutable systems such as SteamOS.
public static class StartupRegistration
{
    private const string EntryName = "PaperNexus";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    // Names written by earlier versions of the app, removed on every update so an upgrade
    // does not leave a second entry pointing at a path that no longer exists.
    private static readonly string[] LegacyEntryNames =
    [
        "Excogitated Wallpaper Service",
        "Wallpaper Nexus",
    ];

    // exePath is passed explicitly because the installer must register the *installed*
    // copy, not the temporary one it is currently running from.
    public static void Update(bool enable, string? exePath = null)
    {
        var target = exePath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(target))
            return;

        if (OperatingSystem.IsWindows())
            UpdateWindows(enable, target);
        else if (OperatingSystem.IsLinux())
            UpdateLinux(enable, target);
    }

    [SupportedOSPlatform("windows")]
    private static void UpdateWindows(bool enable, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        foreach (var legacyName in LegacyEntryNames)
            key?.DeleteValue(legacyName, throwOnMissingValue: false);

        if (enable)
        {
            // Pass --startup so the app knows it was launched at login rather than by the user.
            key?.SetValue(EntryName, $"\"{exePath}\" --startup");
        }
        else
        {
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
        }
    }

    // The autostart directory is read by GNOME, KDE Plasma, and every other
    // freedesktop-compliant session manager at login.
    private static string AutostartFilePath
    {
        get
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configHome = Path.Combine(home, ".config");
            }
            return Path.Combine(configHome, "autostart", $"{EntryName}.desktop");
        }
    }

    private static void UpdateLinux(bool enable, string exePath)
    {
        var desktopFile = AutostartFilePath;

        if (!enable)
        {
            try { File.Delete(desktopFile); }
            catch (IOException) { /* best-effort - a stale entry only costs an extra launch */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
            return;
        }

        // Exec values are shell-like: quote the path so directories containing spaces work.
        // Icon and StartupWMClass mirror the application launcher so the session manager
        // and the dock resolve an autostarted window to the same entry rather than
        // creating a second, icon-less one.
        var contents = $"""
            [Desktop Entry]
            Type=Application
            Name=PaperNexus
            Comment=Automated wallpaper rotation
            Exec="{exePath}" --startup
            Icon={(File.Exists(DesktopEntry.IconPath) ? "papernexus" : DesktopEntry.IconPath)}
            Terminal=false
            StartupWMClass={DesktopEntry.WindowClass}
            X-GNOME-Autostart-enabled=true
            """;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(desktopFile)!);
            File.WriteAllText(desktopFile, contents + Environment.NewLine);
        }
        catch (IOException) { /* best-effort - startup registration is not critical to run */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }
}

// The desktop's run-at-sign-in setting for the shared view model. Registers the running
// executable, as the settings window always has.
internal sealed class DesktopStartupRegistration : IStartupRegistration, IAddSingleton<IStartupRegistration>
{
    public void Update(bool enable)
    {
        StartupRegistration.Update(enable);
    }
}

namespace PaperNexus.Core.Platform;

// Centralises the file-system conventions that differ between Windows and Linux so the
// rest of the app never hardcodes ".exe" or assumes case-insensitive path comparison.
public static class PlatformPaths
{
    // Windows produces "PaperNexus.exe"; Linux produces an extensionless "PaperNexus" apphost.
    public static string ExecutableName => OperatingSystem.IsWindows() ? "PaperNexus.exe" : "PaperNexus";

    // Linux file systems are case-sensitive, so comparing paths case-insensitively there
    // would wrongly treat "/home/a/Pic.png" and "/home/a/pic.png" as the same file.
    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool PathEquals(string? left, string? right)
    {
        if (left is null || right is null)
            return ReferenceEquals(left, right);
        return string.Equals(left, right, PathComparison);
    }

    // Default install directory. LocalApplicationData maps to %LOCALAPPDATA% on Windows and
    // ~/.local/share on Linux - both are user-writable, which matters on immutable
    // distributions such as SteamOS where the system root is read-only.
    public static string DefaultInstallDirectory
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
                localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(localAppData, "PaperNexus");
        }
    }

    // Marks a freshly copied file as executable. No-op on Windows, which has no execute bit.
    public static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute);
        }
        catch (IOException) { /* best-effort - the copy may already carry the execute bit */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }
}

// The desktop's settings folder for the shared settings store: the default install
// directory, whatever folder the executable itself was installed to.
internal sealed class DesktopSettingsDirectory : ISettingsDirectory, IAddSingleton<ISettingsDirectory>
{
    public string FullPath => PlatformPaths.DefaultInstallDirectory;
}

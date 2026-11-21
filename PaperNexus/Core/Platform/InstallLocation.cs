namespace PaperNexus.Core.Platform;

// The one answer to "is this executable the installed copy?" (issue #211). Program.Main asks it
// once for the running executable, before any other decision, and keeps the answer in
// Program.IsInstalledCopy: that answer routes a launch to the install screen, and it is the
// only thing that lets a copy write the launcher and the sign-in entry (DesktopIntegration).
// Location alone decides, so the installed executable started with --debug is still the
// installed copy, and a build folder or a temporary folder never is.
public static class InstallLocation
{
    // Written beside the executable by the install flow (Program.TryInstall). It marks the
    // folder as an install location whether or not it is the default one.
    public const string SentinelFileName = ".installed";

    // True when the install flow's sentinel file is beside the executable, or when the
    // executable is at the default install path (installs that predate the sentinel file).
    public static bool IsInstalledCopy(string exePath, string defaultInstallDirectory)
    {
        var current = Path.GetFullPath(exePath);
        var exeDir = Path.GetDirectoryName(current);
        if (exeDir is not null && File.Exists(Path.Combine(exeDir, SentinelFileName)))
            return true;

        var defaultExePath = Path.Combine(defaultInstallDirectory, PlatformPaths.ExecutableName);
        var defaultExe = Path.GetFullPath(defaultExePath);
        return PlatformPaths.PathEquals(current, defaultExe);
    }
}

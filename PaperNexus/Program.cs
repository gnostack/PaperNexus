global using PaperNexus.Core;
global using PaperNexus;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Newtonsoft.Json;
global using System.Diagnostics;
global using System.Reflection;
global using Avalonia;
global using PaperNexus.Core.Platform;
global using PaperNexus.Core.Imaging;

internal sealed class Program
{
    // Owns the cross-process lock and the show-window channel for the primary instance.
    // Null until RunAsPrimaryInstance acquires it, and again after shutdown.
    internal static ISingleInstance? Instance { get; private set; }
    internal static bool IsDebugMode { get; private set; }
    internal static bool IsInstallMode { get; private set; }

    // Install path data exposed for InstallScreen to use when performing the actual install.
    internal static string CurrentExePath { get; private set; } = string.Empty;
    internal static string InstallDir { get; private set; } = string.Empty;
    internal static string InstallExePath { get; private set; } = string.Empty;

    [STAThread]
    public static void Main(string[] args)
    {
        // Debug mode bypasses the install check and single-instance logic entirely.
        if (TryRunDebugMode(args))
            return;

        var (installDir, installPath) = GetInstallPaths();
        var currentPath = GetCurrentProcessPath();

        // Cache paths so InstallScreen can reference them without recomputing.
        InstallDir = installDir;
        InstallExePath = installPath;
        CurrentExePath = currentPath;

        // If running from a location other than the install directory, perform
        // first-run installation and then hand off to the installed copy.
        if (!IsRunningFromInstallLocation(currentPath, installPath))
        {
            HandleNotInstalledLaunch(args);
            return;
        }

        RunAsPrimaryInstance(args);
    }

    private static bool TryRunDebugMode(string[] args)
    {
        if (!args.Contains("--debug"))
            return false;
        IsDebugMode = true;
        RunApp(args);
        return true;
    }

    private static (string installDir, string installPath) GetInstallPaths()
    {
        var installDir = PlatformPaths.DefaultInstallDirectory;
        return (installDir, Path.Combine(installDir, PlatformPaths.ExecutableName));
    }

    private static string GetCurrentProcessPath()
    {
        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, PlatformPaths.ExecutableName);
    }

    private static bool IsRunningFromInstallLocation(string currentPath, string installPath)
    {
        // Check for a sentinel file written alongside the exe during install.
        // This handles custom install paths that differ from the default AppData location.
        var current = Path.GetFullPath(currentPath);
        var exeDir = Path.GetDirectoryName(current);
        if (exeDir is not null && File.Exists(Path.Combine(exeDir, ".installed")))
            return true;

        // Fall back to the default AppData path comparison for existing installations
        // that predate the sentinel file (i.e. installed before this fix was added).
        var install = Path.GetFullPath(installPath);
        return PlatformPaths.PathEquals(current, install);
    }

    // Handles startup when the exe is not yet at the install location.
    // Signals any already-running instance first; if none is running, shows the install screen.
    private static void HandleNotInstalledLaunch(string[] args)
    {
        // If a running instance exists, signal it to show its UI and exit without installing.
        if (TrySignalExistingInstance())
            return;

        // A previous install may have copied the exe elsewhere and left a redirect marker.
        // Launch the installed copy instead of showing the install screen again - this
        // prevents an "install loop" when the user re-runs the original downloaded exe.
        if (TryRedirectToInstalledCopy())
            return;

        // Show the install screen - it performs the actual copy and relaunch when confirmed.
        IsInstallMode = true;
        RunApp(args);
    }

    // Checks for a ".installed-at" redirect marker next to the current exe.
    // If the marker points to an installed copy that still exists, launches it and returns true.
    private static bool TryRedirectToInstalledCopy()
    {
        var exeDir = Path.GetDirectoryName(Path.GetFullPath(CurrentExePath));
        if (exeDir is null)
            return false;

        var redirectFile = Path.Combine(exeDir, ".installed-at");
        if (!File.Exists(redirectFile))
            return false;

        try
        {
            var installedExe = File.ReadAllText(redirectFile).Trim();
            if (!File.Exists(installedExe))
            {
                // Installed copy was removed - clean up the stale redirect.
                File.Delete(redirectFile);
                return false;
            }

            ShellOpener.LaunchExecutable(installedExe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Copies the exe to the install directory and migrates any adjacent data files.
    // Returns true if install succeeded or the installed copy already exists (race with another instance).
    internal static bool TryInstall(string currentPath, string installDir, string installPath)
    {
        try
        {
            Directory.CreateDirectory(installDir);

            // Skip the copy when the source and destination resolve to the same file.
            // This happens when re-installing from an existing install location (e.g.,
            // the user's exe is already on the Desktop and they pick Desktop as the
            // install folder). File.Copy throws IOException for same-path copies and
            // for locked files (the running exe can't overwrite itself).
            var source = Path.GetFullPath(currentPath);
            var dest = Path.GetFullPath(installPath);
            var isSamePath = PlatformPaths.PathEquals(source, dest);
            if (!isSamePath)
            {
                File.Copy(currentPath, installPath, overwrite: true);
                // File.Copy does not carry the Unix execute bit, so the installed copy
                // would not be launchable on Linux without restoring it.
                PlatformPaths.EnsureExecutable(installPath);
            }

            // Mark this directory as an install location so startup recognises it
            // regardless of whether it matches the default AppData path.
            File.WriteAllText(Path.Combine(installDir, ".installed"), string.Empty);

            // Leave a redirect marker next to the source exe so that launching it
            // again opens the installed copy instead of showing the install screen.
            if (!isSamePath)
            {
                var sourceDir = Path.GetDirectoryName(source);
                if (sourceDir is not null)
                {
                    try { File.WriteAllText(Path.Combine(sourceDir, ".installed-at"), dest); }
                    catch { /* best-effort - failing just means no redirect */ }
                }
            }
            // Carry over persisted data from beside the downloaded exe, if present.
            MigrateFileIfNeeded("settings.json", currentPath, installDir);
            MigrateFileIfNeeded("timers.json", currentPath, installDir);
            return true;
        }
        catch (IOException)
        {
            // File may be locked by a running instance we could not detect.
            // The exe might already exist at the install path from a previous install -
            // ensure the sentinel is written so the app won't loop back to the install screen.
            if (!File.Exists(installPath))
                return false;
            try { File.WriteAllText(Path.Combine(installDir, ".installed"), string.Empty); }
            catch (IOException) { /* best effort - if this also fails, the install can't complete */ }

            // Also write the redirect for the source exe.
            var source = Path.GetFullPath(currentPath);
            var dest = Path.GetFullPath(installPath);
            if (!PlatformPaths.PathEquals(source, dest))
            {
                var sourceDir = Path.GetDirectoryName(source);
                if (sourceDir is not null)
                {
                    try { File.WriteAllText(Path.Combine(sourceDir, ".installed-at"), dest); }
                    catch { /* best-effort */ }
                }
            }

            return true;
        }
    }

    // Enforces single-instance semantics: takes the cross-process lock or signals the
    // already-running instance to show its window, then runs the Avalonia app loop.
    private static void RunAsPrimaryInstance(string[] args)
    {
        var instance = SingleInstance.Create();
        if (!instance.TryAcquire())
        {
            // Another instance owns the lock - hand the request over and exit.
            instance.SignalExisting();
            instance.Dispose();
            return;
        }

        Instance = instance;

        // The tray "Exit" command calls Environment.Exit, which skips the disposal below,
        // so release the lock from a process-exit handler as well. Disposal is idempotent.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => instance.Dispose();

        RunApp(args);
        Instance = null;
        instance.Dispose();
    }

    private static bool TrySignalExistingInstance()
    {
        using var probe = SingleInstance.Create();
        return probe.SignalExisting();
    }

    // Wires up global exception logging, then starts the Avalonia application loop.
    // The FileLoggerProvider is kept alive for the duration of the process so all
    // log messages are flushed before the process exits.
    private static void RunApp(string[] args)
    {
        var minLogLevel = Program.IsDebugMode ? LogLevel.Debug : LogLevel.Information;
        using var loggerProvider = new FileLoggerProvider(minLogLevel);
        var logger = loggerProvider.CreateLogger(nameof(Program));

        // Catch exceptions that escape all managed thread roots (non-UI threads, finalizers, etc.)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.LogCritical(ex, "Unhandled exception");
            else
                logger.LogCritical("Unhandled non-exception: {ExceptionObject}", e.ExceptionObject);
        };

        // Prevent fire-and-forget tasks from silently swallowing exceptions
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogCritical(e.Exception, "Unobserved task exception");
            // Mark as observed so the process does not crash on GC finalisation
            e.SetObserved();
        };

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    // Copies a data file from alongside the source exe to the install directory,
    // but only if it doesn't already exist at the destination (never overwrites).
    private static void MigrateFileIfNeeded(string fileName, string sourceExePath, string installDir)
    {
        var sourceDir = Path.GetDirectoryName(sourceExePath);
        if (sourceDir is null)
            return;
        var source = Path.Combine(sourceDir, fileName);
        var dest = Path.Combine(installDir, fileName);
        if (File.Exists(source) && !File.Exists(dest))
        {
            try { File.Copy(source, dest); }
            catch { /* best-effort migration */ }
        }
    }
}

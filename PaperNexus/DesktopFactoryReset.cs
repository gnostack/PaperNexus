namespace PaperNexus;

// The desktop's Factory Reset: deletes exactly the app's own data - settings.json in the
// settings folder, and the processed current wallpaper (current.*), timers.json and the logs
// folder in the data folder - then starts the app again with the arguments it was started
// with, less --startup, and ends this copy. The downloaded wallpapers, the executable and everything else
// beside it (the install sentinel, a build's libraries) are left alone.
internal sealed class DesktopFactoryReset : IFactoryReset, IAddSingleton<IFactoryReset>
{
    private readonly IAppFolders _appFolders;

    public DesktopFactoryReset(IAppFolders appFolders)
    {
        _appFolders = appFolders.ThrowIfNull();
    }

    // Nothing is logged: the logs folder is part of what is deleted.
    public Task ResetAsync()
    {
        DeleteAppData(_appFolders);

        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, PlatformPaths.ExecutableName);
        var arguments = RelaunchArguments(Program.Arguments);
        ShellOpener.LaunchExecutable(exePath, arguments);
        Environment.Exit(0);
        return Task.CompletedTask;
    }

    // The arguments the new copy starts with: this copy's, except --startup. The person pressed
    // the button in the window, so the new copy shows its window, as a copy started without
    // --startup does; a copy launched at sign-in would otherwise come back as a tray icon only.
    // --debug and anything else are kept.
    internal static IReadOnlyList<string> RelaunchArguments(IEnumerable<string> arguments)
    {
        var kept = arguments
            .Where(argument => !string.Equals(argument, StartupArgument, StringComparison.Ordinal))
            .ToList();
        return kept;
    }

    // The argument the sign-in entry starts the app with.
    private const string StartupArgument = "--startup";

    // Deletes the app's data in the given folders and returns the paths it deleted. A file
    // another process holds open is skipped rather than stopping the reset.
    internal static IReadOnlyList<string> DeleteAppData(IAppFolders folders)
    {
        var deleted = new List<string>();
        var settingsFile = Path.Combine(folders.SettingsFolder, SettingsStore.FileName);
        TryDeleteFile(settingsFile, deleted);

        var dataFolder = folders.DataFolder;
        if (Directory.Exists(dataFolder))
        {
            foreach (var current in Directory.GetFiles(dataFolder, "current.*"))
                TryDeleteFile(current, deleted);
        }
        TryDeleteFile(Path.Combine(dataFolder, JobTimerStore.FileName), deleted);

        var logs = FileLoggerProvider.LogDirectoryIn(dataFolder);
        try
        {
            if (Directory.Exists(logs))
            {
                Directory.Delete(logs, recursive: true);
                deleted.Add(logs);
            }
        }
        catch (IOException) { /* a log file held open elsewhere */ }
        catch (UnauthorizedAccessException) { }

        return deleted;
    }

    private static void TryDeleteFile(string path, List<string> deleted)
    {
        try
        {
            if (!File.Exists(path))
                return;
            File.Delete(path);
            deleted.Add(path);
        }
        catch (IOException) { /* held open elsewhere */ }
        catch (UnauthorizedAccessException) { }
    }
}

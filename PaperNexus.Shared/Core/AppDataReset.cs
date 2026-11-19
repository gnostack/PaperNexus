namespace PaperNexus.Core;

// What a Factory Reset deletes, on every device: the app's own data, and nothing the person
// downloaded. That is settings.json in the settings folder, and the processed current
// wallpaper (current.*), timers.json and the logs folder in the data folder. The downloaded
// wallpapers stay wherever they are (the desktop's Pictures folder, the phone's no-backup
// Wallpapers folder), and so does everything else in those folders (the desktop's executable
// and libraries, the phone's installed code).
public static class AppDataReset
{
    // Deletes the app's data in the given folders and returns the paths it deleted. A file
    // another process holds open is skipped rather than stopping the reset.
    public static IReadOnlyList<string> DeleteAppData(IAppFolders folders)
    {
        folders.ThrowIfNull();
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

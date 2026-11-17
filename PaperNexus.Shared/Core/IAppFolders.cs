namespace PaperNexus.Core;

// Where this device keeps the app's files. Each device lays them out its own way, so the
// host supplies the answers: shared code never assumes a folder beside the executable or a
// Pictures folder.
public interface IAppFolders
{
    // The folder that holds settings.json.
    public string SettingsFolder { get; }

    // The folder for the app's working files: the processed current wallpaper, timers.json
    // and the logs folder.
    public string DataFolder { get; }

    // The download folder used when the settings name none.
    public string DefaultDownloadFolder { get; }
}

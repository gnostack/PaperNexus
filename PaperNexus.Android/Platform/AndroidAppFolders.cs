using PaperNexus.Core;

namespace PaperNexus;

// Where the phone keeps the app's files, all inside the app's own storage (issue #203,
// decision 10): nothing appears in the phone's gallery, and uninstalling removes it all.
// - settings.json in the files folder, which Android's automatic backup copies;
// - downloaded wallpapers, the processed current wallpaper, timers.json and the logs in the
//   no-backup folder. Android stops backing up an app whose data passes 25 MB, and the
//   wallpapers alone do that; the pictures are downloaded again on a new phone.
// The app is installed on internal storage only (no android:installLocation), so these
// absolute paths, which settings.json records, never move.
internal sealed class AndroidAppFolders : IAppFolders, IAddSingleton<IAppFolders>
{
    public AndroidAppFolders()
    {
        var context = global::Android.App.Application.Context;
        SettingsFolder = context.FilesDir!.AbsolutePath;
        DataFolder = context.NoBackupFilesDir!.AbsolutePath;
        DefaultDownloadFolder = Path.Combine(DataFolder, "Wallpapers");
    }

    public string SettingsFolder { get; }

    public string DataFolder { get; }

    public string DefaultDownloadFolder { get; }
}

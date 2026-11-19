using Android.Content;
using PaperNexus.Core;

namespace PaperNexus;

// The phone's Factory Reset, with the desktop's meaning: deletes the app's own data
// (AppDataReset: settings.json, current.*, timers.json and the logs) and keeps the downloaded
// pictures, then starts the app again as it is on first open with pictures: the launcher's
// screen in a new task, in a new process, so nothing held in memory (the tabs' view models, the
// settings already read) survives the reset.
//
// The new process: the launcher activity is started as a new task that replaces this one
// (Intent.makeRestartActivityTask), then this process ends. Android starts a process for the
// activity it was asked to show. Measured on the Android 16 emulator, Debug and Release (issue
// #203, step 4): the old process died and a new one (a new process id) showed the Current tab
// about a second later, with the settings back to their defaults (no favorites, both default
// sources) and the downloaded pictures kept - with the network off, the tab set the next
// existing picture without downloading.
internal sealed class AndroidFactoryReset : IFactoryReset, IAddSingleton<IFactoryReset>
{
    private readonly IAppFolders _appFolders;

    public AndroidFactoryReset(IAppFolders appFolders)
    {
        _appFolders = appFolders.ThrowIfNull();
    }

    public Task ResetAsync()
    {
        AppDataReset.DeleteAppData(_appFolders);

        var context = global::Android.App.Application.Context;
        var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
        var restart = Intent.MakeRestartActivityTask(launch.Component)!;
        context.StartActivity(restart);
        global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
        return Task.CompletedTask;
    }
}

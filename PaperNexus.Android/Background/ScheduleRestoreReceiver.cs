using Android.Content;
using Microsoft.Extensions.DependencyInjection;

namespace PaperNexus;

// Arms the background slideshow and downloads again when what was armed is gone or no longer
// right (issue #203, step 5): after a restart (Android clears every alarm), after an app update,
// and after the clock or the time zone changes (a cron's next time moves). The app's process
// starting already plans (BackgroundWork.StartAsync); this plans once more, after Android's
// broadcast, for a process that was already running.
[BroadcastReceiver(Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged])]
public sealed class ScheduleRestoreReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var pending = GoAsync()!;
        var application = (MainApplication)context!.ApplicationContext!;
        var work = application.Services.GetRequiredService<BackgroundWork>();
        _ = Task.Run(async () =>
        {
            try
            {
                await work.PlanAsync();
            }
            finally
            {
                pending.Finish();
            }
        });
    }
}

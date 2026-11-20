using Android.Content;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;

namespace PaperNexus;

// The slideshow's alarm (AndroidBackgroundScheduler): Android starts the app's process if it is
// not running, MainApplication builds the container, and this changes the wallpaper through the
// shared BackgroundWork, which arms the next change. goAsync keeps the broadcast open while the
// change runs on a background thread; Android expects it to finish within 10 seconds.
//
// Measured on the Android 16 emulator with a Release build (issue #203, step 5): see
// docs/android.md, "Background slideshow and downloads". The time each change took, and how long
// before it the process started, are written to the app's log and to logcat (tag PaperNexus).
[BroadcastReceiver(Exported = false)]
public sealed class SlideshowAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var received = SystemClock.ElapsedRealtime();
        var occurrenceMs = intent?.GetLongExtra(AndroidBackgroundScheduler.OccurrenceExtra, 0) ?? 0;
        DateTimeOffset? occurrence = occurrenceMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(occurrenceMs) : null;
        var pending = GoAsync()!;
        var application = (MainApplication)context!.ApplicationContext!;
        var work = application.Services.GetRequiredService<BackgroundWork>();
        _ = Task.Run(async () =>
        {
            try
            {
                await work.RunSlideshowAsync(occurrence);
            }
            finally
            {
                var finished = SystemClock.ElapsedRealtime();
                var processAge = finished - global::Android.OS.Process.StartElapsedRealtime;
                global::Android.Util.Log.Info("PaperNexus",
                    $"Background change for {occurrence:O}: {finished - received} ms in the receiver, {processAge} ms since the process started.");
                pending.Finish();
            }
        });
    }
}

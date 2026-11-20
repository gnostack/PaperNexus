using Android.App;
using Android.App.Job;
using Android.Content;
using PaperNexus.Core;

namespace PaperNexus;

// Arms the phone's background work with Android (issue #203, step 5), so Android starts the
// app's process for it even when the app is closed:
//   - the next slideshow change: a one-shot alarm (AlarmManager, real time, waking the phone)
//     that SlideshowAlarmReceiver handles. With Exact timing off it is a window alarm: never
//     early, delivered within the 10 minutes after the time unless the phone is dozing (then at
//     its next maintenance window or when the screen turns on). With Exact timing on it is an
//     exact alarm that fires even while the phone dozes (setExactAndAllowWhileIdle), plus the
//     same window alarm as a backup: revoking the permission deletes the exact alarm and ends
//     the app with no broadcast, and the backup still changes the wallpaper. Both carry the same
//     time; the first to arrive changes the wallpaper and arms the next pair, which replaces the
//     other, and BackgroundWork changes nothing for a time already handled. Each change arms
//     the next.
//   - the next scheduled download run: a one-shot JobScheduler job (DownloadJobService) that
//     waits until its time and a network, and is kept across a restart.
// Arming again replaces what was armed: the alarm's pending intent and the job's id are fixed.
internal sealed class AndroidBackgroundScheduler : IBackgroundScheduler, IAddSingleton<IBackgroundScheduler>
{
    internal const int DownloadJobId = 1;
    internal const string OccurrenceExtra = "occurrence";
    private const int SlideshowRequestCode = 1;
    private const int ExactSlideshowRequestCode = 2;
    private static readonly long WindowMilliseconds = (long)TimeSpan.FromMinutes(10).TotalMilliseconds;

    public void ArmSlideshow(DateTimeOffset? at, bool exact)
    {
        var context = Application.Context;
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        if (at is not { } time)
        {
            alarms.Cancel(SlideshowIntent(context, SlideshowRequestCode, null));
            alarms.Cancel(SlideshowIntent(context, ExactSlideshowRequestCode, null));
            return;
        }

        var triggerAt = time.ToUnixTimeMilliseconds();
        var backup = SlideshowIntent(context, SlideshowRequestCode, triggerAt);
        alarms.SetWindow(AlarmType.RtcWakeup, triggerAt, WindowMilliseconds, backup);

        // Asked again here: the permission can be taken away between the plan and this call,
        // and an exact alarm without it throws
        var onTheMinute = SlideshowIntent(context, ExactSlideshowRequestCode, triggerAt);
        if (exact && alarms.CanScheduleExactAlarms())
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, onTheMinute);
        else
            alarms.Cancel(onTheMinute);
    }

    public void ArmDownloads(DateTimeOffset? at)
    {
        var context = Application.Context;
        var jobs = (JobScheduler)context.GetSystemService(Context.JobSchedulerService)!;
        if (at is not { } time)
        {
            jobs.Cancel(DownloadJobId);
            return;
        }

        var delay = time - DateTimeOffset.UtcNow;
        var latency = delay > TimeSpan.Zero ? (long)delay.TotalMilliseconds : 0;
        var service = new ComponentName(context, Java.Lang.Class.FromType(typeof(DownloadJobService)));
        var job = new JobInfo.Builder(DownloadJobId, service)
            .SetRequiredNetworkType(NetworkType.Any)!
            .SetMinimumLatency(latency)!
            .SetPersisted(true)!
            .Build()!;
        jobs.Schedule(job);
    }

    // An alarm's pending intent; the scheduled time travels with it, so the change knows which
    // occurrence it is for. The same request code replaces an alarm armed before: one for the
    // window alarm, one for the exact alarm.
    private static PendingIntent SlideshowIntent(Context context, int requestCode, long? triggerAt)
    {
        var intent = new Intent(context, typeof(SlideshowAlarmReceiver));
        if (triggerAt is { } time)
            intent.PutExtra(OccurrenceExtra, time);
        var flags = PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;
        var pending = PendingIntent.GetBroadcast(context, requestCode, intent, flags)!;
        return pending;
    }
}

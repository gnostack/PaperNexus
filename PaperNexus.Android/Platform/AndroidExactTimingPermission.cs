using Android.App;
using Android.Content;
using PaperNexus.Core;

namespace PaperNexus;

// Android's "Alarms & reminders" special access (SCHEDULE_EXACT_ALARM), which the slideshow's
// exact alarm needs (issue #203, step 5). Android 12 and 13 allow it from the start; Android 14
// and later deny it to a new install until the person allows it on the phone's own screen.
// Asking opens that screen (ACTION_REQUEST_SCHEDULE_EXACT_ALARM, for this app) from the activity;
// the answer is read when the activity resumes after the person comes back, as Android's
// documentation describes. Revoking it ends the app's process and deletes its exact alarms, with
// no broadcast; granting it sends SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED
// (ScheduleRestoreReceiver).
internal sealed class AndroidExactTimingPermission : IExactTimingPermission, IAddSingleton<IExactTimingPermission>
{
    private readonly object _lock = new();
    private Activity? _activity;
    private TaskCompletionSource<bool>? _asking;
    private bool _leftForTheScreen;

    public bool IsAllowed
    {
        get
        {
            var alarms = (AlarmManager)Application.Context.GetSystemService(Context.AlarmService)!;
            return alarms.CanScheduleExactAlarms();
        }
    }

    public Task<bool> AskAsync()
    {
        if (IsAllowed)
            return Task.FromResult(true);

        TaskCompletionSource<bool> asking;
        Activity? activity;
        lock (_lock)
        {
            // A second ask while the screen is open waits for the same answer
            if (_asking is not null)
                return _asking.Task;
            asking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _asking = asking;
            _leftForTheScreen = false;
            activity = _activity;
        }

        if (activity is null)
        {
            Finish(false);
            return asking.Task;
        }

        try
        {
            var packageUri = global::Android.Net.Uri.Parse($"package:{activity.PackageName}");
            var intent = new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm, packageUri);
            activity.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // A phone with no such screen
            Finish(false);
        }
        return asking.Task;
    }

    // The activity is on screen: when the person is back from the phone's screen, the answer
    // is whether exact timing is allowed now.
    internal void OnActivityResumed(Activity activity)
    {
        bool answer;
        lock (_lock)
        {
            _activity = activity;
            answer = _asking is not null && _leftForTheScreen;
        }
        if (answer)
            Finish(IsAllowed);
    }

    // The activity left the screen: while asking, that is the phone's screen opening.
    internal void OnActivityPaused()
    {
        lock (_lock)
        {
            if (_asking is not null)
                _leftForTheScreen = true;
        }
    }

    internal void OnActivityDestroyed(Activity activity)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_activity, activity))
                _activity = null;
        }
    }

    private void Finish(bool allowed)
    {
        TaskCompletionSource<bool>? asking;
        lock (_lock)
        {
            asking = _asking;
            _asking = null;
            _leftForTheScreen = false;
        }
        asking?.TrySetResult(allowed);
    }
}

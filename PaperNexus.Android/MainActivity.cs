using Android.OS;
using Android.Views;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using PaperNexus.Core;

namespace PaperNexus;

// The one activity, opened from the launcher. The Avalonia base class shows the view App supplies.
// It is declared in AndroidManifest.xml (the launcher intent filter and the configuration
// changes it handles itself, including asset paths); the attribute only fixes its Java name so
// that declaration can name it.
//
// It tells the Exact timing permission (AndroidExactTimingPermission) when it is on screen and
// when it leaves, so turning Exact timing on can open the phone's "Alarms & reminders" screen
// and read the answer when the person comes back.
//
// It also lays the screen out again when its drawing surface comes back after the activity was
// stopped (StoppedLayoutRepair).
[Activity(Name = "com.gnostack.papernexus.MainActivity")]
public class MainActivity : AvaloniaMainActivity
{
    private ISurfaceHolder? _surfaceHolder;
    private StoppedLayoutRepair? _layoutRepair;

    // Avalonia has built its view by now: the repair joins its drawing surface, once.
    protected override void OnStart()
    {
        base.OnStart();
        if (_surfaceHolder is not null)
            return;
        var content = FindViewById<ViewGroup>(global::Android.Resource.Id.Content);
        var surface = FindSurface(content);
        if (surface?.Holder is not { } holder)
            return;
        _layoutRepair = new StoppedLayoutRepair(() => Content as Avalonia.Visual);
        holder.AddCallback(_layoutRepair);
        _surfaceHolder = holder;
    }

    protected override void OnResume()
    {
        base.OnResume();
        Permission?.OnActivityResumed(this);
    }

    protected override void OnPause()
    {
        Permission?.OnActivityPaused();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        Permission?.OnActivityDestroyed(this);
        if (_surfaceHolder is not null && _layoutRepair is not null)
            _surfaceHolder.RemoveCallback(_layoutRepair);
        _surfaceHolder = null;
        _layoutRepair = null;
        base.OnDestroy();
    }

    // The surface Avalonia draws the screen on, inside its view.
    private static SurfaceView? FindSurface(View? view)
    {
        if (view is SurfaceView surface)
            return surface;
        if (view is not ViewGroup group)
            return null;
        for (var index = 0; index < group.ChildCount; index++)
        {
            var found = FindSurface(group.GetChildAt(index));
            if (found is not null)
                return found;
        }
        return null;
    }

    private AndroidExactTimingPermission? Permission =>
        ((MainApplication)Application!).Services.GetService<IExactTimingPermission>() as AndroidExactTimingPermission;
}

using Android.OS;
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
[Activity(Name = "com.gnostack.papernexus.MainActivity")]
public class MainActivity : AvaloniaMainActivity
{
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
        base.OnDestroy();
    }

    private AndroidExactTimingPermission? Permission =>
        ((MainApplication)Application!).Services.GetService<IExactTimingPermission>() as AndroidExactTimingPermission;
}

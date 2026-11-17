using Android.Content.PM;
using Avalonia.Android;

namespace PaperNexus;

// The one activity, opened from the launcher. The Avalonia base class shows the view App supplies.
// Rotation, window size and dark/light mode changes are handled by Avalonia inside the running
// activity instead of Android destroying and recreating it.
[Activity(
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}

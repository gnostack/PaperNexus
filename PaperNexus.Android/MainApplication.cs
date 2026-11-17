using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace PaperNexus;

// The Android application object. Android creates it before any activity; the Avalonia base class
// builds the app with the Android platform and the phone's App. The label, launcher icon and
// theme set here apply to every activity.
[Application(Label = "PaperNexus", Icon = "@mipmap/icon", Theme = "@style/PaperNexusTheme")]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    protected MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    // Uses the bundled Inter font, as the desktop host does in Program.
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}

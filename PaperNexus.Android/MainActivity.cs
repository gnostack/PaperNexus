using Avalonia.Android;

namespace PaperNexus;

// The one activity, opened from the launcher. The Avalonia base class shows the view App supplies.
// It is declared in AndroidManifest.xml (the launcher intent filter and the configuration
// changes it handles itself, including asset paths); the attribute only fixes its Java name so
// that declaration can name it.
[Activity(Name = "com.gnostack.papernexus.MainActivity")]
public class MainActivity : AvaloniaMainActivity
{
}

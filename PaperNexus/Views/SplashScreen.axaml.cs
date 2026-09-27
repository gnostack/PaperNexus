using Avalonia.Controls;
using PaperNexus.Core;

namespace PaperNexus.Views;

public partial class SplashScreen : Window
{
    // Created from the container so the splash egg is recorded through the injected
    // recorder rather than a static path to settings.json.
    public SplashScreen(EasterEggProgress easterEggProgress)
    {
        easterEggProgress.ThrowIfNull();
        InitializeComponent();
        VersionText.Text = App.AppVersion;
        // Usually the ordinary "Starting up..." line; occasionally something else, so it
        // reads as a surprise rather than a gimmick that wears out by the third launch.
        var splashLine = EasterEggs.SplashMessage(Random.Shared);
        StatusText.Text = splashLine;
        // This egg has no overlay to record it, so it is recorded where it is shown.
        if (splashLine != EasterEggs.DefaultSplashMessage)
            _ = easterEggProgress.RecordAsync(EasterEggCatalog.Splash);
    }
}

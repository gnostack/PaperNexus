using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PaperNexus.Views;

namespace PaperNexus;

// The phone's Avalonia app. The desktop App stays in the desktop host because it owns the tray
// and the windows. Avalonia.Application is written out in full because the Android SDK's
// Android.App.Application is also in scope through the implicit usings.
public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Each activity asks the factory for its view, built from the services MainApplication
        // created for the process. PageNavigationHost passes the system bars' insets to the
        // page, so the bottom tabs sit above Android's navigation bar instead of being drawn
        // underneath it (Android 15 and later always draw apps edge to edge).
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            var application = (MainApplication)global::Android.App.Application.Context;
            activity.MainViewFactory = () => new PageNavigationHost
            {
                Page = application.Services.GetRequiredService<MainView>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

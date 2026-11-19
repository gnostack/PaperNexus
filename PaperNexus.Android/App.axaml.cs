using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.GestureRecognizers;
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
        // underneath it (Android 15 and later always draw apps edge to edge), and passes
        // Android's back button to it. The NavigationPage around the tabs has no bar of its
        // own; it is what shows the Sources tab's form over the tabs as a modal page.
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            var application = (MainApplication)global::Android.App.Application.Context;
            activity.MainViewFactory = () =>
            {
                var mainView = application.Services.GetRequiredService<MainView>();
                NavigationPage.SetHasNavigationBar(mainView, false);
                var navigation = new NavigationPage { Content = mainView, IsGestureEnabled = false };
                // NavigationPage's swipe-back recognizer takes the touch from whatever is under
                // the finger once it moves sideways, anywhere on the page, even with gestures
                // off; a dragged switch knob then never hears the finger lift and stops halfway.
                // The app pushes no page a swipe could go back from (the form is modal).
                foreach (var swipe in navigation.GestureRecognizers.OfType<SwipeGestureRecognizer>())
                    swipe.IsEnabled = false;
                var host = new PageNavigationHost { Page = navigation };
                return host;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

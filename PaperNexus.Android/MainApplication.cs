using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using PaperNexus.Views;

namespace PaperNexus;

// The Android application object. Android creates it before any activity; the Avalonia base class
// builds the app with the Android platform and the phone's App. The label, launcher icon and
// theme set here apply to every activity.
//
// It is also the phone's composition root: it builds the service container once per process,
// so the shared engine (settings, sources, downloads, switching) runs for as long as Android
// keeps the process, and every Android part of the app (the screen now; the widget and the
// background slideshow in later steps) reaches the same services through it.
[Application(Label = "PaperNexus", Icon = "@mipmap/icon", Theme = "@style/PaperNexusTheme")]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    private IHost? _host;
    private FileLoggerProvider? _loggerProvider;

    protected MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    // The process's services. Built in OnCreate, before Avalonia creates the App.
    internal IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("The services are built when the application is created.");

    public override void OnCreate()
    {
        var folders = new AndroidAppFolders();
        _loggerProvider = new FileLoggerProvider(FileLoggerProvider.LogDirectoryIn(folders.DataFolder));
        LogUnhandledExceptions(_loggerProvider.CreateLogger(nameof(MainApplication)));

        // HostBuilder rather than Host.CreateDefaultBuilder: the phone has no configuration
        // files, console or environment variables to read.
        _host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(b => b.AddProvider(_loggerProvider));
                services.AddSingleton<HttpWallpaperSourceService>();
                // Screens are per request: each new activity gets a fresh view and view model.
                services.AddTransient<CurrentWallpaperViewModel>();
                services.AddTransient<MainView>();
                // The shared services, then this host's implementations of the interfaces they
                // need (folders, wallpaper applier). No AddInProcessSchedules: the slideshow
                // does not run on a timer inside the app's process (issue #203, step 5).
                services.AddServicesFrom(typeof(CurrentWallpaperViewModel).Assembly);
                services.AddServicesFrom(typeof(MainApplication).Assembly);
            })
            .Build();

        // Starts the scheduled downloads; the first run begins at once
        _ = _host.StartAsync();

        base.OnCreate();
    }

    // Uses the bundled Inter font, as the desktop host does in Program.
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    // Writes crashes and unobserved task failures to the log, as the desktop's Program does.
    private static void LogUnhandledExceptions(ILogger logger)
    {
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            logger.LogCritical(e.Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogCritical(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }
}

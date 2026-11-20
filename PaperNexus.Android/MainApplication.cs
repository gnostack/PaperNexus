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
// keeps the process, and every Android part of the app (the screen, the background slideshow's
// alarm and download job, and the long-press menu's ShortcutActivity) reaches the same services
// through it.
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
        // The log level follows the debug mode setting (DebugLogLevel applies the stored one
        // when the host starts); the phone has no --debug start.
        var logLevels = new LogLevelSwitch(startedInDebugMode: false);
        _loggerProvider = new FileLoggerProvider(FileLoggerProvider.LogDirectoryIn(folders.DataFolder), logLevels);
        LogUnhandledExceptions(_loggerProvider.CreateLogger(nameof(MainApplication)));

        // HostBuilder rather than Host.CreateDefaultBuilder: the phone has no configuration
        // files, console or environment variables to read.
        _host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(b => b.AddFileLogger(_loggerProvider));
                services.AddSingleton(logLevels);
                services.AddSingleton<HttpWallpaperSourceService>();
                // Each new activity gets a fresh view, but the tabs' view models and the selected
                // tab live as long as the process: Android recreates the activity (a new font
                // size, language or display size) and the new view attaches to the same state,
                // so the person sees what they were looking at. The view models are created on
                // the screen's thread when the first view asks for them.
                services.AddSingleton<CurrentWallpaperViewModel>();
                services.AddSingleton<GalleryViewModel>();
                services.AddSingleton<FavoritesViewModel>();
                services.AddSingleton<SourcesViewModel>();
                // The Settings tab's groups are the desktop window's, held for the process here
                services.AddSingleton<DownloadSettingsViewModel>();
                services.AddSingleton<SlideshowSettingsViewModel>();
                services.AddSingleton<DebugSettingsViewModel>();
                services.AddSingleton<AboutViewModel>();
                services.AddSingleton<SecretsViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainScreenState>();
                // The easter egg playing, so a recreated screen continues it
                services.AddSingleton<EasterEggPlayback>();
                services.AddTransient<MainView>();
                // The shared services, then this host's implementations of the interfaces they
                // need (folders, wallpaper applier, background scheduler). No
                // AddInProcessSchedules: the slideshow and the scheduled downloads do not run on
                // timers inside the app's process, which Android can stop at any time; Android
                // starts the process for each (AndroidBackgroundScheduler, BackgroundWork,
                // issue #203 step 5).
                services.AddServicesFrom(typeof(CurrentWallpaperViewModel).Assembly);
                services.AddServicesFrom(typeof(MainApplication).Assembly);
            })
            .Build();

        // Applies the stored log level and arms the background slideshow and downloads
        // (BackgroundWork). It runs whatever started the process - the screen, an alarm, a job
        // or a broadcast - and starts no download or wallpaper change itself.
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

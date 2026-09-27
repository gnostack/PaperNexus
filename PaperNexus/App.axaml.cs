using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PaperNexus.Views;
using PaperNexus.ViewModels;
using Path = System.IO.Path;

namespace PaperNexus;

public partial class App : Application
{
    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is Version v
            ? $"v{v.Major}"
            : "v0";

    private IHost? _backgroundHost;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SplashScreen? _splashScreen;
    private bool _exiting;

    internal bool IsExiting => _exiting;
    internal IServiceProvider? Services => _backgroundHost?.Services;
    private ILogger<App>? Logger => _backgroundHost?.Services.GetService<ILogger<App>>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep running when any window is closed
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // In install mode, show only the install screen - skip all service/tray setup.
            if (Program.IsInstallMode)
            {
                new Views.InstallScreen().Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // Start background wallpaper services (download + switch)
            _backgroundHost = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    var minLogLevel = Program.IsDebugMode ? LogLevel.Debug : LogLevel.Information;
                    services.AddLogging(b => b.AddProvider(new FileLoggerProvider(minLogLevel)));
                    services.AddSingleton<HttpWallpaperSourceService>();
                    // Auto-discover and register all IAddSingleton / IAddHostedSingleton / IScheduleScopedJob implementations
                    services.AddServicesFrom(typeof(App).Assembly);
                })
                .Build();

            // Install the Linux application launcher and icon before touching startup
            // registration, because the autostart entry points at the icon this writes.
            // Doing it on every launch keeps the Exec line correct if the app moves and
            // repairs installs made before the launcher existed.
            try
            {
                DesktopEntry.Install(
                    Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, PlatformPaths.ExecutableName),
                    () => AssetLoader.Open(new Uri("avares://PaperNexus/Assets/logo.png")));
            }
            catch (Exception ex) { Logger?.LogError(ex, "Failed to install the desktop launcher."); }

            // Apply startup registration based on the persisted setting
            _ = WallpaperNexusSettings.LoadAsync().ContinueWith(t =>
            {
                try { StartupRegistration.Update(t.Result.RunOnStartup); }
                catch (Exception ex) { Logger?.LogError(ex, "Failed to apply startup registration on launch."); }
            });

            var launchedOnStartup = desktop.Args?.Contains("--startup") == true;

            // Show splash screen while background services start (skip on startup and debug mode)
            if (!launchedOnStartup && !Program.IsDebugMode)
            {
                _splashScreen = new SplashScreen();
                _splashScreen.Show();
            }

            // Close the splash once the background host has started, but show it for at least 2 seconds.
            // In debug mode skip the delay and go straight to the main window - avoids a window-count-zero
            // gap that would trigger OnLastWindowClose shutdown before the main window opens.
            var splashDelay = Program.IsDebugMode ? Task.CompletedTask : Task.Delay(2000);
            _ = Task.WhenAll(_backgroundHost.StartAsync(), splashDelay).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() =>
                {
                    _splashScreen?.Close();
                    _splashScreen = null;
                    if (!launchedOnStartup)
                        ShowMainWindow();
                }));

            // Show only the tray icon - no window at startup
            SetupTrayIcon(desktop);

            // Monitor for show-UI signals from second instances. The listener blocks while
            // waiting, so it runs on its own thread and polls internally for _exiting.
            // Window creation must happen on the UI thread, hence the dispatcher post.
            var singleInstance = Program.Instance;
            if (singleInstance is not null)
            {
                _ = Task.Run(() => singleInstance.Listen(
                    onShowRequested: () => Dispatcher.UIThread.Post(ShowMainWindow),
                    stopWhen: () => _exiting));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Builds the system tray icon and context menu.
    // Each menu action runs switcher/downloader work on a background thread to avoid
    // blocking the UI thread, then surfaces errors through the settings window if it is open.
    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var openItem = new NativeMenuItem { Header = "Open Settings", Icon = CreateGearIcon() };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        var nextItem = new NativeMenuItem { Header = "Next Wallpaper", Icon = CreatePlayIcon() };
        nextItem.Click += async (_, _) =>
        {
            try
            {
                var switcher = _backgroundHost?.Services.GetService<ISwitchWallpaper>();
                if (switcher is null)
                    return;
                var next = await Task.Run(switcher.SwitchToNextAsync);
                // If no wallpaper was found, trigger a fresh download then retry the switch
                if (next is null)
                {
                    var downloader = _backgroundHost?.Services.GetService<IDownloadWallpapers>();
                    if (downloader is not null)
                    {
                        await Task.Run(downloader.DownloadAllAsync);
                        next = await Task.Run(switcher.SwitchToNextAsync);
                    }
                }
                if (next is null)
                    Logger?.LogWarning("Tray 'Next Wallpaper' failed: no wallpapers available after download retry.");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error switching wallpaper from tray.");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_mainWindow?.DataContext is WallpaperConfigViewModel vm)
                        _ = vm.ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
                });
            }
        };
        menu.Items.Add(nextItem);

        var randomItem = new NativeMenuItem { Header = "Random Wallpaper", Icon = CreateDiceIcon() };
        randomItem.Click += async (_, _) =>
        {
            try
            {
                var switcher = _backgroundHost?.Services.GetService<ISwitchWallpaper>();
                if (switcher is null)
                    return;
                var next = await Task.Run(switcher.SwitchToRandomAsync);
                // Same fallback pattern as "Next Wallpaper" - download if the folder is empty
                if (next is null)
                {
                    var downloader = _backgroundHost?.Services.GetService<IDownloadWallpapers>();
                    if (downloader is not null)
                    {
                        await Task.Run(downloader.DownloadAllAsync);
                        next = await Task.Run(switcher.SwitchToRandomAsync);
                    }
                }
                if (next is null)
                    Logger?.LogWarning("Tray 'Random Wallpaper' failed: no wallpapers available after download retry.");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error switching to random wallpaper from tray.");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_mainWindow?.DataContext is WallpaperConfigViewModel vm)
                        _ = vm.ShowTransientStatusAsync($"✗ Error switching wallpaper: {ex.Message}");
                });
            }
        };
        menu.Items.Add(randomItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem { Header = "Exit", Icon = CreatePowerIcon() };
        exitItem.Click += (_, _) => ExitApplication(desktop);
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Paper Nexus",
            Icon = CreateTrayIcon(),
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    // Ensures the settings window is created if needed, then brings it to the foreground.
    // Always called via Dispatcher.UIThread.Post to be safe from background threads.
    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += OnMainWindowClosed;
            }
            else if (_mainWindow.DataContext is WallpaperConfigViewModel vm)
            {
                // Refresh preview in case the wallpaper changed while the window was not visible
                vm.RefreshPreviewImage();
            }
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    // Called when the settings window is closed. Decides whether to stay resident in
    // the tray or to shut down, based on the MinimizeToTray setting and debug mode.
    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Closed -= OnMainWindowClosed;
            _mainWindow = null;
        }

        // Reclaim UI memory now that the settings window is closed
        GC.Collect(2, GCCollectionMode.Forced, blocking: false);

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // In debug mode, always exit on close
        if (Program.IsDebugMode)
        {
            ExitApplication(desktop);
            return;
        }

        // Check if minimize-to-tray is disabled; if so, exit on close
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync();
            if (!settings.MinimizeToTray)
                ExitApplication(desktop);
        }
        catch (Exception ex) { Logger?.LogWarning(ex, "Failed to load settings when checking MinimizeToTray; staying resident."); }
    }

    // Performs a graceful shutdown: hides the tray icon, stops background services
    // with a 3-second timeout, then forces process exit to clean up any stray threads.
    private async void ExitApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _exiting = true;
        // Hide the icon immediately so the user doesn't see a phantom tray entry
        if (_trayIcon != null)
            _trayIcon.IsVisible = false;
        if (_backgroundHost is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _backgroundHost.StopAsync(cts.Token); }
            catch (Exception ex) { Logger?.LogError(ex, "Error stopping background host during exit."); }
        }
        desktop.Shutdown();
        // Call Environment.Exit to ensure background threads (IPC listener, etc.) are terminated
        Environment.Exit(0);
    }

    // Loads the app logo asset and scales it to the standard 32×32 tray icon size.
    private static WindowIcon CreateTrayIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://PaperNexus/Assets/logo.png"));
        var png = ImageResizing.ResizeToSquarePng(stream, 32);
        using var ms = new MemoryStream(png);
        return new WindowIcon(new Avalonia.Media.Imaging.Bitmap(ms));
    }

    // Wraps a 16×16 menu icon drawn by MenuIcons (PNG bytes) in an Avalonia Bitmap.
    private static Avalonia.Media.Imaging.Bitmap CreateMenuIcon(byte[] png)
    {
        using var ms = new MemoryStream(png);
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }

    private static Avalonia.Media.Imaging.Bitmap CreateGearIcon() => CreateMenuIcon(MenuIcons.Gear());

    private static Avalonia.Media.Imaging.Bitmap CreatePlayIcon() => CreateMenuIcon(MenuIcons.Play());

    private static Avalonia.Media.Imaging.Bitmap CreateDiceIcon() => CreateMenuIcon(MenuIcons.Dice());

    private static Avalonia.Media.Imaging.Bitmap CreatePowerIcon() => CreateMenuIcon(MenuIcons.Power());

}

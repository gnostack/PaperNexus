using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using System.Globalization;

namespace PaperNexus.Views;

// Strips the " - <urlfile>" suffix that was appended during download, returning
// the human-readable wallpaper title for display in XAML bindings.
public class PathToDisplayNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var lastSep = nameWithoutExt.LastIndexOf(" - ", StringComparison.Ordinal);
        return lastSep > 0 ? nameWithoutExt[..lastSep] : nameWithoutExt;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class FavoriteColorConverter : IValueConverter
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#E06C75"));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BanColorConverter : IValueConverter
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#F97316"));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class MainWindow : Window
{
    private int _versionClickCount;
    private DateTime _lastVersionClick = DateTime.MinValue;

    private static readonly string[] WallpaperCompliments =
    [
        "🎨 Excellent taste in wallpapers!",
        "🖼️ A true connoisseur of desktop aesthetics.",
        "✨ Your monitor is very lucky.",
        "🌅 You really know how to set the mood.",
        "🏆 Best wallpaper picker of the year award: you.",
        "👀 Someone's got an eye for beauty.",
        "🌟 This wallpaper? Chef's kiss.",
    ];

    private readonly KonamiSequence _konami = new();
    private readonly ISettingsStore _settingsStore;
    private readonly Func<EasterEggListDialog> _createSecretsDialog;

    // Created per request from the container, which hands in the settings store, a fresh
    // view model, the egg recorder and a factory for the Secrets dialog. There is
    // deliberately no parameterless constructor: a window built without these would have no
    // store, and the store is the only way into settings.json.
    public MainWindow(
        ISettingsStore settingsStore,
        WallpaperConfigViewModel viewModel,
        EasterEggProgress easterEggProgress,
        Func<EasterEggListDialog> createSecretsDialog)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _createSecretsDialog = createSecretsDialog.ThrowIfNull();
        InitializeComponent();
        // Avalonia builds the overlay from markup, so it cannot take constructor arguments;
        // the window hands it the recorder once here, and Play stays the only place a
        // discovery is recorded.
        EggOverlay.UseProgress(easterEggProgress.ThrowIfNull());
        viewModel.ThrowIfNull();
        // The ViewModel decides whether an egg fires; the window owns how it is drawn.
        viewModel.EasterEggTriggered += show => Dispatcher.UIThread.Post(() => EggOverlay.Play(show));
        DataContext = viewModel;
        // Use the tunnel phase so Shift+Click can be intercepted before the button's own handler fires
        UpdateButton.AddHandler(InputElement.PointerPressedEvent, OnUpdateButtonPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (MainTabControl?.SelectedIndex == 2 && DataContext is WallpaperConfigViewModel vm)
            vm.LoadGalleryCommand.Execute(null);
    }

    private void OnGalleryContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is GalleryItem item)
            _ = item.LoadAsync();
    }

    private void OnGalleryContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container.DataContext is GalleryItem item)
            item.DisposeThumbnail();
    }

    private async void OnWallpaperNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;
        var msg = WallpaperCompliments[Random.Shared.Next(WallpaperCompliments.Length)];
        await vm.ShowTransientStatusAsync(msg, 4000);
    }

    // Easter egg: clicking the version label 5 times within 3 seconds shows a hidden message.
    // The click counter resets if more than 3 seconds elapses between clicks.
    private async void OnVersionLabelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastVersionClick).TotalSeconds > 3)
            _versionClickCount = 0;
        _lastVersionClick = now;
        _versionClickCount++;

        if (_versionClickCount >= 5)
        {
            _versionClickCount = 0;
            EggOverlay.Play(EasterEggShows.Version());
        }
    }

    // Opens the secrets checklist. Discovery state is read when the dialog opens rather
    // than cached, so an egg found moments ago is already ticked.
    private async void OnSecretsClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = _createSecretsDialog();
        await dialog.LoadAsync();
        await dialog.ShowDialog(this);
    }

    // Shift+Click on the update button triggers a forced re-install of the current version,
    // useful for testing the update pipeline without needing a newer build to be published.
    private void OnUpdateButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        // Mark handled so the normal click command does not also fire
        e.Handled = true;
        if (DataContext is WallpaperConfigViewModel vm)
            vm.CheckForUpdatesForceCommand.Execute(null);
    }

    // Implements the Konami Code easter egg. The sequence matching lives in KonamiSequence
    // so it can be unit tested without driving a window; this override only supplies keys
    // and shows the reward.
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Esc closes a playing overlay before anything else looks at the key.
        if (e.Key == Key.Escape && EggOverlay.IsPlaying)
        {
            EggOverlay.Dismiss();
            e.Handled = true;
            return;
        }

        if (!_konami.Advance(e.Key.ToString()))
            return;

        EggOverlay.Play(EasterEggShows.Konami());
    }

    // Restores the saved window position and size from settings, then triggers the ViewModel
    // to load all settings values into its properties so the UI reflects the current state.
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var settings = await _settingsStore.LoadAsync();
        if (settings.WindowX.HasValue && settings.WindowY.HasValue)
            Position = new PixelPoint((int)settings.WindowX.Value, (int)settings.WindowY.Value);
        if (settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
        {
            Width = settings.WindowWidth.Value;
            Height = settings.WindowHeight.Value;
        }
        if (DataContext is WallpaperConfigViewModel vm)
            await vm.LoadAsync();
    }

    private async void AddSource_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new WallpaperSourceDialog();
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved && dialog.Result is not null && DataContext is WallpaperConfigViewModel vm)
        {
            vm.Sources.Add(dialog.Result);
            vm.SelectedSource = dialog.Result;
        }
    }

    private async void EditSource_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenEditDialog();
    }

    private async void SourceList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        await OpenEditDialog();
    }

    // Opens the source edit dialog for the currently selected source.
    // Replaces the item at its original index rather than appending to preserve list order.
    private async Task OpenEditDialog()
    {
        if (DataContext is not WallpaperConfigViewModel vm || vm.SelectedSource is null)
            return;

        var dialog = new WallpaperSourceDialog(vm.SelectedSource);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved && dialog.Result is not null)
        {
            var index = vm.Sources.IndexOf(vm.SelectedSource);
            if (index < 0)
            {
                // Source was removed while the dialog was open - just append the result
                vm.Sources.Add(dialog.Result);
            }
            else
            {
                vm.Sources.RemoveAt(index);
                vm.Sources.Insert(index, dialog.Result);
            }
            vm.SelectedSource = dialog.Result;
        }
    }

    private void DeleteSourceRow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WallpaperSource source } && DataContext is WallpaperConfigViewModel vm)
            vm.Sources.Remove(source);
    }

    private void RemoveFavoriteRow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string path } && DataContext is WallpaperConfigViewModel vm)
        {
            vm.SelectedFavorite = path;
            vm.RemoveFavoriteCommand.Execute(null);
        }
    }

    private void SetFavoriteAsCurrentRow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string path } && DataContext is WallpaperConfigViewModel vm)
        {
            vm.SelectedFavorite = path;
            vm.SetFavoriteAsCurrentCommand.Execute(null);
        }
    }

    // Intercepts the checkbox when the user attempts to turn minimize-to-tray OFF.
    // Reverts the checkbox first, then shows a warning dialog; only applies the change
    // if the user explicitly confirms, preventing accidental loss of background operation.
    private async void MinimizeToTray_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;

        // Only intercept when toggling OFF (now unchecked)
        if (MinimizeToTrayCheckBox.IsChecked.GetValueOrDefault())
            return;

        // Revert and confirm
        MinimizeToTrayCheckBox.IsChecked = true;
        vm.MinimizeToTray = true;

        var confirmed = await ShowYesNoDialog(
            "Disable Minimize to Tray",
            "Without minimize to tray, closing the window will fully exit PaperNexus.\n\nAll the automatic greatness - scheduled wallpaper rotation, auto-downloads, and background updates - will stop until you manually reopen the app.\n\nDisable anyway?");

        if (confirmed)
        {
            MinimizeToTrayCheckBox.IsChecked = false;
            vm.MinimizeToTray = false;
        }
    }

    // Shows a confirmation dialog before permanently deleting the current wallpaper file.
    // The dialog is built programmatically rather than from AXAML to keep it self-contained.
    private async void DeleteWallpaper_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var cancelBtn = new Button { Content = "Cancel" };
        ToolTip.SetTip(cancelBtn, "Cancel deletion");
        var deleteBtn = new Button
        {
            Content = "Delete",
            Foreground = new SolidColorBrush(Color.Parse("#E06C75")),
        };
        ToolTip.SetTip(deleteBtn, "Permanently delete wallpaper file");
        var dialog = new Window
        {
            Title = "Delete Wallpaper",
            Width = 300,
            Height = 130,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Delete this wallpaper file from disk?", TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancelBtn, deleteBtn },
                    },
                },
            },
        };
        cancelBtn.Click += (_, _) => dialog.Close(false);
        deleteBtn.Click += (_, _) => dialog.Close(true);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed && DataContext is WallpaperConfigViewModel vm)
            vm.DeleteCurrentWallpaperCommand.Execute(null);
    }

    private async void BrowseFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Wallpapers Folder",
                AllowMultiple = false,
            });
            if (result.Count > 0 && DataContext is WallpaperConfigViewModel vm)
                vm.Folder = result[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            if (DataContext is WallpaperConfigViewModel vm)
                vm.StatusMessage = $"✗ Error browsing for folder: {ex.Message}";
        }
    }

    // Persists window geometry and cleans up the ViewModel before the window closes.
    // DataContext is nulled after Cleanup so that Avalonia bindings don't attempt
    // to access disposed resources during the final rendering pass.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Application.Current is App { IsExiting: false })
        {
            // Capture position before the window is destroyed
            _ = SaveWindowPositionAsync(Position.X, Position.Y, Width, Height);

            // Release ViewModel resources to reduce memory while running in tray
            if (DataContext is WallpaperConfigViewModel vm)
                vm.Cleanup();
            DataContext = null;
        }
        base.OnClosing(e);
    }

    private static readonly string[] DebugConfirmations =
    [
        "Are you sure you want to enable debug mode?",
        "Are you really sure?",
        "Debug mode is for developers. Are you a developer?",
        "Do you pinky promise you know what you're doing?",
        "Have you consulted your system administrator?",
        "Have you backed up your wallpapers? (You should.)",
        "On a scale of 1 to 10, is your certainty at least 7?",
        "Did you eat breakfast today? Mental clarity is important for decisions like this.",
        "Have you considered that your wallpapers might judge you for this?",
        "If a tree falls in a forest and no one hears it, should debug mode still be enabled?",
        "The last person who enabled debug mode was never seen again. Continue?",
        "We've notified your emergency contacts. Proceed anyway?",
        "This action may void your wallpaper warranty. Accept?",
        "NASA has been consulted and they recommend against it. Override NASA?",
        "The prophecy foretold of one who would enable debug mode. Is it you, The Chosen One?",
        "Final answer? No take-backs. This is legally binding in 47 countries.",
        "Okay fine. But we warned you. SEVENTEEN TIMES. Enable debug mode?",
    ];

    // Requires the user to click through all 17 increasingly absurd confirmation dialogs
    // before debug mode is enabled. If they cancel at any point the checkbox is left unchecked.
    private async void DebugMode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;

        // Only intercept when toggling ON
        if (!DebugModeCheckBox.IsChecked.GetValueOrDefault())
            return;

        // Revert the check immediately - we'll set it if all 17 pass
        DebugModeCheckBox.IsChecked = false;
        vm.DebugMode = false;

        for (var i = 0; i < DebugConfirmations.Length; i++)
        {
            var confirmed = await ShowYesNoDialog(
                $"Confirmation {i + 1} of {DebugConfirmations.Length}",
                DebugConfirmations[i]);

            if (!confirmed)
            {
                await vm.ShowTransientStatusAsync(
                    $"Debug mode cancelled. You only made it through {i} of {DebugConfirmations.Length} confirmations.", 5000);
                return;
            }
        }

        DebugModeCheckBox.IsChecked = true;
        vm.DebugMode = true;
        await vm.ShowTransientStatusAsync("Debug mode enabled. You absolute legend.", 5000);
    }

    // Performs a factory reset after two confirmation dialogs: deletes all application data
    // files in the install directory (settings, logs, timers, current.* wallpaper), then
    // relaunches the exe so the app starts fresh. The user's wallpapers folder is untouched.
    private async void FactoryReset_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ShowYesNoDialog(
            "Factory Reset",
            "This will delete ALL application data (settings, logs, timers) and restart.\n\nYour downloaded wallpapers folder will NOT be deleted.\n\nAre you sure?");

        if (!confirmed)
            return;

        var doubleConfirmed = await ShowYesNoDialog(
            "Factory Reset - Final Confirmation",
            "This cannot be undone. Seriously. Last chance to back out.");

        if (!doubleConfirmed)
            return;

        try
        {
            var appDir = AppContext.BaseDirectory;
            var exePath = Environment.ProcessPath
                ?? Path.Combine(appDir, PlatformPaths.ExecutableName);

            // Delete everything in the app directory except the running exe
            foreach (var file in Directory.GetFiles(appDir))
            {
                // Skip the running exe - it cannot be deleted while in use and is not "data"
                if (PlatformPaths.PathEquals(Path.GetFullPath(file), Path.GetFullPath(exePath)))
                    continue;
                try { File.Delete(file); }
                catch { /* skip locked files */ }
            }

            foreach (var dir in Directory.GetDirectories(appDir))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* skip locked directories */ }
            }

            // Restart the application
            ShellOpener.LaunchExecutable(exePath);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            if (DataContext is WallpaperConfigViewModel vm)
                await vm.ShowTransientStatusAsync($"Factory reset failed: {ex.Message}", 5000);
        }
    }

    // Generic Yes/No modal dialog built programmatically. Returns true if the user
    // clicks Yes, false if they click No or close the window.
    private async Task<bool> ShowYesNoDialog(string title, string message)
    {
        var noBtn = new Button { Content = "No" };
        ToolTip.SetTip(noBtn, "Cancel");
        var yesBtn = new Button { Content = "Yes" };
        ToolTip.SetTip(yesBtn, "Confirm");
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { noBtn, yesBtn },
                    },
                },
            },
        };
        noBtn.Click += (_, _) => dialog.Close(false);
        yesBtn.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task SaveWindowPositionAsync(int x, int y, double w, double h)
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            settings.WindowX = x;
            settings.WindowY = y;
            settings.WindowWidth = w;
            settings.WindowHeight = h;
            await _settingsStore.SaveAsync(settings);
        }
        catch { }
    }
}

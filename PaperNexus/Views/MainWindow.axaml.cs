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
    private readonly EasterEggRequests _easterEggs;
    private readonly DebugSettingsViewModel _debug;
    private readonly Func<EasterEggListDialog> _createSecretsDialog;

    // Created per request from the container, which hands in the settings store, a fresh
    // view model, the egg recorder and request channel, and a factory for the Secrets dialog.
    // There is deliberately no parameterless constructor: a window built without these would
    // have no store, and the store is the only way into settings.json.
    public MainWindow(
        ISettingsStore settingsStore,
        WallpaperConfigViewModel viewModel,
        EasterEggProgress easterEggProgress,
        EasterEggRequests easterEggs,
        Func<EasterEggListDialog> createSecretsDialog)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _easterEggs = easterEggs.ThrowIfNull();
        _createSecretsDialog = createSecretsDialog.ThrowIfNull();
        InitializeComponent();
        // Avalonia builds the overlay from markup, so it cannot take constructor arguments;
        // the window hands it the recorder once here, and Play stays the only place a
        // discovery is recorded.
        EggOverlay.UseProgress(easterEggProgress.ThrowIfNull());
        viewModel.ThrowIfNull();
        // The view models decide whether an egg fires; the window owns how it is drawn. The
        // request channel outlives the window, so the window lets go of it when it closes.
        _easterEggs.Requested += OnEasterEggRequested;
        // A source row's edit button asks the window to open the source dialog for that row.
        viewModel.EditSourceRequested += item => _ = OpenEditDialog(item);
        // Debug mode and Factory Reset ask their questions through the group; the window
        // shows each in a Yes/No dialog.
        _debug = viewModel.Debug;
        _debug.PropertyChanged += OnDebugPropertyChanged;
        DataContext = viewModel;
        // Use the tunnel phase so Shift+Click can be intercepted before the button's own handler fires
        UpdateButton.AddHandler(InputElement.PointerPressedEvent, OnUpdateButtonPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (MainTabControl?.SelectedIndex == 2 && DataContext is WallpaperConfigViewModel vm)
            vm.LoadGalleryCommand.Execute(null);
    }

    // A card's thumbnail is held only while the list shows the card; CardThumbnailHolder
    // releases it when the list clears or reuses the card's container.
    private void OnGalleryContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        CardThumbnailHolder.Prepared(e.Container, ImageResizing.ThumbnailWidth);
    }

    private void OnGalleryContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        CardThumbnailHolder.Clearing(e.Container);
    }

    private async void OnWallpaperNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;
        var msg = WallpaperCompliments[Random.Shared.Next(WallpaperCompliments.Length)];
        await vm.ShowTransientStatusAsync(msg, 4000);
    }

    // Plays an egg a view model asked for, on the window's thread.
    private void OnEasterEggRequested(EasterEggShow show)
    {
        Dispatcher.UIThread.Post(() => EggOverlay.Play(show));
    }

    // Easter egg: clicking the version label 5 times, each within 3 seconds of the last,
    // plays the version egg (AboutViewModel counts the clicks).
    private void OnVersionLabelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is WallpaperConfigViewModel vm)
            vm.About.TapVersion();
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

        _easterEggs.Request(EasterEggShows.Konami());
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

    // Opens the source dialog with an empty form. The form saves the source itself; the
    // window then shows the new row selected.
    private async void AddSource_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;
        var form = vm.CreateSourceForm(null);
        await ShowSourceDialogAsync(vm, form);
    }

    // Double-tapping a row edits it, as the row's edit button does.
    private async void SourceList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is WallpaperConfigViewModel { SelectedSource: { } selected })
            await OpenEditDialog(selected);
    }

    // Opens the source dialog filled with the row's source. The form replaces the source in
    // its place when saved.
    private async Task OpenEditDialog(SourceItem item)
    {
        if (DataContext is not WallpaperConfigViewModel vm)
            return;
        var form = vm.CreateSourceForm(item);
        await ShowSourceDialogAsync(vm, form);
    }

    private async Task ShowSourceDialogAsync(WallpaperConfigViewModel vm, SourceFormViewModel form)
    {
        var dialog = new WallpaperSourceDialog(form);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved)
            await vm.SourceSavedAsync(form);
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
        vm.Desktop.MinimizeToTray = true;

        var confirmed = await ShowYesNoDialog(
            "Disable Minimize to Tray",
            "Without minimize to tray, closing the window will fully exit PaperNexus.\n\nAll the automatic greatness - scheduled wallpaper rotation, auto-downloads, and background updates - will stop until you manually reopen the app.\n\nDisable anyway?");

        if (confirmed)
        {
            MinimizeToTrayCheckBox.IsChecked = false;
            vm.Desktop.MinimizeToTray = false;
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
                vm.Desktop.Folder = result[0].Path.LocalPath;
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

    // The egg request channel lives as long as the process, so a closed window stops
    // listening to it, and to its debug group.
    protected override void OnClosed(EventArgs e)
    {
        _easterEggs.Requested -= OnEasterEggRequested;
        _debug.PropertyChanged -= OnDebugPropertyChanged;
        base.OnClosed(e);
    }

    // The debug mode box shows the group's value and only asks to change it: turning it off
    // takes effect at once, turning it on starts the seventeen questions, and the box shows
    // on only once the last one is answered yes.
    private void DebugMode_Click(object? sender, RoutedEventArgs e)
    {
        var turnOn = DebugModeCheckBox.IsChecked.GetValueOrDefault();
        DebugModeCheckBox.SetCurrentValue(CheckBox.IsCheckedProperty, _debug.DebugMode);
        _debug.RequestDebugMode(turnOn);
    }

    // Asks the two Factory Reset questions, through the debug group.
    private void FactoryReset_Click(object? sender, RoutedEventArgs e)
    {
        _debug.RequestFactoryReset();
    }

    // Shows each question the debug group asks in a Yes/No dialog and passes the answer back.
    private async void OnDebugPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DebugSettingsViewModel.PendingQuestion))
            return;
        var question = _debug.PendingQuestion;
        if (question is null)
            return;
        var confirmed = await ShowYesNoDialog(question.Title, question.Message);
        if (ReferenceEquals(_debug.PendingQuestion, question))
            await _debug.AnswerAsync(confirmed);
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
            // The window owns only its geometry; everything else is left as saved
            await _settingsStore.UpdateAsync(latest =>
            {
                latest.WindowX = x;
                latest.WindowY = y;
                latest.WindowWidth = w;
                latest.WindowHeight = h;
            });
        }
        catch { }
    }
}

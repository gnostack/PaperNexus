using Avalonia.Controls;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Created from the service container for each activity, with a fresh view model for the
// Current tab. When Android recreates the activity the old view leaves the screen and its view
// model stops listening to the shared switcher.
public partial class MainView : TabbedPage
{
    private readonly CurrentWallpaperViewModel _currentWallpaper;
    private bool _loaded;

    public MainView(CurrentWallpaperViewModel currentWallpaper)
    {
        _currentWallpaper = currentWallpaper;
        InitializeComponent();
        CurrentWallpaper.DataContext = currentWallpaper;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += (_, _) => _currentWallpaper.Dispose();
    }

    // The tab decides what to show the first time it appears.
    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_loaded)
            return;
        _loaded = true;
        _ = _currentWallpaper.LoadAsync();
    }
}

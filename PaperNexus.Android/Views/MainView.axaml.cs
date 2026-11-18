using Avalonia.Controls;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Created from the service container for each activity, with fresh view models for the Current,
// Gallery and Favorites tabs. When Android recreates the activity the old view leaves the screen
// and its view models stop listening to the shared switcher and library.
public partial class MainView : TabbedPage
{
    private readonly CurrentWallpaperViewModel _currentWallpaper;
    private readonly GalleryViewModel _gallery;
    private readonly FavoritesViewModel _favorites;
    private bool _loaded;

    public MainView(CurrentWallpaperViewModel currentWallpaper, GalleryViewModel gallery, FavoritesViewModel favorites)
    {
        _currentWallpaper = currentWallpaper;
        _gallery = gallery;
        _favorites = favorites;
        InitializeComponent();
        CurrentWallpaper.DataContext = currentWallpaper;
        Gallery.DataContext = gallery;
        Favorites.DataContext = favorites;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += (_, _) => DisposeViewModels();
    }

    // The Current tab decides what to show the first time it appears.
    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_loaded)
            return;
        _loaded = true;
        _ = _currentWallpaper.LoadAsync();
    }

    // Gallery and Favorites list their pictures the first time their tab is chosen; after
    // that the library's change notices keep them current. SelectionChanged is raised for the
    // first selection and for every tab change (measured on the emulator).
    private void OnSelectionChanged(object? sender, PageSelectionChangedEventArgs e)
    {
        if (ReferenceEquals(SelectedPage, GalleryPage) && !_gallery.IsLoaded)
            _ = _gallery.LoadAsync();
        else if (ReferenceEquals(SelectedPage, FavoritesPage) && !_favorites.IsLoaded)
            _ = _favorites.LoadAsync();
    }

    private void DisposeViewModels()
    {
        _currentWallpaper.Dispose();
        _gallery.Dispose();
        _favorites.Dispose();
    }
}

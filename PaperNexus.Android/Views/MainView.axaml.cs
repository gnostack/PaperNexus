using Avalonia.Controls;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Created from the service container for each activity. The tabs' view models and the selected
// tab outlive it (see MainApplication): this view attaches to them when it reaches the screen
// and lets go of them when it leaves, so a recreated activity's view shows the same tab, cards
// and confirmation line, and a dead view is never updated by a view model.
public partial class MainView : TabbedPage
{
    private readonly CurrentWallpaperViewModel _currentWallpaper;
    private readonly GalleryViewModel _gallery;
    private readonly FavoritesViewModel _favorites;
    private readonly MainScreenState _state;
    private bool _isOnScreen;

    public MainView(
        CurrentWallpaperViewModel currentWallpaper,
        GalleryViewModel gallery,
        FavoritesViewModel favorites,
        MainScreenState state)
    {
        _currentWallpaper = currentWallpaper;
        _gallery = gallery;
        _favorites = favorites;
        _state = state;
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    // Binds the tabs to the shared view models, returns to the tab shown last, and does the
    // Current tab's first-open work (once per process).
    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        CurrentWallpaper.DataContext = _currentWallpaper;
        Gallery.DataContext = _gallery;
        Favorites.DataContext = _favorites;
        SelectedIndex = _state.SelectedTabIndex;
        _isOnScreen = true;
        LoadSelectedTab();
        _ = _currentWallpaper.EnsureLoadedAsync();
    }

    // Unbinding drops every subscription this view's controls hold on the view models, which
    // outlive it.
    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isOnScreen = false;
        CurrentWallpaper.DataContext = null;
        Gallery.DataContext = null;
        Favorites.DataContext = null;
    }

    // Remembers the chosen tab. Gallery and Favorites list their pictures the first time their
    // tab is chosen; after that the library's change notices keep them current.
    // SelectionChanged is raised for the first selection and for every tab change (measured on
    // the emulator); a change raised while the view is not on the screen is not the person's.
    private void OnSelectionChanged(object? sender, PageSelectionChangedEventArgs e)
    {
        if (!_isOnScreen || SelectedIndex < 0)
            return;
        _state.SelectedTabIndex = SelectedIndex;
        LoadSelectedTab();
    }

    private void LoadSelectedTab()
    {
        if (ReferenceEquals(SelectedPage, GalleryPage))
            _ = _gallery.EnsureLoadedAsync();
        else if (ReferenceEquals(SelectedPage, FavoritesPage))
            _ = _favorites.EnsureLoadedAsync();
    }
}

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Created from the service container for each activity. The tabs' view models and the selected
// tab outlive it (see MainApplication): this view attaches to them when it reaches the screen
// and lets go of them when it leaves, so a recreated activity's view shows the same tab, cards
// and confirmation line, and a dead view is never updated by a view model.
//
// It also shows the Sources tab's add or edit form: while the sources view model holds an open
// form, a SourceFormPage for it covers the tabs as a modal page of the NavigationPage App puts
// around this view, and a recreated screen opens it again with what was typed so far. The
// Settings tab's Secrets page is shown the same way while the settings view model says it is open.
public partial class MainView : TabbedPage
{
    private readonly CurrentWallpaperViewModel _currentWallpaper;
    private readonly GalleryViewModel _gallery;
    private readonly FavoritesViewModel _favorites;
    private readonly SourcesViewModel _sources;
    private readonly SettingsViewModel _settings;
    private readonly MainScreenState _state;
    private bool _isOnScreen;

    // The form page this view shows, or null; and the navigation page that shows it.
    private SourceFormPage? _formPage;
    private SecretsPage? _secretsPage;
    private NavigationPage? _navigation;

    public MainView(
        CurrentWallpaperViewModel currentWallpaper,
        GalleryViewModel gallery,
        FavoritesViewModel favorites,
        SourcesViewModel sources,
        SettingsViewModel settings,
        MainScreenState state)
    {
        _currentWallpaper = currentWallpaper;
        _gallery = gallery;
        _favorites = favorites;
        _sources = sources;
        _settings = settings;
        _state = state;
        InitializeComponent();
        // Android's back reaches the selected tab's page: on Settings it closes an open list or
        // answers an open question with No before it can leave the app.
        SettingsPage.PageNavigationSystemBackButtonPressed += (_, e) =>
        {
            if (Settings.HandleBack())
                e.Handled = true;
        };
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
        Sources.DataContext = _sources;
        Settings.DataContext = _settings;
        SelectedIndex = _state.SelectedTabIndex;
        _isOnScreen = true;
        LoadSelectedTab();
        _ = _currentWallpaper.EnsureLoadedAsync();

        _sources.PropertyChanged += OnSourcesPropertyChanged;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _navigation = Navigation as NavigationPage;
        if (_navigation is not null)
            _navigation.ModalPopped += OnModalPopped;
        // After the first layout, so the navigation page can take a modal page
        Dispatcher.UIThread.Post(() =>
        {
            _ = ShowFormAsync();
            _ = ShowSecretsAsync();
        }, DispatcherPriority.Loaded);
    }

    // Unbinding drops every subscription this view's controls hold on the view models, which
    // outlive it.
    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isOnScreen = false;
        CurrentWallpaper.DataContext = null;
        Gallery.DataContext = null;
        Favorites.DataContext = null;
        Sources.DataContext = null;
        _settings.Flush();
        Settings.DataContext = null;
        _sources.PropertyChanged -= OnSourcesPropertyChanged;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        if (_navigation is not null)
            _navigation.ModalPopped -= OnModalPopped;
        _navigation = null;
        if (_formPage is not null)
            _formPage.DataContext = null;
        _formPage = null;
        if (_secretsPage is not null)
            _secretsPage.DataContext = null;
        _secretsPage = null;
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
        else if (ReferenceEquals(SelectedPage, SourcesPage))
            _ = _sources.EnsureLoadedAsync();
        else if (ReferenceEquals(SelectedPage, SettingsPage))
            _ = _settings.EnsureLoadedAsync();
    }

    // Add or a row's edit opened a form, or the form finished.
    private void OnSourcesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SourcesViewModel.Form))
            return;
        if (_sources.Form is null)
            _ = CloseFormAsync();
        else
            _ = ShowFormAsync();
    }

    // Covers the tabs with the open form, once.
    private async Task ShowFormAsync()
    {
        var form = _sources.Form;
        if (!_isOnScreen || form is null || _formPage is not null || _navigation is null)
            return;
        var page = new SourceFormPage { DataContext = form };
        _formPage = page;
        await _navigation.PushModalAsync(page);
    }

    // Takes the form page away when the form is done.
    private async Task CloseFormAsync()
    {
        var page = _formPage;
        _formPage = null;
        if (page is null || _navigation is null)
            return;
        if (_navigation.ModalStack.Contains(page))
            await _navigation.PopModalAsync();
        page.DataContext = null;
    }

    // The navigation page took a page away itself (Android's back): the form is cancelled, as
    // its Cancel button does, and Secrets is closed, as its Close button does.
    private void OnModalPopped(object? sender, ModalPoppedEventArgs e)
    {
        if (ReferenceEquals(e.Modal, _secretsPage))
        {
            _secretsPage = null;
            _settings.CloseSecretsCommand.Execute(null);
            return;
        }
        if (!ReferenceEquals(e.Modal, _formPage))
            return;
        _formPage = null;
        _sources.Form?.CancelCommand.Execute(null);
    }

    // Secrets was opened or closed on the Settings tab.
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.IsSecretsOpen))
            return;
        if (_settings.IsSecretsOpen)
            _ = ShowSecretsAsync();
        else
            _ = CloseSecretsAsync();
    }

    // Covers the tabs with the Secrets page, once.
    private async Task ShowSecretsAsync()
    {
        if (!_isOnScreen || !_settings.IsSecretsOpen || _secretsPage is not null || _navigation is null)
            return;
        var page = new SecretsPage { DataContext = _settings };
        _secretsPage = page;
        await _navigation.PushModalAsync(page);
    }

    private async Task CloseSecretsAsync()
    {
        var page = _secretsPage;
        _secretsPage = null;
        if (page is null || _navigation is null)
            return;
        if (_navigation.ModalStack.Contains(page))
            await _navigation.PopModalAsync();
        page.DataContext = null;
    }
}

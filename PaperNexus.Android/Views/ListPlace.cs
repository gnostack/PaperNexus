using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Keeps a list's scroll place in its view model, which outlives the view: the place is recorded
// as the person scrolls, and a view that reaches the screen (a recreated activity's new view,
// or the tab chosen again) returns to it once its cards are laid out. The list's rows all have
// the same height (PictureFrame), so the place is the same picture.
internal sealed class ListPlace
{
    private readonly Control _view;
    private readonly ScrollViewer _scroller;
    private bool _restoring;

    public ListPlace(Control view, ScrollViewer scroller)
    {
        _view = view;
        _scroller = scroller;
        _scroller.ScrollChanged += OnScrollChanged;
    }

    private PictureListViewModel? ViewModel => _view.DataContext as PictureListViewModel;

    // Called when the view reaches the screen. The offset is set after the layout pass that
    // builds the cards; until then the scroll position the new layout reports is not the
    // person's and is not recorded.
    public void Restore()
    {
        _restoring = true;
        Dispatcher.UIThread.Post(() =>
        {
            var viewModel = ViewModel;
            if (viewModel is not null && viewModel.ScrollOffset > 0)
                _scroller.Offset = new Vector(_scroller.Offset.X, viewModel.ScrollOffset);
            _restoring = false;
        }, DispatcherPriority.Background);
    }

    // Records where the person scrolled. Ignored while restoring, and when the view has let go
    // of its view model (its list is being emptied).
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var viewModel = ViewModel;
        if (_restoring || viewModel is null || !_view.IsAttachedToVisualTree())
            return;
        viewModel.ScrollOffset = _scroller.Offset.Y;
    }
}

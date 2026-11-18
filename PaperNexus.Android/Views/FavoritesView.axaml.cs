using Avalonia.Controls;

namespace PaperNexus.Views;

// The Favorites tab's cards. Its view model is set by MainView, which also tells it when the
// tab is first shown. The thumbnails of its cards are held only while the tab is on the
// screen, and its scroll place is kept in the view model across recreated screens.
public partial class FavoritesView : UserControl
{
    private readonly ListPlace _place;

    public FavoritesView()
    {
        InitializeComponent();
        _place = new ListPlace(this, Scroller);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CardThumbnails.LoadRealized(Cards);
        _place.Restore();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CardThumbnails.ReleaseAll(Cards);
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e) => CardThumbnails.Load(Cards, e);

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e) => CardThumbnails.Release(e);
}

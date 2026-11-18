using Avalonia.Controls;

namespace PaperNexus.Views;

// The Gallery tab's cards. Its view model is set by MainView, which also tells it when the tab
// is first shown.
public partial class GalleryView : UserControl
{
    public GalleryView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CardThumbnails.LoadRealized(Cards);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CardThumbnails.ReleaseAll(Cards);
    }

        private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e) => CardThumbnails.Load(Cards, e);

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e) => CardThumbnails.Release(e);
}

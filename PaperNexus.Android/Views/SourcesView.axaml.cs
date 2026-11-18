using Avalonia.Controls;

namespace PaperNexus.Views;

// The Sources tab's rows. Its view model is set by MainView, which also tells it when the tab is
// first shown and shows the add or edit form. Its scroll place is kept in the view model across
// recreated screens.
public partial class SourcesView : UserControl
{
    private readonly ListPlace _place;

    public SourcesView()
    {
        InitializeComponent();
        _place = new ListPlace(this, Scroller);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _place.Restore();
    }
}

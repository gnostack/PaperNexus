using Avalonia.Controls;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// The desktop's add or edit source dialog. Its fields, sentences and buttons are the shared
// SourceFormViewModel, which the phone's form also shows; the dialog only closes itself when the
// form is done, answering true when the source was saved.
public partial class WallpaperSourceDialog : Window
{
    private SourceFormViewModel? _form;

    public WallpaperSourceDialog()
    {
        Opacity = 0;
        InitializeComponent();
    }

    public WallpaperSourceDialog(SourceFormViewModel form) : this()
    {
        _form = form.ThrowIfNull();
        _form.Finished += OnFinished;
        DataContext = _form;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Opacity = 1;
    }

    private void OnFinished(bool saved) => Close(saved);

    // Cancels a test still running when the dialog closes, whether by Save, Cancel or the
    // title-bar X, and lets go of the form.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        var form = _form;
        _form = null;
        if (form is null)
            return;
        form.Finished -= OnFinished;
        form.StopTest();
    }
}

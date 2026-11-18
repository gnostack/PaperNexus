using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PaperNexus.Views;

// The phone's in-page dialog, for any screen that must ask before acting. Place it last in the
// page's root panel so it covers the page, then await ShowAsync: it answers true for the
// confirm button, false for cancel or a tap on the dimmed page. Each button's tooltip is also
// what accessibility tools announce.
public partial class ConfirmationDialog : UserControl
{
    private static readonly IBrush DestructiveBrush = new SolidColorBrush(Color.Parse("#E06C75"));

    private TaskCompletionSource<bool>? _answer;

    public ConfirmationDialog()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Answer(false);
        ConfirmButton.Click += (_, _) => Answer(true);
        Backdrop.PointerPressed += OnBackdropPressed;
    }

    // Shows the question and waits for the answer. A destructive confirm is drawn in the
    // delete color. Asking again while open answers the earlier question with false.
    public Task<bool> ShowAsync(
        string message,
        string confirmText,
        string confirmToolTip,
        string cancelText,
        string cancelToolTip,
        bool isDestructive)
    {
        _answer?.TrySetResult(false);
        _answer = new TaskCompletionSource<bool>();

        MessageText.Text = message;
        CancelButton.Content = cancelText;
        ToolTip.SetTip(CancelButton, cancelToolTip);
        ConfirmButton.Content = confirmText;
        ToolTip.SetTip(ConfirmButton, confirmToolTip);
        if (isDestructive)
            ConfirmButton.Foreground = DestructiveBrush;
        else
            ConfirmButton.ClearValue(ForegroundProperty);

        IsVisible = true;
        return _answer.Task;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        Answer(false);
    }

    private void Answer(bool confirmed)
    {
        IsVisible = false;
        var answer = _answer;
        _answer = null;
        answer?.TrySetResult(confirmed);
    }
}

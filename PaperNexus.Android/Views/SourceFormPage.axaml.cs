using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// The full-screen add or edit source form. MainView opens it over the tabs as a modal page with
// the sources view model's open form as its data, and closes it when the form is done. Android's
// back button or gesture cancels the form, as the desktop dialog's Cancel does.
//
// The soft keyboard never covers the field being typed in or the buttons: the page is drawn
// above the part of the screen the keyboard covers, and the field that has the focus is
// scrolled into view. The keyboard's Next key moves to the next field.
public partial class SourceFormPage : ContentPage
{
    private IInputPane? _inputPane;
    private IInsetsManager? _insetsManager;

    public SourceFormPage()
    {
        InitializeComponent();
        TextBox[] order = [NameBox, UrlBox, ImageUrlJPathBox, TitleJPathBox, CronBox];
        for (var index = 0; index < order.Length; index++)
        {
            var next = index + 1 < order.Length ? order[index + 1] : null;
            order[index].KeyDown += (_, e) => OnFieldKeyDown(e, next);
            order[index].GotFocus += OnFieldGotFocus;
        }
    }

    // Back cancels the form; the form being done closes this page.
    protected override bool OnSystemBackButtonPressed()
    {
        if (DataContext is SourceFormViewModel form)
            form.CancelCommand.Execute(null);
        return true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        _inputPane = topLevel?.InputPane;
        if (_inputPane is not null)
            _inputPane.StateChanged += OnInputPaneStateChanged;
        // The keyboard changing height while open (a suggestion strip shown for one field and
        // not the next) raises no state change, only a safe area change (measured).
        _insetsManager = topLevel?.InsetsManager;
        if (_insetsManager is not null)
            _insetsManager.SafeAreaChanged += OnSafeAreaChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_inputPane is not null)
            _inputPane.StateChanged -= OnInputPaneStateChanged;
        if (_insetsManager is not null)
            _insetsManager.SafeAreaChanged -= OnSafeAreaChanged;
        _inputPane = null;
        _insetsManager = null;
    }

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e) => LiftAboveKeyboard();

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => LiftAboveKeyboard();

    // Lifts the page's content above the keyboard by however much of it the keyboard covers.
    // The page reaches the bottom of the screen and already keeps its content above Android's
    // navigation bar (its safe area padding), so only the part of the keyboard above that bar
    // is added. Measured on the emulator: without taking the bar off, the buttons floated a
    // bar's height above the keyboard.
    private void LiftAboveKeyboard()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var pane = _inputPane;
        var covered = 0.0;
        if (topLevel is not null && pane is not null && pane.State == InputPaneState.Open)
        {
            var pageBottom = this.TranslatePoint(new Point(0, Bounds.Height), topLevel)?.Y ?? 0;
            var contentBottom = pageBottom - SafeAreaPadding.Bottom;
            covered = Math.Max(0, contentBottom - pane.OccludedRect.Top);
        }

        if (Layout.Margin.Bottom != covered)
            Layout.Margin = new Thickness(0, 0, 0, covered);
        BringFocusedFieldIntoView();
    }

    private void OnFieldGotFocus(object? sender, FocusChangedEventArgs e) => BringFocusedFieldIntoView();

    // After the layout settles (the keyboard may still be opening), scrolls the focused field,
    // with its label and hint, into view.
    private void BringFocusedFieldIntoView()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;
            var field = focused?.Parent as Control ?? focused;
            field?.BringIntoView();
        }, DispatcherPriority.Background);
    }

    // Enter is what the keyboard's Next key sends: it moves to the next field. On the last
    // field the key is Done, which the keyboard handles itself.
    private static void OnFieldKeyDown(KeyEventArgs e, TextBox? next)
    {
        if (e.Key != Key.Enter || next is null)
            return;
        e.Handled = true;
        next.Focus();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PaperNexus.Views;

// Keeps a screen's fields clear of Android's on-screen keyboard. The app is drawn edge to edge
// and Android does not make the window shorter for the keyboard (measured, docs/android.md), so
// the screen's content is lifted by however much of it the keyboard covers, and the field that
// has the focus (with its label and hint: the nearest ancestor with the class "field") is
// scrolled into view. Used by the add or edit source form and the Settings tab.
internal sealed class KeyboardLift
{
    private readonly Control _owner;
    private readonly Control _layout;
    private readonly double _bottomClearance;
    private IInputPane? _inputPane;
    private IInsetsManager? _insetsManager;

    // owner is the screen; layout is the panel whose bottom margin makes room for the keyboard;
    // bottomClearance is how much of the layout's bottom something else covers (the Settings
    // tab's confirmation line), which the focused field is kept above as well.
    public KeyboardLift(Control owner, Control layout, double bottomClearance = 0)
    {
        _owner = owner;
        _layout = layout;
        _bottomClearance = bottomClearance;
        _owner.AttachedToVisualTree += OnAttached;
        _owner.DetachedFromVisualTree += OnDetached;
        _owner.AddHandler(InputElement.GotFocusEvent, (_, _) => BringFocusedFieldIntoView(), handledEventsToo: true);
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(_owner);
        _inputPane = topLevel?.InputPane;
        if (_inputPane is not null)
            _inputPane.StateChanged += OnInputPaneStateChanged;
        // The keyboard changing height while open (a suggestion strip shown for one field and
        // not the next) raises no state change, only a safe area change (measured).
        _insetsManager = topLevel?.InsetsManager;
        if (_insetsManager is not null)
            _insetsManager.SafeAreaChanged += OnSafeAreaChanged;
        // The keyboard may have opened or closed while this screen was away (the Settings tab
        // when another tab is chosen): the lift is set from where it is now, once laid out
        Dispatcher.UIThread.Post(Lift, DispatcherPriority.Loaded);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_inputPane is not null)
            _inputPane.StateChanged -= OnInputPaneStateChanged;
        if (_insetsManager is not null)
            _insetsManager.SafeAreaChanged -= OnSafeAreaChanged;
        _inputPane = null;
        _insetsManager = null;
    }

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e) => Lift();

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => Lift();

    // Lifts the content above the keyboard by however much of it the keyboard covers. The
    // content's bottom is where the layout's bottom would be without the lift (its bounds plus
    // the margin already added), which already keeps clear of Android's navigation bar and of
    // the tab bar; only the part of the keyboard above that is added.
    private void Lift()
    {
        var topLevel = TopLevel.GetTopLevel(_owner);
        var pane = _inputPane;
        var covered = 0.0;
        if (topLevel is not null && pane is not null && pane.State == InputPaneState.Open)
        {
            var layoutBottom = _layout.TranslatePoint(new Point(0, _layout.Bounds.Height), topLevel)?.Y ?? 0;
            var contentBottom = layoutBottom + _layout.Margin.Bottom;
            covered = Math.Max(0, contentBottom - pane.OccludedRect.Top);
        }

        if (_layout.Margin.Bottom != covered)
            _layout.Margin = new Thickness(0, 0, 0, covered);
        BringFocusedFieldIntoView();
    }

    // After the layout settles (the keyboard may still be opening), scrolls the focused field,
    // with its label and hint, into view.
    private void BringFocusedFieldIntoView()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(_owner)?.FocusManager?.GetFocusedElement() as Control;
            if (focused is null || !_owner.IsVisualAncestorOf(focused))
                return;
            var field = focused.GetSelfAndVisualAncestors()
                .OfType<Control>()
                .TakeWhile(control => !ReferenceEquals(control, _owner))
                .FirstOrDefault(control => control.Classes.Contains("field"));
            var target = field ?? focused;
            var bounds = new Rect(0, 0, target.Bounds.Width, target.Bounds.Height + _bottomClearance);
            target.BringIntoView(bounds);
        }, DispatcherPriority.Background);
    }
}

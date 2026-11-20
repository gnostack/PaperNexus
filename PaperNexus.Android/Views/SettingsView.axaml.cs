using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PaperNexus.Core;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// The Settings tab's list (see SettingsView.axaml). Its view model is set by MainView, which
// also tells it when the tab is first shown and passes it Android's back. It shows the view
// model's open picker and the debug group's open question in their boxes, so a recreated screen
// shows them again; its scroll place is kept in the view model too. The keyboard never covers
// the number or the cron expression being typed (KeyboardLift).
//
// It also watches the strokes made on the tab for the Konami code (KonamiGestures): it only
// looks, never takes a touch from anything, and ignores strokes that start on a control.
public partial class SettingsView : UserControl
{
    private readonly ListPlace _place;
    private SettingsViewModel? _viewModel;

    // The stroke being watched: its finger, where and when it went down, and how far the list
    // has scrolled under it (the list takes the finger once it scrolls).
    private IPointer? _strokePointer;
    private Point _strokeStart;
    private ulong _strokeStartTime;
    private Vector _strokeScrolled;

    public SettingsView()
    {
        InitializeComponent();
        _place = new ListPlace(this, Scroller);
        // The focused field is kept above the confirmation line too, which covers the list's bottom
        _ = new KeyboardLift(this, Layout, bottomClearance: 72);
        ChoiceBox.Chosen += index => _viewModel?.Choose(index);
        ChoiceBox.Cancelled += () => _viewModel?.ClosePicker();
        // Tunnel, and handled events too: the tab sees every touch before the control under it
        // and whatever that control does with it
        AddHandler(PointerPressedEvent, OnStrokePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnStrokeReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.ScrollGestureEvent, OnStrokeScrolled, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(InputElement.ScrollGestureEndedEvent, OnStrokeScrollEnded, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // A tap on the version, five within three seconds of each other playing its egg.
    private void Version_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel?.About.TapVersion();
    }

    // A finger went down. A second finger, a stroke starting on a control (whose own touch it
    // is), or one made while a list or question is open is not watched.
    private void OnStrokePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_strokePointer is not null)
        {
            _strokePointer = null;
            return;
        }
        if (ChoiceBox.IsVisible || Question.IsVisible || StartsOnControl(e.Source))
            return;
        _strokePointer = e.Pointer;
        _strokeStart = e.GetPosition(this);
        _strokeStartTime = e.Timestamp;
        _strokeScrolled = default;
    }

    // The finger lifted without the list scrolling: the stroke is where it went.
    private void OnStrokeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _strokePointer))
            return;
        var end = e.GetPosition(this);
        var moved = end - _strokeStart;
        var duration = TimeSpan.FromMilliseconds(e.Timestamp - _strokeStartTime);
        EndStroke(moved.X, moved.Y, duration);
    }

    // The list scrolls by the opposite of the finger's movement.
    private void OnStrokeScrolled(object? sender, ScrollGestureEventArgs e)
    {
        if (_strokePointer is not null)
            _strokeScrolled += e.Delta;
    }

    // The list took the finger and has stopped: the stroke is the way the list was pushed.
    private void OnStrokeScrollEnded(object? sender, ScrollGestureEndedEventArgs e)
    {
        if (_strokePointer is null || _strokeScrolled == default)
            return;
        EndStroke(-_strokeScrolled.X, -_strokeScrolled.Y, TimeSpan.MaxValue);
    }

    private void EndStroke(double dx, double dy, TimeSpan duration)
    {
        _strokePointer = null;
        var stroke = KonamiGestures.Classify(dx, dy, duration);
        _viewModel?.AddStroke(stroke);
    }

    private bool StartsOnControl(object? source)
    {
        if (source is not Visual visual)
            return false;
        foreach (var element in visual.GetSelfAndVisualAncestors())
        {
            if (ReferenceEquals(element, this))
                return false;
            if (element is Avalonia.Controls.Button or ToggleSwitch or TextBox or NumericUpDown)
                return true;
        }
        return false;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _place.Restore();
    }

    // A change still waiting for its half second is saved when the tab leaves the screen.
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _viewModel?.Flush();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Debug.PropertyChanged -= OnDebugPropertyChanged;
            _viewModel.Slideshow.PropertyChanged -= OnSlideshowPropertyChanged;
        }
        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is null)
        {
            ChoiceBox.Show(null);
            return;
        }
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Debug.PropertyChanged += OnDebugPropertyChanged;
        _viewModel.Slideshow.PropertyChanged += OnSlideshowPropertyChanged;
        ChoiceBox.Show(_viewModel.Picker);
        ShowDebugMode();
        ShowExactTiming();
        ShowQuestion();
    }

    // The debug mode switch shows the group's value and only asks to change it, as the desktop's
    // box does: turning it off takes effect at once, turning it on starts the seventeen questions,
    // and the switch shows on only once the last one is answered yes. A tap or a drag both land here.
    private void DebugMode_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var debug = _viewModel?.Debug;
        if (debug is null)
            return;
        var turnOn = DebugModeToggle.IsChecked.GetValueOrDefault();
        if (turnOn == debug.DebugMode)
            return;
        // Set back once this change has finished: the switch places its knob after this
        // handler, for the value the touch gave it, and would leave it there
        Avalonia.Threading.Dispatcher.UIThread.Post(ShowDebugMode);
        debug.RequestDebugMode(turnOn);
    }

    // The Exact timing switch shows the saved value and only asks to change it, as Debug mode's
    // does: off takes effect at once; on may first open the phone's "Alarms & reminders" screen,
    // and shows on only once the phone allows it. A tap or a drag both land here.
    private void ExactTiming_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var slideshow = _viewModel?.Slideshow;
        if (slideshow is null)
            return;
        var turnOn = ExactTimingToggle.IsChecked.GetValueOrDefault();
        if (turnOn == slideshow.ExactTiming)
            return;
        // Set back once this change has finished, for the same reason as Debug mode's
        Avalonia.Threading.Dispatcher.UIThread.Post(ShowExactTiming);
        _ = slideshow.RequestExactTimingAsync(turnOn);
    }

    private void ShowExactTiming()
    {
        var isOn = _viewModel?.Slideshow.ExactTiming ?? false;
        ExactTimingToggle.SetCurrentValue(ToggleSwitch.IsCheckedProperty, isOn);
    }

    private void OnSlideshowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlideshowSettingsViewModel.ExactTiming))
            ShowExactTiming();
    }

    private void ShowDebugMode()
    {
        var isOn = _viewModel?.Debug.DebugMode ?? false;
        DebugModeToggle.SetCurrentValue(ToggleSwitch.IsCheckedProperty, isOn);
    }

    // Android's back closes an open list or answers an open question with No. True when it did.
    public bool HandleBack()
    {
        if (_viewModel?.Picker is not null)
        {
            _viewModel.ClosePicker();
            return true;
        }
        if (Question.IsVisible)
        {
            Question.Cancel();
            return true;
        }
        return false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Picker))
            ChoiceBox.Show(_viewModel?.Picker);
    }

    private void OnDebugPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DebugSettingsViewModel.PendingQuestion))
            ShowQuestion();
        else if (e.PropertyName == nameof(DebugSettingsViewModel.DebugMode))
            ShowDebugMode();
    }

    // Shows the debug group's open question with the desktop's title, question and No and Yes,
    // and passes the answer back; the group then asks the next one or closes.
    private async void ShowQuestion()
    {
        var debug = _viewModel?.Debug;
        var question = debug?.PendingQuestion;
        if (debug is null || question is null)
            return;
        var yes = await Question.ShowAsync(
            question.Message,
            "Yes",
            "Confirm",
            "No",
            "Cancel",
            isDestructive: false,
            title: question.Title);
        if (ReferenceEquals(debug.PendingQuestion, question))
            await debug.AnswerAsync(yes);
    }
}

using System.ComponentModel;
using Avalonia.Controls;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// The Settings tab's list (see SettingsView.axaml). Its view model is set by MainView, which
// also tells it when the tab is first shown and passes it Android's back. It shows the view
// model's open picker and the debug group's open question in their boxes, so a recreated screen
// shows them again; its scroll place is kept in the view model too. The keyboard never covers
// the number or the cron expression being typed (KeyboardLift).
public partial class SettingsView : UserControl
{
    private readonly ListPlace _place;
    private SettingsViewModel? _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        _place = new ListPlace(this, Scroller);
        // The focused field is kept above the confirmation line too, which covers the list's bottom
        _ = new KeyboardLift(this, Layout, bottomClearance: 72);
        ChoiceBox.Chosen += index => _viewModel?.Choose(index);
        ChoiceBox.Cancelled += () => _viewModel?.ClosePicker();
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
        }
        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is null)
        {
            ChoiceBox.Show(null);
            return;
        }
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Debug.PropertyChanged += OnDebugPropertyChanged;
        ChoiceBox.Show(_viewModel.Picker);
        ShowDebugMode();
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

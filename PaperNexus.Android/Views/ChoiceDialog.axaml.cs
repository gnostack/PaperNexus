using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Shows an open picker over the page (see ChoiceDialog.axaml). The page that owns it sets the
// picker as its data and listens for the person's answer.
public partial class ChoiceDialog : UserControl
{
    public ChoiceDialog()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Cancelled?.Invoke();
        Backdrop.PointerPressed += OnBackdropPressed;
    }

    // The person chose the choice at this index.
    public event Action<int>? Chosen;

    // The person closed the list without choosing.
    public event Action? Cancelled;

    // Shows the picker, or hides the box when there is none.
    public void Show(SettingsPicker? picker)
    {
        DataContext = picker;
        IsVisible = picker is not null;
    }

    private void OnChoiceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PickerChoice choice } || DataContext is not SettingsPicker picker)
            return;
        var index = IndexOf(picker, choice);
        if (index >= 0)
            Chosen?.Invoke(index);
    }

    private static int IndexOf(SettingsPicker picker, PickerChoice choice)
    {
        for (var i = 0; i < picker.Choices.Count; i++)
        {
            if (ReferenceEquals(picker.Choices[i], choice))
                return i;
        }
        return -1;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        Cancelled?.Invoke();
    }
}

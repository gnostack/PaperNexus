using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;
using PaperNexus.Core;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// One row of the Secrets checklist as the desktop draws it: the shared row's text, with the
// desktop's colours for found and still hidden.
public sealed record EasterEggListItem(string Name, string Hint, string Mark, IBrush MarkColor, IBrush NameColor)
{
    public static EasterEggListItem From(SecretRow row) => new(
        Name: row.Name,
        // A found egg shows its hint as a reminder of how it was triggered; an unfound one
        // shows the same text, because the hint is the point of the list.
        Hint: row.Hint,
        Mark: row.Mark,
        MarkColor: row.IsFound ? FoundBrush : PendingBrush,
        NameColor: row.IsFound ? FoundNameBrush : PendingBrush);

    private static readonly IBrush FoundBrush = new SolidColorBrush(Color.Parse("#FFD93D"));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#6A6A6E"));
    private static readonly IBrush FoundNameBrush = new SolidColorBrush(Color.Parse("#F5F5F5"));
}

public partial class EasterEggListDialog : Window
{
    // The bar's full width: the dialog's fixed width less its margins.
    private const double ProgressBarWidth = 436.0;

    private readonly SecretsViewModel _secrets;

    // Created per click from the container with a fresh checklist, which reads discovery
    // state through the same store as the rest of the app.
    public EasterEggListDialog(SecretsViewModel secrets)
    {
        _secrets = secrets.ThrowIfNull();
        InitializeComponent();
    }

    // Loads discovery state and fills the list. Called after the window is constructed so
    // settings are not read on the UI thread during layout.
    public async Task LoadAsync()
    {
        await _secrets.LoadAsync();

        EggList.ItemsSource = _secrets.Rows
            .Select(EasterEggListItem.From)
            .ToList();
        ProgressText.Text = _secrets.ProgressText;

        // The bar is sized directly rather than bound, because its container has a fixed
        // width and this avoids a converter for a single value.
        ProgressBar.Width = ProgressBarWidth * _secrets.FoundFraction;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}

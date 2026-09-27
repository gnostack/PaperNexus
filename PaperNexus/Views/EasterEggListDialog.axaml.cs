using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;
using PaperNexus.Core;

namespace PaperNexus.Views;

// One row of the secrets checklist. The display strings are computed once here rather than
// through converters, since the list is rebuilt each time the dialog opens anyway.
public sealed record EasterEggListItem(string Name, string Hint, string Mark, IBrush MarkColor, IBrush NameColor)
{
    public static EasterEggListItem From(EasterEggDefinition egg, bool found) => new(
        Name: egg.Name,
        // A found egg shows its hint as a reminder of how it was triggered; an unfound one
        // shows the same text, because the hint is the point of the list.
        Hint: egg.Hint,
        Mark: found ? "■" : "□",
        MarkColor: found ? FoundBrush : PendingBrush,
        NameColor: found ? FoundNameBrush : PendingBrush);

    private static readonly IBrush FoundBrush = new SolidColorBrush(Color.Parse("#FFD93D"));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.Parse("#6A6A6E"));
    private static readonly IBrush FoundNameBrush = new SolidColorBrush(Color.Parse("#F5F5F5"));
}

public partial class EasterEggListDialog : Window
{
    private readonly ISettingsStore _settingsStore;

    // Created per click from the container, so the checklist reads discovery state through
    // the same store as the rest of the app.
    public EasterEggListDialog(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        InitializeComponent();
    }

    // Loads discovery state and fills the list. Called after the window is constructed so
    // settings are not read on the UI thread during layout.
    public async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var entries = EasterEggProgress.Build(settings.DiscoveredEasterEggs);

        EggList.ItemsSource = entries
            .Select(entry => EasterEggListItem.From(entry.Egg, entry.Found))
            .ToList();

        var found = entries.Count(entry => entry.Found);
        var total = EasterEggCatalog.Total;
        ProgressText.Text = found == total
            ? $"{found} / {total} found - all of them. Nothing left to hide."
            : $"{found} / {total} found";

        // The bar is sized directly rather than bound, because its container has a fixed
        // width and this avoids a converter for a single value.
        ProgressBar.Width = total == 0 ? 0 : 436.0 * found / total;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}

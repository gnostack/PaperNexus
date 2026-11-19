using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// One row of the Secrets checklist: the egg's name and hint, and whether it has been found.
public sealed record SecretRow(string Name, string Hint, bool IsFound)
{
    // A filled box for found, an empty one for still hidden.
    public string Mark => IsFound ? "■" : "□";
}

// The Secrets checklist, as both devices show it: one row per easter egg this device has
// (IDeviceEasterEggs), whether each was found, and the progress. Discoveries are read when the
// list opens, so an egg found moments ago is already ticked.
public sealed partial class SecretsViewModel : ObservableObject
{
    public const string Title = "SECRETS";
    public const string Subtitle = "Hidden things scattered through the app. Find them all.";

    private readonly ISettingsStore _settingsStore;
    private readonly IDeviceEasterEggs _deviceEggs;

    [ObservableProperty]
    private IReadOnlyList<SecretRow> _rows = [];

    [ObservableProperty]
    private string _progressText = string.Empty;

    // The share of this device's eggs found, from 0 to 1, for the progress bar.
    [ObservableProperty]
    private double _foundFraction;

    public SecretsViewModel(ISettingsStore settingsStore, IDeviceEasterEggs deviceEggs)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _deviceEggs = deviceEggs.ThrowIfNull();
    }

    public static string Progress(int found, int total) => found == total
        ? $"{found} / {total} found - all of them. Nothing left to hide."
        : $"{found} / {total} found";

    // Reads which eggs were found and fills the list.
    public async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var discovered = new HashSet<string>(settings.DiscoveredEasterEggs, StringComparer.OrdinalIgnoreCase);
        var rows = _deviceEggs.Eggs
            .Select(egg => new SecretRow(egg.Name, egg.Hint, discovered.Contains(egg.Id)))
            .ToList();

        var found = rows.Count(row => row.IsFound);
        var total = rows.Count;
        Rows = rows;
        ProgressText = Progress(found, total);
        FoundFraction = total == 0 ? 0 : (double)found / total;
    }
}

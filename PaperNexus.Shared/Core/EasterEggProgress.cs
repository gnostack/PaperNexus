namespace PaperNexus.Core;

// Records which easter eggs have been triggered, so the checklist can show progress.
// Every trigger routes through here rather than writing settings itself, which keeps the
// discovery bookkeeping in one place and out of the views. It is a container singleton
// holding the settings store, whose update lock serialises every egg in the process.
public sealed class EasterEggProgress : IAddSingleton<EasterEggProgress>
{
    private readonly ISettingsStore _settingsStore;

    public EasterEggProgress(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore.ThrowIfNull();
    }

    // Returns true when this is the first time the egg has been found, so the caller can
    // celebrate a new discovery differently from a repeat. The store's update adds the id to
    // the latest list under its lock, so two eggs found together both keep their tick, and
    // nothing else saved at the same moment is lost.
    public async Task<bool> RecordAsync(string eggId)
    {
        if (string.IsNullOrWhiteSpace(eggId))
            return false;

        try
        {
            var isNew = false;
            await _settingsStore.UpdateAsync(latest =>
            {
                isNew = !latest.DiscoveredEasterEggs.Contains(eggId, StringComparer.OrdinalIgnoreCase);
                if (isNew)
                    latest.DiscoveredEasterEggs.Add(eggId);
            }).ConfigureAwait(false);
            return isNew;
        }
        catch (Exception)
        {
            // Losing a checklist tick is not worth interrupting the animation the user is
            // currently watching.
            return false;
        }
    }

    // Pure functions of a discovery list; they never touch settings, so they stay static.
    // The catalog paired with whether each entry has been found, in catalog order.
    public static IReadOnlyList<(EasterEggDefinition Egg, bool Found)> Build(IEnumerable<string> discoveredIds)
    {
        var discovered = new HashSet<string>(discoveredIds, StringComparer.OrdinalIgnoreCase);
        return EasterEggCatalog.All
            .Select(egg => (egg, discovered.Contains(egg.Id)))
            .ToList();
    }

    public static int CountFound(IEnumerable<string> discoveredIds) =>
        Build(discoveredIds).Count(entry => entry.Found);
}

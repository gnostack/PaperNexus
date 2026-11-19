namespace PaperNexus;

// The desktop has every easter egg in the catalog, with the catalog's own hints.
internal sealed class DesktopEasterEggs : IDeviceEasterEggs, IAddSingleton<IDeviceEasterEggs>
{
    public IReadOnlyList<EasterEggDefinition> Eggs => EasterEggCatalog.All;
}

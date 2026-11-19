using PaperNexus.Core;

namespace PaperNexus;

// The easter eggs the phone's Secrets list shows (issue #203, step 4). Fleeting Words is left
// out: it is a line on the desktop's startup screen, and the phone has no startup screen with
// words. Two hints name where and how on the phone: the Konami code is swiped on the Settings
// tab, and the version is in the About section, not a corner. The ids are the catalog's, so a
// discovery counts on both devices.
internal sealed class AndroidDeviceEasterEggs : IDeviceEasterEggs, IAddSingleton<IDeviceEasterEggs>
{
    public const string KonamiHint = "Every arcade player knew this one by heart. Swipe it here in Settings.";
    public const string VersionHint = "The version number in About does not look tappable. Try anyway. Keep going.";

    public AndroidDeviceEasterEggs()
    {
        Eggs = EasterEggCatalog.All
            .Where(egg => egg.Id != EasterEggCatalog.Splash)
            .Select(egg => egg.Id switch
            {
                EasterEggCatalog.Konami => egg with { Hint = KonamiHint },
                EasterEggCatalog.Version => egg with { Hint = VersionHint },
                _ => egg,
            })
            .ToList();
    }

    public IReadOnlyList<EasterEggDefinition> Eggs { get; }
}

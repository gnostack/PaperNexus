namespace PaperNexus.Core;

// One entry per easter egg, shown in the checklist so the eggs are findable rather than
// buried in the source. The Id is persisted in settings, so changing an existing Id would
// reset that egg to undiscovered for everyone - add new ones instead.
public sealed record EasterEggDefinition(string Id, string Name, string Hint);

public static class EasterEggCatalog
{
    public const string Konami = "konami";
    public const string Version = "version";
    public const string Favorites = "favorites";
    public const string Colors = "colors";
    public const string Splash = "splash";

    // Hints say where to look and what kind of action is involved, without giving the exact
    // input away - enough to make each one findable, not enough to make finding it pointless.
    public static IReadOnlyList<EasterEggDefinition> All { get; } =
    [
        new(Konami, "The Old Cheat Code",
            "Every arcade player knew this one by heart. Type it here in Settings."),
        new(Version, "Impatient Clicker",
            "The version number in the corner does not look clickable. Try anyway. Keep going."),
        new(Favorites, "Curator",
            "Hearts add up. Round numbers get noticed."),
        new(Colors, "Hexspeak",
            "Some colour codes spell words. The annotation colour box is waiting."),
        new(Splash, "Fleeting Words",
            "The startup screen does not always say the same thing. Blink and you will miss it."),
    ];

    public static EasterEggDefinition? Find(string id) =>
        All.FirstOrDefault(egg => string.Equals(egg.Id, id, StringComparison.OrdinalIgnoreCase));

    public static int Total => All.Count;
}

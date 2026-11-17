namespace PaperNexus.Core;

// Hidden bits of personality scattered through the UI. Everything here is a pure function
// over its inputs so the triggers can be unit tested without driving the interface, and so
// none of it can affect wallpaper switching, downloading, or settings.
public static class EasterEggs
{
    // Cycled by repeated clicks on the version label in the footer, so a second discovery
    // is not the same message as the first.
    private static readonly string[] VersionMessages =
    [
        "🥚 You found the easter egg! No wallpapers were harmed.",
        "🖼️ Still clicking? The wallpapers rotate on their own, you know.",
        "🎨 Fun fact: this app has looked at more sunsets than you have.",
        "🧦 Somewhere, a pixel is very proud of you.",
        "🛸 Nothing here but us wallpapers.",
        "🏆 Achievement unlocked: Reads The Small Text.",
    ];

    private static int _versionMessageIndex;

    // Advances through the list and wraps, so clicking forever keeps producing something.
    public static string NextVersionMessage()
    {
        var message = VersionMessages[_versionMessageIndex % VersionMessages.Length];
        _versionMessageIndex++;
        return message;
    }

    // Shown under the logo while the background services start. The ordinary line appears
    // most of the time; the rest are rare enough to be a surprise rather than a gimmick.
    private static readonly string[] SplashMessages =
    [
        "Reticulating splines...",
        "Consulting the pixels...",
        "Waking the wallpapers...",
        "Negotiating with your desktop...",
        "Choosing something nicer than that...",
        "Warming up the photons...",
    ];

    private const double SplashMessageChance = 0.25;

    // The ordinary line, shown most of the time. Public so callers can tell an egg apart
    // from the normal case without repeating the string.
    public const string DefaultSplashMessage = "Starting up...";

    public static string SplashMessage(Random random)
    {
        if (random.NextDouble() >= SplashMessageChance)
            return DefaultSplashMessage;
        return SplashMessages[random.Next(SplashMessages.Length)];
    }

    // Congratulates the user on round numbers of favorites. Returns null at every other
    // count so the caller can simply skip showing anything.
    public static string? FavoriteMilestoneMessage(int favoriteCount) => favoriteCount switch
    {
        1 => "❤️ First favorite! It will stick around longer than the others.",
        10 => "❤️ Ten favorites. You are developing taste.",
        25 => "🖼️ Twenty-five favorites. This is basically a gallery now.",
        50 => "🏛️ Fifty favorites. Have you considered curating professionally?",
        100 => "🌌 One hundred favorites. At this point the wallpapers work for you.",
        _ => null,
    };

    // A handful of hex colours spell words. Typing one into the annotation colour box is a
    // reasonable thing to try, so it gets a nod. The colour still applies normally.
    public static string? MagicColorMessage(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        // Trim whitespace before the hash, otherwise a leading space stops TrimStart from
        // reaching it and a pasted "  #C0FFEE  " never matches.
        var normalized = hex.Trim().TrimStart('#').Trim().ToUpperInvariant();
        return normalized switch
        {
            "C0FFEE" => "☕ Excellent choice. Brewed at exactly the right temperature.",
            "DECADE" => "🕰️ A colour ten years in the making.",
            "FACADE" => "🎭 Nothing is quite what it seems.",
            "BADA55" => "😎 That is, objectively, a very good colour.",
            "DEFACE" => "🖌️ We prefer to think of it as 'annotating'.",
            _ => null,
        };
    }
}

// Builds the staged overlay for each egg. Kept separate from the message text above so the
// wording and the visuals can change independently, and so a trigger that should stay quiet
// returns null rather than an empty show.
public static class EasterEggShows
{
    // Arcade palettes: saturated, few colours, no gradients.
    private static readonly string[] LivesPalette = ["#FF5C57", "#FF9F43", "#FFD93D"];
    private static readonly string[] EggPalette = ["#F5F5F5", "#FFD93D", "#8BE9FD"];
    private static readonly string[] HeartPalette = ["#E06C75", "#FF5C57", "#FF79C6"];
    private static readonly string[] CoffeePalette = ["#C69C6D", "#8B5E34", "#F5F5F5"];

    // The Konami reward bursts outward like a firework - the loudest egg, and the one people
    // deliberately go looking for.
    public static EasterEggShow Konami() => new(
        Id: EasterEggCatalog.Konami,
        Message: "+30 LIVES GRANTED",
        Sprite: EasterEggSprites.Coin,
        Palette: LivesPalette,
        Motion: EasterEggMotion.Burst,
        ParticleCount: 28);

    public static EasterEggShow Version() => new(
        Id: EasterEggCatalog.Version,
        Message: EasterEggs.NextVersionMessage(),
        Sprite: EasterEggSprites.Egg,
        Palette: EggPalette,
        Motion: EasterEggMotion.Rise,
        ParticleCount: 18);

    // Null at every count that is not a milestone, so the caller shows its ordinary
    // confirmation instead of an overlay.
    public static EasterEggShow? Favorite(int favoriteCount)
    {
        var message = EasterEggs.FavoriteMilestoneMessage(favoriteCount);
        if (message is null)
            return null;

        return new EasterEggShow(
            Id: EasterEggCatalog.Favorites,
            Message: message,
            Sprite: EasterEggSprites.Heart,
            Palette: HeartPalette,
            Motion: EasterEggMotion.Rise,
            ParticleCount: 22);
    }

    // Coffee falls for #C0FFEE; the other word-colours get stars, since a coffee cup only
    // makes sense for the one.
    public static EasterEggShow? MagicColor(string? hex)
    {
        var message = EasterEggs.MagicColorMessage(hex);
        if (message is null)
            return null;

        var isCoffee = string.Equals(
            hex?.Trim().TrimStart('#').Trim(), "C0FFEE", StringComparison.OrdinalIgnoreCase);

        return new EasterEggShow(
            Id: EasterEggCatalog.Colors,
            Message: message,
            Sprite: isCoffee ? EasterEggSprites.Coffee : EasterEggSprites.Star,
            Palette: isCoffee ? CoffeePalette : EggPalette,
            Motion: EasterEggMotion.Fall,
            ParticleCount: 20);
    }
}

// Recognises the Konami code typed into a window. Fed one key at a time; reports true on
// the key that completes the sequence, then resets so it can be triggered again.
// A wrong key restarts matching rather than only resetting, so a stray press mid-sequence
// does not require lifting off and starting over from scratch.
public sealed class KonamiSequence
{
    private static readonly string[] Sequence =
    [
        "Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B", "A",
    ];

    private int _position;

    // keyName is the Avalonia Key enum name, so the matcher stays free of UI types.
    public bool Advance(string keyName)
    {
        if (string.Equals(keyName, Sequence[_position], StringComparison.OrdinalIgnoreCase))
        {
            _position++;
            if (_position < Sequence.Length)
                return false;

            _position = 0;
            return true;
        }

        // Restart, but allow this key to count as the first step of a fresh attempt -
        // otherwise "Up, Up, Up, Down..." would never match despite containing the code.
        _position = string.Equals(keyName, Sequence[0], StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return false;
    }
}

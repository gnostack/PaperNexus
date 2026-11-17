namespace PaperNexus.Core;

// Pixel art for the easter egg overlay, defined as bitmasks rather than image assets so the
// sprites stay small, editable, and unit testable. Each row is one scanline; 'X' is a filled
// pixel and any other character is transparent. The renderer draws one square per filled
// pixel, which is what gives the overlay its chunky arcade look.
public static class EasterEggSprites
{
    public static readonly string[] Heart =
    [
        ".XX.XX.",
        "XXXXXXX",
        "XXXXXXX",
        ".XXXXX.",
        "..XXX..",
        "...X...",
    ];

    public static readonly string[] Star =
    [
        "...X...",
        "...X...",
        ".XXXXX.",
        "..XXX..",
        ".XX.XX.",
        ".X...X.",
    ];

    public static readonly string[] Egg =
    [
        "..XXX..",
        ".XXXXX.",
        "XXXXXXX",
        "XXXXXXX",
        "XXXXXXX",
        ".XXXXX.",
    ];

    // Steam sits on its own rows with a clear gap above the mug, and the handle is a
    // detached ring on the right. Drawn solid, the two merged into an unreadable blob.
    public static readonly string[] Coffee =
    [
        ".X.X...",
        "..X.X..",
        ".......",
        "XXXXX..",
        "X...XXX",
        "X...X.X",
        "X...XXX",
        "XXXXX..",
    ];

    public static readonly string[] Coin =
    [
        "..XXX..",
        ".XX.XX.",
        "XX.X.XX",
        "XX.X.XX",
        ".XX.XX.",
        "..XXX..",
    ];

    // Returns the filled pixel coordinates of a sprite, as (column, row) pairs.
    // Callers draw one square per coordinate.
    public static IEnumerable<(int X, int Y)> FilledPixels(string[] sprite)
    {
        for (var y = 0; y < sprite.Length; y++)
        {
            var row = sprite[y];
            for (var x = 0; x < row.Length; x++)
            {
                if (row[x] == 'X')
                    yield return (x, y);
            }
        }
    }

    public static int Width(string[] sprite) => sprite.Length == 0 ? 0 : sprite.Max(row => row.Length);

    public static int Height(string[] sprite) => sprite.Length;
}

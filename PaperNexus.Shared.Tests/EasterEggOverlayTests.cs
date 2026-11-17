using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The overlay's visuals are driven entirely by pure functions, so the motion and artwork can
// be asserted here without constructing a window. The control itself only creates rectangles
// and moves them to the coordinates these functions return.
public class EasterEggSpriteTests
{
    public static TheoryData<string> SpriteNames => new() { "Heart", "Star", "Egg", "Coffee", "Coin" };

    private static string[] Sprite(string name) => name switch
    {
        "Heart" => EasterEggSprites.Heart,
        "Star" => EasterEggSprites.Star,
        "Egg" => EasterEggSprites.Egg,
        "Coffee" => EasterEggSprites.Coffee,
        "Coin" => EasterEggSprites.Coin,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(SpriteNames))]
    public void EverySprite_HasFilledPixels(string name)
    {
        // An all-blank sprite would render an invisible animation that still burns three
        // seconds of the user's attention.
        Assert.NotEmpty(EasterEggSprites.FilledPixels(Sprite(name)));
    }

    [Theory]
    [MemberData(nameof(SpriteNames))]
    public void EverySprite_HasRowsOfEqualLength(string name)
    {
        // Ragged rows shift pixels leftward and distort the artwork, which is easy to
        // introduce by eye when editing the bitmask strings.
        var sprite = Sprite(name);
        Assert.All(sprite, row => Assert.Equal(sprite[0].Length, row.Length));
    }

    [Theory]
    [MemberData(nameof(SpriteNames))]
    public void EverySprite_UsesOnlyTheTwoBitmaskCharacters(string name)
    {
        // A stray character reads as transparent, silently punching a hole in the art.
        Assert.All(Sprite(name), row => Assert.All(row, c => Assert.True(c is 'X' or '.', $"unexpected '{c}'")));
    }

    [Fact]
    public void FilledPixels_MatchesTheBitmask()
    {
        var sprite = new[] { "X.X", ".X." };
        var filled = EasterEggSprites.FilledPixels(sprite).ToList();
        Assert.Equal(3, filled.Count);
        Assert.Contains((0, 0), filled);
        Assert.Contains((2, 0), filled);
        Assert.Contains((1, 1), filled);
    }
}

public class EasterEggAnimationTests
{
    private const double Width = 900;
    private const double Height = 600;

    [Theory]
    [InlineData(EasterEggMotion.Rise)]
    [InlineData(EasterEggMotion.Burst)]
    [InlineData(EasterEggMotion.Fall)]
    public void Frame_IsDeterministic(EasterEggMotion motion)
    {
        // The overlay recomputes every frame from scratch rather than accumulating state, so
        // identical inputs must give identical output or sprites would jitter.
        var first = EasterEggAnimation.Frame(motion, 3, 10, Width, Height);
        var second = EasterEggAnimation.Frame(motion, 3, 10, Width, Height);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(EasterEggMotion.Rise)]
    [InlineData(EasterEggMotion.Burst)]
    [InlineData(EasterEggMotion.Fall)]
    public void Frame_SnapsPositionsToThePixelGrid(EasterEggMotion motion)
    {
        // Sub-pixel positions would smooth the motion out and lose the stepped arcade feel.
        for (var frame = 0; frame <= EasterEggAnimation.TotalFrames; frame++)
        {
            var sprite = EasterEggAnimation.Frame(motion, 7, frame, Width, Height);
            Assert.Equal(0, sprite.X % EasterEggAnimation.PixelSize, 6);
            Assert.Equal(0, sprite.Y % EasterEggAnimation.PixelSize, 6);
        }
    }

    [Fact]
    public void Rise_ShowsSpritesOnTheVeryFirstFrame()
    {
        // Regression: every rising sprite used to start below the bottom edge, so the overlay
        // opened on a blank screen for roughly half a second and read as a failure.
        var visibleAtStart = Enumerable.Range(0, 22)
            .Select(i => EasterEggAnimation.Frame(EasterEggMotion.Rise, i, 0, Width, Height))
            .Count(f => f.Y >= 0 && f.Y <= Height);

        Assert.True(visibleAtStart >= 5, $"only {visibleAtStart} sprites on screen at frame 0");
    }

    [Fact]
    public void Rise_MovesUpwardOverTime()
    {
        var start = EasterEggAnimation.Frame(EasterEggMotion.Rise, 4, 0, Width, Height);
        var end = EasterEggAnimation.Frame(EasterEggMotion.Rise, 4, EasterEggAnimation.TotalFrames, Width, Height);
        Assert.True(end.Y < start.Y, "rising sprites must end higher than they started");
    }

    [Fact]
    public void Fall_MovesDownwardOverTime()
    {
        var start = EasterEggAnimation.Frame(EasterEggMotion.Fall, 4, 0, Width, Height);
        var end = EasterEggAnimation.Frame(EasterEggMotion.Fall, 4, EasterEggAnimation.TotalFrames, Width, Height);
        Assert.True(end.Y > start.Y, "falling sprites must end lower than they started");
    }

    [Fact]
    public void Burst_StartsAtTheCentreAndRadiatesOutward()
    {
        var start = EasterEggAnimation.Frame(EasterEggMotion.Burst, 9, 0, Width, Height);
        var end = EasterEggAnimation.Frame(EasterEggMotion.Burst, 9, EasterEggAnimation.TotalFrames, Width, Height);

        static double DistanceFromCentre(SpriteFrame f) =>
            Math.Sqrt(Math.Pow(f.X - Width / 2, 2) + Math.Pow(f.Y - Height / 2, 2));

        Assert.True(DistanceFromCentre(start) < 10, "burst must begin at the centre");
        Assert.True(DistanceFromCentre(end) > DistanceFromCentre(start));
    }

    [Fact]
    public void Frame_IsFullyOpaqueEarlyAndTransparentAtTheEnd()
    {
        Assert.Equal(1.0, EasterEggAnimation.Frame(EasterEggMotion.Rise, 0, 0, Width, Height).Opacity);
        Assert.Equal(0.0, EasterEggAnimation.Frame(EasterEggMotion.Rise, 0, EasterEggAnimation.TotalFrames, Width, Height).Opacity);
    }

    [Fact]
    public void Frame_ClampsBeyondTheFinalFrame()
    {
        // A timer tick arriving late must not fling sprites away or produce negative opacity.
        var past = EasterEggAnimation.Frame(EasterEggMotion.Burst, 2, EasterEggAnimation.TotalFrames + 50, Width, Height);
        Assert.InRange(past.Opacity, 0, 1);
    }

    [Fact]
    public void Frame_AlwaysReturnsAPositiveScale()
    {
        // A zero scale would size the rectangles to nothing and render an empty overlay.
        for (var i = 0; i < 40; i++)
            Assert.True(EasterEggAnimation.Frame(EasterEggMotion.Rise, i, 5, Width, Height).Scale > 0);
    }
}

public class EasterEggShowTests
{
    [Fact]
    public void EveryShow_HasAMessageSpritePaletteAndParticles()
    {
        EasterEggShow[] shows =
        [
            EasterEggShows.Konami(),
            EasterEggShows.Version(),
            EasterEggShows.Favorite(10)!,
            EasterEggShows.MagicColor("#C0FFEE")!,
        ];

        Assert.All(shows, show =>
        {
            Assert.False(string.IsNullOrWhiteSpace(show.Message));
            Assert.NotEmpty(show.Sprite);
            Assert.NotEmpty(show.Palette);
            Assert.True(show.ParticleCount > 0);
        });
    }

    [Fact]
    public void Favorite_StaysSilentOffMilestone()
    {
        // Otherwise every single favourite would take over the window.
        Assert.Null(EasterEggShows.Favorite(7));
        Assert.NotNull(EasterEggShows.Favorite(10));
    }

    [Fact]
    public void MagicColor_StaysSilentForOrdinaryColours()
    {
        Assert.Null(EasterEggShows.MagicColor("#F5F5F5"));
        Assert.NotNull(EasterEggShows.MagicColor("#BADA55"));
    }

    [Fact]
    public void MagicColor_UsesTheMugOnlyForCoffee()
    {
        // A coffee cup makes sense for #C0FFEE and for nothing else.
        Assert.Equal(EasterEggSprites.Coffee, EasterEggShows.MagicColor("#C0FFEE")!.Sprite);
        Assert.Equal(EasterEggSprites.Star, EasterEggShows.MagicColor("#BADA55")!.Sprite);
    }
}

namespace PaperNexus.Core;

public enum EasterEggMotion
{
    // Sprites drift upward from below the bottom edge, swaying as they rise.
    Rise,

    // Sprites radiate outward from the centre and decelerate, like a firework.
    Burst,

    // Sprites fall from above the top edge under gravity.
    Fall,
}

// Everything the overlay needs to stage one easter egg.
public sealed record EasterEggShow(
    // Catalog id, so playing a show is also what marks it discovered - the trigger sites
    // never have to remember to record anything.
    string Id,
    string Message,
    string[] Sprite,
    string[] Palette,
    EasterEggMotion Motion,
    int ParticleCount);

// Where a single sprite sits on a given frame. Snapped to the pixel grid by the animator,
// so motion steps rather than glides - the chunky, low-frame-rate arcade look.
public readonly record struct SpriteFrame(double X, double Y, double Opacity, int Scale);

// The motion is deliberately a pure function of (particle index, frame number). No Random
// and no accumulated state, so a frame can be computed in isolation, rendered offscreen for
// inspection, and asserted in tests.
public static class EasterEggAnimation
{
    // 12 frames per second. Chosen rather than a smooth 60 because stepped motion is a large
    // part of what makes the overlay read as retro rather than as a modern UI transition.
    public const int FramesPerSecond = 12;

    // Roughly three seconds, after which the overlay dismisses itself.
    public const int TotalFrames = 36;

    // Sprites fade over the final quarter so they do not vanish abruptly.
    private const int FadeStartFrame = 27;

    // Edge length in device pixels of one sprite pixel, and the grid motion snaps to.
    public const int PixelSize = 4;

    // A cheap deterministic hash, so each particle gets a stable spread of start positions,
    // speeds and phases without a random number generator.
    private static double Scatter(int particleIndex, int salt)
    {
        var hash = (particleIndex * 73856093) ^ (salt * 19349663);
        hash &= 0x7FFFFFFF;
        return (hash % 10000) / 10000.0;
    }

    public static SpriteFrame Frame(
        EasterEggMotion motion,
        int particleIndex,
        int frame,
        double canvasWidth,
        double canvasHeight)
    {
        // Progress runs 0..1 across the animation and is clamped so a late frame holds the
        // end state rather than flying off.
        var progress = Math.Clamp(frame / (double)TotalFrames, 0, 1);
        var opacity = frame < FadeStartFrame
            ? 1.0
            : Math.Clamp(1.0 - (frame - FadeStartFrame) / (double)(TotalFrames - FadeStartFrame), 0, 1);

        // Larger sprites in front, smaller behind, for a little depth.
        var scale = 2 + (int)(Scatter(particleIndex, 5) * 3);

        var (x, y) = motion switch
        {
            EasterEggMotion.Rise => RisePosition(particleIndex, progress, canvasWidth, canvasHeight),
            EasterEggMotion.Burst => BurstPosition(particleIndex, progress, canvasWidth, canvasHeight),
            EasterEggMotion.Fall => FallPosition(particleIndex, progress, canvasWidth, canvasHeight),
            _ => (canvasWidth / 2, canvasHeight / 2),
        };

        // Snap to the pixel grid so movement steps between whole pixels.
        return new SpriteFrame(Snap(x), Snap(y), opacity, scale);
    }

    private static double Snap(double value) => Math.Round(value / PixelSize) * PixelSize;

    private static (double X, double Y) RisePosition(int i, double progress, double width, double height)
    {
        var startX = Scatter(i, 1) * width;
        var speed = 0.6 + Scatter(i, 2) * 0.8;
        var swayAmplitude = 10 + Scatter(i, 3) * 30;
        var swayPhase = Scatter(i, 4) * Math.PI * 2;

        // Spread the starting heights so some sprites are already on screen at frame zero.
        // Starting them all below the bottom edge left the first half-second blank, which
        // read as the overlay having failed rather than as an animation beginning.
        var headStart = Scatter(i, 6) * height * 0.8;
        var y = height + 20 - headStart - progress * speed * (height + 120);
        var x = startX + Math.Sin(progress * Math.PI * 3 + swayPhase) * swayAmplitude;
        return (x, y);
    }

    private static (double X, double Y) BurstPosition(int i, double progress, double width, double height)
    {
        var angle = Scatter(i, 1) * Math.PI * 2;
        var distance = 60 + Scatter(i, 2) * 200;

        // Ease out so the burst is fast at the start and settles, like a firework.
        var eased = 1 - Math.Pow(1 - progress, 3);
        return (width / 2 + Math.Cos(angle) * distance * eased,
                height / 2 + Math.Sin(angle) * distance * eased);
    }

    private static (double X, double Y) FallPosition(int i, double progress, double width, double height)
    {
        var startX = Scatter(i, 1) * width;
        var delay = Scatter(i, 2) * 0.35;
        var drift = (Scatter(i, 3) - 0.5) * 60;

        // Stagger the start so they do not fall as one sheet, and accelerate under gravity.
        var local = Math.Clamp((progress - delay) / (1 - delay), 0, 1);
        var y = -40 + local * local * (height + 120);
        return (startX + drift * local, y);
    }
}

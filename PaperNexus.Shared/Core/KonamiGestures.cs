namespace PaperNexus.Core;

// What one touch stroke was, for the Konami code on a touch screen.
public enum KonamiStroke
{
    // Neither a swipe nor a tap (too short, too slow, or as much across as along); it is ignored.
    None,
    Up,
    Down,
    Left,
    Right,
    Tap,
}

// The Konami code by touch (the phone's Settings tab): the eight arrows as swipes in the
// direction the finger moves, then two taps for B and A. Strokes go into the same KonamiSequence
// the desktop's keys go into, so a wrong stroke restarts the code there too, and an Up still
// counts as its first step. Distances are in device-independent pixels.
public sealed class KonamiGestures
{
    // A swipe moves at least this far along one axis and at least twice as far as across it.
    public const double SwipeMinDistance = 48;
    public const double SwipeAxisRatio = 2;

    // A tap moves less than this, in less than TapMaxDuration.
    public const double TapMaxDistance = 8;
    public static readonly TimeSpan TapMaxDuration = TimeSpan.FromMilliseconds(300);

    private readonly KonamiSequence _sequence = new();

    // The stroke a finger made, from where it went down to where it lifted (dx to the right,
    // dy downwards) and how long it stayed down.
    public static KonamiStroke Classify(double dx, double dy, TimeSpan duration)
    {
        var across = Math.Abs(dx);
        var along = Math.Abs(dy);
        if (across < TapMaxDistance && along < TapMaxDistance)
            return duration < TapMaxDuration ? KonamiStroke.Tap : KonamiStroke.None;

        if (along >= SwipeMinDistance && along >= across * SwipeAxisRatio)
            return dy < 0 ? KonamiStroke.Up : KonamiStroke.Down;
        if (across >= SwipeMinDistance && across >= along * SwipeAxisRatio)
            return dx < 0 ? KonamiStroke.Left : KonamiStroke.Right;
        return KonamiStroke.None;
    }

    // Adds a stroke; true when it completes the code. A stroke that is neither a swipe nor a
    // tap changes nothing. A tap is B when B is next and A when A is next; anywhere else it is
    // a wrong step.
    public bool Add(KonamiStroke stroke)
    {
        if (stroke == KonamiStroke.None)
            return false;

        var key = stroke == KonamiStroke.Tap ? TapKey() : stroke.ToString();
        var completed = _sequence.Advance(key);
        return completed;
    }

    private string TapKey()
    {
        var expected = _sequence.Expected;
        var isLetter = expected is "B" or "A";
        return isLetter ? expected : "Tap";
    }
}

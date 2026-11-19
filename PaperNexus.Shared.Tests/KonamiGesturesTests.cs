using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The Konami code by touch: what counts as a swipe or a tap, and the code swiped then tapped.
public class KonamiGesturesTests
{
    private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(150);

    [Theory]
    [InlineData(0, -48, KonamiStroke.Up)]
    [InlineData(0, 120, KonamiStroke.Down)]
    [InlineData(-60, 10, KonamiStroke.Left)]
    [InlineData(200, -40, KonamiStroke.Right)]
    [InlineData(20, -47, KonamiStroke.None)] // Too short
    [InlineData(60, -70, KonamiStroke.None)] // As much across as along
    [InlineData(50, 25, KonamiStroke.Right)] // Exactly twice as far along
    [InlineData(3, -4, KonamiStroke.Tap)]
    [InlineData(7.9, 0, KonamiStroke.Tap)]
    [InlineData(8, 0, KonamiStroke.None)] // Moved too far for a tap, too little for a swipe
    public void Classify_SwipesAndTaps(double dx, double dy, KonamiStroke expected)
    {
        Assert.Equal(expected, KonamiGestures.Classify(dx, dy, Quick));
    }

    [Fact]
    public void Classify_ATouchHeldTooLongIsNoTap()
    {
        Assert.Equal(KonamiStroke.Tap, KonamiGestures.Classify(0, 0, TimeSpan.FromMilliseconds(299)));
        Assert.Equal(KonamiStroke.None, KonamiGestures.Classify(0, 0, TimeSpan.FromMilliseconds(300)));
        // A swipe may take as long as it likes
        Assert.Equal(KonamiStroke.Up, KonamiGestures.Classify(0, -100, TimeSpan.FromSeconds(2)));
    }

    private static readonly KonamiStroke[] Arrows =
    [
        KonamiStroke.Up, KonamiStroke.Up, KonamiStroke.Down, KonamiStroke.Down,
        KonamiStroke.Left, KonamiStroke.Right, KonamiStroke.Left, KonamiStroke.Right,
    ];

    // Feeds strokes and returns the index of each one that completed the code.
    private static List<int> Completions(KonamiGestures gestures, IEnumerable<KonamiStroke> strokes)
    {
        var completed = new List<int>();
        var index = 0;
        foreach (var stroke in strokes)
        {
            if (gestures.Add(stroke))
                completed.Add(index);
            index++;
        }
        return completed;
    }

    [Fact]
    public void Add_TheArrowsThenTwoTapsCompleteTheCode()
    {
        var gestures = new KonamiGestures();

        var completed = Completions(gestures, [.. Arrows, KonamiStroke.Tap, KonamiStroke.Tap]);

        Assert.Equal([9], completed);
    }

    [Fact]
    public void Add_TheCodeCanBeEnteredAgain()
    {
        var gestures = new KonamiGestures();
        KonamiStroke[] code = [.. Arrows, KonamiStroke.Tap, KonamiStroke.Tap];

        var completed = Completions(gestures, [.. code, .. code]);

        Assert.Equal([9, 19], completed);
    }

    [Fact]
    public void Add_AWrongStrokeRestartsAndAnUpCountsAsTheFirst()
    {
        var gestures = new KonamiGestures();

        // A stray Up restarts as the first Up, as the keys do on the desktop (EasterEggTests):
        // two stray Ups, then the whole code
        KonamiStroke[] strokes = [KonamiStroke.Up, KonamiStroke.Up, .. Arrows, KonamiStroke.Tap, KonamiStroke.Tap];
        Assert.Equal([11], Completions(gestures, strokes));

        // A wrong direction in the middle loses the arrows before it
        var restarted = new KonamiGestures();
        KonamiStroke[] broken = [KonamiStroke.Up, KonamiStroke.Up, KonamiStroke.Down, KonamiStroke.Left, .. Arrows[3..], KonamiStroke.Tap, KonamiStroke.Tap];
        Assert.Empty(Completions(restarted, broken));
    }

    [Fact]
    public void Add_ATapBeforeTheArrowsAreDoneRestarts()
    {
        var gestures = new KonamiGestures();

        KonamiStroke[] strokes = [.. Arrows[..4], KonamiStroke.Tap, .. Arrows[4..], KonamiStroke.Tap, KonamiStroke.Tap];

        Assert.Empty(Completions(gestures, strokes));
    }

    [Fact]
    public void Add_AStrokeThatIsNeitherChangesNothing()
    {
        var gestures = new KonamiGestures();

        KonamiStroke[] strokes = [.. Arrows[..4], KonamiStroke.None, .. Arrows[4..], KonamiStroke.None, KonamiStroke.Tap, KonamiStroke.Tap];

        Assert.Equal([11], Completions(gestures, strokes));
    }
}

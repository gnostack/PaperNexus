using System.Diagnostics;
using PaperNexus.Core;

namespace PaperNexus.Views;

// The easter egg the phone is playing and when it started, kept for the process's lifetime like
// the tabs' view models, so a screen recreated mid-show (a new font size) continues the show
// where it was instead of losing it or starting it again.
public sealed class EasterEggPlayback
{
    private long _startedAt;

    public EasterEggShow? Current { get; private set; }

    public void Start(EasterEggShow show)
    {
        Current = show;
        _startedAt = Stopwatch.GetTimestamp();
    }

    public void Clear()
    {
        Current = null;
    }

    // The animation frame the show has reached by now.
    public int ElapsedFrame
    {
        get
        {
            var elapsed = Stopwatch.GetElapsedTime(_startedAt);
            var frame = (int)(elapsed.TotalSeconds * EasterEggAnimation.FramesPerSecond);
            return frame;
        }
    }
}

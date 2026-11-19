namespace PaperNexus.Core;

// Where a view model asks for an easter egg to play. The view model decides whether an egg
// fires; whichever view owns the overlay (the desktop's settings window, the phone's main
// screen) subscribes and plays it. One per process, so every screen's eggs reach the overlay
// that is showing.
public sealed class EasterEggRequests : IAddSingleton<EasterEggRequests>
{
    // Raised on the thread that asked; the subscriber moves to its screen's thread.
    public event Action<EasterEggShow>? Requested;

    public void Request(EasterEggShow show)
    {
        show.ThrowIfNull();
        Requested?.Invoke(show);
    }
}

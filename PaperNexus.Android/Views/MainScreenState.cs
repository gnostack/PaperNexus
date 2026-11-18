namespace PaperNexus.Views;

// What the main screen showed that is not a view model's: the selected tab. Held by the
// container for the process's lifetime, so when Android destroys and recreates the screen
// (a new font size, language or display size) the new screen opens on the same tab. The tabs'
// own state (cards, confirmation lines, place in the list) lives in their view models, which
// the container also holds for the process's lifetime.
public sealed class MainScreenState
{
    // The tab shown last; the Current tab until the person chooses another.
    public int SelectedTabIndex { get; set; }
}

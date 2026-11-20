namespace PaperNexus.ViewModels;

// What a tap on the phone's long-press menu ("Next Wallpaper", "Random Wallpaper") shows in its
// pop-up, from what the change came to (issue #203, step 6). A change that worked shows nothing:
// the new wallpaper is the answer. A tap while a change was running shows nothing either, and
// with no pictures the app opens instead.
public static class ShortcutSentence
{
    public const string FailedText = "Could not change the wallpaper.";

    // The pop-up's sentence, or null when the tap shows none.
    public static string? For(ShortcutChangeOutcome outcome)
    {
        var sentence = outcome switch
        {
            ShortcutChangeOutcome.Failed => FailedText,
            ShortcutChangeOutcome.NotAllowed => CurrentWallpaperViewModel.NotAllowedText,
            _ => null,
        };
        return sentence;
    }
}

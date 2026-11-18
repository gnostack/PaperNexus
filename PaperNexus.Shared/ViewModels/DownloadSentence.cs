namespace PaperNexus.ViewModels;

// What "Download now" says when it finishes, on either device, from what the download run
// reports. The desktop puts its ✓ or ✗ in front; the phone shows the tone as the line's color.
public static class DownloadSentence
{
    public const string RunningText = "Downloading wallpapers...";
    public const string CompleteText = "Download complete.";
    public const string AllFailedText = "Download failed: check your connection.";
    public const string NoSourcesText = "No enabled sources to download from.";

    // The sentence and its tone: green when every enabled source worked, red when any failed,
    // white when there was nothing to download from.
    public static (string Text, StatusTone Tone) For(DownloadOutcome outcome)
    {
        outcome.ThrowIfNull();
        if (outcome.SourcesTried == 0)
            return (NoSourcesText, StatusTone.Neutral);
        if (outcome.SourcesFailed == 0)
            return (CompleteText, StatusTone.Success);
        if (outcome.SourcesFailed >= outcome.SourcesTried)
            return (AllFailedText, StatusTone.Error);
        var someFailed = $"Download failed for {outcome.SourcesFailed} of {outcome.SourcesTried} sources.";
        return (someFailed, StatusTone.Error);
    }

    // The sentence when the run itself could not run (the settings could not be read).
    public static string ErrorText(Exception ex) => $"Download failed: {ex.Message}";
}

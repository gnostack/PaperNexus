namespace PaperNexus.ViewModels;

// The choices the settings pickers list. Each keeps its label for ToString, so a picker shows
// the label, and its description for the line under the picker.

public record ResolutionOption(string Label, string Description, int Width, int Height)
{
    public override string ToString() => Label;
}

public record FillStyleOption(string Label, string Description, WallpaperFillStyle Style)
{
    public override string ToString() => Label;
}

public record SlideshowOrderOption(string Label, string Description, SlideshowOrder Order)
{
    public override string ToString() => Label;
}

public record AnnotationPositionOption(string Label, string Description, AnnotationPosition Position)
{
    public override string ToString() => Label;
}

public record ScheduleModeOption(string Label, SlideshowScheduleMode Mode)
{
    public override string ToString() => Label;
}

public record IntervalTypeOption(string Label, IntervalType Type, double Minimum, double Maximum)
{
    public override string ToString() => Label;
}

// A line a settings group reports for the screen's status line: what happened, its tone, how
// long it stays (milliseconds), and whether it stays until the next line instead.
public sealed record SettingsStatus(string Text, StatusTone Tone, int DurationMs = StatusLine.DurationMs, bool Holds = false);

// A yes-or-no question a settings group needs answered before it acts: the window's title (the
// desktop's dialog title) and the question.
public sealed record SettingsQuestion(string Title, string Message);

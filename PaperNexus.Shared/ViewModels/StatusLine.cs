using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// What the phone's confirmation line looks like: white while something runs, green when an
// action worked, red when it failed.
public enum StatusTone
{
    Neutral,
    Success,
    Error,
}

// The short line on a phone tab that confirms each tap: it shows a sentence for three seconds
// in the tone of what happened, replacing any earlier one, or holds a "working" sentence until
// the next one arrives. Every phone tab that confirms its actions owns one, so the timing and
// the colors are written once.
public sealed partial class StatusLine : ObservableObject, IDisposable
{
    // How long a confirmation stays, as on the desktop.
    internal const int DurationMs = 3000;

    private CancellationTokenSource _clearCts = new();
    private readonly object _lock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    private StatusTone _tone;

    public bool IsShown => Message is not null;

    public bool IsSuccess => Tone == StatusTone.Success;

    public bool IsError => Tone == StatusTone.Error;

    // Shows a line for three seconds (or durationMs), replacing any earlier one; hold keeps it
    // until the next.
    public void Show(string message, StatusTone tone, bool hold = false, int durationMs = DurationMs)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _clearCts.Cancel();
            _clearCts.Dispose();
            cts = new CancellationTokenSource();
            _clearCts = cts;
        }

        Tone = tone;
        Message = message;
        if (!hold)
            _ = ClearAfterAsync(durationMs, cts.Token);
    }

    // Takes the line away now.
    public void Clear()
    {
        lock (_lock)
            _clearCts.Cancel();
        Message = null;
    }

    private async Task ClearAfterAsync(int durationMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(durationMs, token);
            Message = null;
        }
        catch (OperationCanceledException) { }
    }

    // Stops a pending clear; the owner is going away.
    public void Dispose()
    {
        lock (_lock)
            _clearCts.Cancel();
    }
}

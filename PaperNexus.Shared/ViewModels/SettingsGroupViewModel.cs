using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// One group of settings on a settings screen (Download, Slideshow, the title, Debug mode, the
// desktop's own). Both devices' screens use the same groups, so each rule is written once.
//
// A group shows the settings it owns and saves only those: every change is written through
// ISettingsStore.UpdateAsync, which hands it the latest saved settings, and the group sets its
// own fields on them and nothing else. A screen that shows fewer groups (the phone) therefore
// can never write a setting it does not show.
//
// Changes made in quick succession (typing a number, a cron expression) are saved once, half a
// second after the last one, as the desktop window always has. Each save reports a line for
// the screen's status line.
public abstract class SettingsGroupViewModel : ObservableObject
{
    public const string SavedText = "Settings saved.";

    // How long a group waits after a change before saving it.
    internal const int SaveDelayMs = 500;

    private readonly object _saveLock = new();
    private CancellationTokenSource _saveCts = new();
    private bool _hasPendingSave;
    private bool _isLoading;

    // Set while a save is being captured when part of it was refused; that save then shows
    // the refusal instead of "Settings saved.".
    private bool _refusedPart;

    protected SettingsGroupViewModel(ISettingsStore settingsStore)
    {
        Store = settingsStore.ThrowIfNull();
    }

    protected ISettingsStore Store { get; }

    // A line for the screen's status line; raised on the thread that made the change.
    public event Action<SettingsStatus>? StatusReported;

    // True while the group is filling its fields from the saved settings; a change made then is
    // not the person's and is not saved.
    protected bool IsLoading => _isLoading;

    // Fills the group from settings the screen read once for all its groups.
    public void Load(WallpaperNexusSettings settings)
    {
        settings.ThrowIfNull();
        _isLoading = true;
        try
        {
            ReadFrom(settings);
        }
        finally
        {
            _isLoading = false;
        }
    }

    // Reads the saved settings and fills the group.
    public async Task LoadAsync()
    {
        var settings = await Store.LoadAsync();
        Load(settings);
    }

    // Sets the group's fields from the saved settings.
    protected abstract void ReadFrom(WallpaperNexusSettings settings);

    // Reads the group's values here, on the screen's thread, and returns the change that sets
    // them on the latest settings under the store's lock. Returns null when nothing can be
    // saved. A value that cannot be saved is left out of the change and reported through
    // ReportRefusal.
    protected abstract Action<WallpaperNexusSettings>? CaptureChange();

    // Saves the group half a second after the last change. Ignored while loading.
    protected void SaveSoon()
    {
        if (_isLoading)
            return;
        _ = SaveAfterDelayAsync();
    }

    // Cancels any waiting save and starts the wait again; only the save that waits out the
    // whole delay writes.
    private async Task SaveAfterDelayAsync()
    {
        CancellationTokenSource cts;
        lock (_saveLock)
        {
            _saveCts.Cancel();
            _saveCts.Dispose();
            cts = new CancellationTokenSource();
            _saveCts = cts;
            _hasPendingSave = true;
        }

        try
        {
            await Task.Delay(SaveDelayMs, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_saveLock)
            _hasPendingSave = false;
        await SaveNowAsync();
    }

    // Writes the group's settings now and reports the result.
    public async Task SaveNowAsync()
    {
        try
        {
            _refusedPart = false;
            var change = CaptureChange();
            var refused = _refusedPart;
            if (change is null)
                return;
            await Store.UpdateAsync(change);
            if (!refused)
                Report(SavedText, StatusTone.Success);
        }
        catch (Exception ex)
        {
            Report($"Error saving settings: {ex.Message}", StatusTone.Error, holds: true);
        }
    }

    // Saves a change still waiting for its delay at once (the screen is closing), so nothing
    // the person changed is lost, and stops waiting.
    public void Flush()
    {
        bool pending;
        lock (_saveLock)
        {
            pending = _hasPendingSave;
            _hasPendingSave = false;
            _saveCts.Cancel();
        }

        if (pending)
            _ = SaveNowAsync();
    }

    // A value the person entered that cannot be saved: the line says why, and this save does
    // not say "Settings saved.".
    protected void ReportRefusal(string text)
    {
        _refusedPart = true;
        Report(text, StatusTone.Error);
    }

    protected void Report(string text, StatusTone tone, int durationMs = StatusLine.DurationMs, bool holds = false)
    {
        var status = new SettingsStatus(text, tone, durationMs, holds);
        StatusReported?.Invoke(status);
    }
}

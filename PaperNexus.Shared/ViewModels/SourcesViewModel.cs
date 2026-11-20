using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

// The phone's Sources tab: one row per wallpaper source with its on/off switch, edit and delete,
// Add and Download now above the list, the line that confirms each tap, and the add or edit form
// while one is open.
//
// The rows are listed the first time the tab is shown; after that the sources' Changed event
// keeps them current, applied on the thread that created the view model (the screen's thread).
// The view model lives as long as the process, not the screen: Android destroys and recreates
// its screen (a new font size, language or display size), and the new screen's view attaches to
// this same view model, so the rows, the confirmation line and an open form with what was typed
// so far are still there. It never holds a view.
public sealed partial class SourcesViewModel : ObservableObject, IScrollPlace, IDisposable
{
    public const string EmptySentence = "No sources yet. Tap Add to create one.";
    public const string SavedText = "Settings saved.";

    // A scheduled download in which some sources failed (issue #203, step 5)
    public static string ScheduledDownloadFailedText(DownloadOutcome outcome) =>
        $"The last scheduled download failed for {outcome.SourcesFailed} of {outcome.SourcesTried} sources.";

    private readonly IWallpaperSources _sources;
    private readonly IDownloadWallpapers _downloadWallpapers;
    private readonly ILogger<SourcesViewModel> _logger;
    private readonly SynchronizationContext? _screenContext;
    private readonly IScheduledWorkFailures? _scheduledFailures;
    // The failed scheduled download's line while it is shown, so a run that works removes it
    // and nothing else
    private string? _failureLine;
    private bool _isDisposed;

    // Counts the listings started, so only the newest is shown.
    private int _listing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    // The add or edit form while it is open, with what was typed so far; null when closed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private SourceFormViewModel? _form;

    public SourcesViewModel(
        IWallpaperSources sources,
        IDownloadWallpapers downloadWallpapers,
        ILogger<SourcesViewModel> logger,
        IScheduledWorkFailures? scheduledFailures = null)
    {
        _sources = sources.ThrowIfNull();
        _downloadWallpapers = downloadWallpapers.ThrowIfNull();
        _logger = logger.ThrowIfNull();
        _screenContext = SynchronizationContext.Current;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        _sources.Changed += OnSourcesChanged;
        _scheduledFailures = scheduledFailures;
        if (_scheduledFailures is not null)
            _scheduledFailures.Changed += OnScheduledFailuresChanged;
    }

    public ObservableCollection<SourceItem> Items { get; } = [];

    public string EmptyText => EmptySentence;

    public bool IsEmpty => IsLoaded && Items.Count == 0;

    public bool IsFormOpen => Form is not null;

    // The line that confirms each tap.
    public StatusLine Status { get; } = new();

    public double ScrollOffset { get; set; }

    // Lists the sources the first time a view shows the tab; a later view (after the screen was
    // recreated) finds them already listed and kept current.
    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded)
            return;
        await LoadAsync();
        await ShowScheduledDownloadFailureAsync();
    }

    // A failed scheduled download shows on the line, held until the next line replaces it; the
    // next scheduled run that works takes it away (IScheduledWorkFailures).
    private async Task ShowScheduledDownloadFailureAsync()
    {
        if (_scheduledFailures is null)
            return;
        try
        {
            var failure = await _scheduledFailures.ScheduledDownloadFailureAsync();
            if (failure is not null)
            {
                _failureLine = ScheduledDownloadFailedText(failure);
                Status.Show(_failureLine, StatusTone.Error, hold: true);
            }
            else if (_failureLine is not null && Status.Message == _failureLine)
            {
                Status.Clear();
            }
            if (failure is null)
                _failureLine = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reading the last scheduled download failed.");
        }
    }

    // Raised on a background thread; applied on the screen's thread once the tab has listed.
    private void OnScheduledFailuresChanged()
    {
        if (!IsLoaded || _isDisposed)
            return;
        if (_screenContext is null)
        {
            _ = ShowScheduledDownloadFailureAsync();
            return;
        }

        _screenContext.Post(_ => _ = ShowScheduledDownloadFailureAsync(), null);
    }

    // Lists the sources. Two changes saved close together start two listings; only the one
    // started last is shown, since it read after every change, so an older listing finishing
    // late cannot put back a row that was deleted or a switch that was flipped.
    public async Task LoadAsync()
    {
        var listing = Interlocked.Increment(ref _listing);
        try
        {
            var sources = await _sources.ListAsync();
            if (listing == Volatile.Read(ref _listing))
                SourceItem.Reconcile(Items, sources, CreateItem);
        }
        catch (Exception ex)
        {
            // The settings store answers defaults rather than failing, so this is not expected
            _logger.LogError(ex, "Listing the sources failed.");
        }
        finally
        {
            IsLoaded = true;
        }
    }

    // Opens an empty form, as the desktop's Add button does.
    [RelayCommand]
    private void Add() => OpenForm(new SourceFormViewModel(_sources));

    // Downloads from every enabled source now and says what happened. Dimmed while it runs.
    [RelayCommand]
    private async Task DownloadNow()
    {
        Status.Show(DownloadSentence.RunningText, StatusTone.Neutral, hold: true);
        try
        {
            var outcome = await Task.Run(_downloadWallpapers.DownloadAllAsync);
            var (text, tone) = DownloadSentence.For(outcome);
            Status.Show(text, tone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download now failed.");
            Status.Show(DownloadSentence.ErrorText(ex), StatusTone.Error);
        }
    }

    private SourceItem CreateItem(ListedSource source)
    {
        var item = new SourceItem(source, EditSource, DeleteSourceAsync, SetEnabledAsync);
        return item;
    }

    private void EditSource(SourceItem item) => OpenForm(new SourceFormViewModel(_sources, item.Key, item.Fields));

    // One form at a time; a second tap while one is open does nothing.
    private void OpenForm(SourceFormViewModel form)
    {
        if (Form is not null)
            return;
        form.Finished += OnFormFinished;
        Form = form;
    }

    private void OnFormFinished(bool saved)
    {
        var form = Form;
        if (form is not null)
            form.Finished -= OnFormFinished;
        Form = null;
        if (saved)
            Status.Show(SavedText, StatusTone.Success);
    }

    // Deletes without asking, as the desktop does.
    private async Task DeleteSourceAsync(SourceItem item)
    {
        try
        {
            await _sources.DeleteAsync(item.Key);
            Items.Remove(item);
            Status.Show(SavedText, StatusTone.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting the source {Name} failed.", item.Name);
            Status.Show($"Error saving settings: {ex.Message}", StatusTone.Error);
        }
    }

    // The switch saves at once; a failed save puts it back.
    private async Task SetEnabledAsync(SourceItem item, bool isEnabled)
    {
        try
        {
            await _sources.SetEnabledAsync(item.Key, isEnabled);
            item.Apply(item.Fields with { IsEnabled = isEnabled });
            Status.Show(SavedText, StatusTone.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Turning the source {Name} on or off failed.", item.Name);
            item.Apply(item.Fields);
            Status.Show($"Error saving settings: {ex.Message}", StatusTone.Error);
        }
    }

    // Raised on a background thread after any saved change; applied on the screen's thread.
    // Ignored until the tab has loaded, since its first load reads everything anyway.
    private void OnSourcesChanged()
    {
        if (_screenContext is null)
        {
            _ = ReloadIfLoadedAsync();
            return;
        }

        _screenContext.Post(_ => _ = ReloadIfLoadedAsync(), null);
    }

    private async Task ReloadIfLoadedAsync()
    {
        if (!IsLoaded || _isDisposed)
            return;
        await LoadAsync();
    }

    // Stops following the sources, which outlive the screen.
    public void Dispose()
    {
        _isDisposed = true;
        _sources.Changed -= OnSourcesChanged;
        if (_scheduledFailures is not null)
            _scheduledFailures.Changed -= OnScheduledFailuresChanged;
        Form?.StopTest();
        Status.Dispose();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

// The form that adds or edits a wallpaper source, on either device: the desktop's source dialog
// and the phone's full-screen form both show this one view model, so their fields, defaults,
// sentences and the Test button behave the same. The rules themselves are IWallpaperSources';
// this turns their answers into the desktop's sentences.
//
// Save writes the source at once and finishes the form; a refusal or a failed save shows inside
// the form, which stays open with what was typed. On the phone the form lives in the sources
// view model, which lives as long as the process, so a recreated screen shows the same draft.
public sealed partial class SourceFormViewModel : ObservableObject
{
    public const string AddTitle = "Add Wallpaper Source";
    public const string EditTitle = "Edit Wallpaper Source";
    public const string TestText = "Test";
    public const string TestingText = "Testing…";
    public const string DefaultImageUrlJPath = "$[*].imageUrl";
    public const string DefaultTitleJPath = "$[*].title";

    private readonly IWallpaperSources _sources;

    // The source being edited, or null when adding.
    private readonly SourceKey? _editedKey;

    // The test running now, so a second Test or closing the form can cancel it.
    private CancellationTokenSource? _testCts;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _imageUrlJPath = DefaultImageUrlJPath;

    [ObservableProperty]
    private string _titleJPath = DefaultTitleJPath;

    [ObservableProperty]
    private string _cronExpression = string.Empty;

    // The red line: why the source was not saved or tested. Null when there is nothing wrong.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    // The green box: what the last test found.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestResult))]
    private string? _testResultText;

    [ObservableProperty]
    private string _testButtonText = TestText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isSaving;

    // Raised once when the form is done: true when the source was saved, false on Cancel.
    public event Action<bool>? Finished;

    // A form for a new source: enabled, the usual picture-finding rules, no schedule (every
    // hour once saved).
    public SourceFormViewModel(IWallpaperSources sources)
    {
        _sources = sources.ThrowIfNull();
        Title = AddTitle;
    }

    // A form filled with an existing source's fields; Save replaces exactly that source.
    public SourceFormViewModel(IWallpaperSources sources, SourceKey key, SourceFields editing)
        : this(sources)
    {
        editing.ThrowIfNull();
        _editedKey = key.ThrowIfNull();
        Title = EditTitle;
        IsEnabled = editing.IsEnabled;
        Name = editing.Name;
        Url = editing.Url;
        ImageUrlJPath = editing.ImageUrlJPath;
        TitleJPath = editing.TitleJPath;
        CronExpression = editing.CronExpression;
    }

    public string Title { get; }

    public bool IsEditing => _editedKey is not null;

    public bool HasError => ErrorText is not null;

    public bool HasTestResult => TestResultText is not null;

    public bool CanSave => !IsSaving;

    // The name the source was saved under, once Save has worked.
    public string? SavedName { get; private set; }

    // The key of the saved source, once Save has worked: an edit that keeps its name keeps its
    // key; any other saved name is unique, so its key is the first of that name.
    public SourceKey? SavedKey { get; private set; }

    // Reads the feed with the typed address and rules and shows what it found. A second Test
    // cancels the first, whose result is dropped.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Test()
    {
        HideMessages();
        var fields = CurrentFields();
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _testCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        TestButtonText = TestingText;
        try
        {
            var outcome = await _sources.TestAsync(fields, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            if (outcome.Problem is { } problem)
            {
                ShowError(TestRefusalText(problem));
                return;
            }

            ShowTestResult(SuccessText(outcome.Images));
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer test, or the form closed: the result is not wanted
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
                ShowError($"Test failed: {ex.Message}");
        }
        finally
        {
            // Only the newest test sets the button back; an older one finishing must not
            // label a test that is still running.
            if (ReferenceEquals(_testCts, cts))
                TestButtonText = TestText;
        }
    }

    // Saves the source. A refusal or a failure shows here and the form stays open.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        var fields = CurrentFields();
        IsSaving = true;
        try
        {
            var problem = _editedKey is null
                ? await _sources.AddAsync(fields)
                : await _sources.ReplaceAsync(_editedKey, fields);
            if (problem is { } refused)
            {
                ShowError(SaveRefusalText(refused, fields.Name.Trim()));
                return;
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error saving settings: {ex.Message}");
            return;
        }
        finally
        {
            IsSaving = false;
        }

        SavedName = fields.Name.Trim();
        var keptName = _editedKey is not null
            && string.Equals(_editedKey.Name.Trim(), SavedName, StringComparison.OrdinalIgnoreCase);
        SavedKey = keptName ? _editedKey with { Name = SavedName } : new SourceKey(SavedName, 0);
        StopTest();
        Finished?.Invoke(true);
    }

    // Closes the form without saving, as the desktop's Cancel and close button do.
    [RelayCommand]
    private void Cancel()
    {
        StopTest();
        Finished?.Invoke(false);
    }

    // Cancels a test still running; its result is then dropped.
    public void StopTest()
    {
        var cts = Interlocked.Exchange(ref _testCts, null);
        cts?.Cancel();
        cts?.Dispose();
        TestButtonText = TestText;
    }

    // The sentence for a problem found when saving.
    public static string SaveRefusalText(SourceProblem problem, string name) => problem switch
    {
        SourceProblem.NameRequired => "Name is required.",
        SourceProblem.UrlRequired => "URL is required.",
        SourceProblem.UrlNotHttps => "URL must use HTTPS.",
        SourceProblem.ImageUrlJPathRequired => "Image URL JPath is required.",
        SourceProblem.TitleJPathRequired => "Title JPath is required.",
        SourceProblem.InvalidCron => "Invalid cron expression.",
        SourceProblem.NameTaken => $"A source named {name} already exists.",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
    };

    // The sentence for a problem found before testing; only the address reads differently.
    public static string TestRefusalText(SourceProblem problem) => problem == SourceProblem.UrlRequired
        ? "URL is required to test the source."
        : SaveRefusalText(problem, string.Empty);

    // The desktop's test result: the count, then each picture's title and address.
    public static string SuccessText(IReadOnlyList<WallpaperImage> images)
    {
        var preview = images.Select(img => $"Title: {img.Title}\nImage: {img.ImageUrl}");
        var text = $"Success \u2014 {images.Count} image(s) found.\n\n{string.Join("\n\n", preview)}";
        return text;
    }

    private SourceFields CurrentFields()
    {
        var fields = new SourceFields(Name ?? string.Empty, Url ?? string.Empty, ImageUrlJPath ?? string.Empty, TitleJPath ?? string.Empty, CronExpression ?? string.Empty, IsEnabled);
        return fields;
    }

    private void HideMessages()
    {
        ErrorText = null;
        TestResultText = null;
    }

    private void ShowError(string message)
    {
        TestResultText = null;
        ErrorText = message;
    }

    private void ShowTestResult(string message)
    {
        ErrorText = null;
        TestResultText = message;
    }
}

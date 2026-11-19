using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

// The pickers on the phone's Settings tab: each opens a list to choose one value from.
public enum SettingsPickerKind
{
    Resolution,
    ScheduleMode,
    IntervalType,
    Order,
    FillStyle,
}

// One choice in an open picker: its label, the line that explains it (none for the schedule
// modes and the interval units, as on the desktop), and whether it is the one chosen now.
public sealed record PickerChoice(string Label, string? Description, bool IsSelected)
{
    public bool HasDescription => !string.IsNullOrEmpty(Description);
}

// An open picker: what it chooses and its choices.
public sealed record SettingsPicker(SettingsPickerKind Kind, string Title, IReadOnlyList<PickerChoice> Choices);

// The phone's Settings tab (issue #203, step 4): the Download, Slideshow and General groups
// (the same groups the desktop window uses), About, the line that confirms each change, the open
// picker, whether Secrets is open, and the Konami code swiped on the tab. The phone has no
// "Title on Wallpaper" group: it draws no title (docs/android.md).
//
// It lives as long as the process, not the screen: Android destroys and recreates its screen (a
// new font size, language or display size), and the new screen's view attaches to this same view
// model, so the scroll place, an open picker and an open question (DebugSettingsViewModel's
// PendingQuestion) are still there. It never holds a view.
public sealed partial class SettingsViewModel : ObservableObject, IScrollPlace, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly EasterEggRequests _easterEggs;
    private readonly KonamiGestures _konami = new();
    private bool _isLoaded;

    [ObservableProperty]
    private SettingsPicker? _picker;

    // The Secrets page is open. A recreated screen opens it again.
    [ObservableProperty]
    private bool _isSecretsOpen;

    public SettingsViewModel(
        ISettingsStore settingsStore,
        DownloadSettingsViewModel download,
        SlideshowSettingsViewModel slideshow,
        DebugSettingsViewModel debug,
        AboutViewModel about,
        SecretsViewModel secrets,
        EasterEggRequests easterEggs)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        Download = download.ThrowIfNull();
        Slideshow = slideshow.ThrowIfNull();
        Debug = debug.ThrowIfNull();
        About = about.ThrowIfNull();
        Secrets = secrets.ThrowIfNull();
        _easterEggs = easterEggs.ThrowIfNull();
        foreach (var group in Groups)
            group.StatusReported += OnStatusReported;
        About.StatusReported += OnStatusReported;
    }

    public DownloadSettingsViewModel Download { get; }

    public SlideshowSettingsViewModel Slideshow { get; }

    public DebugSettingsViewModel Debug { get; }

    // The version (its taps play the version egg), Report a bug and the project link.
    public AboutViewModel About { get; }

    // The Secrets page's list.
    public SecretsViewModel Secrets { get; }

    // The line that confirms each change.
    public StatusLine Status { get; } = new();

    public double ScrollOffset { get; set; }

    private IEnumerable<SettingsGroupViewModel> Groups => [Download, Slideshow, Debug];

    // Reads the settings the first time a view shows the tab; a later view (after the screen
    // was recreated) finds them already read.
    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded)
            return;
        _isLoaded = true;
        var settings = await _settingsStore.LoadAsync();
        foreach (var group in Groups)
            group.Load(settings);
    }

    // Saves a change still waiting for its half second, because the tab is leaving the screen.
    public void Flush()
    {
        foreach (var group in Groups)
            group.Flush();
    }

    [RelayCommand]
    private void OpenResolutionPicker() => Open(SettingsPickerKind.Resolution);

    [RelayCommand]
    private void OpenScheduleModePicker() => Open(SettingsPickerKind.ScheduleMode);

    [RelayCommand]
    private void OpenIntervalTypePicker() => Open(SettingsPickerKind.IntervalType);

    [RelayCommand]
    private void OpenOrderPicker() => Open(SettingsPickerKind.Order);

    [RelayCommand]
    private void OpenFillStylePicker() => Open(SettingsPickerKind.FillStyle);

    // The Factory Reset button: the debug group asks its two questions.
    [RelayCommand]
    private void FactoryReset() => Debug.RequestFactoryReset();

    // The Secrets row: reads which eggs were found, then opens the page, so an egg found
    // moments ago is already ticked.
    [RelayCommand]
    private async Task OpenSecrets()
    {
        await Secrets.LoadAsync();
        IsSecretsOpen = true;
    }

    // Close on the Secrets page, or Android's back.
    [RelayCommand]
    private void CloseSecrets() => IsSecretsOpen = false;

    // A stroke made on the tab (KonamiGestures.Classify); the one that completes the Konami
    // code plays its egg.
    public void AddStroke(KonamiStroke stroke)
    {
        if (_konami.Add(stroke))
            _easterEggs.Request(EasterEggShows.Konami());
    }

    // Opens a picker on the value chosen now.
    public void Open(SettingsPickerKind kind)
    {
        Picker = kind switch
        {
            SettingsPickerKind.Resolution => Build(kind, "Saved Resolution", DownloadSettingsViewModel.ResolutionOptions,
                o => o.Label, o => o.Description, Download.SelectedResolution),
            SettingsPickerKind.ScheduleMode => Build(kind, "Schedule", SlideshowSettingsViewModel.ScheduleModeOptions,
                o => o.Label, _ => null, Slideshow.SelectedScheduleMode),
            SettingsPickerKind.IntervalType => Build(kind, "Interval", SlideshowSettingsViewModel.IntervalTypeOptions,
                o => o.Label, _ => null, Slideshow.SelectedIntervalType),
            SettingsPickerKind.Order => Build(kind, "Order", SlideshowSettingsViewModel.SlideshowOrderOptions,
                o => o.Label, o => o.Description, Slideshow.SelectedOrder),
            _ => Build(kind, "Fill Style", Slideshow.FillStyleOptions,
                o => o.Label, o => o.Description, Slideshow.SelectedFillStyle),
        };
    }

    // The person chose the choice at index: it is set (and saved by its group), and the
    // picker closes.
    public void Choose(int index)
    {
        var picker = Picker;
        if (picker is null || index < 0 || index >= picker.Choices.Count)
            return;
        switch (picker.Kind)
        {
            case SettingsPickerKind.Resolution:
                Download.SelectedResolution = DownloadSettingsViewModel.ResolutionOptions[index];
                break;
            case SettingsPickerKind.ScheduleMode:
                Slideshow.SelectedScheduleMode = SlideshowSettingsViewModel.ScheduleModeOptions[index];
                break;
            case SettingsPickerKind.IntervalType:
                Slideshow.SelectedIntervalType = SlideshowSettingsViewModel.IntervalTypeOptions[index];
                break;
            case SettingsPickerKind.Order:
                Slideshow.SelectedOrder = SlideshowSettingsViewModel.SlideshowOrderOptions[index];
                break;
            case SettingsPickerKind.FillStyle:
                Slideshow.SelectedFillStyle = Slideshow.FillStyleOptions[index];
                break;
        }
        Picker = null;
    }

    // Closes the picker without changing anything (Cancel, a tap outside it, Android's back).
    public void ClosePicker() => Picker = null;

    private static SettingsPicker Build<T>(
        SettingsPickerKind kind,
        string title,
        IReadOnlyList<T> options,
        Func<T, string> label,
        Func<T, string?> description,
        T selected)
    {
        var choices = options
            .Select(option => new PickerChoice(label(option), description(option), EqualityComparer<T>.Default.Equals(option, selected)))
            .ToList();
        return new SettingsPicker(kind, title, choices);
    }

    private void OnStatusReported(SettingsStatus status)
    {
        Status.Show(status.Text, status.Tone, status.Holds, status.DurationMs);
    }

    // Stops following the groups, which live as long as the process too.
    public void Dispose()
    {
        foreach (var group in Groups)
            group.StatusReported -= OnStatusReported;
        About.StatusReported -= OnStatusReported;
        Status.Dispose();
    }
}

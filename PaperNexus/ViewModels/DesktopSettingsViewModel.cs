using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

// The settings only the desktop has (issue #203, decision 8 keeps them off the phone): the
// download folder, launching at sign-in, minimizing to the tray on close, and automatic
// updates. It lives in the desktop host; the settings window adds it to its view model's
// groups (WallpaperConfigViewModel.AddHostGroup). Like every group it writes only these fields.
public sealed partial class DesktopSettingsViewModel : SettingsGroupViewModel
{
    private readonly IShellOpener _shellOpener;
    private readonly IStartupRegistration? _startupRegistration;

    [ObservableProperty]
    private string _folder = string.Empty;

    [ObservableProperty]
    private bool _runOnStartup = true;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _autoUpdatesEnabled = true;

    // A host with no run-at-sign-in setting registers no IStartupRegistration.
    public DesktopSettingsViewModel(
        ISettingsStore settingsStore,
        IShellOpener shellOpener,
        IStartupRegistration? startupRegistration = null)
        : base(settingsStore)
    {
        _shellOpener = shellOpener.ThrowIfNull();
        _startupRegistration = startupRegistration;
    }

    // The hint under the Run on startup box when this copy can change it.
    public const string RunOnStartupDescription = "Automatically launch PaperNexus when you sign in";

    // False in a copy that is not the installed one (issue #211): the box shows the saved
    // value but cannot be changed, and this group never saves it, so a build-folder or --debug
    // copy sharing settings.json cannot change what the installed copy registers at its next
    // start. True when the host has no registration to ask.
    public bool CanChangeRunOnStartup => _startupRegistration?.CanUpdate ?? true;

    // The grey line under the box: what the setting does, or why this copy cannot change it.
    public string RunOnStartupHint
    {
        get
        {
            if (CanChangeRunOnStartup)
                return RunOnStartupDescription;
            var folder = _startupRegistration?.ExecutableFolder;
            return $"Only the installed copy of PaperNexus can change this. This copy runs from {folder}.";
        }
    }

    partial void OnFolderChanged(string value) => SaveSoon();

    partial void OnMinimizeToTrayChanged(bool value) => SaveSoon();

    partial void OnAutoUpdatesEnabledChanged(bool value) => SaveSoon();

    // The sign-in registration follows the box at once, and is applied again when the window
    // loads the setting, as it always has been; the setting is saved with the group. A copy
    // that cannot change it neither registers nor saves.
    partial void OnRunOnStartupChanged(bool value)
    {
        if (!CanChangeRunOnStartup)
            return;
        try
        {
            _startupRegistration?.Update(value);
        }
        catch (Exception ex)
        {
            Report($"Failed to update startup registration: {ex.Message}", StatusTone.Error);
        }
        SaveSoon();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Folder;
        if (string.IsNullOrEmpty(folder))
            return;
        try
        {
            _shellOpener.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            Report($"Could not open folder: {ex.Message}", StatusTone.Error);
        }
    }

    protected override void ReadFrom(WallpaperNexusSettings settings)
    {
        Folder = settings.Download.Folder;
        RunOnStartup = settings.RunOnStartup;
        MinimizeToTray = settings.MinimizeToTray;
        AutoUpdatesEnabled = settings.AutoUpdatesEnabled;
    }

    protected override Action<WallpaperNexusSettings>? CaptureChange()
    {
        var folder = Folder;
        var runOnStartup = RunOnStartup;
        var minimizeToTray = MinimizeToTray;
        var autoUpdatesEnabled = AutoUpdatesEnabled;
        var canChangeRunOnStartup = CanChangeRunOnStartup;
        return latest =>
        {
            latest.Download.Folder = folder;
            // Left out from a copy that cannot change it, so the installed copy's value stands.
            if (canChangeRunOnStartup)
                latest.RunOnStartup = runOnStartup;
            latest.MinimizeToTray = minimizeToTray;
            latest.AutoUpdatesEnabled = autoUpdatesEnabled;
        };
    }
}

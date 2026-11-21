using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The desktop's own settings group: the folder, run on startup, minimize to tray and automatic
// updates. Like every group it writes only its own settings, and the sign-in registration
// follows the box.
public class DesktopSettingsViewModelTests : IDisposable
{
    private readonly string _directory;
    private readonly SettingsStore _store;

    public DesktopSettingsViewModelTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_DesktopGroup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _store = new SettingsStore(new GroupFolders(_directory));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    [Fact]
    public async Task WritesOnlyTheDesktopsOwnSettings()
    {
        await _store.UpdateAsync(latest =>
        {
            latest.Download.RetentionDays = 12;
            latest.Slideshow.Order = SlideshowOrder.Random;
            latest.Annotation.FontSize = 33;
            latest.DebugMode = true;
            latest.FavoriteWallpapers.Add("/kept/favorite.png");
        });
        var group = new DesktopSettingsViewModel(_store, new IgnoringShellOpener());
        await group.LoadAsync();

        group.Folder = "/new/folder";
        group.RunOnStartup = false;
        group.MinimizeToTray = false;
        group.AutoUpdatesEnabled = false;
        await group.SaveNowAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal("/new/folder", saved.Download.Folder);
        Assert.False(saved.RunOnStartup);
        Assert.False(saved.MinimizeToTray);
        Assert.False(saved.AutoUpdatesEnabled);
        Assert.Equal(12, saved.Download.RetentionDays);
        Assert.Equal(SlideshowOrder.Random, saved.Slideshow.Order);
        Assert.Equal(33, saved.Annotation.FontSize);
        Assert.True(saved.DebugMode);
        Assert.Equal(["/kept/favorite.png"], saved.FavoriteWallpapers);
    }

    [Fact]
    public async Task RunOnStartup_UpdatesTheSignInRegistrationAtOnce()
    {
        var registration = new RecordingStartupRegistration();
        var group = new DesktopSettingsViewModel(_store, new IgnoringShellOpener(), registration);
        await group.LoadAsync();

        group.RunOnStartup = false;
        group.RunOnStartup = true;

        Assert.Equal([false, true], registration.Updates);
    }

    [Fact]
    public async Task RunOnStartup_AFailedRegistrationIsReported()
    {
        var registration = new RecordingStartupRegistration { Failure = new IOException("read-only") };
        var group = new DesktopSettingsViewModel(_store, new IgnoringShellOpener(), registration);
        await group.LoadAsync();
        var reports = new List<SettingsStatus>();
        group.StatusReported += reports.Add;

        group.RunOnStartup = false;

        var report = Assert.Single(reports);
        Assert.Equal("Failed to update startup registration: read-only", report.Text);
        Assert.Equal(StatusTone.Error, report.Tone);
    }

    [Fact]
    public async Task RunOnStartup_TheInstalledCopyCanChangeIt()
    {
        var registration = new RecordingStartupRegistration();
        var group = new DesktopSettingsViewModel(_store, new IgnoringShellOpener(), registration);
        await group.LoadAsync();

        Assert.True(group.CanChangeRunOnStartup);
        Assert.Equal(DesktopSettingsViewModel.RunOnStartupDescription, group.RunOnStartupHint);
    }

    [Fact]
    public async Task RunOnStartup_ACopyThatIsNotInstalledShowsWhyItCannotChangeIt()
    {
        var registration = new RecordingStartupRegistration
        {
            CanUpdate = false,
            ExecutableFolder = "/home/someone/build/net10.0",
        };
        var group = new DesktopSettingsViewModel(_store, new IgnoringShellOpener(), registration);
        await group.LoadAsync();

        Assert.False(group.CanChangeRunOnStartup);
        Assert.Equal(
            "Only the installed copy of PaperNexus can change this. This copy runs from /home/someone/build/net10.0.",
            group.RunOnStartupHint);
    }

    [Fact]
    public async Task RunOnStartup_ACopyThatIsNotInstalledNeverRegistersOrSavesIt()
    {
        // The installed copy saved false; a build-folder copy shares the same settings.json.
        await _store.UpdateAsync(latest => latest.RunOnStartup = false);
        var registration = new RecordingStartupRegistration { CanUpdate = false };
        var group = new DesktopSettingsViewModel(_store, new IgnoringShellOpener(), registration);
        await group.LoadAsync();

        // Loading the saved false does not apply it, and neither does a change to the box.
        Assert.False(group.RunOnStartup);
        group.RunOnStartup = true;
        // Another of the group's settings is saved; Run on startup stays out of that save.
        group.MinimizeToTray = false;
        await group.SaveNowAsync();

        Assert.Empty(registration.Updates);
        var saved = await _store.LoadAsync();
        Assert.False(saved.RunOnStartup);
        Assert.False(saved.MinimizeToTray);
    }

    [Fact]
    public async Task RunOnStartup_TheInstalledCopyAppliesASavedFalseWhenTheWindowLoads()
    {
        // As it always has: loading a saved false updates the sign-in registration.
        await _store.UpdateAsync(latest => latest.RunOnStartup = false);
        var registration = new RecordingStartupRegistration();
        var group = new DesktopSettingsViewModel(_store, new IgnoringShellOpener(), registration);

        await group.LoadAsync();

        Assert.Equal([false], registration.Updates);
    }

    private sealed class GroupFolders : IAppFolders
    {
        public GroupFolders(string folder)
        {
            SettingsFolder = folder;
            DataFolder = folder;
            DefaultDownloadFolder = Path.Combine(folder, "Downloads");
        }

        public string SettingsFolder { get; }

        public string DataFolder { get; }

        public string DefaultDownloadFolder { get; }
    }

    private sealed class IgnoringShellOpener : IShellOpener
    {
        public void Open(string target) { }

        public void OpenFolder(string folderPath) { }
    }

    private sealed class RecordingStartupRegistration : IStartupRegistration
    {
        public List<bool> Updates { get; } = [];

        public bool CanUpdate { get; set; } = true;

        public string ExecutableFolder { get; set; } = "/opt/PaperNexus";

        public Exception? Failure { get; set; }

        public void Update(bool enable)
        {
            if (Failure is not null)
                throw Failure;
            Updates.Add(enable);
        }
    }
}

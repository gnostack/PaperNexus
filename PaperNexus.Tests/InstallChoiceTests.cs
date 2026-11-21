using PaperNexus.Core;
using PaperNexus.Views;
using Xunit;

namespace PaperNexus.Tests;

// The install screen saves its Run on startup box into the settings rather than writing the
// sign-in entry itself; the installed copy applies it at its first start (issue #211).
public class InstallChoiceTests : IDisposable
{
    private readonly string _directory;
    private readonly SettingsStore _store;

    public InstallChoiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_InstallChoice_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _store = new SettingsStore(new ChoiceFolders(_directory));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithNoSettingsTheDefaultsAreWrittenWithTheChoice(bool runOnStartup)
    {
        await InstallScreen.SaveRunOnStartupChoiceAsync(_store, runOnStartup);

        Assert.True(File.Exists(_store.FilePath));
        var saved = await _store.LoadAsync();
        Assert.Equal(runOnStartup, saved.RunOnStartup);
        Assert.True(saved.MinimizeToTray);
    }

    [Fact]
    public async Task ExistingSettingsKeepEverythingButTakeTheChoice()
    {
        // Settings left by an earlier install, or migrated from beside the downloaded file:
        // the install screen's box now wins over their RunOnStartup, and nothing else changes.
        await _store.UpdateAsync(latest =>
        {
            latest.RunOnStartup = true;
            latest.MinimizeToTray = false;
            latest.FavoriteWallpapers.Add("/kept/favorite.png");
        });

        await InstallScreen.SaveRunOnStartupChoiceAsync(_store, runOnStartup: false);

        var saved = await _store.LoadAsync();
        Assert.False(saved.RunOnStartup);
        Assert.False(saved.MinimizeToTray);
        Assert.Equal(["/kept/favorite.png"], saved.FavoriteWallpapers);
    }

    private sealed class ChoiceFolders : IAppFolders
    {
        public ChoiceFolders(string folder)
        {
            SettingsFolder = folder;
            DataFolder = folder;
            DefaultDownloadFolder = Path.Combine(folder, "Downloads");
        }

        public string SettingsFolder { get; }

        public string DataFolder { get; }

        public string DefaultDownloadFolder { get; }
    }
}

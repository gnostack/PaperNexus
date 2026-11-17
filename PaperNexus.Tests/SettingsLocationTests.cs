using PaperNexus.Core;
using PaperNexus.Core.Platform;
using Xunit;

namespace PaperNexus.Tests;

// The desktop host decides where the real settings.json lives, so the guarantee that a
// test store never touches it is checked here, against that real location.
public class SettingsLocationTests : IDisposable
{
    private readonly string _directory;
    private readonly SettingsStore _store;

    public SettingsLocationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_Settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _store = new SettingsStore(new TemporarySettingsDirectory(_directory));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        catch { }
    }

    [Fact]
    public void DesktopSettingsDirectory_IsTheDefaultInstallDirectory()
    {
        var directory = new DesktopSettingsDirectory();

        Assert.Equal(PlatformPaths.DefaultInstallDirectory, directory.FullPath);
    }

    // Guards the reason the store exists (issue 190): the suite used to delete and overwrite
    // the developer's real settings file. This only reads the real file, then drives a full
    // load-and-save cycle through a temporary store and checks the real file is exactly as
    // it was - or still absent, when there was none to begin with.
    [Fact]
    public async Task TemporaryStoreCycle_LeavesRealSettingsFileUnchanged()
    {
        var realDirectory = new DesktopSettingsDirectory().FullPath;
        var realPath = Path.Combine(realDirectory, SettingsStore.FileName);
        var existedBefore = File.Exists(realPath);
        var timestampBefore = existedBefore ? File.GetLastWriteTimeUtc(realPath) : DateTime.MinValue;
        var contentBefore = existedBefore ? await File.ReadAllBytesAsync(realPath) : [];

        var settings = await _store.LoadAsync();
        settings.RunOnStartup = !settings.RunOnStartup;
        settings.DiscoveredEasterEggs.Add("konami");
        await _store.SaveAsync(settings);
        var reloaded = await _store.LoadAsync();

        Assert.NotEqual(realPath, _store.FilePath);
        Assert.Contains("konami", reloaded.DiscoveredEasterEggs);
        Assert.Equal(existedBefore, File.Exists(realPath));
        if (existedBefore)
        {
            var timestampAfter = File.GetLastWriteTimeUtc(realPath);
            var contentAfter = await File.ReadAllBytesAsync(realPath);
            Assert.Equal(timestampBefore, timestampAfter);
            Assert.Equal(contentBefore, contentAfter);
        }
    }

    private sealed class TemporarySettingsDirectory : ISettingsDirectory
    {
        public TemporarySettingsDirectory(string fullPath)
        {
            FullPath = fullPath;
        }

        public string FullPath { get; }
    }
}

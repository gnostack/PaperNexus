using Microsoft.Extensions.DependencyInjection;
using PaperNexus.Core;
using PaperNexus.Core.Platform;
using Xunit;

namespace PaperNexus.Tests;

// Tests for ISettingsStore: the round trip, the corrupt-file fallback, and the guarantee
// that a test store never reaches the real settings.json in the install directory.
public class SettingsStoreTests : IDisposable
{
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.DeleteSettingsStore(_store);
    }

    // Guards the reason the store exists (issue 190): the suite used to delete and overwrite
    // the developer's real settings file. This only reads the real file, then drives a full
    // load-and-save cycle through a temporary store and checks the real file is exactly as
    // it was - or still absent, when there was none to begin with.
    [Fact]
    public async Task TemporaryStoreCycle_LeavesRealSettingsFileUnchanged()
    {
        var realPath = Path.Combine(PlatformPaths.DefaultInstallDirectory, "settings.json");
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

    [Fact]
    public async Task SaveThenLoad_RoundTripsValues()
    {
        var settings = new WallpaperNexusSettings
        {
            RunOnStartup = false,
            DebugMode = true,
            FavoriteWallpapers = ["/tmp/a.png"],
            Sources = [],
        };

        await _store.SaveAsync(settings);
        var loaded = await _store.LoadAsync();

        Assert.False(loaded.RunOnStartup);
        Assert.True(loaded.DebugMode);
        Assert.Equal(["/tmp/a.png"], loaded.FavoriteWallpapers);
        Assert.Empty(loaded.Sources);
    }

    // The save writes a temporary file and renames it over settings.json; nothing but the
    // settings file itself should be left behind.
    [Fact]
    public async Task Save_LeavesOnlyTheSettingsFile()
    {
        await _store.SaveAsync(new WallpaperNexusSettings());

        var directory = Path.GetDirectoryName(_store.FilePath);
        var files = Directory.GetFiles(directory);
        var onlyFile = Assert.Single(files);
        Assert.Equal(SettingsStore.FileName, Path.GetFileName(onlyFile));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsDefaults()
    {
        await File.WriteAllTextAsync(_store.FilePath, "{ this is not json");

        var loaded = await _store.LoadAsync();

        Assert.Equal(WallpaperNexusSettings.DefaultSources.Count, loaded.Sources.Count);
        Assert.True(loaded.RunOnStartup);
    }

    // The container hands out one production store, so every consumer shares it.
    [Fact]
    public void Container_ResolvesOneProductionStore()
    {
        var services = new ServiceCollection();
        services.AddServicesFrom(typeof(SettingsStore).Assembly);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ISettingsStore>();
        var second = provider.GetRequiredService<ISettingsStore>();

        Assert.IsType<SettingsStore>(first);
        Assert.Same(first, second);
    }
}

// Tests for EasterEggProgress recording through an injected store.
public class EasterEggProgressTests : IDisposable
{
    private readonly SettingsStore _store;

    public EasterEggProgressTests()
    {
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.DeleteSettingsStore(_store);
    }

    [Fact]
    public async Task RecordAsync_FirstDiscoveryIsNewAndPersisted_RepeatIsNot()
    {
        var progress = new EasterEggProgress(_store);

        var first = await progress.RecordAsync("konami");
        var repeat = await progress.RecordAsync("KONAMI");
        var settings = await _store.LoadAsync();

        Assert.True(first);
        Assert.False(repeat);
        Assert.Equal(["konami"], settings.DiscoveredEasterEggs);
    }

    // Two eggs firing together must both be kept; the recorder's gate serialises them.
    [Fact]
    public async Task RecordAsync_ConcurrentRecords_KeepsBoth()
    {
        var progress = new EasterEggProgress(_store);

        await Task.WhenAll(progress.RecordAsync("konami"), progress.RecordAsync("colors"));
        var settings = await _store.LoadAsync();

        Assert.Equal(2, settings.DiscoveredEasterEggs.Count);
    }
}

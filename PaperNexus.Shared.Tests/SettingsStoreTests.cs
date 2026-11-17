using Microsoft.Extensions.DependencyInjection;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// Tests for ISettingsStore: the round trip, the corrupt-file fallback and the container
// registration. The guarantee that a test store never reaches the real settings.json lives
// with the desktop host's tests, because the host decides where the real file is.
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

    // The host names the download folder used when the settings name none: a fresh install
    // (no file) and a file whose folder is blank both get it.
    [Fact]
    public async Task Load_NoFile_UsesTheHostsDownloadFolder()
    {
        var directory = Path.GetDirectoryName(_store.FilePath);

        var loaded = await _store.LoadAsync();

        Assert.Equal(Path.Combine(directory, "Downloads"), loaded.Download.Folder);
        Assert.True(loaded.IsConfigured);
    }

    [Fact]
    public async Task Load_BlankFolder_UsesTheHostsDownloadFolder()
    {
        var directory = Path.GetDirectoryName(_store.FilePath);
        await _store.SaveAsync(new WallpaperNexusSettings { Download = new DownloadSettings { Folder = "  " } });

        var loaded = await _store.LoadAsync();

        Assert.Equal(Path.Combine(directory, "Downloads"), loaded.Download.Folder);
    }

    // A folder the person chose is kept.
    [Fact]
    public async Task Load_ChosenFolder_IsKept()
    {
        await _store.SaveAsync(new WallpaperNexusSettings { Download = new DownloadSettings { Folder = "/somewhere/else" } });

        var loaded = await _store.LoadAsync();

        Assert.Equal("/somewhere/else", loaded.Download.Folder);
    }

    // The settings model itself names no folder, so it never depends on the device.
    [Fact]
    public void NewSettings_NameNoDownloadFolder()
    {
        Assert.Equal(string.Empty, new WallpaperNexusSettings().Download.Folder);
    }

    // The container hands out one production store, so every consumer shares it. The host
    // supplies the settings folder; a temporary one stands in for it here.
    [Fact]
    public void Container_ResolvesOneProductionStore()
    {
        var directory = Path.GetDirectoryName(_store.FilePath);
        var services = new ServiceCollection();
        services.AddSingleton<IAppFolders>(new TestAppFolders(directory));
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

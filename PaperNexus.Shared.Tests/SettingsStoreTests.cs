using Microsoft.Extensions.DependencyInjection;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// Tests for ISettingsStore: the round trip, updates that change only their own fields, the
// corrupt-file fallback and the container registration. The guarantee that a test store never reaches the real settings.json lives
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
    public async Task WriteThenLoad_RoundTripsValues()
    {
        var settings = new WallpaperNexusSettings
        {
            RunOnStartup = false,
            DebugMode = true,
            FavoriteWallpapers = ["/tmp/a.png"],
            Sources = [],
        };

        await _store.WriteAsync(settings);
        var loaded = await _store.LoadAsync();

        Assert.False(loaded.RunOnStartup);
        Assert.True(loaded.DebugMode);
        Assert.Equal(["/tmp/a.png"], loaded.FavoriteWallpapers);
        Assert.Empty(loaded.Sources);
    }

    // A write puts a temporary file down and renames it over settings.json; nothing but the
    // settings file itself should be left behind, after an update as after a whole write.
    [Fact]
    public async Task Write_LeavesOnlyTheSettingsFile()
    {
        await _store.WriteAsync(new WallpaperNexusSettings());
        await _store.UpdateAsync(latest => latest.DebugMode = true);

        var directory = Path.GetDirectoryName(_store.FilePath);
        var files = Directory.GetFiles(directory);
        var onlyFile = Assert.Single(files);
        Assert.Equal(SettingsStore.FileName, Path.GetFileName(onlyFile));
    }

    // An update changes only what its change sets; every other saved field stays as it was.
    [Fact]
    public async Task Update_ChangesOnlyWhatTheChangeSets()
    {
        await _store.WriteAsync(new WallpaperNexusSettings
        {
            CurrentWallpaperPath = "/tmp/current.png",
            FavoriteWallpapers = ["/tmp/a.png"],
            BannedWallpapers = ["/tmp/b.png"],
            DebugMode = true,
            Sources = [],
        });

        var written = await _store.UpdateAsync(latest => latest.WindowX = 40);
        var loaded = await _store.LoadAsync();

        Assert.Equal(40, written.WindowX);
        Assert.Equal(40, loaded.WindowX);
        Assert.Equal("/tmp/current.png", loaded.CurrentWallpaperPath);
        Assert.Equal(["/tmp/a.png"], loaded.FavoriteWallpapers);
        Assert.Equal(["/tmp/b.png"], loaded.BannedWallpapers);
        Assert.True(loaded.DebugMode);
    }

    // A change that throws writes nothing, and the failure reaches the caller.
    [Fact]
    public async Task Update_ChangeThrows_WritesNothing()
    {
        await _store.WriteAsync(new WallpaperNexusSettings { FavoriteWallpapers = ["/tmp/a.png"], Sources = [] });

        var ex = await Record.ExceptionAsync(() => _store.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers.Clear();
            throw new InvalidOperationException("change failed");
        }));
        var loaded = await _store.LoadAsync();

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal(["/tmp/a.png"], loaded.FavoriteWallpapers);
    }

    // Many updates at once from different writers, each changing its own field, all survive:
    // every update reads the settings the previous one wrote.
    [Fact]
    public async Task Update_ConcurrentWriters_KeepEveryChange()
    {
        var favorites = Enumerable.Range(0, 25).Select(i => $"/tmp/favorite-{i}.png").ToList();
        var bans = Enumerable.Range(0, 25).Select(i => $"/tmp/banned-{i}.png").ToList();
        var updates = new List<Task>();
        foreach (var favorite in favorites)
            updates.Add(Task.Run(() => _store.UpdateAsync(latest => latest.FavoriteWallpapers.Add(favorite))));
        foreach (var ban in bans)
            updates.Add(Task.Run(() => _store.UpdateAsync(latest => latest.BannedWallpapers.Add(ban))));
        updates.Add(Task.Run(() => _store.UpdateAsync(latest => latest.CurrentWallpaperPath = "/tmp/current.png")));
        updates.Add(Task.Run(() => _store.UpdateAsync(latest => latest.WindowWidth = 900)));
        updates.Add(Task.Run(() => _store.UpdateAsync(latest => latest.Slideshow.Enabled = false)));

        await Task.WhenAll(updates);
        var loaded = await _store.LoadAsync();

        Assert.Equal(favorites.Order(StringComparer.Ordinal), loaded.FavoriteWallpapers.Order(StringComparer.Ordinal));
        Assert.Equal(bans.Order(StringComparer.Ordinal), loaded.BannedWallpapers.Order(StringComparer.Ordinal));
        Assert.Equal("/tmp/current.png", loaded.CurrentWallpaperPath);
        Assert.Equal(900, loaded.WindowWidth);
        Assert.False(loaded.Slideshow.Enabled);
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
        await _store.WriteAsync(new WallpaperNexusSettings { Download = new DownloadSettings { Folder = "  " } });

        var loaded = await _store.LoadAsync();

        Assert.Equal(Path.Combine(directory, "Downloads"), loaded.Download.Folder);
    }

    // A folder the person chose is kept.
    [Fact]
    public async Task Load_ChosenFolder_IsKept()
    {
        await _store.WriteAsync(new WallpaperNexusSettings { Download = new DownloadSettings { Folder = "/somewhere/else" } });

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

    // Two eggs firing together must both be kept; the store's update lock serialises them.
    [Fact]
    public async Task RecordAsync_ConcurrentRecords_KeepsBoth()
    {
        var progress = new EasterEggProgress(_store);

        await Task.WhenAll(progress.RecordAsync("konami"), progress.RecordAsync("colors"));
        var settings = await _store.LoadAsync();

        Assert.Equal(2, settings.DiscoveredEasterEggs.Count);
    }
}

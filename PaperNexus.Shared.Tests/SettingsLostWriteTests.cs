using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// A change saved through the settings store while another writer is between reading the
// settings and writing them back must survive that writer's save. The switcher reads the
// settings, renders and sets the wallpaper, then records the new current wallpaper; the
// downloader reads them, downloads for as long as the network takes, then records each
// source's download time. Each test makes a second writer save a favorite inside that gap.
[Collection("Wallpaper")]
public class SettingsLostWriteTests : IDisposable
{
    private readonly string _wallpaperDir;
    private readonly SettingsStore _store;

    public SettingsLostWriteTests()
    {
        _wallpaperDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_LostWrite_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_wallpaperDir);
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.Cleanup();
        try { if (Directory.Exists(_wallpaperDir)) Directory.Delete(_wallpaperDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    // The second writer: hearts a picture through the store, the way a settings window or
    // the phone's Gallery does.
    private static Task AddFavoriteAsync(ISettingsStore store, string path)
    {
        return store.UpdateAsync(latest => latest.FavoriteWallpapers.Add(path));
    }

    [Fact]
    public async Task FavoriteSavedWhileASwitchIsSettingTheWallpaper_IsKept()
    {
        var picture = Path.Combine(_wallpaperDir, "Lake - a.png");
        TestHelpers.CreateSmallPng(picture);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        // The device sets the wallpaper after the switcher read the settings and before it
        // records the new current wallpaper.
        var applier = new CallbackWallpaperApplier(() => AddFavoriteAsync(_store, picture).GetAwaiter().GetResult());
        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, applier, _store, TestHelpers.AppFolders);

        var switched = await switcher.SwitchToNextAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(picture, switched);
        Assert.Equal(picture, saved.CurrentWallpaperPath);
        Assert.Contains(picture, saved.FavoriteWallpapers);
    }

    [Fact]
    public async Task FavoriteSavedWhileADownloadRuns_IsKept()
    {
        // A recent picture already in the folder, so the download's retention pass keeps it
        var favorite = Path.Combine(_wallpaperDir, "Desert - b.png");
        TestHelpers.CreateSmallPng(favorite);
        using var server = new LocalFeedServer(TestHelpers.CreateSmallPngBytes());
        var source = new WallpaperSource { Name = "Local", Url = server.FeedUrl };
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        await _store.UpdateAsync(latest => latest.Sources = [source]);
        // The picture is requested after the downloader read the settings and before it saves
        // the source's download time.
        server.OnImageRequested = () => AddFavoriteAsync(_store, favorite).GetAwaiter().GetResult();
        var sourceService = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var downloader = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, sourceService, _store, TestHelpers.UnusedTimerStore());

        await downloader.DownloadAllAsync();

        var saved = await _store.LoadAsync();
        Assert.Equal(1, server.ImageRequests);
        Assert.NotNull(saved.Sources.Single().LastDownloadUtc);
        Assert.Contains(favorite, saved.FavoriteWallpapers);
    }

    // The retention pass decides what to delete from the favorites as saved when it runs, not
    // as they were when the download began: an old picture hearted during the download stays.
    [Fact]
    public async Task OldPictureHeartedWhileADownloadRuns_IsNotDeletedByRetention()
    {
        var old = Path.Combine(_wallpaperDir, "Glacier - c.png");
        TestHelpers.CreateSmallPng(old);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-400));
        using var server = new LocalFeedServer(TestHelpers.CreateSmallPngBytes());
        var source = new WallpaperSource { Name = "Local", Url = server.FeedUrl };
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        await _store.UpdateAsync(latest => latest.Sources = [source]);
        server.OnImageRequested = () => AddFavoriteAsync(_store, old).GetAwaiter().GetResult();
        var sourceService = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var downloader = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, sourceService, _store, TestHelpers.UnusedTimerStore());

        await downloader.DownloadAllAsync();

        var saved = await _store.LoadAsync();
        Assert.True(File.Exists(old));
        Assert.Contains(old, saved.FavoriteWallpapers);
    }

    // The settings window writes only the settings it edits: a favorite, a ban and a new
    // current wallpaper saved after the window loaded survive the window's save.
    [Fact]
    public async Task SettingsWindowSave_KeepsWhatOtherWritersSaved()
    {
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();
        await _store.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers.Add("/tmp/favorite.png");
            latest.BannedWallpapers.Add("/tmp/banned.png");
            latest.CurrentWallpaperPath = "/tmp/current.png";
            latest.DiscoveredEasterEggs.Add("konami");
            latest.WindowX = 40;
        });

        // Editing a setting saves the window's settings after a short pause
        vm.Download.RetentionDays = 30;
        for (var i = 0; i < 100 && (await _store.LoadAsync()).Download.RetentionDays != 30; i++)
            await Task.Delay(50);

        var saved = await _store.LoadAsync();
        Assert.Equal(30, saved.Download.RetentionDays);
        Assert.Equal(["/tmp/favorite.png"], saved.FavoriteWallpapers);
        Assert.Equal(["/tmp/banned.png"], saved.BannedWallpapers);
        Assert.Equal("/tmp/current.png", saved.CurrentWallpaperPath);
        Assert.Equal(["konami"], saved.DiscoveredEasterEggs);
        Assert.Equal(40, saved.WindowX);
        vm.Cleanup();
    }

    // A switch, a download and a run of hearts all at once: each keeps what it wrote.
    [Fact]
    public async Task SwitchDownloadAndHeartsTogether_KeepEveryChange()
    {
        var picture = Path.Combine(_wallpaperDir, "Lake - a.png");
        TestHelpers.CreateSmallPng(picture);
        using var server = new LocalFeedServer(TestHelpers.CreateSmallPngBytes());
        var source = new WallpaperSource { Name = "Local", Url = server.FeedUrl };
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        await _store.UpdateAsync(latest => latest.Sources = [source]);
        // Pictures in the folder, so the download's retention pass keeps them as favorites
        var favorites = Enumerable.Range(0, 20).Select(i => Path.Combine(_wallpaperDir, $"Picture {i} - p{i}.png")).ToList();
        foreach (var favorite in favorites)
            TestHelpers.CreateSmallPng(favorite);
        var progress = new EasterEggProgress(_store);
        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store, TestHelpers.AppFolders);
        var sourceService = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var downloader = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, sourceService, _store, TestHelpers.UnusedTimerStore());

        var writers = new List<Task>
        {
            Task.Run(switcher.SwitchToNextAsync),
            Task.Run(downloader.DownloadAllAsync),
            Task.Run(() => progress.RecordAsync("konami")),
        };
        writers.AddRange(favorites.Select(f => Task.Run(() => AddFavoriteAsync(_store, f))));
        await Task.WhenAll(writers);

        var saved = await _store.LoadAsync();
        Assert.False(string.IsNullOrEmpty(saved.CurrentWallpaperPath));
        Assert.NotNull(saved.Sources.Single().LastDownloadUtc);
        Assert.Contains("konami", saved.DiscoveredEasterEggs);
        Assert.Equal(favorites.Order(StringComparer.Ordinal), saved.FavoriteWallpapers.Order(StringComparer.Ordinal));
    }

    // Sets the wallpaper by running a callback, so a test can act at the moment the device
    // takes the new wallpaper.
    private sealed class CallbackWallpaperApplier : IWallpaperApplier
    {
        private readonly Action _onSetWallpaper;

        public CallbackWallpaperApplier(Action onSetWallpaper)
        {
            _onSetWallpaper = onSetWallpaper;
        }

        public bool CanSetWallpaper => true;

        public WallpaperCanvas? GetCanvas() => null;

        public bool SetWallpaper(string wallpaperPath)
        {
            _onSetWallpaper();
            return true;
        }

        public void ApplyFillStyle(WallpaperFillStyle style) { }
    }

    // A wallpaper feed on the loopback address: the feed lists one picture, and the picture
    // request runs OnImageRequested before it is answered.
    private sealed class LocalFeedServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _image;
        private readonly Task _loop;
        private readonly string _baseUrl;

        public LocalFeedServer(byte[] image)
        {
            _image = image;
            var port = FreePort();
            _baseUrl = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        public string FeedUrl => _baseUrl + "feed.json";

        public Action? OnImageRequested { get; set; }

        public int ImageRequests { get; private set; }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }

                byte[] body;
                if (context.Request.Url!.AbsolutePath.EndsWith("feed.json", StringComparison.Ordinal))
                {
                    var feed = $"[{{\"imageUrl\":\"{_baseUrl}lake.png\",\"title\":\"Lake\"}}]";
                    body = Encoding.UTF8.GetBytes(feed);
                }
                else
                {
                    ImageRequests++;
                    OnImageRequested?.Invoke();
                    body = _image;
                }

                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }
}

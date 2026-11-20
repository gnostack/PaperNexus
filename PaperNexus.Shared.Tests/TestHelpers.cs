using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using SkiaSharp;

namespace PaperNexus.Tests;

internal static class TestHelpers
{
    // The data folder every test's host folders name: a temporary folder for this test run,
    // so the processed current.png/current.jpg never land beside the test assembly.
    internal static readonly string BaseDir = CreateDataFolder();
    internal static readonly string PngPath = Path.Combine(BaseDir, "current.png");
    internal static readonly string JpgPath = Path.Combine(BaseDir, "current.jpg");

    // Host folders for tests: settings and downloads are given per test, working files go to
    // BaseDir.
    internal static readonly TestAppFolders AppFolders = new(BaseDir);

    private static string CreateDataFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"PaperNexus_Data_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    internal static void CreateSmallPng(string path, byte r = 100, byte g = 150, byte b = 200)
    {
        CreateTestPng(path, 100, 100, r, g, b);
    }

    // Creates a solid-colour PNG at the specified pixel dimensions.
    // Useful for testing resize and resolution-cap logic with controlled image sizes.
    internal static void CreateTestPng(string path, int width, int height, byte r = 100, byte g = 150, byte b = 200)
    {
        using var bitmap = CreateSolidBitmap(width, height, new SKColor(r, g, b));
        Save(bitmap, path, SKEncodedImageFormat.Png, 100);
    }

    // The encoded bytes of a small PNG, for a test that serves a picture rather than writing one.
    internal static byte[] CreateSmallPngBytes()
    {
        using var bitmap = CreateSolidBitmap(100, 100, new SKColor(100, 150, 200));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // A JPEG of the given size with a color gradient and shapes, so a downscaled copy has
    // detail to compare.
    internal static void CreateDetailedJpeg(string path, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, height),
                [new SKColor(20, 60, 160), new SKColor(240, 180, 40)], SKShaderTileMode.Clamp);
            using var fill = new SKPaint { Shader = gradient };
            canvas.DrawRect(0, 0, width, height, fill);
            using var shape = new SKPaint { Color = new SKColor(250, 250, 250), IsAntialias = true };
            for (var i = 0; i < 8; i++)
                canvas.DrawCircle(width * (i + 1) / 9f, height / 2f, height / 10f, shape);
        }
        Save(bitmap, path, SKEncodedImageFormat.Jpeg, 90);
    }

    internal static void CreateSmallJpeg(string path, byte r = 200, byte g = 100, byte b = 50)
    {
        using var bitmap = CreateSolidBitmap(100, 100, new SKColor(r, g, b));
        Save(bitmap, path, SKEncodedImageFormat.Jpeg, 75);
    }

    /// <summary>
    /// Creates a large PNG with random pixel data that exceeds 16 MB when
    /// re-encoded as RGB8 PNG, forcing the JPEG fallback in ApplyWallpaperAsync.
    /// </summary>
    internal static void CreateOversizedPng(string path)
    {
        using var bitmap = CreateNoiseBitmap(4000, 4000, seed: 42);
        Save(bitmap, path, SKEncodedImageFormat.Png, 100);
    }

    // An opaque bitmap filled with one colour.
    internal static SKBitmap CreateSolidBitmap(int width, int height, SKColor color)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        bitmap.Erase(color);
        return bitmap;
    }

    // An opaque bitmap of seeded random pixels - incompressible, so its PNG is large.
    internal static SKBitmap CreateNoiseBitmap(int width, int height, int seed)
    {
        var rng = new Random(seed);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        var pixels = new byte[width * height * 4];
        rng.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 0xFF;
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    // Width and height of an encoded image file, read from its header.
    internal static (int Width, int Height) ReadDimensions(string path)
    {
        using var codec = SKCodec.Create(path);
        return (codec.Info.Width, codec.Info.Height);
    }

    internal static (int Width, int Height) ReadDimensions(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data);
        return (codec.Info.Width, codec.Info.Height);
    }

    private static void Save(SKBitmap bitmap, string path, SKEncodedImageFormat format, int quality)
    {
        using var file = File.Create(path);
        bitmap.Encode(file, format, quality);
    }

    // A settings store rooted in its own new temporary directory. Tests never use the
    // production store, so no test can read, write or delete the real settings.json.
    // Its default download folder is a "Downloads" folder beside the settings, which no test
    // creates unless it means to.
    internal static SettingsStore CreateSettingsStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_Settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return new SettingsStore(directory, Path.Combine(directory, "Downloads"));
    }

    // A settings view model wired to the given store and to do-nothing host services, so no
    // test opens a browser or file manager. The background services stay unregistered, as
    // they were when the view model looked them up from a running app that tests never had.
    // Its picture library uses a real switcher with a no-op wallpaper applier, so deleting the
    // current wallpaper sets the next one without touching the machine's wallpaper.
    internal static WallpaperConfigViewModel CreateViewModel(
        ISettingsStore store,
        IWallpaperSources? sources = null,
        IDownloadWallpapers? downloader = null,
        EasterEggRequests? easterEggs = null)
    {
        var library = CreateLibrary(store);
        var wallpaperSources = sources ?? CreateWallpaperSources(store);
        var eggs = easterEggs ?? new EasterEggRequests();
        var appInfo = new TestAppInfo();
        var shellOpener = new NoOpShellOpener();
        var viewModel = WithoutSynchronizationContext(
            () => new WallpaperConfigViewModel(
                store,
                appInfo,
                shellOpener,
                AppFolders,
                library,
                wallpaperSources,
                eggs,
                new DownloadSettingsViewModel(store),
                new SlideshowSettingsViewModel(store, TestSettingRules.Desktop),
                new AnnotationSettingsViewModel(store, eggs),
                new DebugSettingsViewModel(store, appInfo, new LogLevelSwitch(false), new RecordingFactoryReset()),
                new AboutViewModel(appInfo, shellOpener, eggs),
                downloadWallpapers: downloader));
        return viewModel;
    }

    // Waits until the saved settings satisfy condition, for a group's save, which runs half a
    // second after the change. Gives up after five seconds; the caller asserts afterwards.
    internal static async Task<WallpaperNexusSettings> WaitForSavedAsync(ISettingsStore store, Func<WallpaperNexusSettings, bool> condition)
    {
        var settings = await store.LoadAsync();
        for (var i = 0; i < 100 && !condition(settings); i++)
        {
            await Task.Delay(50);
            settings = await store.LoadAsync();
        }
        return settings;
    }

    // The sources service on the given store. Its feed reader answers through handler when one
    // is given (a test feed at an https address); without one it would use the network, which
    // only tests that never read a feed may do.
    internal static WallpaperSources CreateWallpaperSources(ISettingsStore store, HttpMessageHandler? handler = null)
    {
        var logger = NullLogger<HttpWallpaperSourceService>.Instance;
        var feedReader = handler is null
            ? new HttpWallpaperSourceService(logger)
            : new HttpWallpaperSourceService(logger, handler);
        var sources = new WallpaperSources(store, feedReader);
        return sources;
    }

    // Builds a view model with no synchronization context, so it applies the library's change
    // notices inline, in order with the command that caused them. xunit runs tests under a
    // context that posts to the thread pool, where a notice and the command would race; on a
    // screen both run on the one UI thread.
    internal static T WithoutSynchronizationContext<T>(Func<T> create)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return create();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // A picture library on the given store, with a real switcher that changes nothing on the
    // machine (a no-op wallpaper applier, working files in BaseDir).
    internal static WallpaperLibrary CreateLibrary(ISettingsStore store, ISwitchWallpaper? switcher = null)
    {
        switcher ??= new SwitchWallpaper(Microsoft.Extensions.Logging.Abstractions.NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, store, AppFolders, TestSettingRules.Desktop);
        var library = new WallpaperLibrary(store, switcher);
        return library;
    }

    // Removes the temporary directory a test store was rooted in.
    internal static void DeleteSettingsStore(SettingsStore store)
    {
        var directory = Path.GetDirectoryName(store.FilePath);
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch { }
    }

    internal static async Task WriteSettingsAsync(SettingsStore store, string wallpaperFolder, string currentWallpaperPath = "")
    {
        var settings = new WallpaperNexusSettings
        {
            Slideshow = new SlideshowSettings
            {
                Enabled = false,
                Order = SlideshowOrder.Alphabetical,
            },
            Download = new DownloadSettings { Folder = wallpaperFolder },
            CurrentWallpaperPath = currentWallpaperPath,
            AnnotateWallpaper = false,
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
            Sources = [],
        };
        await store.WriteAsync(settings);
    }

    // Removes the processed current.* wallpaper files, which the switcher writes beside the
    // test assembly. Settings are not touched here: each test owns a temporary store.
    internal static void Cleanup()
    {
        TryDelete(PngPath);
        TryDelete(JpgPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}

// Host stand-ins for the shared code's host interfaces. Every folder is the one given.
internal sealed class TestAppFolders : IAppFolders
{
    public TestAppFolders(string folder)
    {
        SettingsFolder = folder;
        DataFolder = folder;
        DefaultDownloadFolder = Path.Combine(folder, "Downloads");
    }

    public string SettingsFolder { get; }

    public string DataFolder { get; }

    public string DefaultDownloadFolder { get; }
}

internal sealed class TestAppInfo : IAppInfo
{
    public TestAppInfo(bool startedInDebugMode = false)
    {
        StartedInDebugMode = startedInDebugMode;
    }

    public string Version => "v0";

    public bool StartedInDebugMode { get; }
}

// The two devices' setting rules, as their hosts give them.
internal sealed class TestSettingRules : IDeviceSettingRules
{
    public static readonly TestSettingRules Desktop = new(
        true,
        [WallpaperFillStyle.Fill, WallpaperFillStyle.Fit, WallpaperFillStyle.Stretch, WallpaperFillStyle.Tile, WallpaperFillStyle.Center, WallpaperFillStyle.Span]);

    public static readonly TestSettingRules Phone = new(
        false,
        [WallpaperFillStyle.Fill, WallpaperFillStyle.Fit, WallpaperFillStyle.Stretch, WallpaperFillStyle.Tile, WallpaperFillStyle.Center]);

    public TestSettingRules(bool drawsWallpaperTitle, IReadOnlyList<WallpaperFillStyle> fillStyles)
    {
        DrawsWallpaperTitle = drawsWallpaperTitle;
        FillStyles = fillStyles;
    }

    public bool DrawsWallpaperTitle { get; }

    public IReadOnlyList<WallpaperFillStyle> FillStyles { get; }
}

// A factory reset that only counts its calls, or fails when told to.
internal sealed class RecordingFactoryReset : IFactoryReset
{
    public int Calls { get; private set; }

    public Exception? Failure { get; set; }

    public Task ResetAsync()
    {
        Calls++;
        if (Failure is not null)
            throw Failure;
        return Task.CompletedTask;
    }
}

// A device whose Secrets list is the given eggs.
internal sealed class TestDeviceEggs : IDeviceEasterEggs
{
    public TestDeviceEggs(IReadOnlyList<EasterEggDefinition> eggs)
    {
        Eggs = eggs;
    }

    public IReadOnlyList<EasterEggDefinition> Eggs { get; }
}

internal sealed class NoOpShellOpener : IShellOpener
{
    public void Open(string target) { }

    public void OpenFolder(string folderPath) { }
}

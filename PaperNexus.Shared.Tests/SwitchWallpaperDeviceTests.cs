using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// A wallpaper applier whose answers a test sets: whether the device takes the wallpaper,
// whether it allows changes at all, and the canvas it wants.
internal sealed class FakeWallpaperApplier : IWallpaperApplier
{
    public bool Accepts { get; set; } = true;
    public bool CanSetWallpaper { get; set; } = true;
    public WallpaperCanvas? Canvas { get; set; }
    public List<string> SetPaths { get; } = [];

    // When set, SetWallpaper waits for it, so a test can hold a switch half way.
    public TaskCompletionSource? Gate { get; set; }

    public WallpaperCanvas? GetCanvas() => Canvas;

    public bool SetWallpaper(string wallpaperPath)
    {
        Gate?.Task.Wait();
        lock (SetPaths)
            SetPaths.Add(wallpaperPath);
        return Accepts;
    }

    public void ApplyFillStyle(WallpaperFillStyle style) { }
}

// What the switcher does with the device's answers: a refused wallpaper, a canvas, the
// pictures it counts, and two switches asked for at once.
[Collection("Wallpaper")]
public class SwitchWallpaperDeviceTests : IDisposable
{
    private readonly string _wallpaperDir;
    private readonly SettingsStore _store;

    public SwitchWallpaperDeviceTests()
    {
        _wallpaperDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Test_{Guid.NewGuid():N}");
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

    private SwitchWallpaper CreateSwitcher(IWallpaperApplier applier, IDeviceSettingRules? deviceRules = null)
    {
        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, applier, _store, TestHelpers.AppFolders, deviceRules ?? TestSettingRules.Desktop);
        return switcher;
    }

    // A wallpaper the device refuses is not recorded: the previous one stays current, and no
    // one is told the wallpaper changed.
    [Fact]
    public async Task SwitchToNext_DeviceRefuses_KeepsThePreviousWallpaperCurrent()
    {
        var first = Path.Combine(_wallpaperDir, "a.png");
        var second = Path.Combine(_wallpaperDir, "b.png");
        TestHelpers.CreateSmallPng(first);
        TestHelpers.CreateSmallPng(second);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: first);
        var applier = new FakeWallpaperApplier { Accepts = false };
        var switcher = CreateSwitcher(applier);
        var changed = new List<string>();
        switcher.WallpaperChanged += changed.Add;

        var refusal = await Assert.ThrowsAsync<WallpaperRefusedException>(switcher.SwitchToNextAsync);

        Assert.Equal(second, refusal.WallpaperPath);
        Assert.Single(applier.SetPaths);
        Assert.Empty(changed);
        var settings = await _store.LoadAsync();
        Assert.Equal(first, settings.CurrentWallpaperPath);
    }

    [Fact]
    public async Task SwitchToRandom_DeviceRefuses_KeepsThePreviousWallpaperCurrent()
    {
        var first = Path.Combine(_wallpaperDir, "a.png");
        TestHelpers.CreateSmallPng(first);
        TestHelpers.CreateSmallPng(Path.Combine(_wallpaperDir, "b.png"));
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: first);
        var switcher = CreateSwitcher(new FakeWallpaperApplier { Accepts = false });
        var changed = new List<string>();
        switcher.WallpaperChanged += changed.Add;

        await Assert.ThrowsAsync<WallpaperRefusedException>(switcher.SwitchToRandomAsync);

        Assert.Empty(changed);
        Assert.Equal(first, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    [Fact]
    public async Task SwitchToSpecific_DeviceRefuses_KeepsThePreviousWallpaperCurrent()
    {
        var first = Path.Combine(_wallpaperDir, "a.png");
        var second = Path.Combine(_wallpaperDir, "b.png");
        TestHelpers.CreateSmallPng(first);
        TestHelpers.CreateSmallPng(second);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: first);
        var switcher = CreateSwitcher(new FakeWallpaperApplier { Accepts = false });

        await Assert.ThrowsAsync<WallpaperRefusedException>(() => switcher.SwitchToSpecificAsync(second));

        Assert.Equal(first, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    // After a refusal the next accepted switch starts from the wallpaper that is still current.
    [Fact]
    public async Task SwitchToNext_AfterARefusal_AdvancesFromTheStillCurrentWallpaper()
    {
        var first = Path.Combine(_wallpaperDir, "a.png");
        var second = Path.Combine(_wallpaperDir, "b.png");
        TestHelpers.CreateSmallPng(first);
        TestHelpers.CreateSmallPng(second);
        TestHelpers.CreateSmallPng(Path.Combine(_wallpaperDir, "c.png"));
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: first);
        var applier = new FakeWallpaperApplier { Accepts = false };
        var switcher = CreateSwitcher(applier);
        await Assert.ThrowsAsync<WallpaperRefusedException>(switcher.SwitchToNextAsync);

        applier.Accepts = true;
        var result = await switcher.SwitchToNextAsync();

        Assert.Equal(second, result);
    }

    // With a canvas the processed wallpaper is the canvas's size, not the picture's.
    [Fact]
    public async Task SwitchToNext_WithACanvas_WritesACanvasSizedWallpaper()
    {
        var picture = Path.Combine(_wallpaperDir, "wide - x.png");
        TestHelpers.CreateTestPng(picture, 800, 450);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        var canvas = WallpaperCanvas.ForScreen(270, 600, 1.1f);
        var applier = new FakeWallpaperApplier { Canvas = canvas };

        var result = await CreateSwitcher(applier).SwitchToNextAsync();

        Assert.Equal(picture, result);
        var written = Assert.Single(applier.SetPaths);
        Assert.Equal(TestHelpers.PngPath, written);
        Assert.Equal((canvas.Width, canvas.Height), TestHelpers.ReadDimensions(written));
    }

    // A device that draws no title (the phone) writes the same wallpaper with the title switched
    // on in the settings as with it off: an older phone settings file has it on.
    [Fact]
    public async Task SwitchToNext_ADeviceThatDrawsNoTitle_DrawsNoneWhenTheSettingIsOn()
    {
        var withTitleOn = await RenderAsync(TestSettingRules.Phone, annotate: true);
        var withTitleOff = await RenderAsync(TestSettingRules.Phone, annotate: false);

        Assert.Equal(withTitleOff, withTitleOn);
    }

    // A device that draws titles (the desktop) draws one when the setting is on.
    [Fact]
    public async Task SwitchToNext_ADeviceThatDrawsTitles_DrawsOneWhenTheSettingIsOn()
    {
        var withTitleOn = await RenderAsync(TestSettingRules.Desktop, annotate: true);
        var withTitleOff = await RenderAsync(TestSettingRules.Desktop, annotate: false);

        Assert.NotEqual(withTitleOff, withTitleOn);
    }

    // Switches to a dark picture with a long title on the given device and returns the
    // processed wallpaper's pixels.
    private async Task<byte[]> RenderAsync(IDeviceSettingRules deviceRules, bool annotate)
    {
        var picture = Path.Combine(_wallpaperDir, "A Lake at Night - x.png");
        TestHelpers.CreateTestPng(picture, 800, 450, 20, 30, 40);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        await _store.UpdateAsync(latest => latest.AnnotateWallpaper = annotate);
        var applier = new FakeWallpaperApplier();

        await CreateSwitcher(applier, deviceRules).SwitchToNextAsync();

        using var written = SkiaSharp.SKBitmap.Decode(Assert.Single(applier.SetPaths));
        return written.Bytes;
    }

    // Without a canvas (the desktop) the processed wallpaper keeps the picture's size.
    [Fact]
    public async Task SwitchToNext_WithoutACanvas_KeepsThePictureSize()
    {
        var picture = Path.Combine(_wallpaperDir, "wide - x.png");
        TestHelpers.CreateTestPng(picture, 800, 450);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        var applier = new FakeWallpaperApplier();

        await CreateSwitcher(applier).SwitchToNextAsync();

        Assert.Equal((800, 450), TestHelpers.ReadDimensions(Assert.Single(applier.SetPaths)));
    }

    // Partial downloads and banned pictures are not wallpapers.
    [Fact]
    public async Task HasWallpapers_CountsOnlyPicturesThatAreNotBanned()
    {
        var switcher = CreateSwitcher(new FakeWallpaperApplier());
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        File.WriteAllBytes(Path.Combine(_wallpaperDir, "half.png" + DownloadWallpapers.PartialDownloadExtension), [0x89]);
        Assert.False(await switcher.HasWallpapersAsync());

        var banned = Path.Combine(_wallpaperDir, "banned.png");
        TestHelpers.CreateSmallPng(banned);
        var settings = await _store.LoadAsync();
        settings.BannedWallpapers.Add(banned);
        await _store.WriteAsync(settings);
        Assert.False(await switcher.HasWallpapersAsync());

        TestHelpers.CreateSmallPng(Path.Combine(_wallpaperDir, "good.png"));
        Assert.True(await switcher.HasWallpapersAsync());
    }

    // Two switches asked for at once run one after the other: the second waits for the first
    // to set its wallpaper and starts from it.
    [Fact]
    public async Task SwitchesAskedTogether_RunOneAfterTheOther()
    {
        var first = Path.Combine(_wallpaperDir, "a.png");
        var second = Path.Combine(_wallpaperDir, "b.png");
        var third = Path.Combine(_wallpaperDir, "c.png");
        TestHelpers.CreateSmallPng(first);
        TestHelpers.CreateSmallPng(second);
        TestHelpers.CreateSmallPng(third);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir, currentWallpaperPath: first);
        var gate = new TaskCompletionSource();
        var applier = new FakeWallpaperApplier { Gate = gate };
        var switcher = CreateSwitcher(applier);

        var one = Task.Run(switcher.SwitchToNextAsync);
        var two = Task.Run(switcher.SwitchToNextAsync);
        await Task.Delay(300);
        // The first is held inside SetWallpaper; the second has not reached it
        Assert.True(applier.SetPaths.Count == 0);
        gate.SetResult();
        var results = await Task.WhenAll(one, two);

        Assert.Equal([second, third], results.Order());
        Assert.Equal(third, (await _store.LoadAsync()).CurrentWallpaperPath);
    }

    // The long-press menu reads IsSwitching (issue #203, step 6): true from the moment a switch
    // is asked for, including while a second one waits its turn, and false once each has ended.
    [Fact]
    public async Task IsSwitching_IsTrueWhileASwitchRunsOrWaits_AndFalseAfter()
    {
        TestHelpers.CreateSmallPng(Path.Combine(_wallpaperDir, "a.png"));
        TestHelpers.CreateSmallPng(Path.Combine(_wallpaperDir, "b.png"));
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        var gate = new TaskCompletionSource();
        var switcher = CreateSwitcher(new FakeWallpaperApplier { Gate = gate });
        Assert.False(switcher.IsSwitching);

        var one = switcher.SwitchToNextAsync();
        Assert.True(switcher.IsSwitching);
        var two = switcher.SwitchToRandomAsync();
        gate.SetResult();
        await one;
        await two;

        Assert.False(switcher.IsSwitching);
    }

    [Fact]
    public async Task IsSwitching_IsFalseAfterARefusedSwitch()
    {
        TestHelpers.CreateSmallPng(Path.Combine(_wallpaperDir, "a.png"));
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);
        var switcher = CreateSwitcher(new FakeWallpaperApplier { Accepts = false });

        await Assert.ThrowsAsync<WallpaperRefusedException>(switcher.SwitchToNextAsync);

        Assert.False(switcher.IsSwitching);
    }
}

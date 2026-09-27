using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

/// <summary>
/// Tests for RefreshPreviewImage file selection and the end-to-end
/// switch-then-refresh flow. Bitmap creation requires an Avalonia
/// platform backend (not available in a plain xUnit host), so these
/// tests verify file-system state rather than the Bitmap object.
/// </summary>
[Collection("Wallpaper")]
public class RefreshPreviewImageTests : IAsyncLifetime, IDisposable
{
    private readonly string _wallpaperDir;
    private readonly SettingsStore _store;

    public RefreshPreviewImageTests()
    {
        _wallpaperDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_wallpaperDir);
        _store = TestHelpers.CreateSettingsStore();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        TestHelpers.Cleanup();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_wallpaperDir)) Directory.Delete(_wallpaperDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    [Fact]
    public void WhenNeitherExists_SetsNull()
    {
        // Arrange
        TestHelpers.Cleanup();
        var vm = new WallpaperConfigViewModel(_store);

        // Act
        vm.RefreshPreviewImage();

        // Assert: no file means no preview
        Assert.Null(vm.PreviewImage);
        vm.Cleanup();
    }

    [Fact]
    public void WhenPngExists_DoesNotThrow()
    {
        // Arrange
        TestHelpers.Cleanup();
        TestHelpers.CreateSmallPng(TestHelpers.PngPath);
        var vm = new WallpaperConfigViewModel(_store);

        // Act & Assert: method should not throw
        vm.RefreshPreviewImage();
        vm.Cleanup();
    }

    [Fact]
    public void WhenJpgExists_DoesNotThrow()
    {
        // Arrange
        TestHelpers.Cleanup();
        TestHelpers.CreateSmallJpeg(TestHelpers.JpgPath);
        var vm = new WallpaperConfigViewModel(_store);

        // Act & Assert: method should not throw
        vm.RefreshPreviewImage();
        vm.Cleanup();
    }

    [Fact]
    public async Task SwitchThenRefresh_OnlyCorrectFormatFileExists()
    {
        // Arrange: wallpaper + stale counterpart file
        var wallpaperPath = Path.Combine(_wallpaperDir, "test-wallpaper.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        File.WriteAllBytes(TestHelpers.JpgPath, [0xFF, 0xD8, 0xFF]);
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);
        var vm = new WallpaperConfigViewModel(_store);

        // Act: switch wallpaper, then refresh preview (simulates window reopen)
        var result = await switcher.SwitchToNextAsync();
        vm.RefreshPreviewImage();

        // Assert: stale file cleaned up, correct file exists
        Assert.NotNull(result);
        Assert.True(File.Exists(TestHelpers.PngPath), "current.png should exist after switch");
        Assert.False(File.Exists(TestHelpers.JpgPath), "stale current.jpg should be deleted");
        vm.Cleanup();
    }
}

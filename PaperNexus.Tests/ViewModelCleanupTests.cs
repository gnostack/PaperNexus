using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// Tests for WallpaperConfigViewModel lifecycle: construction, Cleanup, and related edge cases.
[Collection("Wallpaper")]
public class ViewModelCleanupTests : IAsyncLifetime, IDisposable
{
    private readonly string _wallpaperDir;
    private readonly SettingsStore _store;

    public ViewModelCleanupTests()
    {
        _wallpaperDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_VM_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_wallpaperDir);
        _store = TestHelpers.CreateSettingsStore();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        TestHelpers.Cleanup();
        // Allow any fire-and-forget saves triggered during the test to complete before
        // the collection teardown runs so they do not hold a file lock on the test's settings file.
        await Task.Delay(1000);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_wallpaperDir)) Directory.Delete(_wallpaperDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    // Regression guard: calling Cleanup() while a debounced save is still pending must not throw
    // ObjectDisposedException. Before the fix, _statusCts was disposed before SaveSettingsAsync
    // completed, so ShowTransientStatusAsync would crash when it tried to Cancel() the disposed CTS.
    [Fact]
    public async Task Cleanup_WithPendingSave_DoesNotThrow()
    {
        var vm = new WallpaperConfigViewModel(_store);

        // Trigger a property change to arm the debounce timer (sets _hasPendingSave = true
        // while _isLoading is false, as would happen during normal use).
        vm.Folder = _wallpaperDir;

        // Act: Cleanup() is called synchronously before the debounce delay elapses.
        // The pending save task is fired as fire-and-forget; it must not produce an
        // unobserved ObjectDisposedException from calling ShowTransientStatusAsync
        // on a CTS that was already disposed by Cleanup().
        var ex = Record.Exception(() => vm.Cleanup());

        Assert.Null(ex);

        // Wait for the in-flight SaveSettingsAsync to finish so it releases settings.json
        // before the next test in the [Collection("Wallpaper")] group runs.
        await Task.Delay(1000);
    }

    // Verifies that ShowTransientStatusAsync called after Cleanup does not throw
    // an ObjectDisposedException — the fresh CTS created in Cleanup allows the method
    // to complete without crashing the in-flight SaveSettingsAsync task.
    [Fact]
    public async Task ShowTransientStatus_AfterCleanup_DoesNotThrow()
    {
        var vm = new WallpaperConfigViewModel(_store);

        // Arm a pending save and flush it via Cleanup; this leaves a fresh _statusCts in place
        vm.Folder = _wallpaperDir;
        vm.Cleanup();

        // If the in-flight save calls ShowTransientStatusAsync after Cleanup, it must not throw.
        // Simulate that path directly: the fresh CTS should be usable.
        await vm.ShowTransientStatusAsync("test", durationMs: 1);

        // Wait for the in-flight SaveSettingsAsync to finish so it releases settings.json
        // before the next test in the [Collection("Wallpaper")] group runs.
        await Task.Delay(1000);
    }
}

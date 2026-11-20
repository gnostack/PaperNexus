using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// ISettingsStore.Saved (issue #203, step 5): raised once after each update that wrote the file,
// with the settings as written, after the store's lock is released; never for a change that
// failed.
public class SettingsSavedEventTests : IDisposable
{
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();

    public void Dispose() => TestHelpers.DeleteSettingsStore(_store);

    [Fact]
    public async Task AnUpdate_RaisesSavedOnce_WithTheSettingsAsWritten()
    {
        var raised = new List<WallpaperNexusSettings>();
        _store.Saved += raised.Add;

        var written = await _store.UpdateAsync(s => s.Slideshow.Order = SlideshowOrder.Random);

        var only = Assert.Single(raised);
        Assert.Same(written, only);
        Assert.Equal(SlideshowOrder.Random, only.Slideshow.Order);
        var onDisk = await _store.LoadAsync();
        Assert.Equal(SlideshowOrder.Random, onDisk.Slideshow.Order);
    }

    [Fact]
    public async Task AChangeThatThrows_RaisesNothing()
    {
        var raised = 0;
        _store.Saved += _ => raised++;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(_ => throw new InvalidOperationException("No.")));

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task AHandler_MayUseTheStore()
    {
        // The event comes after the lock is released, so a handler that reads and writes the
        // store does not wait on the update that raised it
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        _store.Saved += async _ =>
        {
            if (!first)
                return;
            first = false;
            await _store.LoadAsync();
            await _store.UpdateAsync(s => s.DebugMode = true);
            handled.TrySetResult();
        };

        await _store.UpdateAsync(s => s.Slideshow.Enabled = false);
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var onDisk = await _store.LoadAsync();
        Assert.False(onDisk.Slideshow.Enabled);
        Assert.True(onDisk.DebugMode);
    }
}

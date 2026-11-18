using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// "Download now" says what really happened: the download run reports how many enabled sources
// it tried and how many failed, and DownloadSentence turns that into the sentence both devices
// show.
public class DownloadOutcomeTests : IDisposable
{
    private readonly string _downloadDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Outcome_{Guid.NewGuid():N}");
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();

    public DownloadOutcomeTests()
    {
        Directory.CreateDirectory(_downloadDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_downloadDir)) Directory.Delete(_downloadDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    private static WallpaperSource Source(string name, bool works, bool isEnabled = true) => new()
    {
        Name = name,
        Url = works ? "https://ok.test/feed" : "https://broken.test/feed",
        IsEnabled = isEnabled,
    };

    private async Task<DownloadOutcome> DownloadAllAsync(params WallpaperSource[] sources)
    {
        await _store.WriteAsync(new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Sources = [.. sources],
        });
        var feedReader = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance, new FeedsHandler());
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, feedReader, _store, TestHelpers.UnusedTimerStore());
        var outcome = await sut.DownloadAllAsync();
        return outcome;
    }

    [Fact]
    public async Task EveryEnabledSourceWorked_DownloadComplete()
    {
        var outcome = await DownloadAllAsync(Source("A", works: true), Source("B", works: true), Source("Off", works: false, isEnabled: false));

        Assert.Equal(new DownloadOutcome(2, 0), outcome);
        Assert.Equal(("Download complete.", StatusTone.Success), DownloadSentence.For(outcome));
    }

    [Fact]
    public async Task SomeSourcesFailed_SaysHowMany()
    {
        var outcome = await DownloadAllAsync(Source("A", works: true), Source("B", works: false), Source("C", works: true));

        Assert.Equal(new DownloadOutcome(3, 1), outcome);
        Assert.Equal(("Download failed for 1 of 3 sources.", StatusTone.Error), DownloadSentence.For(outcome));
    }

    [Fact]
    public async Task EverySourceFailed_AsksToCheckTheConnection()
    {
        var outcome = await DownloadAllAsync(Source("A", works: false), Source("B", works: false));

        Assert.Equal(new DownloadOutcome(2, 2), outcome);
        Assert.Equal(("Download failed: check your connection.", StatusTone.Error), DownloadSentence.For(outcome));
    }

    [Fact]
    public async Task NoEnabledSource_SaysSo()
    {
        var outcome = await DownloadAllAsync(Source("Off", works: true, isEnabled: false));

        Assert.Equal(new DownloadOutcome(0, 0), outcome);
        Assert.Equal(("No enabled sources to download from.", StatusTone.Neutral), DownloadSentence.For(outcome));
    }

    // The working feed lists no pictures; the broken one answers 404.
    private sealed class FeedsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var works = request.RequestUri?.Host == "ok.test";
            var response = works
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("gone") };
            return Task.FromResult(response);
        }
    }
}

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The scheduled download, split from the download work (issue #203, step 5): DownloadDueAsync
// downloads only the sources that are due, and DownloadSchedule is the desktop's timer for it,
// which only AddInProcessSchedules (or, on the phone until Android schedules its downloads,
// AddInProcessDownloadSchedule) registers.
public class DownloadScheduleTests : IDisposable
{
    private readonly string _downloadDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_DueRun_{Guid.NewGuid():N}");
    private readonly string _timerDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Timers_{Guid.NewGuid():N}");
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();

    public DownloadScheduleTests()
    {
        Directory.CreateDirectory(_downloadDir);
        Directory.CreateDirectory(_timerDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_downloadDir)) Directory.Delete(_downloadDir, true); }
        catch { }
        try { if (Directory.Exists(_timerDir)) Directory.Delete(_timerDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    // --- DownloadDueAsync ---

    [Fact]
    public async Task DownloadDue_DownloadsOnlyTheSourcesThatAreDue()
    {
        var recent = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _store.WriteAsync(new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Sources =
            [
                new WallpaperSource { Name = "Never", Url = "https://never.test/feed" },
                new WallpaperSource { Name = "Recent", Url = "https://recent.test/feed", CronExpression = "0 */8 * * *", LastDownloadUtc = recent },
                new WallpaperSource { Name = "Off", Url = "https://off.test/feed", IsEnabled = false },
            ],
        });
        var handler = new EmptyFeeds();
        var feedReader = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance, handler);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, feedReader, _store);

        var outcome = await sut.DownloadDueAsync();

        Assert.Equal(new DownloadOutcome(1, 0), outcome);
        Assert.Equal(["never.test"], handler.Hosts);
        var saved = await _store.LoadAsync();
        Assert.NotNull(saved.Sources.Single(s => s.Name == "Never").LastDownloadUtc);
        Assert.Equal(recent, saved.Sources.Single(s => s.Name == "Recent").LastDownloadUtc);
    }

    [Fact]
    public async Task DownloadAll_StillDownloadsEveryEnabledSource()
    {
        await _store.WriteAsync(new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Sources =
            [
                new WallpaperSource { Name = "Never", Url = "https://never.test/feed" },
                new WallpaperSource { Name = "Recent", Url = "https://recent.test/feed", LastDownloadUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
            ],
        });
        var handler = new EmptyFeeds();
        var feedReader = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance, handler);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, feedReader, _store);

        var outcome = await sut.DownloadAllAsync();

        Assert.Equal(new DownloadOutcome(2, 0), outcome);
        Assert.Equal(["never.test", "recent.test"], handler.Hosts.Order());
    }

    // --- DownloadSchedule: when the desktop's timer wakes ---

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 7, 0, TimeSpan.Zero);

    [Fact]
    public void NextRun_IsTheSoonestOccurrenceAcrossTheEnabledSources()
    {
        WallpaperSource[] sources =
        [
            new() { Name = "Twice an hour", CronExpression = "*/30 * * * *" },
            new() { Name = "Every minute but off", CronExpression = "* * * * *", IsEnabled = false },
            new() { Name = "Every 8 hours", CronExpression = "0 */8 * * *" },
        ];

        var next = DownloadSchedule.NextRun(sources, Now, TimeZoneInfo.Utc, _ => { });

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 12, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextRun_NothingSooner_IsAnHourFromNow()
    {
        WallpaperSource[] sources = [new() { Name = "Daily", CronExpression = "0 0 * * *" }];

        var next = DownloadSchedule.NextRun(sources, Now, TimeZoneInfo.Utc, _ => { });

        Assert.Equal(Now.AddHours(1), next);
    }

    [Fact]
    public void NextRun_AnInvalidSchedule_IsReportedAndLeftOut()
    {
        WallpaperSource[] sources = [new() { Name = "Broken", CronExpression = "not a cron" }];
        var reported = new List<string>();

        var next = DownloadSchedule.NextRun(sources, Now, TimeZoneInfo.Utc, s => reported.Add(s.Name));

        Assert.Equal(Now.AddHours(1), next);
        Assert.Equal(["Broken"], reported);
    }

    [Fact]
    public void DownloadSchedule_KeepsTheDownloadersJobName()
    {
        // Its entry in timers.json is the one the downloader's own loop wrote before the split
        var sut = new DownloadSchedule(NullLogger<DownloadSchedule>.Instance, new CountingDownloader(), _store, new JobTimerStore(_timerDir));

        Assert.Equal("PaperNexus.DownloadWallpapers", sut.JobName);
    }

    [Fact]
    public async Task DownloadSchedule_RunsTheScheduledDownloadAtStart()
    {
        var downloader = new CountingDownloader();
        var sut = new DownloadSchedule(NullLogger<DownloadSchedule>.Instance, downloader, _store, new JobTimerStore(_timerDir));

        await sut.StartAsync(CancellationToken.None);
        var ran = await downloader.FirstDueRun.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sut.StopAsync(CancellationToken.None);
        sut.Dispose();

        Assert.True(ran);
        Assert.Equal(0, downloader.AllRuns);
    }

    // --- Which host runs the timer ---

    private static List<ServiceDescriptor> HostedServices(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();

    [Fact]
    public void AddServicesFrom_RegistersTheDownloaderWithNoTimer()
    {
        var services = new ServiceCollection();

        services.AddServicesFrom(typeof(DownloadWallpapers).Assembly);

        var downloader = services.Single(d => d.ServiceType == typeof(IDownloadWallpapers));
        Assert.Equal(typeof(DownloadWallpapers), downloader.ImplementationType);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(DownloadWallpapers));
        Assert.DoesNotContain(HostedServices(services), d => d.ImplementationType == typeof(DownloadSchedule));
        // The hosted services the shared assembly adds by itself: the debug log level, and the
        // background work, which arms nothing on a device with no background scheduler
        Assert.Equal(2, HostedServices(services).Count);
    }

    [Fact]
    public void AddInProcessSchedules_RunsTheDownloadScheduleAndTheSlideshow()
    {
        var services = new ServiceCollection();

        services.AddInProcessSchedules(typeof(DownloadWallpapers).Assembly);

        var hosted = HostedServices(services).Select(d => d.ImplementationType).ToList();
        Assert.Contains(typeof(DownloadSchedule), hosted);
        Assert.Contains(typeof(ScheduledJobHostedService<SwitchWallpaperJob>), hosted);
        Assert.Equal(2, hosted.Count);
    }

    [Fact]
    public void AddInProcessDownloadSchedule_RunsOnlyTheDownloadSchedule_Once()
    {
        var services = new ServiceCollection();

        services.AddInProcessDownloadSchedule();
        services.AddInProcessDownloadSchedule();

        var hosted = HostedServices(services).Select(d => d.ImplementationType).ToList();
        Assert.Equal([typeof(DownloadSchedule)], hosted);
    }

    // Answers every feed with an empty list and records which hosts were asked.
    private sealed class EmptyFeeds : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Hosts)
                Hosts.Add(request.RequestUri!.Host);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            return Task.FromResult(response);
        }
    }

    // Counts each kind of run.
    private sealed class CountingDownloader : IDownloadWallpapers
    {
        public TaskCompletionSource<bool> FirstDueRun { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AllRuns { get; private set; }

        public event Action? WallpapersDownloaded
        {
            add { }
            remove { }
        }

        public Task<DownloadOutcome> DownloadAllAsync()
        {
            AllRuns++;
            return Task.FromResult(new DownloadOutcome(0, 0));
        }

        public Task<DownloadOutcome> DownloadDueAsync()
        {
            FirstDueRun.TrySetResult(true);
            return Task.FromResult(new DownloadOutcome(0, 0));
        }
    }
}

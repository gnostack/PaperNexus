using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// Each test roots its own store in a new temporary folder, the way the host roots the real
// one in its data folder. A second store over the same folder stands in for the next start
// of the app, which has an empty cache and reads timers.json from disk.
public class JobTimerStoreTests : IDisposable
{
    private readonly string _directory;

    public JobTimerStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"PaperNexus_Timers_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    private string TimersPath => Path.Combine(_directory, JobTimerStore.FileName);

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task LoadContextAsync_CorruptJson_ReturnsDefault()
    {
        await File.WriteAllTextAsync(TimersPath, "not valid json {{{");
        var store = new JobTimerStore(_directory);
        var result = await store.LoadContextAsync("SomeJob");
        Assert.Equal(default(JobExecutionContext), result);
    }

    [Fact]
    public async Task LoadContextAsync_WrongJsonShape_ReturnsDefault()
    {
        await File.WriteAllTextAsync(TimersPath, "[1, 2, 3]");
        var store = new JobTimerStore(_directory);
        var result = await store.LoadContextAsync("SomeJob");
        Assert.Equal(default(JobExecutionContext), result);
    }

    [Fact]
    public async Task LoadContextAsync_FileMissing_ReturnsDefault()
    {
        var store = new JobTimerStore(_directory);
        var result = await store.LoadContextAsync("SomeJob");
        Assert.Equal(default(JobExecutionContext), result);
    }

    [Fact]
    public async Task LoadContextAsync_ValidFile_ReturnsContext()
    {
        var expected = new JobExecutionContext
        {
            StartedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
            FinishedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 5, TimeSpan.Zero),
            Duration = TimeSpan.FromSeconds(5),
            LastExecutionSucceeded = true,
        };
        await new JobTimerStore(_directory).SaveContextAsync("TestJob", expected);

        var restarted = new JobTimerStore(_directory);
        var result = await restarted.LoadContextAsync("TestJob");
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SaveContextAsync_AfterCorruptLoad_StillWorks()
    {
        await File.WriteAllTextAsync(TimersPath, "corrupt");
        var store = new JobTimerStore(_directory);
        await store.LoadContextAsync("RecoverJob");

        var context = new JobExecutionContext
        {
            LastExecutionSucceeded = true,
            Duration = TimeSpan.FromSeconds(1),
        };
        await store.SaveContextAsync("RecoverJob", context);

        var restarted = new JobTimerStore(_directory);
        var result = await restarted.LoadContextAsync("RecoverJob");
        Assert.Equal(context, result);
    }

    // The store writes timers.json in the host's data folder and nowhere else.
    [Fact]
    public async Task SaveContextAsync_WritesTimersJsonInTheDataFolder()
    {
        var folders = new TestAppFolders(_directory);
        var store = new JobTimerStore(folders);

        await store.SaveContextAsync("AnyJob", new JobExecutionContext { LastExecutionSucceeded = true });

        Assert.Equal(TimersPath, store.FilePath);
        Assert.True(File.Exists(TimersPath));
    }
}

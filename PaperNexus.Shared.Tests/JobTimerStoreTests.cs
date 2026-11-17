using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

[Collection("JobTimerStore")]
public class JobTimerStoreTests : IDisposable
{
    private static readonly string TimersPath = Path.Combine(AppContext.BaseDirectory, "timers.json");

    public JobTimerStoreTests()
    {
        JobTimerStore.ResetForTesting();
        TryDelete(TimersPath);
    }

    public void Dispose()
    {
        JobTimerStore.ResetForTesting();
        TryDelete(TimersPath);
    }

    [Fact]
    public async Task LoadContextAsync_CorruptJson_ReturnsDefault()
    {
        await File.WriteAllTextAsync(TimersPath, "not valid json {{{");
        var result = await JobTimerStore.LoadContextAsync("SomeJob");
        Assert.Equal(default(JobExecutionContext), result);
    }

    [Fact]
    public async Task LoadContextAsync_WrongJsonShape_ReturnsDefault()
    {
        await File.WriteAllTextAsync(TimersPath, "[1, 2, 3]");
        var result = await JobTimerStore.LoadContextAsync("SomeJob");
        Assert.Equal(default(JobExecutionContext), result);
    }

    [Fact]
    public async Task LoadContextAsync_FileMissing_ReturnsDefault()
    {
        var result = await JobTimerStore.LoadContextAsync("SomeJob");
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
        await JobTimerStore.SaveContextAsync("TestJob", expected);
        JobTimerStore.ResetForTesting();

        var result = await JobTimerStore.LoadContextAsync("TestJob");
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SaveContextAsync_AfterCorruptLoad_StillWorks()
    {
        await File.WriteAllTextAsync(TimersPath, "corrupt");
        await JobTimerStore.LoadContextAsync("RecoverJob");

        var context = new JobExecutionContext
        {
            LastExecutionSucceeded = true,
            Duration = TimeSpan.FromSeconds(1),
        };
        await JobTimerStore.SaveContextAsync("RecoverJob", context);
        JobTimerStore.ResetForTesting();

        var result = await JobTimerStore.LoadContextAsync("RecoverJob");
        Assert.Equal(context, result);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}

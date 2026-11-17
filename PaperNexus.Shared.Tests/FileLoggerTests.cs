using Microsoft.Extensions.Logging;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

public class FileLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public FileLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PaperNexus_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    // When the minimum level is Warning, Debug and Information should be filtered out.
    [Fact]
    public void IsEnabled_ReturnsFalse_WhenBelowMinimum()
    {
        using var provider = new FileLoggerProvider(_tempDir, LogLevel.Warning);
        var logger = provider.CreateLogger("Test");

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Information));
    }

    // When the minimum level is Warning, Warning and Error should pass the filter.
    [Fact]
    public void IsEnabled_ReturnsTrue_WhenAtOrAboveMinimum()
    {
        using var provider = new FileLoggerProvider(_tempDir, LogLevel.Warning);
        var logger = provider.CreateLogger("Test");

        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    // LogLevel.None is a sentinel value that should never be enabled.
    [Fact]
    public void IsEnabled_ReturnsFalse_ForLogLevelNone()
    {
        using var provider = new FileLoggerProvider(_tempDir, LogLevel.Trace);
        var logger = provider.CreateLogger("Test");

        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    // The folder-only constructor defaults to Information, so Debug should be
    // filtered out while Information should pass.
    [Fact]
    public void IsEnabled_DefaultMinLevel_IsInformation()
    {
        using var provider = new FileLoggerProvider(_tempDir);
        var logger = provider.CreateLogger("Test");

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
    }

    // The log file is written in the folder the host names, not beside the executable.
    [Fact]
    public async Task Log_WritesTheLogFileInTheGivenFolder()
    {
        var logDirectory = FileLoggerProvider.LogDirectoryIn(_tempDir);
        var provider = new FileLoggerProvider(logDirectory);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("hello from the test");
        await provider.DisposeAsync();

        var file = Assert.Single(Directory.GetFiles(logDirectory, "*.log"));
        var text = await File.ReadAllTextAsync(file);
        Assert.Contains("hello from the test", text);
    }
}

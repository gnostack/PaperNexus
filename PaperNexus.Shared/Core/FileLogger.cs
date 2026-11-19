using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PaperNexus.Core;

// Adds the app's log file to a host's logging.
public static class FileLoggingExtensions
{
    // The provider decides which entries it writes (its LogLevelSwitch), so the logging
    // factory's own minimum, Information unless configured, must not drop debug entries before
    // they reach it: without this rule debug mode and --debug wrote no debug entry from any
    // service in the container.
    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder, FileLoggerProvider provider)
    {
        builder.ThrowIfNull();
        provider.ThrowIfNull();
        builder.AddProvider(provider);
        builder.AddFilter<FileLoggerProvider>(null, LogLevel.Trace);
        return builder;
    }
}

public class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly SemaphoreSlim _messageCount = new(0);
    private readonly Task _writeTask;
    // Read for every entry, so a change of level applies to the next entry written.
    private readonly Func<LogLevel> _minLevel;
    private readonly string _logDirectory;
    // volatile ensures the writer thread always reads the latest value without the JIT caching
    // it in a register across loop iterations that do not cross a full memory-barrier boundary.
    private volatile bool _running = true;

    // Still running if _running is true, or if there are queued messages that have not yet been flushed.
    private bool Running => _running || _messages.Count > 0 || _messageCount.CurrentCount > 0;

    // Logs go to logDirectory, which the host names: the logs folder inside its data folder
    // (IAppFolders.DataFolder). The provider is created before the service container exists,
    // so it takes the folder itself rather than the interface.
    public FileLoggerProvider(string logDirectory) : this(logDirectory, LogLevel.Information) { }

    // A fixed least important level.
    public FileLoggerProvider(string logDirectory, LogLevel minLevel)
        : this(logDirectory, () => minLevel)
    {
    }

    // The level the switch holds at the moment each entry is written: the host's logger
    // follows the debug mode setting without being created again.
    public FileLoggerProvider(string logDirectory, LogLevelSwitch logLevels)
        : this(logDirectory, () => logLevels.MinimumLevel)
    {
        logLevels.ThrowIfNull();
    }

    private FileLoggerProvider(string logDirectory, Func<LogLevel> minLevel)
    {
        _logDirectory = logDirectory.ThrowIfNull();
        _minLevel = minLevel;
        _writeTask = StartWriter();
    }

    // The logs folder inside a data folder: where every host writes its log.
    public static string LogDirectoryIn(string dataFolder) => Path.Combine(dataFolder, "logs");

    public void Dispose() => DisposeAsync().AsTask().Wait();

    // Signal the writer loop to stop, then wait up to 5 seconds for it to flush remaining messages.
    // Release the semaphore once after clearing _running so the writer wakes immediately from its
    // WaitAsync call instead of blocking for up to 5 s before noticing the shutdown signal.
    public async ValueTask DisposeAsync()
    {
        _running = false;
        // Wake the writer loop so it sees _running == false without waiting for the next 5 s timeout
        _messageCount.Release();
        await Task.WhenAny(_writeTask, Task.Delay(5000));
    }

    private const long MaxLogBytes = 5 * 1024 * 1024; // 5 MB per log file

    // Background writer loop: waits for queued messages (with a 5 s timeout to re-check Running),
    // then drains the queue into the log file in a single append. Uses FileShare.ReadWrite so the
    // file can be tailed by external tools while the app is running.
    // Rotates the log file to .1.log when it exceeds MaxLogBytes, keeping one previous generation.
    private async Task StartWriter()
    {
        var assName = Assembly.GetEntryAssembly()?.GetName().Name ?? "app";
        var logFileName = Path.Combine(_logDirectory, $"{assName}.log");
        var logFile = new FileInfo(logFileName);
        if (!logFile.Directory.Exists)
            logFile.Directory.Create();
        // Rotate on startup if the log from the previous session is already over the limit
        RotateIfNeeded(logFile);
        while (Running)
        {
            try
            {
                // WaitAsync with timeout so the loop eventually terminates after _running is set to false
                await _messageCount.WaitAsync(5000);
                if (_messages.Count > 0)
                {
                    logFile.Refresh();
                    RotateIfNeeded(logFile);
                    using var stream = logFile.Open(FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    while (_messages.TryDequeue(out var message))
                        await writer.WriteLineAsync(message);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    // Renames the active log to .1.log (overwriting any previous rotation) when it exceeds MaxLogBytes.
    private static void RotateIfNeeded(FileInfo logFile)
    {
        try
        {
            if (logFile.Exists && logFile.Length >= MaxLogBytes)
            {
                var rotated = Path.ChangeExtension(logFile.FullName, ".1.log");
                logFile.MoveTo(rotated, overwrite: true);
            }
        }
        catch { }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this, _minLevel);

    private void Log(string message)
    {
        _messages.Enqueue(message);
        _messageCount.Release();
    }

    private class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _fileLoggerProvider;
        private readonly Func<LogLevel> _minLevel;

        public FileLogger(string categoryName, FileLoggerProvider fileLoggerProvider, Func<LogLevel> minLevel)
        {
            _categoryName = categoryName;
            _fileLoggerProvider = fileLoggerProvider;
            _minLevel = minLevel;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            private NullScope() { }
            public void Dispose() { }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel() && logLevel != LogLevel.None;

        // Formats the log entry as "timestamp | level | category | message" and appends
        // the full exception (including stack trace) on subsequent lines when present.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter?.Invoke(state, exception);
            message = string.Join(" | ", DateTimeOffset.Now.ToString("O"), logLevel, _categoryName, message);
            if (exception is not null)
                message = string.Join(Environment.NewLine, message, exception);
            _fileLoggerProvider.Log(message);
        }
    }
}

namespace PaperNexus.Core;

// The least important log entry the app writes, changed while the app runs. Debug mode
// (the setting) writes debug entries from the moment it is turned on; starting the desktop
// with --debug writes them whatever the setting says. The host creates one before its logger
// and its service container, gives it to both, and the debug mode setting moves it.
public sealed class LogLevelSwitch
{
    private readonly bool _startedInDebugMode;
    private volatile bool _debugMode;

    public LogLevelSwitch(bool startedInDebugMode)
    {
        _startedInDebugMode = startedInDebugMode;
    }

    public LogLevel MinimumLevel
    {
        get
        {
            var verbose = _startedInDebugMode || _debugMode;
            return verbose ? LogLevel.Debug : LogLevel.Information;
        }
    }

    // Follows the debug mode setting.
    public void SetDebugMode(bool isOn)
    {
        _debugMode = isOn;
    }
}

// Applies the stored debug mode setting to the log level when the app starts, so a person who
// turned debug mode on keeps the detailed log after a restart.
public sealed class DebugLogLevel : IHostedService, IAddHostedSingleton<DebugLogLevel>
{
    private readonly ISettingsStore _settingsStore;
    private readonly LogLevelSwitch _logLevels;

    public DebugLogLevel(ISettingsStore settingsStore, LogLevelSwitch logLevels)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _logLevels = logLevels.ThrowIfNull();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        _logLevels.SetDebugMode(settings.DebugMode);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

namespace PaperNexus;

// The desktop's Factory Reset: deletes exactly the app's own data (AppDataReset) - leaving the
// downloaded wallpapers, the executable and everything else beside it (the install sentinel, a
// build's libraries) alone - then starts the app again with the arguments it was started with,
// less --startup, and ends this copy.
internal sealed class DesktopFactoryReset : IFactoryReset, IAddSingleton<IFactoryReset>
{
    private readonly IAppFolders _appFolders;

    public DesktopFactoryReset(IAppFolders appFolders)
    {
        _appFolders = appFolders.ThrowIfNull();
    }

    // Nothing is logged: the logs folder is part of what is deleted.
    public Task ResetAsync()
    {
        AppDataReset.DeleteAppData(_appFolders);

        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, PlatformPaths.ExecutableName);
        var arguments = RelaunchArguments(Program.Arguments);
        ShellOpener.LaunchExecutable(exePath, arguments);
        Environment.Exit(0);
        return Task.CompletedTask;
    }

    // The arguments the new copy starts with: this copy's, except --startup. The person pressed
    // the button in the window, so the new copy shows its window, as a copy started without
    // --startup does; a copy launched at sign-in would otherwise come back as a tray icon only.
    // --debug and anything else are kept.
    internal static IReadOnlyList<string> RelaunchArguments(IEnumerable<string> arguments)
    {
        var kept = arguments
            .Where(argument => !string.Equals(argument, StartupArgument, StringComparison.Ordinal))
            .ToList();
        return kept;
    }

    // The argument the sign-in entry starts the app with.
    private const string StartupArgument = "--startup";
}

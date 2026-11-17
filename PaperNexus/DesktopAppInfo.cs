namespace PaperNexus;

// The desktop's facts about the running app for the shared view model.
internal sealed class DesktopAppInfo : IAppInfo, IAddSingleton<IAppInfo>
{
    // The executable's own version, e.g. "v12".
    public string Version => App.AppVersion;

    // Read on every access: Program sets it from --debug before the container exists.
    public bool StartedInDebugMode => Program.IsDebugMode;
}

namespace PaperNexus.Core;

// Facts about the running app that only the host knows.
public interface IAppInfo
{
    // The version label shown to the person, e.g. "v12".
    public string Version { get; }

    // True when the app was started in debug mode (--debug on the desktop).
    public bool StartedInDebugMode { get; }
}

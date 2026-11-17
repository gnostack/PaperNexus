namespace PaperNexus;

// Checks for and installs a newer release. Only a host that updates itself registers an
// implementation; the settings view model reports the service as unavailable otherwise.
public interface ICheckForUpdates
{
    public Task CheckAsync(bool forceUpdate = false, IProgress<string>? progress = null);
}

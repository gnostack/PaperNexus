namespace PaperNexus.Core;

// Launches the app when the person signs in. Only a host that has such a setting registers
// an implementation; the settings view model skips the call when none is registered.
public interface IStartupRegistration
{
    // False when this copy of the app may not change the sign-in entry (on the desktop, a copy
    // that is not the installed one); Update then does nothing, and the setting is shown but
    // cannot be changed or saved from this copy.
    public bool CanUpdate { get; }

    // The folder of the executable this copy runs from, named to the person when CanUpdate is
    // false.
    public string ExecutableFolder { get; }

    public void Update(bool enable);
}

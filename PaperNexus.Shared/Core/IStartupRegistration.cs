namespace PaperNexus.Core;

// Launches the app when the person signs in. Only a host that has such a setting registers
// an implementation; the settings view model skips the call when none is registered.
public interface IStartupRegistration
{
    public void Update(bool enable);
}

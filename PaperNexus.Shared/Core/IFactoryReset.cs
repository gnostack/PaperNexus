namespace PaperNexus.Core;

// Deletes the app's own data and starts the app again, as it was on first open. Each device
// keeps its data in its own places and restarts its own way, so the host supplies it. The
// downloaded pictures are never deleted. When it returns at all, the reset failed partway.
public interface IFactoryReset
{
    public Task ResetAsync();
}

using Avalonia.Platform;

namespace PaperNexus.Core.Platform;

// Writes the two per-user entries that name an executable: the Linux application launcher
// (DesktopEntry) and the sign-in entry (StartupRegistration: the Run key on Windows, the XDG
// autostart entry on Linux). Only DesktopIntegration calls it; tests replace it with a
// recording double so no unit test touches the real entries.
public interface IDesktopEntryWriter
{
    public void InstallLauncher(string exePath);

    public void UpdateSignIn(bool enable, string exePath);
}

internal sealed class DesktopEntryWriter : IDesktopEntryWriter, IAddSingleton<IDesktopEntryWriter>
{
    public void InstallLauncher(string exePath)
    {
        DesktopEntry.Install(exePath, () => AssetLoader.Open(new Uri("avares://PaperNexus/Assets/logo.png")));
    }

    public void UpdateSignIn(bool enable, string exePath)
    {
        StartupRegistration.Update(enable, exePath);
    }
}

// The one owner of the running copy's launcher and sign-in entries (issue #211). Only the
// installed copy (InstallLocation, answered once in Program.Main) writes, creates or removes
// either entry, always naming itself; a copy that is not installed - a --debug run from a
// build folder, a run from a temporary folder - leaves both exactly as they are. App calls
// InstallLauncher and Update at launch, and the settings window's Run on startup box calls
// Update through IStartupRegistration.
internal sealed class DesktopIntegration : IStartupRegistration
{
    private readonly string _exePath;
    private readonly bool _isInstalledCopy;
    private readonly IDesktopEntryWriter _writer;
    private readonly ILogger? _logger;

    public DesktopIntegration(string exePath, bool isInstalledCopy, IDesktopEntryWriter writer, ILogger? logger = null)
    {
        _exePath = exePath.ThrowIfNull();
        _isInstalledCopy = isInstalledCopy;
        _writer = writer.ThrowIfNull();
        _logger = logger;
    }

    public bool CanUpdate => _isInstalledCopy;

    public string ExecutableFolder
    {
        get
        {
            var fullPath = Path.GetFullPath(_exePath);
            return Path.GetDirectoryName(fullPath) ?? fullPath;
        }
    }

    // Writes the launcher and icon on every launch of the installed copy: it repairs installs
    // made before the launcher existed and corrects the Exec line if the install folder moved.
    // Called before the sign-in entry, which points at the icon this writes.
    public void InstallLauncher()
    {
        if (!_isInstalledCopy)
        {
            _logger?.LogInformation(
                "{ExePath} is not the installed copy; the launcher and sign-in entries are left as they are.",
                _exePath);
            return;
        }

        _writer.InstallLauncher(_exePath);
    }

    // Creates or removes the sign-in entry for this copy. Does nothing in a copy that is not
    // installed.
    public void Update(bool enable)
    {
        if (!_isInstalledCopy)
            return;

        _writer.UpdateSignIn(enable, _exePath);
    }
}

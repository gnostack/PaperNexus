namespace PaperNexus.Core;

// The folder that holds settings.json. Each device keeps its data in its own place, so the
// host supplies it: on the desktop it is the default install directory, whatever folder the
// executable itself was installed to.
public interface ISettingsDirectory
{
    public string FullPath { get; }
}

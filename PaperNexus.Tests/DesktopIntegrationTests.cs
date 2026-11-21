using PaperNexus.Core.Platform;
using Xunit;

namespace PaperNexus.Tests;

// Only the installed copy writes, creates or removes the launcher and the sign-in entry
// (issue #211). The writer is a recording double, so these cover the rule on both Linux (the
// launcher and the autostart entry) and Windows (the Run key) without touching either.
public class DesktopIntegrationTests
{
    private const string InstalledExe = "/home/someone/.local/share/PaperNexus/PaperNexus";
    private const string BuildExe = "/home/someone/repo/PaperNexus/bin/Release/net10.0/PaperNexus";

    [Fact]
    public void TheInstalledCopyWritesBothEntriesNamingItselfAtLaunch()
    {
        var writer = new RecordingWriter();
        var integration = new DesktopIntegration(InstalledExe, isInstalledCopy: true, writer);

        integration.InstallLauncher();
        integration.Update(true);

        Assert.Equal([InstalledExe], writer.Launchers);
        Assert.Equal([(true, InstalledExe)], writer.SignIns);
    }

    [Fact]
    public void TheInstalledCopyRemovesAndRecreatesTheSignInEntry()
    {
        var writer = new RecordingWriter();
        var integration = new DesktopIntegration(InstalledExe, isInstalledCopy: true, writer);

        integration.Update(false);
        integration.Update(true);

        Assert.True(integration.CanUpdate);
        Assert.Equal([(false, InstalledExe), (true, InstalledExe)], writer.SignIns);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACopyThatIsNotInstalledNeverWritesOrRemovesEitherEntry(bool runOnStartup)
    {
        var writer = new RecordingWriter();
        var integration = new DesktopIntegration(BuildExe, isInstalledCopy: false, writer);

        integration.InstallLauncher();
        integration.Update(runOnStartup);

        Assert.False(integration.CanUpdate);
        Assert.Empty(writer.Launchers);
        Assert.Empty(writer.SignIns);
    }

    [Fact]
    public void ExecutableFolderIsTheFolderTheCopyRunsFrom()
    {
        var integration = new DesktopIntegration(BuildExe, isInstalledCopy: false, new RecordingWriter());

        var expected = Path.GetDirectoryName(Path.GetFullPath(BuildExe));
        Assert.Equal(expected, integration.ExecutableFolder);
    }

    private sealed class RecordingWriter : IDesktopEntryWriter
    {
        public List<string> Launchers { get; } = [];

        public List<(bool Enable, string ExePath)> SignIns { get; } = [];

        public void InstallLauncher(string exePath)
        {
            Launchers.Add(exePath);
        }

        public void UpdateSignIn(bool enable, string exePath)
        {
            SignIns.Add((enable, exePath));
        }
    }
}

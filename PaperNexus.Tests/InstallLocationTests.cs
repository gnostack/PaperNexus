using PaperNexus.Core.Platform;
using Xunit;

namespace PaperNexus.Tests;

// The one rule for "is this executable the installed copy?" (issue #211). The folders are
// temporary; nothing here looks at the real install directory.
public class InstallLocationTests : IDisposable
{
    private readonly string _root;
    private readonly string _defaultInstallDirectory;

    public InstallLocationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"PaperNexus_InstallLocation_{Guid.NewGuid():N}");
        _defaultInstallDirectory = Path.Combine(_root, "share", "PaperNexus");
        Directory.CreateDirectory(_defaultInstallDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp cleanup is best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AnExecutableBesideTheInstallSentinelIsTheInstalledCopy()
    {
        // A custom install folder: the install flow wrote the sentinel beside the executable.
        var customFolder = Path.Combine(_root, "Apps", "PaperNexus");
        Directory.CreateDirectory(customFolder);
        File.WriteAllText(Path.Combine(customFolder, InstallLocation.SentinelFileName), string.Empty);
        var exePath = Path.Combine(customFolder, PlatformPaths.ExecutableName);

        Assert.True(InstallLocation.IsInstalledCopy(exePath, _defaultInstallDirectory));
    }

    [Fact]
    public void TheDefaultInstallPathIsTheInstalledCopyWithoutASentinel()
    {
        // Installs made before the sentinel file existed.
        var exePath = Path.Combine(_defaultInstallDirectory, PlatformPaths.ExecutableName);

        Assert.True(InstallLocation.IsInstalledCopy(exePath, _defaultInstallDirectory));
    }

    [Fact]
    public void ABuildFolderIsNotTheInstalledCopy()
    {
        var buildFolder = Path.Combine(_root, "repo", "PaperNexus", "bin", "Release", "net10.0");
        Directory.CreateDirectory(buildFolder);
        var exePath = Path.Combine(buildFolder, PlatformPaths.ExecutableName);

        Assert.False(InstallLocation.IsInstalledCopy(exePath, _defaultInstallDirectory));
    }

    [Fact]
    public void ASentinelInAnotherFolderDoesNotCount()
    {
        // The default install folder has its sentinel; a copy elsewhere is still not installed.
        File.WriteAllText(Path.Combine(_defaultInstallDirectory, InstallLocation.SentinelFileName), string.Empty);
        var elsewhere = Path.Combine(_root, "tmp");
        Directory.CreateDirectory(elsewhere);
        var exePath = Path.Combine(elsewhere, PlatformPaths.ExecutableName);

        Assert.False(InstallLocation.IsInstalledCopy(exePath, _defaultInstallDirectory));
    }

    [Fact]
    public void ADefaultPathDifferingOnlyInCaseIsNotTheInstalledCopyOnLinux()
    {
        if (OperatingSystem.IsWindows())
            return; // Windows paths are case-insensitive, so the same path there is the installed copy.

        var exePath = Path.Combine(_defaultInstallDirectory.ToUpperInvariant(), PlatformPaths.ExecutableName);

        Assert.False(InstallLocation.IsInstalledCopy(exePath, _defaultInstallDirectory));
    }
}

using PaperNexus.Core.Platform;
using Xunit;

namespace PaperNexus.Tests;

// Covers the cross-platform conventions that replaced the app's Windows-only assumptions.
// Nothing here touches the registry, the desktop shell, or a real wallpaper.
public class PlatformPathsTests
{
    [Fact]
    public void ExecutableName_MatchesTheHostPlatformConvention()
    {
        var expected = OperatingSystem.IsWindows() ? "PaperNexus.exe" : "PaperNexus";
        Assert.Equal(expected, PlatformPaths.ExecutableName);
    }

    [Fact]
    public void PathEquals_IsCaseSensitiveOnlyWhereTheFileSystemIs()
    {
        // Windows paths are case-insensitive; Linux paths are not. Treating "/a/Pic.png"
        // and "/a/pic.png" as the same file on Linux would collide distinct wallpapers.
        var matches = PlatformPaths.PathEquals("/tmp/Pic.png", "/tmp/pic.png");
        Assert.Equal(OperatingSystem.IsWindows(), matches);
    }

    [Fact]
    public void PathEquals_MatchesIdenticalPaths()
    {
        Assert.True(PlatformPaths.PathEquals("/tmp/pic.png", "/tmp/pic.png"));
    }

    [Fact]
    public void PathEquals_TreatsNullsAsUnequalToAnyPath()
    {
        Assert.False(PlatformPaths.PathEquals(null, "/tmp/pic.png"));
        Assert.False(PlatformPaths.PathEquals("/tmp/pic.png", null));
        Assert.True(PlatformPaths.PathEquals(null, null));
    }

    [Fact]
    public void DefaultInstallDirectory_IsAnAbsoluteUserWritablePath()
    {
        // A relative path here would place the install (and settings.json) wherever the
        // process happened to be launched from - the failure mode when Linux has no
        // XDG user-dirs configured.
        var installDir = PlatformPaths.DefaultInstallDirectory;
        Assert.True(Path.IsPathRooted(installDir));
        Assert.EndsWith("PaperNexus", installDir);
    }
}

public class LinuxDesktopTests
{
    [Theory]
    [InlineData("KDE")]
    [InlineData("plasma")]
    [InlineData("KDE:plasmawayland")]
    public void Classify_RecognisesPlasmaSessions(string sessionName)
    {
        Assert.Equal(LinuxDesktopEnvironment.Kde, LinuxDesktop.Classify([sessionName]));
    }

    [Theory]
    [InlineData("GNOME")]
    [InlineData("ubuntu:GNOME")]
    [InlineData("X-Cinnamon")]
    public void Classify_RecognisesGnomeDerivedSessions(string sessionName)
    {
        Assert.Equal(LinuxDesktopEnvironment.Gnome, LinuxDesktop.Classify([sessionName]));
    }

    [Fact]
    public void Classify_FallsThroughEmptyValuesToTheNextCandidate()
    {
        // XDG_CURRENT_DESKTOP is often unset while DESKTOP_SESSION is populated.
        Assert.Equal(LinuxDesktopEnvironment.Kde, LinuxDesktop.Classify([null, "", "plasma"]));
    }

    [Fact]
    public void Classify_ReturnsUnknownForUnrecognisedSessions()
    {
        Assert.Equal(LinuxDesktopEnvironment.Unknown, LinuxDesktop.Classify(["sway", "i3"]));
        Assert.Equal(LinuxDesktopEnvironment.Unknown, LinuxDesktop.Classify([]));
    }
}

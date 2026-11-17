using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The default download folder sits under the user's pictures folder.
public class DownloadSettingsTests
{
    [Fact]
    public void DefaultPicturesDirectory_IsAnAbsolutePath()
    {
        Assert.True(Path.IsPathRooted(DownloadSettings.DefaultPicturesDirectory));
    }
}

using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The version label: "v" and the major version.
public class AppVersionLabelTests
{
    [Theory]
    [InlineData("1.0.0", "v1")]
    [InlineData("168.0.0", "v168")]
    [InlineData("12", "v12")]
    [InlineData(" 7.2 ", "v7")]
    [InlineData("", "v0")]
    [InlineData(null, "v0")]
    [InlineData("beta", "v0")]
    [InlineData("-1.0", "v0")]
    public void FromVersionName_IsVAndTheMajorNumber(string? versionName, string label)
    {
        Assert.Equal(label, AppVersionLabel.FromVersionName(versionName));
    }
}

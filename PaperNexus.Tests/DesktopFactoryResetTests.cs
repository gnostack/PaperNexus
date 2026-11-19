using Xunit;

namespace PaperNexus.Tests;

// The desktop's Factory Reset starts the new copy with this copy's arguments, less --startup.
// What it deletes is AppDataReset's, tested with the shared code.
public class DesktopFactoryResetTests
{
    [Theory]
    [InlineData(new[] { "--startup" }, new string[0])]
    [InlineData(new[] { "--debug" }, new[] { "--debug" })]
    [InlineData(new[] { "--startup", "--debug" }, new[] { "--debug" })]
    [InlineData(new[] { "--updated", "--startup", "--other" }, new[] { "--updated", "--other" })]
    [InlineData(new string[0], new string[0])]
    public void RelaunchArguments_KeepAllButStartup(string[] started, string[] relaunched)
    {
        // The person pressed Factory Reset in the window, so the new copy shows its window
        Assert.Equal(relaunched, DesktopFactoryReset.RelaunchArguments(started));
    }
}

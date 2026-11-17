using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The eggs are cosmetic, but their triggers sit on paths that also do real work - favoriting
// a wallpaper, changing the annotation colour, typing in the settings window. These tests
// pin the trigger conditions so an egg can never fire where it would be mistaken for an
// error, and never swallow the normal behaviour.
public class EasterEggTests
{
    [Fact]
    public void FavoriteMilestone_FiresOnlyOnRoundNumbers()
    {
        Assert.NotNull(EasterEggs.FavoriteMilestoneMessage(1));
        Assert.NotNull(EasterEggs.FavoriteMilestoneMessage(10));
        Assert.NotNull(EasterEggs.FavoriteMilestoneMessage(100));

        // Everything else must return null so the caller shows its ordinary confirmation.
        Assert.Null(EasterEggs.FavoriteMilestoneMessage(2));
        Assert.Null(EasterEggs.FavoriteMilestoneMessage(11));
        Assert.Null(EasterEggs.FavoriteMilestoneMessage(0));
        Assert.Null(EasterEggs.FavoriteMilestoneMessage(-1));
        Assert.Null(EasterEggs.FavoriteMilestoneMessage(101));
    }

    [Theory]
    [InlineData("#C0FFEE")]
    [InlineData("c0ffee")]
    [InlineData("  #BADA55  ")]
    [InlineData("FACADE")]
    public void MagicColor_MatchesRegardlessOfCaseHashOrSurroundingSpace(string hex)
    {
        // The colour box is free text, so the user may type any of these forms.
        Assert.NotNull(EasterEggs.MagicColorMessage(hex));
    }

    [Theory]
    [InlineData("#F5F5F5")]   // the shipped default
    [InlineData("#000000")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a color")]
    public void MagicColor_StaysSilentForOrdinaryValues(string? hex)
    {
        // A message on a normal colour would look like a warning about the value.
        Assert.Null(EasterEggs.MagicColorMessage(hex));
    }

    [Fact]
    public void Konami_CompletesOnTheFullSequence()
    {
        var konami = new KonamiSequence();
        string[] code = ["Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B"];

        foreach (var key in code)
            Assert.False(konami.Advance(key));

        Assert.True(konami.Advance("A"));
    }

    [Fact]
    public void Konami_ResetsAfterCompletingSoItCanFireAgain()
    {
        var konami = new KonamiSequence();
        string[] code = ["Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B", "A"];

        foreach (var key in code)
            konami.Advance(key);

        // Second run through must also complete rather than being stuck at the end.
        string[] second = ["Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B"];
        foreach (var key in second)
            Assert.False(konami.Advance(key));
        Assert.True(konami.Advance("A"));
    }

    [Fact]
    public void Konami_TreatsAStrayLeadingKeyAsTheStartOfAFreshAttempt()
    {
        var konami = new KonamiSequence();

        // "Up, Up, Up, Down, ..." contains the code from the second Up onward. Resetting to
        // zero rather than one would make it impossible to match without lifting off.
        Assert.False(konami.Advance("Up"));
        Assert.False(konami.Advance("Up"));
        Assert.False(konami.Advance("Up"));
        foreach (var key in new[] { "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B" })
            Assert.False(konami.Advance(key));
        Assert.True(konami.Advance("A"));
    }

    [Fact]
    public void Konami_IgnoresUnrelatedTyping()
    {
        var konami = new KonamiSequence();

        // Typing in a settings text box must never accidentally trigger the egg.
        foreach (var key in new[] { "H", "E", "L", "L", "O", "Space", "W", "O", "R", "L", "D" })
            Assert.False(konami.Advance(key));
    }

    [Fact]
    public void VersionMessages_CycleRatherThanRepeatingTheSameOne()
    {
        // Clicking again should reward the user with something new.
        var first = EasterEggs.NextVersionMessage();
        var second = EasterEggs.NextVersionMessage();
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void VersionMessages_AreNeverEmpty()
    {
        for (var i = 0; i < 20; i++)
            Assert.False(string.IsNullOrWhiteSpace(EasterEggs.NextVersionMessage()));
    }

    [Fact]
    public void SplashMessage_IsUsuallyTheOrdinaryLine()
    {
        // A joke on every launch stops being a surprise, so the plain line must dominate.
        var random = new Random(12345);
        var ordinary = 0;
        const int runs = 2000;
        for (var i = 0; i < runs; i++)
        {
            if (EasterEggs.SplashMessage(random) == "Starting up...")
                ordinary++;
        }

        Assert.InRange(ordinary / (double)runs, 0.6, 0.9);
    }

    [Fact]
    public void SplashMessage_IsNeverEmpty()
    {
        var random = new Random(99);
        for (var i = 0; i < 200; i++)
            Assert.False(string.IsNullOrWhiteSpace(EasterEggs.SplashMessage(random)));
    }
}

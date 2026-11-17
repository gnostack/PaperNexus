using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The checklist is only useful if it stays in step with the eggs that actually exist, and if
// discovery ids never drift - a changed id silently un-finds that egg for every user.
public class EasterEggCatalogTests
{
    [Fact]
    public void EveryPlayableShow_IsListedInTheCatalog()
    {
        // An egg that can fire but is missing from the catalog would be unfindable in the
        // checklist and could never be ticked off.
        string[] playableIds =
        [
            EasterEggShows.Konami().Id,
            EasterEggShows.Version().Id,
            EasterEggShows.Favorite(10)!.Id,
            EasterEggShows.MagicColor("#C0FFEE")!.Id,
        ];

        Assert.All(playableIds, id => Assert.NotNull(EasterEggCatalog.Find(id)));
    }

    [Fact]
    public void EveryCatalogEntry_HasAUniqueId()
    {
        // Duplicate ids would tick two rows at once and corrupt the progress count.
        var ids = EasterEggCatalog.All.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryCatalogEntry_HasANameAndHint()
    {
        // A blank hint makes the row useless: the point of the list is to be findable.
        Assert.All(EasterEggCatalog.All, egg =>
        {
            Assert.False(string.IsNullOrWhiteSpace(egg.Id));
            Assert.False(string.IsNullOrWhiteSpace(egg.Name));
            Assert.False(string.IsNullOrWhiteSpace(egg.Hint));
        });
    }

    [Fact]
    public void CatalogIds_AreTheOnesPersistedInSettings()
    {
        // These strings are written to settings.json. Renaming one resets that egg to
        // undiscovered for everyone who already found it, so pin them explicitly.
        Assert.Equal(
            ["konami", "version", "favorites", "colors", "splash"],
            EasterEggCatalog.All.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Build_MarksOnlyTheDiscoveredEntries()
    {
        var entries = EasterEggProgress.Build(["konami", "colors"]);

        Assert.Equal(EasterEggCatalog.Total, entries.Count);
        Assert.True(entries.Single(e => e.Egg.Id == "konami").Found);
        Assert.True(entries.Single(e => e.Egg.Id == "colors").Found);
        Assert.False(entries.Single(e => e.Egg.Id == "version").Found);
    }

    [Fact]
    public void Build_IgnoresIdsItDoesNotRecognise()
    {
        // Downgrading the app leaves ids from a newer version in settings; they must not
        // throw or inflate the count.
        var entries = EasterEggProgress.Build(["konami", "an-egg-from-the-future"]);
        Assert.Equal(EasterEggCatalog.Total, entries.Count);
        Assert.Equal(1, EasterEggProgress.CountFound(["konami", "an-egg-from-the-future"]));
    }

    [Fact]
    public void Build_MatchesIdsCaseInsensitively()
    {
        // Settings are hand-editable, so a differently-cased id must still count.
        Assert.Equal(1, EasterEggProgress.CountFound(["KONAMI"]));
    }

    [Fact]
    public void Build_HandlesNoDiscoveriesAndAllDiscoveries()
    {
        Assert.Equal(0, EasterEggProgress.CountFound([]));
        Assert.Equal(EasterEggCatalog.Total,
            EasterEggProgress.CountFound(EasterEggCatalog.All.Select(e => e.Id)));
    }

    [Fact]
    public void Build_PreservesCatalogOrder()
    {
        // The checklist should not reshuffle as eggs are found.
        var entries = EasterEggProgress.Build(["splash"]);
        Assert.Equal(
            EasterEggCatalog.All.Select(e => e.Id),
            entries.Select(e => e.Egg.Id));
    }
}

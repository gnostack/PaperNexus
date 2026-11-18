using System.Net;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The rules for wallpaper sources, which both the desktop window and the phone's Sources tab
// use: listing, adding, editing, deleting, the on/off switch, the checks a source must pass,
// and testing a feed.
public class WallpaperSourcesTests : IDisposable
{
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();

    public void Dispose() => TestHelpers.DeleteSettingsStore(_store);

    private static SourceFields Fields(
        string name = "Lakes",
        string url = "https://feeds.test/lakes.json",
        string imageUrlJPath = "$[*].imageUrl",
        string titleJPath = "$[*].title",
        string cron = "0 */8 * * *",
        bool isEnabled = true) => new(name, url, imageUrlJPath, titleJPath, cron, isEnabled);

    private static SourceKey Key(string name, int occurrence = 0) => new(name, occurrence);

    private async Task WriteSourcesAsync(params WallpaperSource[] sources)
    {
        await _store.WriteAsync(new WallpaperNexusSettings { Sources = [.. sources] });
    }

    [Fact]
    public async Task List_ReturnsTheSavedSourcesInOrder()
    {
        await WriteSourcesAsync(
            new WallpaperSource { Name = "One", Url = "https://one.test/feed" },
            new WallpaperSource { Name = "Two", Url = "https://two.test/feed", IsEnabled = false });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var sources = await sut.ListAsync();

        Assert.Equal(["One", "Two"], sources.Select(s => s.Fields.Name));
        Assert.True(sources[0].Fields.IsEnabled);
        Assert.False(sources[1].Fields.IsEnabled);
        Assert.Equal("https://two.test/feed", sources[1].Fields.Url);
        Assert.Equal(Key("One"), sources[0].Key);
        Assert.Equal(Key("Two"), sources[1].Key);
    }

    [Fact]
    public async Task Add_SavesTheTrimmedSourceAtTheEnd()
    {
        await WriteSourcesAsync(new WallpaperSource { Name = "One", Url = "https://one.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.AddAsync(Fields(name: "  Lakes  ", url: " https://feeds.test/lakes.json ", isEnabled: false));

        Assert.Null(problem);
        var saved = (await _store.LoadAsync()).Sources;
        Assert.Equal(["One", "Lakes"], saved.Select(s => s.Name));
        Assert.Equal("https://feeds.test/lakes.json", saved[1].Url);
        Assert.False(saved[1].IsEnabled);
        Assert.Equal(WallpaperSourceType.HttpJson, saved[1].Type);
        Assert.Null(saved[1].LastDownloadUtc);
    }

    [Fact]
    public async Task Add_BlankSchedule_BecomesEveryHour()
    {
        var sut = TestHelpers.CreateWallpaperSources(_store);

        await sut.AddAsync(Fields(cron: "   "));

        var saved = (await _store.LoadAsync()).Sources.Single(s => s.Name == "Lakes");
        Assert.Equal("0 * * * *", saved.CronExpression);
    }

    // The checks run in the order a person reads the form; the first problem is the answer.
    [Theory]
    [InlineData("", "https://a.test/f", "$[*].i", "$[*].t", "0 * * * *", SourceProblem.NameRequired)]
    [InlineData("A", " ", "$[*].i", "$[*].t", "0 * * * *", SourceProblem.UrlRequired)]
    [InlineData("A", "http://a.test/f", "$[*].i", "$[*].t", "0 * * * *", SourceProblem.UrlNotHttps)]
    [InlineData("A", "not an address", "$[*].i", "$[*].t", "0 * * * *", SourceProblem.UrlNotHttps)]
    [InlineData("A", "https://a.test/f", "", "$[*].t", "0 * * * *", SourceProblem.ImageUrlJPathRequired)]
    [InlineData("A", "https://a.test/f", "$[*].i", " ", "0 * * * *", SourceProblem.TitleJPathRequired)]
    [InlineData("A", "https://a.test/f", "$[*].i", "$[*].t", "every day", SourceProblem.InvalidCron)]
    [InlineData("A", "https://a.test/f", "$[*].i", "$[*].t", "0 0 * * * *", SourceProblem.InvalidCron)]
    [InlineData("", "", "", "", "nonsense", SourceProblem.NameRequired)]
    public async Task Add_RefusesASourceThatFailsACheck_AndSavesNothing(
        string name, string url, string imageUrlJPath, string titleJPath, string cron, SourceProblem expected)
    {
        await WriteSourcesAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.AddAsync(Fields(name, url, imageUrlJPath, titleJPath, cron));

        Assert.Equal(expected, problem);
        Assert.Empty((await _store.LoadAsync()).Sources);
    }

    [Theory]
    [InlineData("Lakes")]
    [InlineData("lakes")]
    [InlineData("  LAKES ")]
    public async Task Add_RefusesANameAlreadyUsed_IgnoringCaseAndSpaces(string name)
    {
        await WriteSourcesAsync(new WallpaperSource { Name = "Lakes", Url = "https://one.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.AddAsync(Fields(name: name));

        Assert.Equal(SourceProblem.NameTaken, problem);
        Assert.Single((await _store.LoadAsync()).Sources);
    }

    [Fact]
    public async Task Replace_KeepsThePlaceAndTheDownloadTime_WhenTheNameStays()
    {
        var downloaded = DateTimeOffset.UtcNow.AddHours(-2);
        await WriteSourcesAsync(
            new WallpaperSource { Name = "One", Url = "https://one.test/feed" },
            new WallpaperSource { Name = "Lakes", Url = "https://old.test/feed", LastDownloadUtc = downloaded },
            new WallpaperSource { Name = "Three", Url = "https://three.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync(Key("Lakes"), Fields(name: "LAKES", url: "https://new.test/feed"));

        Assert.Null(problem);
        var saved = (await _store.LoadAsync()).Sources;
        Assert.Equal(["One", "LAKES", "Three"], saved.Select(s => s.Name));
        Assert.Equal("https://new.test/feed", saved[1].Url);
        Assert.Equal(downloaded, saved[1].LastDownloadUtc);
    }

    [Fact]
    public async Task Replace_RenamedSource_CountsAsNeverDownloaded()
    {
        await WriteSourcesAsync(new WallpaperSource { Name = "Lakes", Url = "https://old.test/feed", LastDownloadUtc = DateTimeOffset.UtcNow });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        await sut.ReplaceAsync(Key("Lakes"), Fields(name: "Rivers"));

        var saved = (await _store.LoadAsync()).Sources.Single();
        Assert.Equal("Rivers", saved.Name);
        Assert.Null(saved.LastDownloadUtc);
    }

    [Fact]
    public async Task Replace_RefusesTheNameOfAnotherSource()
    {
        await WriteSourcesAsync(
            new WallpaperSource { Name = "Lakes", Url = "https://one.test/feed" },
            new WallpaperSource { Name = "Rivers", Url = "https://two.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync(Key("Lakes"), Fields(name: "rivers"));

        Assert.Equal(SourceProblem.NameTaken, problem);
        Assert.Equal(["Lakes", "Rivers"], (await _store.LoadAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Replace_SourceDeletedMeanwhile_IsAddedAtTheEnd()
    {
        await WriteSourcesAsync(new WallpaperSource { Name = "One", Url = "https://one.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync(Key("Gone"), Fields(name: "Lakes"));

        Assert.Null(problem);
        Assert.Equal(["One", "Lakes"], (await _store.LoadAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task DeleteAndSwitch_ActOnTheNamedSourceOnly()
    {
        await WriteSourcesAsync(
            new WallpaperSource { Name = "One", Url = "https://one.test/feed" },
            new WallpaperSource { Name = "Two", Url = "https://two.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        await sut.SetEnabledAsync(Key("two"), false);
        await sut.DeleteAsync(Key("ONE"));
        await sut.DeleteAsync(Key("Nobody"));

        var saved = (await _store.LoadAsync()).Sources;
        Assert.Equal("Two", saved.Single().Name);
        Assert.False(saved.Single().IsEnabled);
    }

    // A file written before names were unique keeps both sources. Each is listed with its own
    // key, and every action changes exactly the source its key names.
    private async Task WriteTwinsAsync()
    {
        await WriteSourcesAsync(
            new WallpaperSource { Name = "Twin", Url = "https://first.test/feed", LastDownloadUtc = DateTimeOffset.UnixEpoch },
            new WallpaperSource { Name = "Other", Url = "https://other.test/feed" },
            new WallpaperSource { Name = "twin", Url = "https://second.test/feed", LastDownloadUtc = DateTimeOffset.UnixEpoch.AddDays(1) });
    }

    private async Task<List<WallpaperSource>> SavedAsync() => (await _store.LoadAsync()).Sources;

    [Fact]
    public async Task OlderFileWithTwins_ListsEachWithItsOwnKey()
    {
        await WriteTwinsAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var listed = await sut.ListAsync();

        Assert.Equal([Key("Twin", 0), Key("Other", 0), Key("twin", 1)], listed.Select(l => l.Key));
        Assert.True(listed[0].Key.IsSameSource(Key(" TWIN ", 0)));
        Assert.False(listed[0].Key.IsSameSource(listed[2].Key));
    }

    [Fact]
    public async Task OlderFileWithTwins_DeleteOfTheSecond_DeletesTheSecond()
    {
        await WriteTwinsAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);

        await sut.DeleteAsync(Key("twin", 1));

        Assert.Equal(["https://first.test/feed", "https://other.test/feed"], (await SavedAsync()).Select(s => s.Url));
    }

    [Fact]
    public async Task OlderFileWithTwins_SwitchOfTheSecond_SwitchesTheSecond()
    {
        await WriteTwinsAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);

        await sut.SetEnabledAsync(Key("twin", 1), false);

        var saved = await SavedAsync();
        Assert.True(saved[0].IsEnabled);
        Assert.False(saved[2].IsEnabled);
    }

    // Keeping its own name is allowed for either twin; nothing is renamed.
    [Fact]
    public async Task OlderFileWithTwins_EditOfTheSecondKeepingItsName_EditsTheSecond()
    {
        await WriteTwinsAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync(Key("twin", 1), Fields(name: "twin", url: "https://edited.test/feed"));

        Assert.Null(problem);
        var saved = await SavedAsync();
        Assert.Equal(["Twin", "Other", "twin"], saved.Select(s => s.Name));
        Assert.Equal(["https://first.test/feed", "https://other.test/feed", "https://edited.test/feed"], saved.Select(s => s.Url));
        Assert.Equal(DateTimeOffset.UnixEpoch, saved[0].LastDownloadUtc);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), saved[2].LastDownloadUtc);
    }

    [Fact]
    public async Task OlderFileWithTwins_RenamingTheSecondToAnotherSourcesName_IsRefused()
    {
        await WriteTwinsAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync(Key("twin", 1), Fields(name: "other"));

        Assert.Equal(SourceProblem.NameTaken, problem);
        Assert.Equal(["Twin", "Other", "twin"], (await SavedAsync()).Select(s => s.Name));
    }

    [Fact]
    public async Task OlderFileWithTwins_RenamingTheSecondToANewName_RenamesOnlyIt()
    {
        await WriteTwinsAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync(Key("twin", 1), Fields(name: "Rivers"));

        Assert.Null(problem);
        Assert.Equal(["Twin", "Other", "Rivers"], (await SavedAsync()).Select(s => s.Name));
        Assert.Equal(["Twin", "Other", "Rivers"], (await sut.ListAsync()).Select(l => l.Key.Name));
    }

    // An edited source deleted meanwhile is added back, but never as a second source of a name.
    [Fact]
    public async Task Replace_SourceDeletedMeanwhileWhoseNameIsNowTaken_IsRefused()
    {
        await WriteSourcesAsync(new WallpaperSource { Name = "Twin", Url = "https://first.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync(Key("twin", 1), Fields(name: "twin"));

        Assert.Equal(SourceProblem.NameTaken, problem);
        Assert.Single(await SavedAsync());
    }

    // Each change touches only the sources: a favorite and a download time saved by others stay.
    [Fact]
    public async Task Changes_KeepWhatOtherWritersSaved()
    {
        await WriteSourcesAsync(new WallpaperSource { Name = "Lakes", Url = "https://one.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);
        var downloaded = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _store.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers.Add("/tmp/favorite.png");
            latest.Sources[0].LastDownloadUtc = downloaded;
        });

        await sut.SetEnabledAsync(Key("Lakes"), false);
        await sut.AddAsync(Fields(name: "Rivers"));

        var saved = await _store.LoadAsync();
        Assert.Equal(["/tmp/favorite.png"], saved.FavoriteWallpapers);
        Assert.Equal(downloaded, saved.Sources[0].LastDownloadUtc);
    }

    [Fact]
    public async Task Changed_IsRaisedAfterEverySavedChange_AndNotForARefusal()
    {
        await WriteSourcesAsync();
        var sut = TestHelpers.CreateWallpaperSources(_store);
        var raised = 0;
        sut.Changed += () => raised++;

        await sut.AddAsync(Fields(name: "Lakes"));
        await sut.AddAsync(Fields(name: "lakes"));
        await sut.AddAsync(Fields(name: ""));
        await sut.ReplaceAsync(Key("Lakes"), Fields(name: "Rivers"));
        await sut.SetEnabledAsync(Key("Rivers"), false);
        await sut.DeleteAsync(Key("Rivers"));

        Assert.Equal(4, raised);
    }

    [Fact]
    public async Task Test_ReturnsThePicturesTheFeedLists()
    {
        var handler = new FeedHandler(FeedHandler.TwoPictures);
        var sut = TestHelpers.CreateWallpaperSources(_store, handler);

        var outcome = await sut.TestAsync(Fields(name: "", url: " https://feeds.test/lakes.json "), CancellationToken.None);

        Assert.Null(outcome.Problem);
        Assert.Equal(["Lake", "Hill"], outcome.Images.Select(i => i.Title));
        Assert.Equal("https://feeds.test/lakes.json", handler.Requests.Single().ToString());
        Assert.DoesNotContain((await _store.LoadAsync()).Sources, s => s.Name == "Lakes");
    }

    // Test checks the address and both picture-finding rules as Save does, before any request.
    [Theory]
    [InlineData("", "$[*].i", "$[*].t", SourceProblem.UrlRequired)]
    [InlineData("http://feeds.test/f", "$[*].i", "$[*].t", SourceProblem.UrlNotHttps)]
    [InlineData("https://feeds.test/f", " ", "$[*].t", SourceProblem.ImageUrlJPathRequired)]
    [InlineData("https://feeds.test/f", "$[*].i", "", SourceProblem.TitleJPathRequired)]
    public async Task Test_RefusesBeforeReadingTheFeed(string url, string imageUrlJPath, string titleJPath, SourceProblem expected)
    {
        var handler = new FeedHandler(FeedHandler.TwoPictures);
        var sut = TestHelpers.CreateWallpaperSources(_store, handler);

        var outcome = await sut.TestAsync(Fields(url: url, imageUrlJPath: imageUrlJPath, titleJPath: titleJPath), CancellationToken.None);

        Assert.Equal(expected, outcome.Problem);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Test_FeedThatAnswersAnError_Throws()
    {
        var handler = new FeedHandler("gone", HttpStatusCode.NotFound);
        var sut = TestHelpers.CreateWallpaperSources(_store, handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => sut.TestAsync(Fields(), CancellationToken.None));

        Assert.Contains("404", error.Message);
    }

    [Fact]
    public async Task Test_Cancelled_ThrowsOperationCanceled()
    {
        var handler = new FeedHandler(FeedHandler.TwoPictures) { Gate = new TaskCompletionSource() };
        var sut = TestHelpers.CreateWallpaperSources(_store, handler);
        using var cts = new CancellationTokenSource();

        var test = sut.TestAsync(Fields(), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
    }
}

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

        Assert.Equal(["One", "Two"], sources.Select(s => s.Name));
        Assert.True(sources[0].IsEnabled);
        Assert.False(sources[1].IsEnabled);
        Assert.Equal("https://two.test/feed", sources[1].Url);
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

        var problem = await sut.ReplaceAsync("Lakes", Fields(name: "LAKES", url: "https://new.test/feed"));

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

        await sut.ReplaceAsync("Lakes", Fields(name: "Rivers"));

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

        var problem = await sut.ReplaceAsync("Lakes", Fields(name: "rivers"));

        Assert.Equal(SourceProblem.NameTaken, problem);
        Assert.Equal(["Lakes", "Rivers"], (await _store.LoadAsync()).Sources.Select(s => s.Name));
    }

    [Fact]
    public async Task Replace_SourceDeletedMeanwhile_IsAddedAtTheEnd()
    {
        await WriteSourcesAsync(new WallpaperSource { Name = "One", Url = "https://one.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var problem = await sut.ReplaceAsync("Gone", Fields(name: "Lakes"));

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

        await sut.SetEnabledAsync("two", false);
        await sut.DeleteAsync("ONE");
        await sut.DeleteAsync("Nobody");

        var saved = (await _store.LoadAsync()).Sources;
        Assert.Equal("Two", saved.Single().Name);
        Assert.False(saved.Single().IsEnabled);
    }

    // A file written before names were unique keeps both sources; each action takes the first.
    [Fact]
    public async Task OlderFileWithTwoSourcesOfOneName_LoadsAndActsOnTheFirst()
    {
        await WriteSourcesAsync(
            new WallpaperSource { Name = "Twin", Url = "https://first.test/feed" },
            new WallpaperSource { Name = "twin", Url = "https://second.test/feed" });
        var sut = TestHelpers.CreateWallpaperSources(_store);

        var listed = await sut.ListAsync();
        await sut.SetEnabledAsync("Twin", false);
        var problem = await sut.ReplaceAsync("Twin", Fields(name: "Twin", url: "https://edited.test/feed", isEnabled: false));

        Assert.Equal(2, listed.Count);
        Assert.Null(problem);
        var saved = (await _store.LoadAsync()).Sources;
        Assert.Equal(["https://edited.test/feed", "https://second.test/feed"], saved.Select(s => s.Url));
        Assert.False(saved[0].IsEnabled);
        Assert.True(saved[1].IsEnabled);

        await sut.DeleteAsync("TWIN");
        Assert.Equal("https://second.test/feed", (await _store.LoadAsync()).Sources.Single().Url);
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

        await sut.SetEnabledAsync("Lakes", false);
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
        await sut.ReplaceAsync("Lakes", Fields(name: "Rivers"));
        await sut.SetEnabledAsync("Rivers", false);
        await sut.DeleteAsync("Rivers");

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

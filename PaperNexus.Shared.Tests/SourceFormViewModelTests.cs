using System.Net;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The add or edit source form both devices show: its defaults, the desktop's sentences for each
// refusal, the Test button, and Save keeping the form open on a refusal or a failure.
public class SourceFormViewModelTests : IDisposable
{
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();

    public void Dispose() => TestHelpers.DeleteSettingsStore(_store);

    private async Task<WallpaperSources> CreateSourcesAsync(FeedHandler? handler = null, params WallpaperSource[] sources)
    {
        await _store.WriteAsync(new WallpaperNexusSettings { Sources = [.. sources] });
        var wallpaperSources = TestHelpers.CreateWallpaperSources(_store, handler ?? new FeedHandler(FeedHandler.TwoPictures));
        return wallpaperSources;
    }

    private static void FillValid(SourceFormViewModel form)
    {
        form.Name = "Lakes";
        form.Url = "https://feeds.test/lakes.json";
    }

    [Fact]
    public async Task AddForm_StartsWithTheDesktopsDefaults()
    {
        var form = new SourceFormViewModel(await CreateSourcesAsync());

        Assert.Equal("Add Wallpaper Source", form.Title);
        Assert.True(form.IsEnabled);
        Assert.Equal(string.Empty, form.Name);
        Assert.Equal(string.Empty, form.Url);
        Assert.Equal("$[*].imageUrl", form.ImageUrlJPath);
        Assert.Equal("$[*].title", form.TitleJPath);
        Assert.Equal(string.Empty, form.CronExpression);
        Assert.Equal("Test", form.TestButtonText);
        Assert.False(form.HasError);
        Assert.False(form.HasTestResult);
    }

    [Fact]
    public async Task EditForm_IsFilledWithTheSource()
    {
        var fields = new SourceFields("Lakes", "https://feeds.test/l", "$.a", "$.b", "0 1 * * *", false);
        var form = new SourceFormViewModel(await CreateSourcesAsync(), fields);

        Assert.Equal("Edit Wallpaper Source", form.Title);
        Assert.False(form.IsEnabled);
        Assert.Equal("Lakes", form.Name);
        Assert.Equal("https://feeds.test/l", form.Url);
        Assert.Equal("$.a", form.ImageUrlJPath);
        Assert.Equal("$.b", form.TitleJPath);
        Assert.Equal("0 1 * * *", form.CronExpression);
    }

    [Theory]
    [InlineData("", "https://a.test/f", "$[*].i", "$[*].t", "", "Name is required.")]
    [InlineData("A", "", "$[*].i", "$[*].t", "", "URL is required.")]
    [InlineData("A", "http://a.test/f", "$[*].i", "$[*].t", "", "URL must use HTTPS.")]
    [InlineData("A", "https://a.test/f", "", "$[*].t", "", "Image URL JPath is required.")]
    [InlineData("A", "https://a.test/f", "$[*].i", "", "", "Title JPath is required.")]
    [InlineData("A", "https://a.test/f", "$[*].i", "$[*].t", "soon", "Invalid cron expression.")]
    [InlineData(" bing ", "https://a.test/f", "$[*].i", "$[*].t", "", "A source named bing already exists.")]
    public async Task Save_Refused_ShowsTheSentenceAndStaysOpen(
        string name, string url, string imageUrlJPath, string titleJPath, string cron, string expected)
    {
        var sources = await CreateSourcesAsync(null, new WallpaperSource { Name = "Bing", Url = "https://bing.test/f" });
        var form = new SourceFormViewModel(sources)
        {
            Name = name,
            Url = url,
            ImageUrlJPath = imageUrlJPath,
            TitleJPath = titleJPath,
            CronExpression = cron,
        };
        bool? finished = null;
        form.Finished += saved => finished = saved;

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expected, form.ErrorText);
        Assert.Null(finished);
        Assert.Equal(name, form.Name);
        Assert.Single((await _store.LoadAsync()).Sources);
    }

    [Fact]
    public async Task Save_Valid_SavesAndFinishes()
    {
        var form = new SourceFormViewModel(await CreateSourcesAsync());
        FillValid(form);
        bool? finished = null;
        form.Finished += saved => finished = saved;

        await form.SaveCommand.ExecuteAsync(null);

        Assert.True(finished);
        Assert.Equal("Lakes", form.SavedName);
        Assert.Equal("Lakes", (await _store.LoadAsync()).Sources.Single().Name);
    }

    [Fact]
    public async Task Save_EditKeepingItsName_IsNotAClash()
    {
        var sources = await CreateSourcesAsync(null, new WallpaperSource { Name = "Lakes", Url = "https://old.test/f" });
        var editing = (await sources.ListAsync()).Single();
        var form = new SourceFormViewModel(sources, editing) { Url = "https://new.test/f" };
        bool? finished = null;
        form.Finished += saved => finished = saved;

        await form.SaveCommand.ExecuteAsync(null);

        Assert.True(finished);
        Assert.Equal("https://new.test/f", (await _store.LoadAsync()).Sources.Single().Url);
    }

    [Fact]
    public async Task Save_WriteFails_ShowsTheErrorAndKeepsTheForm()
    {
        var form = new SourceFormViewModel(new FailingSources(new IOException("The disk is full.")));
        FillValid(form);
        bool? finished = null;
        form.Finished += saved => finished = saved;

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Error saving settings: The disk is full.", form.ErrorText);
        Assert.Null(finished);
    }

    [Fact]
    public async Task Cancel_FinishesWithoutSaving()
    {
        var form = new SourceFormViewModel(await CreateSourcesAsync());
        FillValid(form);
        bool? finished = null;
        form.Finished += saved => finished = saved;

        form.CancelCommand.Execute(null);

        Assert.False(finished);
        Assert.Empty((await _store.LoadAsync()).Sources);
    }

    [Fact]
    public async Task Test_Success_ShowsTheCountAndEachPicture()
    {
        var form = new SourceFormViewModel(await CreateSourcesAsync());
        form.Url = "https://feeds.test/lakes.json";

        await form.TestCommand.ExecuteAsync(null);

        Assert.Equal(
            "Success \u2014 2 image(s) found.\n\nTitle: Lake\nImage: https://img.test/a.jpg\n\nTitle: Hill\nImage: https://img.test/b.jpg",
            form.TestResultText);
        Assert.False(form.HasError);
        Assert.Equal("Test", form.TestButtonText);
    }

    [Theory]
    [InlineData("", "$[*].imageUrl", "$[*].title", "URL is required to test the source.")]
    [InlineData("http://feeds.test/f", "$[*].imageUrl", "$[*].title", "URL must use HTTPS.")]
    [InlineData("https://feeds.test/f", "", "$[*].title", "Image URL JPath is required.")]
    [InlineData("https://feeds.test/f", "$[*].imageUrl", " ", "Title JPath is required.")]
    public async Task Test_Refused_ShowsTheSentence(string url, string imageUrlJPath, string titleJPath, string expected)
    {
        var form = new SourceFormViewModel(await CreateSourcesAsync())
        {
            Url = url,
            ImageUrlJPath = imageUrlJPath,
            TitleJPath = titleJPath,
        };

        await form.TestCommand.ExecuteAsync(null);

        Assert.Equal(expected, form.ErrorText);
        Assert.False(form.HasTestResult);
    }

    [Fact]
    public async Task Test_FeedFails_ShowsWhy_AndReplacesAnEarlierResult()
    {
        var form = new SourceFormViewModel(await CreateSourcesAsync(new FeedHandler("not json")));
        form.Url = "https://feeds.test/f";

        await form.TestCommand.ExecuteAsync(null);

        Assert.StartsWith("Test failed: Source '': response is not valid JSON.", form.ErrorText);
        Assert.False(form.HasTestResult);
    }

    [Fact]
    public async Task Test_ShowsTestingWhileItRuns_AndASecondTestReplacesTheFirst()
    {
        var handler = new FeedHandler(FeedHandler.TwoPictures) { Gate = new TaskCompletionSource() };
        var form = new SourceFormViewModel(await CreateSourcesAsync(handler));
        form.Url = "https://feeds.test/f";

        var first = form.TestCommand.ExecuteAsync(null);
        Assert.Equal("Testing…", form.TestButtonText);
        var second = form.TestCommand.ExecuteAsync(null);
        await first;
        // The first test was cancelled by the second: it shows nothing and leaves the label
        Assert.Equal("Testing…", form.TestButtonText);
        Assert.False(form.HasTestResult);
        Assert.False(form.HasError);

        handler.Gate.SetResult();
        await second;
        Assert.Equal("Test", form.TestButtonText);
        Assert.StartsWith("Success \u2014 2 image(s) found.", form.TestResultText);
    }

    [Fact]
    public async Task Cancel_StopsARunningTest()
    {
        var handler = new FeedHandler(FeedHandler.TwoPictures) { Gate = new TaskCompletionSource() };
        var form = new SourceFormViewModel(await CreateSourcesAsync(handler));
        form.Url = "https://feeds.test/f";

        var test = form.TestCommand.ExecuteAsync(null);
        form.CancelCommand.Execute(null);
        await test;

        Assert.Equal("Test", form.TestButtonText);
        Assert.False(form.HasTestResult);
        Assert.False(form.HasError);
    }

    // A sources service whose every write fails.
    private sealed class FailingSources : IWallpaperSources
    {
        private readonly Exception _error;

        public FailingSources(Exception error) => _error = error;

        public event Action? Changed { add { } remove { } }

        public Task<IReadOnlyList<SourceFields>> ListAsync() => Task.FromResult<IReadOnlyList<SourceFields>>([]);

        public Task<SourceProblem?> AddAsync(SourceFields fields) => throw _error;

        public Task<SourceProblem?> ReplaceAsync(string name, SourceFields fields) => throw _error;

        public Task DeleteAsync(string name) => throw _error;

        public Task SetEnabledAsync(string name, bool isEnabled) => throw _error;

        public Task<SourceTestOutcome> TestAsync(SourceFields fields, CancellationToken cancellationToken) => throw _error;
    }
}

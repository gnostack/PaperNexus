using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The phone's Sources tab: the rows, the empty sentence, the form Add and edit open, the switch
// and delete saving at once with the desktop's sentences, Download now's sentences, and the
// rows following changes made anywhere.
public class SourcesViewModelTests : IDisposable
{
    private readonly SettingsStore _store = TestHelpers.CreateSettingsStore();
    private readonly FakeDownloader _downloader = new();

    public void Dispose() => TestHelpers.DeleteSettingsStore(_store);

    private async Task<WallpaperSources> SourcesAsync(params WallpaperSource[] sources)
    {
        await _store.WriteAsync(new WallpaperNexusSettings { Sources = [.. sources] });
        var wallpaperSources = TestHelpers.CreateWallpaperSources(_store, new FeedHandler(FeedHandler.TwoPictures));
        return wallpaperSources;
    }

    private SourcesViewModel Create(IWallpaperSources sources)
    {
        return TestHelpers.WithoutSynchronizationContext(
            () => new SourcesViewModel(sources, _downloader, NullLogger<SourcesViewModel>.Instance));
    }

    private static WallpaperSource Source(string name, bool isEnabled = true) => new()
    {
        Name = name,
        Url = $"https://{name.ToLowerInvariant()}.test/feed",
        IsEnabled = isEnabled,
    };

    [Fact]
    public async Task Lists_TheSourcesWithTheirSwitches()
    {
        using var vm = Create(await SourcesAsync(Source("One"), Source("Two", isEnabled: false)));

        Assert.False(vm.IsEmpty);
        await vm.EnsureLoadedAsync();

        Assert.Equal(["One", "Two"], vm.Items.Select(i => i.Name));
        Assert.True(vm.Items[0].IsEnabled);
        Assert.False(vm.Items[1].IsEnabled);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task NoSources_ShowsTheEmptySentence()
    {
        using var vm = Create(await SourcesAsync());

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.Equal("No sources yet. Tap Add to create one.", vm.EmptyText);
    }

    [Fact]
    public async Task Add_OpensAnEmptyForm_SavingItAddsTheRowAndConfirms()
    {
        using var vm = Create(await SourcesAsync(Source("One")));
        await vm.LoadAsync();

        vm.AddCommand.Execute(null);
        var form = vm.Form;
        Assert.NotNull(form);
        Assert.True(vm.IsFormOpen);
        Assert.Equal("Add Wallpaper Source", form.Title);
        form.Name = "Lakes";
        form.Url = "https://lakes.test/feed";
        await form.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.Form);
        await WaitUntil(() => vm.Items.Count == 2);
        Assert.Equal(["One", "Lakes"], vm.Items.Select(i => i.Name));
        Assert.Equal("Settings saved.", vm.Status.Message);
        Assert.True(vm.Status.IsSuccess);
    }

    [Fact]
    public async Task Add_RefusedSave_KeepsTheFormOpenWithWhatWasTyped()
    {
        using var vm = Create(await SourcesAsync(Source("One")));
        await vm.LoadAsync();

        vm.AddCommand.Execute(null);
        var form = vm.Form;
        form.Name = "one";
        form.Url = "https://other.test/feed";
        await form.SaveCommand.ExecuteAsync(null);

        Assert.Same(form, vm.Form);
        Assert.Equal("A source named one already exists.", form.ErrorText);
        Assert.Single(vm.Items);
        Assert.False(vm.Status.IsShown);
    }

    [Fact]
    public async Task Edit_OpensTheFormFilledWithTheRow_SavingReplacesIt()
    {
        using var vm = Create(await SourcesAsync(Source("One"), Source("Two")));
        await vm.LoadAsync();
        var row = vm.Items[1];

        row.EditCommand.Execute(null);
        var form = vm.Form;
        Assert.Equal("Edit Wallpaper Source", form.Title);
        Assert.Equal("Two", form.Name);
        Assert.Equal("https://two.test/feed", form.Url);
        form.Name = "Rivers";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Settings saved.", vm.Status.Message);

        // The rows are listed again on a background thread here (the tests have no screen
        // thread): a renamed source's row is inserted before the old one is removed, so the list
        // is read only as a whole and once it has settled
        await WaitUntil(() => NamesOf(vm) is ["One", "Rivers"]);
    }

    [Fact]
    public async Task Cancel_ClosesTheFormAndSavesNothing()
    {
        using var vm = Create(await SourcesAsync(Source("One")));
        await vm.LoadAsync();

        vm.AddCommand.Execute(null);
        vm.Form.Name = "Lakes";
        vm.Form.CancelCommand.Execute(null);

        Assert.Null(vm.Form);
        Assert.Single((await _store.LoadAsync()).Sources);
        Assert.False(vm.Status.IsShown);
    }

    [Fact]
    public async Task OneFormAtATime()
    {
        using var vm = Create(await SourcesAsync(Source("One")));
        await vm.LoadAsync();

        vm.AddCommand.Execute(null);
        var first = vm.Form;
        vm.Items[0].EditCommand.Execute(null);

        Assert.Same(first, vm.Form);
    }

    [Fact]
    public async Task Switch_SavesAtOnce()
    {
        using var vm = Create(await SourcesAsync(Source("One")));
        await vm.LoadAsync();

        vm.Items[0].IsEnabled = false;
        await WaitUntil(() => vm.Status.IsShown);

        Assert.False((await _store.LoadAsync()).Sources.Single().IsEnabled);
        Assert.Equal("Settings saved.", vm.Status.Message);
        Assert.False(vm.Items[0].IsEnabled);
    }

    [Fact]
    public async Task Switch_FailedSave_PutsTheSwitchBack()
    {
        var sources = new FailingWrites(await SourcesAsync(Source("One")));
        using var vm = Create(sources);
        await vm.LoadAsync();

        vm.Items[0].IsEnabled = false;
        await WaitUntil(() => vm.Status.IsShown);

        Assert.True(vm.Items[0].IsEnabled);
        Assert.Equal("Error saving settings: The disk is full.", vm.Status.Message);
        Assert.True(vm.Status.IsError);
    }

    [Fact]
    public async Task Delete_RemovesTheRowWithoutAsking()
    {
        using var vm = Create(await SourcesAsync(Source("One"), Source("Two")));
        await vm.LoadAsync();

        await vm.Items[0].DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["Two"], vm.Items.Select(i => i.Name));
        Assert.Equal(["Two"], (await _store.LoadAsync()).Sources.Select(s => s.Name));
        Assert.Equal("Settings saved.", vm.Status.Message);
    }

    [Fact]
    public async Task ChangeMadeElsewhere_ShowsHere()
    {
        var sources = await SourcesAsync(Source("One"));
        using var vm = Create(sources);
        await vm.LoadAsync();
        var row = vm.Items[0];

        await sources.SetEnabledAsync(new SourceKey("One", 0), false);
        await sources.AddAsync(new SourceFields("Two", "https://two.test/feed", "$[*].imageUrl", "$[*].title", "", true));
        await WaitUntil(() => vm.Items.Count == 2);

        Assert.Same(row, vm.Items[0]);
        Assert.False(row.IsEnabled);
        Assert.Equal("Two", vm.Items[1].Name);
    }

    // An older file with "Twin" and "twin": the second row's buttons change the second source and
    // leave the first untouched.
    private static WallpaperSource[] Twins() =>
    [
        new WallpaperSource { Name = "Twin", Url = "https://first.test/feed" },
        new WallpaperSource { Name = "twin", Url = "https://second.test/feed" },
    ];

    [Fact]
    public async Task Twins_DeleteOnTheSecondRow_DeletesTheSecondSource()
    {
        using var vm = Create(await SourcesAsync(Twins()));
        await vm.LoadAsync();

        await vm.Items[1].DeleteCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.Items.Count == 1);

        Assert.Equal(["https://first.test/feed"], (await _store.LoadAsync()).Sources.Select(s => s.Url));
        Assert.Equal("https://first.test/feed", vm.Items.Single().Url);
    }

    [Fact]
    public async Task Twins_SwitchOnTheSecondRow_SwitchesTheSecondSource()
    {
        using var vm = Create(await SourcesAsync(Twins()));
        await vm.LoadAsync();

        vm.Items[1].IsEnabled = false;
        await WaitUntil(() => vm.Status.IsShown);

        var saved = (await _store.LoadAsync()).Sources;
        Assert.True(saved[0].IsEnabled);
        Assert.False(saved[1].IsEnabled);
        Assert.True(vm.Items[0].IsEnabled);
        Assert.False(vm.Items[1].IsEnabled);
    }

    [Fact]
    public async Task Twins_EditOnTheSecondRow_KeepingItsName_EditsTheSecondSource()
    {
        using var vm = Create(await SourcesAsync(Twins()));
        await vm.LoadAsync();

        vm.Items[1].EditCommand.Execute(null);
        var form = vm.Form;
        Assert.Equal("twin", form.Name);
        Assert.Equal("https://second.test/feed", form.Url);
        form.Url = "https://edited.test/feed";
        await form.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.Form);
        var saved = (await _store.LoadAsync()).Sources;
        Assert.Equal(["Twin", "twin"], saved.Select(s => s.Name));
        Assert.Equal(["https://first.test/feed", "https://edited.test/feed"], saved.Select(s => s.Url));
        await WaitUntil(() => vm.Items[1].Url == "https://edited.test/feed");
        Assert.Equal("https://first.test/feed", vm.Items[0].Url);
    }

    [Theory]
    [InlineData(2, 0, "Download complete.", StatusTone.Success)]
    [InlineData(3, 2, "Download failed for 2 of 3 sources.", StatusTone.Error)]
    [InlineData(2, 2, "Download failed: check your connection.", StatusTone.Error)]
    [InlineData(0, 0, "No enabled sources to download from.", StatusTone.Neutral)]
    public async Task DownloadNow_SaysWhatHappened(int tried, int failed, string expected, StatusTone tone)
    {
        using var vm = Create(await SourcesAsync(Source("One")));
        _downloader.Outcome = new DownloadOutcome(tried, failed);
        _downloader.Gate = new TaskCompletionSource();

        var download = vm.DownloadNowCommand.ExecuteAsync(null);
        Assert.Equal("Downloading wallpapers...", vm.Status.Message);
        Assert.False(vm.DownloadNowCommand.CanExecute(null));
        _downloader.Gate.SetResult();
        await download;

        Assert.Equal(expected, vm.Status.Message);
        Assert.Equal(tone, vm.Status.Tone);
    }

    [Fact]
    public async Task DownloadNow_RunCannotStart_SaysWhy()
    {
        using var vm = Create(await SourcesAsync(Source("One")));
        _downloader.Error = new IOException("The disk is full.");

        await vm.DownloadNowCommand.ExecuteAsync(null);

        Assert.Equal("Download failed: The disk is full.", vm.Status.Message);
        Assert.True(vm.Status.IsError);
    }

    // The rows' names, or none while another thread is changing the list.
    private static string[] NamesOf(SourcesViewModel vm)
    {
        try
        {
            return vm.Items.Select(i => i.Name).ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not reached.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeDownloader : IDownloadWallpapers
    {
        // No test here runs the scheduled download
        public Task<DownloadOutcome> DownloadDueAsync() =>
            throw new NotSupportedException("The scheduled download is not part of this test.");

        public DownloadOutcome Outcome { get; set; } = new(1, 0);
        public TaskCompletionSource? Gate { get; set; }
        public Exception? Error { get; set; }

        public event Action? WallpapersDownloaded { add { } remove { } }

        public async Task<DownloadOutcome> DownloadAllAsync()
        {
            if (Gate is not null)
                await Gate.Task;
            if (Error is not null)
                throw Error;
            return Outcome;
        }
    }

    // The real sources for reading, with every write failing.
    private sealed class FailingWrites : IWallpaperSources
    {
        private readonly IWallpaperSources _inner;

        public FailingWrites(IWallpaperSources inner) => _inner = inner;

        public event Action? Changed { add { } remove { } }

        public Task<IReadOnlyList<ListedSource>> ListAsync() => _inner.ListAsync();

        public Task<SourceProblem?> AddAsync(SourceFields fields) => Task.FromException<SourceProblem?>(new IOException("The disk is full."));

        public Task<SourceProblem?> ReplaceAsync(SourceKey key, SourceFields fields) => Task.FromException<SourceProblem?>(new IOException("The disk is full."));

        public Task DeleteAsync(SourceKey key) => Task.FromException(new IOException("The disk is full."));

        public Task SetEnabledAsync(SourceKey key, bool isEnabled) => Task.FromException(new IOException("The disk is full."));

        public Task<SourceTestOutcome> TestAsync(SourceFields fields, CancellationToken cancellationToken) => _inner.TestAsync(fields, cancellationToken);
    }
}

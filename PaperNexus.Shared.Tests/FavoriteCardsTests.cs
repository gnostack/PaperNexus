using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// Tests for the Favorites tab cards: one GalleryItem per favorite path, kept in step
// with settings.json when a favorite is added or removed.
[Collection("Wallpaper")]
public class FavoriteCardsTests : IDisposable
{
    private readonly SettingsStore _store;

    public FavoriteCardsTests()
    {
        TestHelpers.Cleanup();
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.Cleanup();
        TestHelpers.DeleteSettingsStore(_store);
    }

    // The commands end by holding a status message on screen for a few seconds, so the
    // tests wait for the card list to change instead of for the command to return.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition());
    }

    [Fact]
    public async Task LoadAsync_CreatesOneCardPerFavorite()
    {
        var settings = await _store.LoadAsync();
        settings.FavoriteWallpapers = ["/tmp/Mountain Lake - abc.png", "/tmp/Desert - def.jpg"];
        await _store.WriteAsync(settings);
        var vm = TestHelpers.CreateViewModel(_store);

        await vm.LoadAsync();

        var paths = vm.FavoriteWallpapers.Select(f => f.FilePath).ToList();
        Assert.Equal(["/tmp/Mountain Lake - abc.png", "/tmp/Desert - def.jpg"], paths);
        Assert.Equal("Mountain Lake", vm.FavoriteWallpapers[0].DisplayName);
        vm.Cleanup();
    }

    [Fact]
    public async Task RemoveOnCard_RemovesCardAndPersistedFavorite()
    {
        var settings = await _store.LoadAsync();
        settings.FavoriteWallpapers = ["/tmp/a.png", "/tmp/b.png"];
        await _store.WriteAsync(settings);
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();
        var card = vm.FavoriteWallpapers[0];

        // The card's favorite action is its "Remove from favorites" button.
        card.ToggleFavoriteCommand.Execute(null);
        await WaitForAsync(() => vm.FavoriteWallpapers.Count == 1);

        Assert.Equal("/tmp/b.png", vm.FavoriteWallpapers[0].FilePath);
        var saved = await _store.LoadAsync();
        Assert.Equal(["/tmp/b.png"], saved.FavoriteWallpapers);
        vm.Cleanup();
    }

    [Fact]
    public async Task ToggleFavoriteOnCurrentWallpaper_AddsThenRemovesCard()
    {
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();
        vm.CurrentWallpaperPath = "/tmp/current.png";

        // The library's change notice can add the card before the command has finished, so
        // the first tap is awaited: a second tap on a running command is ignored.
        await vm.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.Single(vm.FavoriteWallpapers);
        Assert.Equal("/tmp/current.png", vm.FavoriteWallpapers[0].FilePath);

        vm.ToggleFavoriteCommand.Execute(null);
        await WaitForAsync(() => vm.FavoriteWallpapers.Count == 0);
        var saved = await _store.LoadAsync();
        Assert.Empty(saved.FavoriteWallpapers);
        vm.Cleanup();
    }
}

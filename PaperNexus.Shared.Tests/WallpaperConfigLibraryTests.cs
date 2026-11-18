using PaperNexus.Core;
using PaperNexus.ViewModels;
using Xunit;

namespace PaperNexus.Tests;

// The desktop settings window's gallery, favorites and delete commands, now going through the
// shared picture library: the same messages as before, the one delete rule, and cards that
// follow changes made elsewhere in the window.
[Collection("Wallpaper")]
public class WallpaperConfigLibraryTests : IDisposable
{
    private readonly string _folder;
    private readonly SettingsStore _store;

    public WallpaperConfigLibraryTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"PaperNexus_Window_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        TestHelpers.Cleanup();
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    private string AddPicture(string name, int minutesAgo = 0)
    {
        var path = Path.Combine(_folder, name);
        TestHelpers.CreateSmallPng(path);
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-minutesAgo));
        return path;
    }

    // The window's commands hold their message for three seconds before returning, so the
    // tests wait for the message rather than for the command.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition());
    }

    private async Task<WallpaperConfigViewModel> OpenWindowAsync(string current)
    {
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: current);
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();
        vm.LoadGalleryCommand.Execute(null);
        await WaitForAsync(() => vm.StatusMessage.EndsWith("loaded."));
        return vm;
    }

    [Fact]
    public async Task LoadGallery_NewestFirst_SaysHowManyLoaded()
    {
        var a = AddPicture("A - a.png", minutesAgo: 5);
        var b = AddPicture("B - b.png", minutesAgo: 1);
        var vm = await OpenWindowAsync(a);

        Assert.Equal([b, a], vm.GalleryItems.Select(g => g.FilePath));
        Assert.Equal("✓ 2 images loaded.", vm.StatusMessage);
        vm.Cleanup();
    }

    [Fact]
    public async Task LoadGallery_FolderMissing_SaysSo()
    {
        await TestHelpers.WriteSettingsAsync(_store, Path.Combine(_folder, "missing"));
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();

        vm.LoadGalleryCommand.Execute(null);

        await WaitForAsync(() => vm.StatusMessage == "✗ Wallpapers folder not configured or not found.");
        vm.Cleanup();
    }

    // The gallery heart shows no message, as before; the card and the Favorites tab follow.
    [Fact]
    public async Task GalleryHeart_AddsTheFavoriteCard_WithoutAMessage()
    {
        var a = AddPicture("A - a.png");
        var b = AddPicture("B - b.png");
        var vm = await OpenWindowAsync(a);
        await WaitForAsync(() => vm.StatusMessage == string.Empty);
        var card = vm.GalleryItems.Single(g => g.FilePath == b);

        await card.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.True(card.IsFavorite);
        Assert.Equal([b], vm.FavoriteWallpapers.Select(f => f.FilePath));
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Equal([b], (await _store.LoadAsync()).FavoriteWallpapers);
        vm.Cleanup();
    }

    // Removing a favorite on its Favorites card now also clears the gallery card's heart.
    [Fact]
    public async Task RemovingAFavoriteCard_ClearsTheGalleryHeart()
    {
        var a = AddPicture("A - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: a);
        await _store.UpdateAsync(latest => latest.FavoriteWallpapers.Add(a));
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();
        vm.LoadGalleryCommand.Execute(null);
        await WaitForAsync(() => vm.GalleryItems.Count == 1);
        Assert.True(vm.GalleryItems[0].IsFavorite);
        Assert.True(vm.IsCurrentWallpaperFavorited);

        vm.FavoriteWallpapers[0].ToggleFavoriteCommand.Execute(null);

        await WaitForAsync(() => vm.StatusMessage == "✓ Removed from favorites.");
        Assert.False(vm.GalleryItems[0].IsFavorite);
        Assert.False(vm.IsCurrentWallpaperFavorited);
        Assert.Empty(vm.FavoriteWallpapers);
        vm.Cleanup();
    }

    [Fact]
    public async Task GalleryBan_FlagsTheCard_WithoutAMessage()
    {
        var a = AddPicture("A - a.png");
        var b = AddPicture("B - b.png");
        var vm = await OpenWindowAsync(a);
        await WaitForAsync(() => vm.StatusMessage == string.Empty);
        var card = vm.GalleryItems.Single(g => g.FilePath == b);

        await card.ToggleBanCommand.ExecuteAsync(null);

        Assert.True(card.IsBanned);
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Equal([b], (await _store.LoadAsync()).BannedWallpapers);
        vm.Cleanup();
    }

    [Fact]
    public async Task GalleryDelete_TheCurrent_SwitchesAndSaysWhich()
    {
        var a = AddPicture("A - a.png");
        var b = AddPicture("B - b.png");
        var vm = await OpenWindowAsync(b);

        vm.GalleryItems.Single(g => g.FilePath == b).DeleteCommand.Execute(null);

        await WaitForAsync(() => vm.StatusMessage == "✓ Deleted and switched to: A");
        Assert.Equal(a, vm.CurrentWallpaperPath);
        Assert.Equal([a], vm.GalleryItems.Select(g => g.FilePath));
        vm.Cleanup();
    }

    // The window's own delete removes the picture from favorites and bans too (one delete rule
    // everywhere) and from the gallery, then switches to the next.
    [Fact]
    public async Task DeleteCurrentWallpaper_RemovesFavoriteAndBan_ThenSwitches()
    {
        var a = AddPicture("A - a.png");
        var b = AddPicture("B - b.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: b);
        await _store.UpdateAsync(latest =>
        {
            latest.FavoriteWallpapers.Add(b);
            latest.BannedWallpapers.Add(b);
        });
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();
        vm.LoadGalleryCommand.Execute(null);
        await WaitForAsync(() => vm.GalleryItems.Count == 2);

        vm.DeleteCurrentWallpaperCommand.Execute(null);

        await WaitForAsync(() => vm.StatusMessage == "✓ Deleted and switched to: A");
        var saved = await _store.LoadAsync();
        Assert.False(File.Exists(b));
        Assert.Empty(saved.FavoriteWallpapers);
        Assert.Empty(saved.BannedWallpapers);
        Assert.Empty(vm.FavoriteWallpapers);
        Assert.Equal([a], vm.GalleryItems.Select(g => g.FilePath));
        vm.Cleanup();
    }

    [Fact]
    public async Task DeleteCurrentWallpaper_TheLast_SaysNoMoreInFolder()
    {
        var only = AddPicture("Only - a.png");
        await TestHelpers.WriteSettingsAsync(_store, _folder, currentWallpaperPath: only);
        var vm = TestHelpers.CreateViewModel(_store);
        await vm.LoadAsync();

        vm.DeleteCurrentWallpaperCommand.Execute(null);

        await WaitForAsync(() => vm.StatusMessage == "✓ Wallpaper deleted. No more wallpapers in folder.");
        Assert.Equal("(none)", vm.CurrentWallpaperName);
        vm.Cleanup();
    }
}

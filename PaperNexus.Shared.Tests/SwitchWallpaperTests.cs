using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// No-op implementation so tests don't call the real Windows API
internal sealed class NoOpWallpaperApplier : IWallpaperApplier
{
    public static readonly NoOpWallpaperApplier Instance = new();
    public bool SetWallpaper(string wallpaperPath) => true;
    public void ApplyFillStyle(WallpaperFillStyle style) { }
}

[Collection("Wallpaper")]
public class SwitchWallpaperTests : IAsyncLifetime, IDisposable
{
    private readonly string _wallpaperDir;
    private readonly SettingsStore _store;

    public SwitchWallpaperTests()
    {
        _wallpaperDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_wallpaperDir);
        _store = TestHelpers.CreateSettingsStore();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        TestHelpers.Cleanup();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_wallpaperDir)) Directory.Delete(_wallpaperDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    [Fact]
    public async Task SwitchToNext_DeletesStaleJpg()
    {
        // Arrange: one small wallpaper + a stale current.jpg
        var wallpaperPath = Path.Combine(_wallpaperDir, "test-wallpaper.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        File.WriteAllBytes(TestHelpers.JpgPath, [0xFF, 0xD8, 0xFF]); // fake JPEG marker
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(TestHelpers.PngPath), "current.png should exist after switch");
        Assert.False(File.Exists(TestHelpers.JpgPath), "stale current.jpg should be deleted");
    }

    [Fact]
    public async Task SwitchToNext_DeletesStalePng_WhenJpegFallback()
    {
        // Arrange: one oversized wallpaper + a stale current.png
        var wallpaperPath = Path.Combine(_wallpaperDir, "huge-wallpaper.png");
        TestHelpers.CreateOversizedPng(wallpaperPath);
        File.WriteAllBytes(TestHelpers.PngPath, [0x89, 0x50, 0x4E, 0x47]); // fake PNG marker
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(TestHelpers.JpgPath), "current.jpg should exist after JPEG fallback");
        Assert.False(File.Exists(TestHelpers.PngPath), "stale current.png should be deleted");
    }

    [Fact]
    public async Task SwitchToNext_NoCounterpart_DoesNotThrow()
    {
        // Arrange: one wallpaper, no pre-existing current files
        var wallpaperPath = Path.Combine(_wallpaperDir, "test-wallpaper.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        TestHelpers.Cleanup(); // ensure no current.png/jpg
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert
        Assert.NotNull(result);
        var pngExists = File.Exists(TestHelpers.PngPath);
        var jpgExists = File.Exists(TestHelpers.JpgPath);
        Assert.True(pngExists || jpgExists, "At least one current file should exist");
        Assert.False(pngExists && jpgExists, "Only one format should exist at a time");
    }

    [Fact]
    public async Task SwitchToNext_FolderDoesNotExist_ReturnsNull()
    {
        // Arrange: point settings at a folder that was never created
        var missingFolder = Path.Combine(Path.GetTempPath(), $"PaperNexus_Missing_{Guid.NewGuid():N}");
        await TestHelpers.WriteSettingsAsync(_store, missingFolder);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act — should not throw DirectoryNotFoundException
        var result = await switcher.SwitchToNextAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SwitchToNext_AllWallpapersBanned_ReturnsNull()
    {
        // Arrange: one wallpaper, but it is in the banned list
        var wallpaperPath = Path.Combine(_wallpaperDir, "banned.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            BannedWallpapers = [wallpaperPath],
            Sources = [],
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert: banned file excluded → candidate pool is empty → null
        Assert.Null(result);
    }

    // --- Sequential ordering tests ---

    [Fact]
    public async Task SwitchToNext_Alphabetical_AdvancesToNextFile()
    {
        // Arrange: three wallpapers named so they sort a < b < c.
        // Current is 'b'; next alphabetically is 'c'.
        var pathA = Path.Combine(_wallpaperDir, "a_wall.png");
        var pathB = Path.Combine(_wallpaperDir, "b_wall.png");
        var pathC = Path.Combine(_wallpaperDir, "c_wall.png");
        TestHelpers.CreateSmallPng(pathA, r: 100);
        TestHelpers.CreateSmallPng(pathB, r: 120);
        TestHelpers.CreateSmallPng(pathC, r: 140);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings { Order = SlideshowOrder.Alphabetical, Enabled = false },
            CurrentWallpaperPath = pathB,
            Sources = [],
            AnnotateWallpaper = false,
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert: 'b' → 'c' in alphabetical order
        Assert.NotNull(result);
        Assert.Equal(pathC, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwitchToNext_Alphabetical_WrapsAroundToFirstFile()
    {
        // Arrange: three wallpapers. Current is the last one alphabetically ('c').
        // Wrap-around should advance to 'a'.
        var pathA = Path.Combine(_wallpaperDir, "a_wall.png");
        var pathB = Path.Combine(_wallpaperDir, "b_wall.png");
        var pathC = Path.Combine(_wallpaperDir, "c_wall.png");
        TestHelpers.CreateSmallPng(pathA, r: 100);
        TestHelpers.CreateSmallPng(pathB, r: 120);
        TestHelpers.CreateSmallPng(pathC, r: 140);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings { Order = SlideshowOrder.Alphabetical, Enabled = false },
            CurrentWallpaperPath = pathC,
            Sources = [],
            AnnotateWallpaper = false,
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert: last file wraps around to the first
        Assert.NotNull(result);
        Assert.Equal(pathA, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwitchToNext_Alphabetical_MissingCurrentPath_StartsFromFirst()
    {
        // Arrange: three wallpapers. Current points to a file that no longer exists.
        // Expected: index == -1 → (−1 + 1) % 3 == 0 → first file alphabetically.
        var pathA = Path.Combine(_wallpaperDir, "a_wall.png");
        var pathB = Path.Combine(_wallpaperDir, "b_wall.png");
        var pathC = Path.Combine(_wallpaperDir, "c_wall.png");
        TestHelpers.CreateSmallPng(pathA, r: 100);
        TestHelpers.CreateSmallPng(pathB, r: 120);
        TestHelpers.CreateSmallPng(pathC, r: 140);
        TestHelpers.Cleanup();

        var missingPath = Path.Combine(_wallpaperDir, "deleted.png");
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings { Order = SlideshowOrder.Alphabetical, Enabled = false },
            CurrentWallpaperPath = missingPath, // not in the folder
            Sources = [],
            AnnotateWallpaper = false,
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert: missing current → index −1 → starts from index 0 (alphabetically first)
        Assert.NotNull(result);
        Assert.Equal(pathA, result, StringComparer.OrdinalIgnoreCase);
    }

    // --- SwitchToRandomAsync tests ---

    [Fact]
    public async Task SwitchToRandom_FolderDoesNotExist_ReturnsNull()
    {
        var missingFolder = Path.Combine(Path.GetTempPath(), $"PaperNexus_Missing_{Guid.NewGuid():N}");
        await TestHelpers.WriteSettingsAsync(_store, missingFolder);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        var result = await switcher.SwitchToRandomAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SwitchToRandom_AllWallpapersBanned_ReturnsNull()
    {
        var wallpaperPath = Path.Combine(_wallpaperDir, "banned.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            BannedWallpapers = [wallpaperPath],
            Sources = [],
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        var result = await switcher.SwitchToRandomAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SwitchToRandom_MultipleWallpapers_NeverReturnsCurrentWallpaper()
    {
        // Arrange: two wallpapers. Current is one of them.
        // With exactly two candidates, the non-current one is the only valid choice.
        var pathA = Path.Combine(_wallpaperDir, "wall_a.png");
        var pathB = Path.Combine(_wallpaperDir, "wall_b.png");
        TestHelpers.CreateSmallPng(pathA, r: 100);
        TestHelpers.CreateSmallPng(pathB, r: 200);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings { Enabled = false },
            CurrentWallpaperPath = pathA,
            Sources = [],
            AnnotateWallpaper = false,
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act: run many times to ensure randomness never returns the current wallpaper
        for (var i = 0; i < 10; i++)
        {
            TestHelpers.Cleanup();
            // Re-write settings to keep CurrentWallpaperPath pointing at pathA across iterations
            await _store.SaveAsync(settings);
            var result = await switcher.SwitchToRandomAsync();

            Assert.NotNull(result);
            // With two files and current == pathA, random must always pick pathB
            Assert.Equal(pathB, result, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SwitchToRandom_SingleWallpaper_ReturnsThatWallpaper()
    {
        // Arrange: only one wallpaper. With one candidate the "exclude current" guard
        // falls back to the full list, so the single file must still be returned.
        var pathA = Path.Combine(_wallpaperDir, "sole.png");
        TestHelpers.CreateSmallPng(pathA);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings { Enabled = false },
            CurrentWallpaperPath = pathA,
            Sources = [],
            AnnotateWallpaper = false,
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        var result = await switcher.SwitchToRandomAsync();

        Assert.NotNull(result);
        Assert.Equal(pathA, result, StringComparer.OrdinalIgnoreCase);
    }

    // --- SwitchToSpecificAsync tests ---

    [Fact]
    public async Task SwitchToSpecific_ExistingFile_ReturnsPath()
    {
        var wallpaperPath = Path.Combine(_wallpaperDir, "specific.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        TestHelpers.Cleanup();
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        var result = await switcher.SwitchToSpecificAsync(wallpaperPath);

        Assert.NotNull(result);
        Assert.Equal(wallpaperPath, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwitchToSpecific_MissingFile_ReturnsNull()
    {
        // Arrange: path that does not exist on disk
        var missingPath = Path.Combine(_wallpaperDir, "nonexistent.png");
        TestHelpers.Cleanup();
        await TestHelpers.WriteSettingsAsync(_store, _wallpaperDir);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        var result = await switcher.SwitchToSpecificAsync(missingPath);

        Assert.Null(result);
    }

    // --- Annotation rendering tests ---

    [Fact]
    public async Task SwitchToNext_AnnotationEnabled_ReturnsNonNull()
    {
        // Arrange: one wallpaper with annotation enabled and valid settings.
        // Exercises the brush/pen creation and img.Clone annotation code path.
        var wallpaperPath = Path.Combine(_wallpaperDir, "annotated - test.png");
        TestHelpers.CreateSmallPng(wallpaperPath);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings { Order = SlideshowOrder.Alphabetical, Enabled = false },
            CurrentWallpaperPath = string.Empty,
            Sources = [],
            AnnotateWallpaper = true,
            Annotation = new AnnotationSettings
            {
                FontSize = 18,
                Color = "#F5F5F5",
                Position = AnnotationPosition.TopLeft,
                OutlineEnabled = true,
            },
        };
        await _store.SaveAsync(settings);

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);

        // Act
        var result = await switcher.SwitchToNextAsync();

        // Assert: annotation path should complete without error and return a valid wallpaper path
        Assert.NotNull(result);
    }

    // --- ApplyFavoritePriority / favorite weighting tests ---

    [Fact]
    public async Task SwitchToRandom_WithFavoritePriority_FavoritedFileSelectedMoreOften()
    {
        // Arrange: two wallpapers, one favorited with weight 10 (very high boost).
        // Over many iterations the favorite should win the overwhelming majority of picks.
        var pathFav = Path.Combine(_wallpaperDir, "favorite.png");
        var pathNorm = Path.Combine(_wallpaperDir, "normal.png");
        TestHelpers.CreateSmallPng(pathFav, r: 100);
        TestHelpers.CreateSmallPng(pathNorm, r: 200);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings
            {
                Enabled = false,
                FavoritePriorityEnabled = true,
                FavoritePriorityWeight = 10,
            },
            // Set current to normal so both are eligible candidates
            CurrentWallpaperPath = string.Empty,
            FavoriteWallpapers = [pathFav],
            Sources = [],
            AnnotateWallpaper = false,
        };

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);
        var favoritePicks = 0;
        const int iterations = 50;

        for (var i = 0; i < iterations; i++)
        {
            TestHelpers.Cleanup();
            await _store.SaveAsync(settings);
            var result = await switcher.SwitchToRandomAsync();
            Assert.NotNull(result);
            if (result.Equals(pathFav, StringComparison.OrdinalIgnoreCase))
                favoritePicks++;
            // Keep CurrentWallpaperPath empty so neither file is excluded each iteration
            settings.CurrentWallpaperPath = string.Empty;
        }

        // With weight 10 the favorite occupies 10/11 (~91%) of the pool.
        // Require at least 70% to avoid rare-but-possible test flakiness.
        Assert.True(favoritePicks >= (int)(iterations * 0.70),
            $"Favorite should be picked far more often with weight 10, but was only picked {favoritePicks}/{iterations} times.");
    }

    [Fact]
    public async Task SwitchToRandom_FavoritePriorityDisabled_DoesNotBiasSelection()
    {
        // Arrange: two wallpapers, one favorited but priority is disabled.
        // Both files should appear as equals in the pool — the test verifies the
        // non-favorite also gets picked at least once over many iterations.
        var pathFav = Path.Combine(_wallpaperDir, "favorite.png");
        var pathNorm = Path.Combine(_wallpaperDir, "normal.png");
        TestHelpers.CreateSmallPng(pathFav, r: 100);
        TestHelpers.CreateSmallPng(pathNorm, r: 200);
        TestHelpers.Cleanup();

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _wallpaperDir },
            Slideshow = new SlideshowSettings
            {
                Enabled = false,
                FavoritePriorityEnabled = false, // priority disabled
                FavoritePriorityWeight = 10,     // weight should be ignored
            },
            CurrentWallpaperPath = string.Empty,
            FavoriteWallpapers = [pathFav],
            Sources = [],
            AnnotateWallpaper = false,
        };

        var switcher = new SwitchWallpaper(NullLogger<SwitchWallpaper>.Instance, NoOpWallpaperApplier.Instance, _store);
        var normalPicks = 0;
        const int iterations = 40;

        for (var i = 0; i < iterations; i++)
        {
            TestHelpers.Cleanup();
            await _store.SaveAsync(settings);
            var result = await switcher.SwitchToRandomAsync();
            Assert.NotNull(result);
            if (result.Equals(pathNorm, StringComparison.OrdinalIgnoreCase))
                normalPicks++;
            settings.CurrentWallpaperPath = string.Empty;
        }

        // Without priority weighting both candidates are equally likely (50/50).
        // Require at least 5 picks of the normal file to prove it is not excluded.
        Assert.True(normalPicks >= 5,
            $"Normal wallpaper should be selected sometimes when priority is disabled, but was only picked {normalPicks}/{iterations} times.");
    }
}

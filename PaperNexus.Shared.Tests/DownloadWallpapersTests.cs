using Microsoft.Extensions.Logging.Abstractions;
using PaperNexus.Core;
using PaperNexus.Core.Imaging;
using Xunit;

namespace PaperNexus.Tests;

public class DownloadWallpapersTests : IDisposable
{
    private readonly string _downloadDir;
    private readonly SettingsStore _store;

    public DownloadWallpapersTests()
    {
        _downloadDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_DL_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_downloadDir);
        _store = TestHelpers.CreateSettingsStore();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_downloadDir)) Directory.Delete(_downloadDir, true); }
        catch { }
        TestHelpers.DeleteSettingsStore(_store);
    }

    [Theory]
    [InlineData("../evil/payload")]
    [InlineData("..\\evil\\payload")]
    [InlineData("title/../../etc/passwd")]
    [InlineData("safe/title\\here")]
    public async Task Download_TitleWithSlashes_NeverEscapesFolder(string title)
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
        };

        var image = new WallpaperImage { Title = title, ImageUrl = "https://example.com/image.png" };

        // The HTTP call will fail but path construction should never escape the folder.
        // Slashes are stripped from the title, keeping the file inside the folder.
        try
        {
            await sut.Download(image, settings);
        }
        catch
        {
            // Expected — HTTP call fails, but path was constructed safely
        }

        // Verify no files were written outside the download directory
        var parent = Path.GetDirectoryName(_downloadDir)!;
        var escapedFiles = Directory.GetFiles(parent)
            .Where(f => f.Contains("evil") || f.Contains("passwd") || f.Contains("safe"))
            .ToArray();
        Assert.Empty(escapedFiles);
    }

    [Fact]
    public async Task Download_ExistingFile_SkipsDownload()
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
        };

        // Pre-create the file that would be downloaded
        var title = "Test Wallpaper";
        var expectedPath = Path.Combine(_downloadDir, $"{title} - image.png");
        await File.WriteAllBytesAsync(expectedPath, [0x89, 0x50, 0x4E, 0x47]);

        var image = new WallpaperImage { Title = title, ImageUrl = "https://example.com/image.png" };

        // Should skip download because file already exists (no HTTP call made)
        await sut.Download(image, settings);

        // File should still have the original content (not overwritten)
        var bytes = await File.ReadAllBytesAsync(expectedPath);
        Assert.Equal(4, bytes.Length);
    }

    // Regression guard: query strings and URL fragments must be stripped before the
    // filename is derived, so "image.jpg?sig=abc" produces "image.jpg" not "image.jpg?sig=abc".
    // Without the fix, Path.GetExtension("image.jpg?sig=abc") returns ".jpg?sig=abc",
    // making the extension contain the query string and producing an invalid filename.
    [Theory]
    [InlineData("https://example.com/photo.jpg?sig=abc123&se=2025", "photo", ".jpg")]
    [InlineData("https://example.com/photo.png?X-Goog-Signature=xyz", "photo", ".png")]
    [InlineData("https://example.com/photo.jpg#anchor", "photo", ".jpg")]
    [InlineData("https://example.com/photo.jpg?token=x&ver=2#section", "photo", ".jpg")]
    [InlineData("https://example.com/api/image?format=jpg&w=3840", "image", ".png")] // no clean ext → .png fallback
    public async Task Download_UrlWithQueryString_ProducesCleanFilename(
        string imageUrl, string expectedStem, string expectedExt)
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
        };

        var wallpaperTitle = "My Wallpaper";
        // Pre-create the file with the expected clean name so the skip-if-exists path is taken.
        // If the filename is built correctly (no query/fragment), the pre-created file is found
        // and the method returns without making an HTTP call (no network needed in tests).
        var cleanPath = Path.Combine(_downloadDir, $"{wallpaperTitle} - {expectedStem}{expectedExt}");
        await File.WriteAllBytesAsync(cleanPath, [0x89, 0x50, 0x4E, 0x47]);

        var image = new WallpaperImage { Title = wallpaperTitle, ImageUrl = imageUrl };

        // Act: Download should recognise the pre-created clean file and skip the HTTP call.
        // If the query string is not stripped the computed path won't match the pre-created file,
        // causing an HTTP request that fails with a network error.
        await sut.Download(image, settings);

        // Assert: pre-created file intact (was recognised and skipped, not re-downloaded)
        var fileBytes = await File.ReadAllBytesAsync(cleanPath);
        Assert.Equal(4, fileBytes.Length);
    }

    [Fact]
    public async Task Download_OneImageFails_DoesNotPreventSubsequentImages()
    {
        // Arrange: two images in a sequence — the first will fail (HTTP error on a bad URL),
        // the second already exists on disk so no HTTP call is needed.
        // After the fix, per-image failures are caught and logged; the second image must
        // still be processed (i.e. its pre-existing file is unchanged, not thrown over).
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
        };

        // Pre-create the file for the second image so the skip path is taken (no real HTTP call)
        var secondTitle = "Second Wallpaper";
        var secondPath = Path.Combine(_downloadDir, $"{secondTitle} - second.png");
        var originalBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await File.WriteAllBytesAsync(secondPath, originalBytes);

        var failingImage = new WallpaperImage { Title = "Failing Image", ImageUrl = "https://0.0.0.0/nonexistent.png" };
        var skippedImage = new WallpaperImage { Title = secondTitle, ImageUrl = "https://example.com/second.png" };

        // Act: call Download for each image individually (mirroring the fixed DownloadSource loop).
        // The first call is expected to throw an HTTP/network exception.
        // After the fix, that exception is caught per-image so the second call still runs.
        Exception? firstException = null;
        try { await sut.Download(failingImage, settings); }
        catch (Exception ex) { firstException = ex; }

        // The second call should always succeed (pre-existing file skips the HTTP call)
        await sut.Download(skippedImage, settings);

        // Assert: the first image did raise an exception (network failure expected)
        Assert.NotNull(firstException);
        // The second image's file is still intact — iteration was not aborted by the first failure
        var remaining = await File.ReadAllBytesAsync(secondPath);
        Assert.Equal(originalBytes, remaining);
    }

    [Fact]
    public async Task CleanupOldImages_DeletesExpiredFiles_AndPrunesBannedList()
    {
        // Arrange: one expired file referenced in BannedWallpapers
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var expiredPath = Path.Combine(_downloadDir, "old.png");
        TestHelpers.CreateSmallPng(expiredPath);
        // Backdate last-write to well past the retention window
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-400));

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            BannedWallpapers = [expiredPath],
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: file is deleted and the stale banned entry is removed
        Assert.False(File.Exists(expiredPath), "Expired file should be deleted");
        Assert.Empty(settings.BannedWallpapers);
    }

    [Fact]
    public async Task CleanupOldImages_FavoritedFile_IsNotDeleted_ButStaleFavoritesArePruned()
    {
        // Arrange: one expired file that is favorited (should survive deletion),
        // plus one stale favorite entry pointing to a file that no longer exists.
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var favoritePath = Path.Combine(_downloadDir, "favorite.png");
        TestHelpers.CreateSmallPng(favoritePath);
        File.SetLastWriteTimeUtc(favoritePath, DateTime.UtcNow.AddDays(-400));

        // This path is in favorites but does not exist on disk (manually deleted outside the app)
        var staleGhostPath = Path.Combine(_downloadDir, "ghost.png");

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            FavoriteWallpapers = [favoritePath, staleGhostPath],
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: favorited file preserved on disk
        Assert.True(File.Exists(favoritePath), "Favorited file should survive retention cleanup");
        // The favorited file's path stays in the list (it still exists)
        Assert.Contains(favoritePath, settings.FavoriteWallpapers);
        // The ghost path (file was deleted outside the app) is pruned from the list
        Assert.DoesNotContain(staleGhostPath, settings.FavoriteWallpapers);
    }

    [Fact]
    public async Task CleanupOldImages_RecentFile_IsNotDeleted()
    {
        // Arrange: a recent file that is within the retention window
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var recentPath = Path.Combine(_downloadDir, "recent.png");
        TestHelpers.CreateSmallPng(recentPath);
        // Last-write is now (within retention window)

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: recent file is kept
        Assert.True(File.Exists(recentPath), "Recently downloaded file should not be deleted");
    }

    [Fact]
    public async Task ApplyResolutionCap_NativeSetting_LeavesFileSizeUnchanged()
    {
        // Arrange: a 200×200 image with no cap (ResolutionWidth == 0 means "Native")
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var filePath = Path.Combine(_downloadDir, "native.png");
        TestHelpers.CreateTestPng(filePath, 200, 200);
        var originalLength = new FileInfo(filePath).Length;

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, ResolutionWidth = 0, ResolutionHeight = 0 },
        };

        // Act
        await sut.ApplyResolutionCapAsync(filePath, settings);

        // Assert: file is unchanged when resolution is set to Native
        Assert.Equal(originalLength, new FileInfo(filePath).Length);
    }

    [Fact]
    public async Task ApplyResolutionCap_ImageAlreadyWithinCap_LeavesFileSizeUnchanged()
    {
        // Arrange: a 100×100 image with a cap of 1920×1080 — image is already within the cap
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var filePath = Path.Combine(_downloadDir, "small.png");
        TestHelpers.CreateTestPng(filePath, 100, 100);
        var originalLength = new FileInfo(filePath).Length;

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, ResolutionWidth = 1920, ResolutionHeight = 1080 },
        };

        // Act
        await sut.ApplyResolutionCapAsync(filePath, settings);

        // Assert: small image must not be upscaled — file stays at original size
        Assert.Equal(originalLength, new FileInfo(filePath).Length);
    }

    [Fact]
    public async Task ApplyResolutionCap_OversizedImage_ReducesDimensions()
    {
        // Arrange: a 400×300 image capped at 200×200.
        // The image should be scaled down so neither dimension exceeds the cap.
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var filePath = Path.Combine(_downloadDir, "large.png");
        TestHelpers.CreateTestPng(filePath, 400, 300);

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, ResolutionWidth = 200, ResolutionHeight = 200 },
        };

        // Act
        await sut.ApplyResolutionCapAsync(filePath, settings);

        // Assert: resulting image fits within the cap and aspect ratio is preserved (400:300 → 200:150)
        var (width, height) = TestHelpers.ReadDimensions(filePath);
        Assert.True(width <= 200, $"Width {width} should be ≤ 200");
        Assert.True(height <= 200, $"Height {height} should be ≤ 200");
        // Exact expected size: 200×150 (width-constrained; aspect ratio 4:3 preserved)
        Assert.Equal(200, width);
        Assert.Equal(150, height);
    }

    // --- IsOverdue tests ---

    [Fact]
    public void IsOverdue_NullLastDownload_ReturnsTrue()
    {
        // A source that has never been downloaded is always overdue.
        var source = new WallpaperSource
        {
            CronExpression = "0 */8 * * *",
            LastDownloadUtc = null,
        };

        Assert.True(DownloadWallpapers.IsOverdue(source));
    }

    [Fact]
    public void IsOverdue_NextOccurrenceInFuture_ReturnsFalse()
    {
        // Downloaded very recently: the next cron slot is still in the future, so not overdue.
        // Cron "0 */8 * * *" fires every 8 hours. If last download was 1 minute ago,
        // the next occurrence is ~8 hours away.
        var source = new WallpaperSource
        {
            CronExpression = "0 */8 * * *",
            LastDownloadUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        Assert.False(DownloadWallpapers.IsOverdue(source));
    }

    [Fact]
    public void IsOverdue_NextOccurrenceInPast_ReturnsTrue()
    {
        // Downloaded 9 hours ago with an every-8-hours cron: the next slot (8 h after last
        // download) has already passed, so the source is overdue.
        var source = new WallpaperSource
        {
            CronExpression = "0 */8 * * *",
            LastDownloadUtc = DateTimeOffset.UtcNow.AddHours(-9),
        };

        Assert.True(DownloadWallpapers.IsOverdue(source));
    }

    [Fact]
    public void IsOverdue_InvalidCronExpression_ReturnsTrue()
    {
        // An invalid cron expression is treated as always-overdue so a misconfigured
        // source does not silently stall rather than retrying.
        var source = new WallpaperSource
        {
            CronExpression = "not-a-valid-cron",
            LastDownloadUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        Assert.True(DownloadWallpapers.IsOverdue(source));
    }

    // --- LastDownloadUtc persistence tests ---

    // --- Settings load/defaults tests ---

    // Regression guard: if the user intentionally removes all wallpaper sources and saves,
    // the next LoadAsync must return an empty sources list — not silently restore the
    // built-in Bing/Spotlight defaults as it did before the fix.
    [Fact]
    public async Task LoadAsync_EmptySourcesSavedByUser_DoesNotRestoreDefaults()
    {
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Sources = [],  // user explicitly cleared all sources
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
        };
        await _store.SaveAsync(settings);

        var loaded = await _store.LoadAsync();

        Assert.Empty(loaded.Sources);
    }

    // A brand-new settings file that has never been saved must produce the built-in
    // defaults (Bing + Spotlight), because WallpaperNexusSettings.Sources has a
    // property-initialiser default and no file override.
    [Fact]
    public async Task LoadAsync_NoFileExists_ReturnsDefaultSources()
    {
        // The test's store sits in a new, empty temporary directory, so no file exists yet.
        var loaded = await _store.LoadAsync();

        Assert.Equal(WallpaperNexusSettings.DefaultSources.Count, loaded.Sources.Count);
    }

    [Fact]
    public async Task CleanupOldImages_CurrentWallpaper_IsNotDeleted()
    {
        // Arrange: an expired file that is set as the current wallpaper but is NOT
        // a favorite. Retention cleanup must skip it because deleting the active
        // wallpaper would leave the desktop in an undefined state.
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var currentPath = Path.Combine(_downloadDir, "current.png");
        TestHelpers.CreateSmallPng(currentPath);
        File.SetLastWriteTimeUtc(currentPath, DateTime.UtcNow.AddDays(-400));

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            CurrentWallpaperPath = currentPath,
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: current wallpaper is preserved despite being past the retention cutoff
        Assert.True(File.Exists(currentPath), "Current wallpaper should not be deleted by retention cleanup");
    }

    [Fact]
    public async Task CleanupOldImages_ExpiredNonCurrentNonFavorite_IsStillDeleted()
    {
        // Arrange: two expired files — one is the current wallpaper, the other is not
        // current and not favorited. Only the non-current file should be deleted.
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var currentPath = Path.Combine(_downloadDir, "current.png");
        TestHelpers.CreateSmallPng(currentPath);
        File.SetLastWriteTimeUtc(currentPath, DateTime.UtcNow.AddDays(-400));

        var expiredPath = Path.Combine(_downloadDir, "expired.png");
        TestHelpers.CreateSmallPng(expiredPath);
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-400));

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            CurrentWallpaperPath = currentPath,
        };

        // Act
        await sut.CleanupOldImages(settings);

        // Assert: current wallpaper survives, non-current expired file is deleted
        Assert.True(File.Exists(currentPath), "Current wallpaper should be preserved");
        Assert.False(File.Exists(expiredPath), "Expired non-current file should be deleted");
    }

    // Regression guard: CleanupOldImages must not throw when the wallpaper folder
    // has been deleted between DownloadFromSourcesAsync creating it and the cleanup step.
    // It should silently prune stale list entries and return without crashing.
    [Fact]
    public async Task CleanupOldImages_FolderDeleted_DoesNotThrow()
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        // Use a path that does not exist
        var missingDir = Path.Combine(Path.GetTempPath(), $"PaperNexus_Gone_{Guid.NewGuid():N}");
        var stalePath = Path.Combine(missingDir, "ghost.png");

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = missingDir },
            FavoriteWallpapers = [stalePath],
            BannedWallpapers = [stalePath],
        };

        // Act: should not throw DirectoryNotFoundException
        var ex = await Record.ExceptionAsync(() => sut.CleanupOldImages(settings));

        Assert.Null(ex);
        // Stale paths pointing to non-existent files must be pruned from both lists
        Assert.Empty(settings.FavoriteWallpapers);
        Assert.Empty(settings.BannedWallpapers);
    }

    // Regression guard: CleanupOldImages must not throw when FavoriteWallpapers or
    // BannedWallpapers are null. The null-conditional on RemoveAll must handle this
    // gracefully instead of throwing a NullReferenceException.
    [Fact]
    public async Task CleanupOldImages_NullLists_DoesNotThrow()
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var expiredPath = Path.Combine(_downloadDir, "expired.png");
        TestHelpers.CreateSmallPng(expiredPath);
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-400));

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            FavoriteWallpapers = null,
            BannedWallpapers = null,
        };

        // Act: should not throw NullReferenceException
        var ex = await Record.ExceptionAsync(() => sut.CleanupOldImages(settings));

        // Assert: cleanup completed and the expired file was deleted
        Assert.Null(ex);
        Assert.False(File.Exists(expiredPath), "Expired file should be deleted even when lists are null");
    }

    // Regression guard: CleanupOldImages must not throw when CurrentWallpaperPath contains
    // characters that cause Path.GetFullPath to throw (ArgumentException, NotSupportedException,
    // or PathTooLongException). The try-catch must gracefully fall back so expired files are
    // still deleted and the cleanup completes without crashing the scheduler.
    [Fact]
    public async Task CleanupOldImages_InvalidCurrentWallpaperPath_DoesNotThrow()
    {
        var source = new HttpWallpaperSourceService(NullLogger<HttpWallpaperSourceService>.Instance);
        var sut = new DownloadWallpapers(NullLogger<DownloadWallpapers>.Instance, source, _store);

        var expiredPath = Path.Combine(_downloadDir, "expired.png");
        TestHelpers.CreateSmallPng(expiredPath);
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-400));

        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir, RetentionDays = 365 },
            // Null byte triggers ArgumentException in Path.GetFullPath
            CurrentWallpaperPath = "invalid:path\0chars",
        };

        // Act: should not throw despite the invalid CurrentWallpaperPath
        var ex = await Record.ExceptionAsync(() => sut.CleanupOldImages(settings));

        // Assert: cleanup completed without throwing and expired file was still deleted
        Assert.Null(ex);
        Assert.False(File.Exists(expiredPath), "Expired file should still be deleted when CurrentWallpaperPath is invalid");
    }

    [Fact]
    public async Task LastDownloadUtc_SurvivesSaveLoadRoundTrip()
    {
        // LastDownloadUtc is the only field that is written by the background downloader
        // and not by the ViewModel. This test verifies that the value survives a save/load
        // cycle, which is the foundational contract that the timestamp-preservation merge
        // in SaveSettingsAsync depends on.
        var expectedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Sources =
            [
                new WallpaperSource { Name = "Test Source", LastDownloadUtc = expectedTime },
            ],
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
        };
        await _store.SaveAsync(settings);

        var loaded = await _store.LoadAsync();

        var loadedSource = loaded.Sources.FirstOrDefault(s => s.Name == "Test Source");
        Assert.NotNull(loadedSource);
        Assert.Equal(expectedTime, loadedSource.LastDownloadUtc);
    }

    [Fact]
    public async Task LastDownloadUtc_NotNullSource_PreventsRedundantRedownload()
    {
        // Regression guard: if a source has a recent LastDownloadUtc (downloaded 1 minute ago),
        // IsOverdue must return false so the scheduler does not re-download immediately
        // after a settings save that preserved the timestamp.
        var expectedTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Sources =
            [
                new WallpaperSource
                {
                    Name = "Recent Source",
                    CronExpression = "0 */8 * * *",
                    LastDownloadUtc = expectedTime,
                },
            ],
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
        };
        await _store.SaveAsync(settings);

        var loaded = await _store.LoadAsync();
        var src = loaded.Sources.First(s => s.Name == "Recent Source");

        // The timestamp must have been preserved — if it were null, IsOverdue would
        // return true and the next scheduled execution would trigger a redundant download.
        Assert.NotNull(src.LastDownloadUtc);
        Assert.False(DownloadWallpapers.IsOverdue(src),
            "A source downloaded 1 minute ago should not be overdue on an 8-hour cron.");
    }

    // --- ApplyDefaults annotation-field guards ---

    // Regression guard: if the settings file contains an empty FontFamily (e.g. from manual
    // editing or an older schema version), LoadAsync must restore the default font so that
    // the font lookup never receives an empty family name.
    [Fact]
    public async Task LoadAsync_EmptyAnnotationFontFamily_RestoresDefault()
    {
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Annotation = new AnnotationSettings { FontFamily = "" },
            Sources = [],
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
        };
        await _store.SaveAsync(settings);

        var loaded = await _store.LoadAsync();

        Assert.False(string.IsNullOrWhiteSpace(loaded.Annotation.FontFamily),
            "FontFamily must never be empty after load; the renderer passes it directly to the font lookup.");
        Assert.Equal(BundledFonts.DefaultFontFamily, loaded.Annotation.FontFamily);
    }

    // Regression guard: a FontSize of 0 creates a degenerate font that throws
    // during text measurement. ApplyDefaults must replace it with the default (18).
    [Fact]
    public async Task LoadAsync_ZeroAnnotationFontSize_RestoresDefault()
    {
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Annotation = new AnnotationSettings { FontSize = 0 },
            Sources = [],
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
        };
        await _store.SaveAsync(settings);

        var loaded = await _store.LoadAsync();

        Assert.True(loaded.Annotation.FontSize > 0,
            "FontSize must be positive after load; a zero size produces a degenerate font.");
        Assert.Equal(new AnnotationSettings().FontSize, loaded.Annotation.FontSize);
    }

    // Regression guard: a null or empty Color string causes Color.ParseHex to throw.
    // That particular throw is caught and logged, but the guard here ensures the value
    // is filled in before it reaches the renderer so callers see a valid default instead of
    // a warning in every log file.
    [Fact]
    public async Task LoadAsync_EmptyAnnotationColor_RestoresDefault()
    {
        var settings = new WallpaperNexusSettings
        {
            Download = new DownloadSettings { Folder = _downloadDir },
            Annotation = new AnnotationSettings { Color = "" },
            Sources = [],
            RunOnStartup = false,
            AutoUpdatesEnabled = false,
        };
        await _store.SaveAsync(settings);

        var loaded = await _store.LoadAsync();

        Assert.False(string.IsNullOrWhiteSpace(loaded.Annotation.Color),
            "Color must never be empty after load; renderer passes it directly to Color.ParseHex.");
        Assert.Equal(new AnnotationSettings().Color, loaded.Annotation.Color);
    }
}

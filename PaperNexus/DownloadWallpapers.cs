using Cronos;

namespace PaperNexus;

internal interface IDownloadWallpapers
{
    public Task DownloadAllAsync();
}

internal class DownloadWallpapers : ScheduledJobService, IDownloadWallpapers, IAddHostedSingleton<IDownloadWallpapers>
{
    private static readonly HashSet<char> InvalidFileNameChars = Path.GetInvalidFileNameChars()
        .Concat(Path.GetInvalidPathChars())
        .Append('/').Append('\\')
        .ToHashSet();

    private readonly HttpWallpaperSourceService _sourceService;
    private readonly ISettingsStore _settingsStore;

    // Shared, long-lived HttpClient for all image downloads. HttpClient is thread-safe for
    // concurrent requests; creating a new one per source/image call drains the ephemeral port
    // pool because disposed clients leave sockets in TIME_WAIT for several minutes.
    // The service is a singleton, so this client lives for the process lifetime.
    private readonly HttpClient _imageClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public DownloadWallpapers(ILogger<DownloadWallpapers> logger, HttpWallpaperSourceService sourceService, ISettingsStore settingsStore) : base(logger)
    {
        _sourceService = sourceService.ThrowIfNull();
        _settingsStore = settingsStore.ThrowIfNull();
        ExecuteOnStartup = true;
    }

    // Returns the soonest upcoming cron occurrence across all enabled sources so the
    // legacy scheduler wakes up at the right time. Falls back to 1 hour if no source
    // has a next occurrence within that window. Sources with an invalid cron expression
    // are skipped rather than crashing the scheduler into a 1-minute error loop.
    protected override async Task<DateTimeOffset> GetNextExecutionAsync(JobExecutionContext context)
    {
        var settings = await _settingsStore.LoadAsync();
        var earliest = DateTimeOffset.Now.AddHours(1);
        foreach (var source in settings.Sources.Where(s => s.IsEnabled))
        {
            try
            {
                var expression = CronExpression.Parse(source.CronExpression);
                var next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
                if (next.HasValue && next.Value < earliest)
                    earliest = next.Value;
            }
            catch (CronFormatException)
            {
                Logger.LogWarning("Source '{Source}' has invalid cron expression '{Expression}' — skipping for next-execution calculation.", source.Name, source.CronExpression);
            }
        }
        return earliest;
    }

    public Task DownloadAllAsync() => DownloadFromSourcesAsync(_ => true);

    // Scheduled execution: only downloads from sources whose cron interval has elapsed
    // since the last successful download, skipping recently-updated sources.
    protected override Task Execute() => DownloadFromSourcesAsync(source =>
    {
        if (!IsOverdue(source))
        {
            Logger.LogInformation("Source '{Source}' is up to date — skipping.", source.Name);
            return false;
        }
        return true;
    });

    // Core download loop: iterates enabled sources that pass the caller-supplied filter,
    // then triggers retention cleanup and persists the updated LastDownloadUtc timestamps.
    // Cleanup and save are skipped entirely if no sources were actually downloaded.
    private async Task DownloadFromSourcesAsync(Func<WallpaperSource, bool> filter)
    {
        var settings = await _settingsStore.LoadAsync();
        if (!settings.IsConfigured)
        {
            Logger.LogInformation("Wallpapers folder not configured — skipping.");
            return;
        }

        Directory.CreateDirectory(settings.Download.Folder);

        var downloaded = false;
        foreach (var source in settings.Sources.Where(s => s.IsEnabled && filter(s)))
        {
            try
            {
                await DownloadSource(source, settings);
                downloaded = true;
            }
            catch (Exception ex)
            {
                // Log and continue so a failed source (e.g. unreachable feed URL) does not
                // block remaining sources from running or prevent timestamps from being saved.
                Logger.LogError(ex, "Failed to download from source '{Source}' — skipping.", source.Name);
            }
        }
        if (downloaded)
        {
            await CleanupOldImages(settings);
            await _settingsStore.SaveAsync(settings);
        }
    }

    // Downloads all images for a single source, reusing the process-wide _imageClient so
    // all images — across all sources — share one socket pool rather than creating one per call.
    private async Task DownloadSource(WallpaperSource source, WallpaperNexusSettings settings)
    {
        var images = await _sourceService.GetImagesAsync(source);
        foreach (var image in images)
        {
            try
            {
                await Download(image, settings, _imageClient);
            }
            catch (Exception ex)
            {
                // Log and continue so one failed image does not abort the rest of the source's images.
                // The source's LastDownloadUtc is still updated below so it is not retried immediately.
                Logger.LogWarning(ex, "Failed to download image '{Title}' from source '{Source}' — skipping.", image.Title, source.Name);
            }
        }
        source.LastDownloadUtc = DateTimeOffset.UtcNow;
    }

    // Returns true if the source has never been downloaded, or if the next cron occurrence
    // after the last download has already passed. An invalid cron expression is treated
    // as always-overdue so a misconfigured source never silently stalls.
    internal static bool IsOverdue(WallpaperSource source)
    {
        if (source.LastDownloadUtc is null)
            return true;

        try
        {
            var cron = CronExpression.Parse(source.CronExpression);
            // Compute the next fire time relative to the last successful download
            var next = cron.GetNextOccurrence(source.LastDownloadUtc.Value, TimeZoneInfo.Local);
            return next.HasValue && next.Value <= DateTimeOffset.UtcNow;
        }
        catch (CronFormatException)
        {
            return true;
        }
    }

    // Downloads a single wallpaper image to the configured folder.
    // The filename is derived from the sanitised title plus the URL's filename component
    // so that the original source identifier is preserved for deduplication.
    // An optional HttpClient may be supplied by the caller; if omitted, the instance-level
    // _imageClient is used. The caller must never dispose a client it did not create.
    public async Task Download(WallpaperImage data, WallpaperNexusSettings settings, HttpClient? client = null)
    {
        // Strip characters that are invalid in file names, and cap length to avoid MAX_PATH issues
        var title = new string(data.Title
            .Where(c => !InvalidFileNameChars.Contains(c))
            .Take(200)
            .ToArray());
        // Strip query string and fragment before parsing so URLs like
        // "image.jpg?sig=xyz" or "image.jpg#anchor" produce a clean filename.
        // Path.GetExtension does not know about URL structure and would otherwise
        // include the query params in the extension (e.g. ".jpg?sig=xyz").
        var cleanUrl = data.ImageUrl;
        var hashIdx = cleanUrl.IndexOf('#');
        if (hashIdx >= 0)
            cleanUrl = cleanUrl[..hashIdx];
        var queryIdx = cleanUrl.IndexOf('?');
        if (queryIdx >= 0)
            cleanUrl = cleanUrl[..queryIdx];
        var urlFile = cleanUrl.Split('/').Last();
        var ext = Path.GetExtension(urlFile);
        if (string.IsNullOrEmpty(ext))
            ext = ".png";
        // Append the URL filename stem so re-downloads of the same title remain distinct
        title += " - " + Path.GetFileNameWithoutExtension(urlFile);
        var path = Path.Combine(settings.Download.Folder, title + ext);

        // Guard against a malicious title that contains ".." path traversal sequences
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetFullPath(settings.Download.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning("Path traversal blocked: {Path}", fullPath);
            return;
        }
        // Skip existing files unless a debugger is attached (useful for re-testing download logic)
        if (!Debugger.IsAttached && File.Exists(path))
            return;

        Logger.LogInformation("Downloading image: {Title}", data.Title);
        var watch = Stopwatch.StartNew();
        // Prefer the caller-supplied client; fall back to the shared instance client.
        // Neither is disposed here — both are long-lived and owned by their respective creators.
        var httpClient = client ?? _imageClient;

        // Use ResponseHeadersRead to start timing from the first byte, not after full download
        using var response = await httpClient.GetAsync(data.ImageUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode} downloading '{data.ImageUrl}': {message}");
        }

        // Stream directly to disk so large images (4K+) don't require a full in-memory buffer
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await response.Content.CopyToAsync(fileStream);
        Logger.LogInformation("Download complete: {Elapsed}", watch.Elapsed);
        // Re-encode to the user's configured resolution cap after the download completes,
        // so a partial resize failure cannot corrupt the downloaded file.
        await ApplyResolutionCapAsync(path, settings);
    }

    // Resizes the image at filePath to fit within the user-configured resolution cap,
    // preserving the original aspect ratio and never upscaling. If the resolution
    // setting is "Native" (width or height == 0) or the image already fits within the
    // cap, the file is left unchanged. The file is re-encoded in-place using the same
    // format (JPEG at quality 95, or 8-bit RGB PNG) so the filename and extension are preserved.
    internal Task ApplyResolutionCapAsync(string filePath, WallpaperNexusSettings settings)
    {
        var maxWidth = settings.Download.ResolutionWidth;
        var maxHeight = settings.Download.ResolutionHeight;

        // Resolution == 0 means "Native" — no cap applied
        if (maxWidth <= 0 || maxHeight <= 0)
            return Task.CompletedTask;

        // Runs on the download job's background thread, so the synchronous decode and
        // encode do not block anything interactive.
        var resized = ImageResizing.ResizeFileToFit(filePath, maxWidth, maxHeight);
        if (resized)
        {
            Logger.LogInformation(
                "Resized '{File}' to fit within {Width}×{Height}.",
                Path.GetFileName(filePath), maxWidth, maxHeight);
        }
        return Task.CompletedTask;
    }

    // Deletes wallpaper files older than the configured retention period and prunes
    // stale paths from FavoriteWallpapers and BannedWallpapers. Favorited files and the
    // currently active wallpaper (CurrentWallpaperPath) are excluded from the age-based
    // deletion but their paths are still pruned if the file no longer exists (e.g.
    // manually deleted outside the app). The caller is responsible for saving settings
    // after this method returns.
    internal async Task CleanupOldImages(WallpaperNexusSettings settings)
    {
        // Guard against the folder being deleted between the Directory.CreateDirectory
        // call in DownloadFromSourcesAsync and this cleanup step. If the folder is gone,
        // skip the age-based pass but still prune any stale list entries.
        if (!Directory.Exists(settings.Download.Folder))
        {
            settings.FavoriteWallpapers?.RemoveAll(p => !File.Exists(p));
            settings.BannedWallpapers?.RemoveAll(p => !File.Exists(p));
            return;
        }

        var favorites = new HashSet<string>(
            settings.FavoriteWallpapers ?? [],
            StringComparer.OrdinalIgnoreCase);
        // Resolve the currently active wallpaper to a full path so the comparison
        // against FileInfo.FullName is reliable regardless of relative vs absolute storage.
        // Path.GetFullPath can throw for several reasons — invalid characters (ArgumentException),
        // unsupported path format such as ":" in the wrong position (NotSupportedException),
        // or exceeding MAX_PATH (PathTooLongException) — so fall back to empty (treat as unmatched).
        var currentWallpaper = string.Empty;
        if (!string.IsNullOrEmpty(settings.CurrentWallpaperPath))
        {
            try
            {
                currentWallpaper = Path.GetFullPath(settings.CurrentWallpaperPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Logger.LogWarning(ex, "CurrentWallpaperPath is invalid — ignoring for retention cleanup.");
            }
        }
        // Materialise the directory listing once so we can reuse it for the stale-path
        // check below without a second filesystem round-trip or a TOCTOU window.
        var allFiles = new DirectoryInfo(settings.Download.Folder).EnumerateFiles().ToList();
        var cutoff = DateTime.UtcNow.AddDays(-settings.Download.RetentionDays);
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in allFiles)
        {
            if (cutoff > file.LastWriteTimeUtc
                && !favorites.Contains(file.FullName)
                && !string.Equals(file.FullName, currentWallpaper, StringComparison.OrdinalIgnoreCase))
            {
                file.Delete();
                deleted.Add(file.FullName);
            }
        }

        // Build the surviving-files set from the already-enumerated list minus what was
        // just deleted. This covers files removed outside the app and the ones we deleted
        // above, without re-reading the directory.
        var existingFiles = new HashSet<string>(
            allFiles.Select(f => f.FullName).Where(p => !deleted.Contains(p)),
            StringComparer.OrdinalIgnoreCase);

        var staleCount = 0;
        staleCount += settings.FavoriteWallpapers?.RemoveAll(p => !existingFiles.Contains(p)) ?? 0;
        staleCount += settings.BannedWallpapers?.RemoveAll(p => !existingFiles.Contains(p)) ?? 0;

        if (deleted.Count > 0 || staleCount > 0)
            Logger.LogInformation(
                "Retention cleanup: {Deleted} file(s) deleted, {Stale} stale list entries pruned.",
                deleted.Count, staleCount);

        await Task.CompletedTask; // preserve async signature for future I/O operations
    }
}

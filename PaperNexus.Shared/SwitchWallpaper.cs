using Cronos;
using PaperNexus.Core;

namespace PaperNexus;

public interface ISwitchWallpaper
{
    public event Action<string>? WallpaperChanged;
    public Task<string?> SwitchToNextAsync();
    public Task<string?> SwitchToRandomAsync();
    public Task<string?> SwitchToSpecificAsync(string path);
}

internal sealed class SwitchWallpaper : ISwitchWallpaper, IAddSingleton<ISwitchWallpaper>
{
    private readonly ILogger<SwitchWallpaper> _logger;
    private readonly IWallpaperApplier _wallpaperApplier;
    private readonly ISettingsStore _settingsStore;

    public event Action<string>? WallpaperChanged;

    public SwitchWallpaper(ILogger<SwitchWallpaper> logger, IWallpaperApplier wallpaperApplier, ISettingsStore settingsStore)
    {
        _logger = logger.ThrowIfNull();
        _wallpaperApplier = wallpaperApplier.ThrowIfNull();
        _settingsStore = settingsStore.ThrowIfNull();
    }

    // Advances to the next wallpaper according to the configured slideshow order.
    // Banned wallpapers are excluded from the candidate pool. In Random order the
    // current wallpaper is excluded from candidates (unless it is the only one) so the
    // same image is not shown twice in a row. For sequential orders the list wraps around.
    public async Task<string?> SwitchToNextAsync()
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        if (!settings.IsConfigured)
            return null;

        var bannedSet = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);
        var allFiles = GetWallpaperFiles(settings.Download.Folder)
            .Where(f => !bannedSet.Contains(f.FullName))
            .ToList();

        if (allFiles.Count == 0)
            return null;

        string next;
        if (settings.Slideshow.Order == SlideshowOrder.Random && allFiles.Count > 1)
        {
            var candidates = allFiles
                .Select(f => f.FullName)
                .Where(f => !f.Equals(settings.CurrentWallpaperPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            // If removing the current leaves nothing (e.g. only one file), fall back to full list
            if (candidates.Count == 0)
                candidates = allFiles.Select(f => f.FullName).ToList();
            candidates = ApplyFavoritePriority(candidates, settings);
            next = candidates[Random.Shared.Next(candidates.Count)];
        }
        else
        {
            var files = settings.Slideshow.Order switch
            {
                SlideshowOrder.OldestFirst => allFiles.OrderBy(f => f.LastWriteTime).Select(f => f.FullName).ToList(),
                SlideshowOrder.NewestFirst => allFiles.OrderByDescending(f => f.LastWriteTime).Select(f => f.FullName).ToList(),
                _ => allFiles.OrderBy(f => f.Name).Select(f => f.FullName).ToList(),
            };
            var index = files.IndexOf(settings.CurrentWallpaperPath);
            // If persisted wallpaper is not in the folder (index == -1), start from the first file.
            next = files[(index + 1) % files.Count];
        }

        return await ApplyWallpaperAsync(next, settings).ConfigureAwait(false);
    }

    // Picks a random wallpaper from the non-banned set, preferring to avoid repeating
    // the current wallpaper when more than one candidate is available.
    public async Task<string?> SwitchToRandomAsync()
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        if (!settings.IsConfigured)
            return null;

        var bannedSet = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);
        var candidates = GetWallpaperFiles(settings.Download.Folder)
            .Select(f => f.FullName)
            .Where(f => !bannedSet.Contains(f))
            .ToList();

        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1)
        {
            // Exclude the current wallpaper so the user always sees something different
            var withoutCurrent = candidates
                .Where(f => !f.Equals(settings.CurrentWallpaperPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (withoutCurrent.Count > 0)
                candidates = withoutCurrent;
        }

        candidates = ApplyFavoritePriority(candidates, settings);
        var next = candidates[Random.Shared.Next(candidates.Count)];
        return await ApplyWallpaperAsync(next, settings).ConfigureAwait(false);
    }

    // Boosts the probability of favorited wallpapers being selected by adding extra
    // copies of each favorite into the candidate pool. The effective probability of
    // a favorite being chosen is roughly weight times that of a non-favorite.
    private static List<string> ApplyFavoritePriority(List<string> candidates, WallpaperNexusSettings settings)
    {
        if (!settings.Slideshow.FavoritePriorityEnabled || settings.FavoriteWallpapers.Count == 0)
            return candidates;

        // Clamp weight to at least 2 so there is always a meaningful boost
        var weight = Math.Max(2, settings.Slideshow.FavoritePriorityWeight);
        var favSet = new HashSet<string>(settings.FavoriteWallpapers, StringComparer.OrdinalIgnoreCase);
        var weighted = new List<string>(candidates);
        foreach (var c in candidates)
        {
            if (favSet.Contains(c))
            {
                // Add weight-1 extra copies (first copy is already in `weighted` from the initial clone)
                for (var i = 1; i < weight; i++)
                    weighted.Add(c);
            }
        }
        return weighted;
    }

    public async Task<string?> SwitchToSpecificAsync(string path)
    {
        if (!File.Exists(path))
            return null;
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        return await ApplyWallpaperAsync(path, settings).ConfigureAwait(false);
    }

    private static List<FileInfo> GetWallpaperFiles(string folder)
    {
        // Return empty list rather than throwing DirectoryNotFoundException if folder hasn't been created yet
        if (!Directory.Exists(folder))
            return [];
        return new DirectoryInfo(folder)
            .EnumerateFiles()
            .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Applies the chosen wallpaper: optionally composites the title annotation, encodes
    // to the processed current file, sets the desktop wallpaper, and persists the
    // current path to settings so the next run knows where it left off.
    //
    // Write to a fixed current file in the execution directory so the original files are never modified.
    // Apply the title overlay here rather than at download time to preserve source image quality.
    // Save as PNG; if it exceeds 16 MB fall back to JPEG stepping quality down by 3% from 97%.
    private async Task<string?> ApplyWallpaperAsync(string next, WallpaperNexusSettings settings)
    {
        // Strip the URL-stem suffix (" - <urlfile>") that was added during download to recover the human-readable title
        var title = Path.GetFileNameWithoutExtension(next);
        var separatorIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex >= 0)
            title = title[..separatorIndex];

        var annotation = settings.AnnotateWallpaper ? BuildAnnotation(title, settings) : null;

        // Decoding, drawing and encoding a 4K image is CPU-bound and synchronous; run it on
        // the thread pool so a switch started from the settings window does not freeze the UI.
        using var encoded = await Task.Run(() => WallpaperRenderer.Render(next, annotation)).ConfigureAwait(false);

        var pngPath = Path.Combine(AppContext.BaseDirectory, "current.png");
        var jpgPath = Path.Combine(AppContext.BaseDirectory, "current.jpg");
        var isPng = encoded.Format == WallpaperFileFormat.Png;
        var currentPath = isPng ? pngPath : jpgPath;
        // Remove the alternate format file so the desktop doesn't pick up a stale version
        var stalePath = isPng ? jpgPath : pngPath;

        // Copy the buffer straight to disk - avoids allocating a second byte[] copy of the encoded image
        using (var file = new FileStream(currentPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await encoded.Data.CopyToAsync(file).ConfigureAwait(false);
        }
        File.Delete(stalePath);

        _wallpaperApplier.ApplyFillStyle(settings.Slideshow.FillStyle);
        // Log a warning if the platform call reports failure so silent wallpaper-not-set bugs surface in logs
        var wallpaperSet = _wallpaperApplier.SetWallpaper(currentPath);
        if (!wallpaperSet)
            _logger.LogWarning("SystemParametersInfo(SPI_SETDESKWALLPAPER) returned 0 for path: {Path}", currentPath);
        _logger.LogInformation("Switching wallpaper to: {Path}", next);

        // Persist the original source path (not the processed current.* path) so ordering is stable across restarts
        settings.CurrentWallpaperPath = next;
        await _settingsStore.SaveAsync(settings).ConfigureAwait(false);
        WallpaperChanged?.Invoke(next);
        return next;
    }

    // Translates the annotation settings into what the imaging layer draws. An invalid
    // colour is logged here and replaced by the default so the title is still drawn.
    private AnnotationRequest BuildAnnotation(string title, WallpaperNexusSettings settings)
    {
        var annotation = settings.Annotation;
        var color = annotation.Color;
        if (!WallpaperAnnotator.IsValidColor(color))
        {
            _logger.LogWarning("Invalid annotation color '{Color}', using default.", color);
            color = WallpaperAnnotator.DefaultColor;
        }

        var fontSize = annotation.FontSize > 0 ? annotation.FontSize : 18;
        // In debug mode, a smaller timestamp label is drawn immediately below/above the title
        var timestamp = settings.DebugMode ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : null;

        var request = new AnnotationRequest(
            title,
            timestamp,
            annotation.FontFamily,
            fontSize,
            color,
            annotation.Position,
            annotation.OutlineEnabled);
        return request;
    }
}

internal sealed class SwitchWallpaperJob : IScheduleScopedJob
{
    private readonly ISwitchWallpaper _switcher;
    private readonly ILogger<SwitchWallpaperJob> _logger;
    private readonly ISettingsStore _settingsStore;

    public SwitchWallpaperJob(ISwitchWallpaper switcher, ILogger<SwitchWallpaperJob> logger, ISettingsStore settingsStore)
    {
        _switcher = switcher.ThrowIfNull();
        _logger = logger.ThrowIfNull();
        _settingsStore = settingsStore.ThrowIfNull();
    }

    // Returns an empty config (no schedule) when the slideshow is disabled or when the
    // stored cron expression is invalid. An invalid expression is treated as disabled rather
    // than crashing the scheduler into a 1-minute error loop; the user can fix it in settings.
    public async Task<JobConfig> GetJobConfigAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        if (!settings.Slideshow.Enabled)
            return new JobConfig();
        var stored = settings.Slideshow.CronExpression;
        try
        {
            var fields = stored.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
            var cronExpression = CronExpression.Parse(stored, format);
            return new JobConfig(CronExpression: cronExpression);
        }
        catch (CronFormatException)
        {
            _logger.LogWarning("Slideshow cron expression '{Expression}' is invalid - wallpaper switching disabled until corrected.", stored);
            return new JobConfig();
        }
    }

    public async Task ExecuteAsync()
    {
        var next = await _switcher.SwitchToNextAsync();
        if (next is null)
            _logger.LogInformation("Wallpapers folder not configured or no wallpapers found - skipping.");
    }
}

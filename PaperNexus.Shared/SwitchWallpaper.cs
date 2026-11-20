using Cronos;
using PaperNexus.Core;

namespace PaperNexus;

public interface ISwitchWallpaper
{
    // Raised with the source picture's path after the device accepted a new wallpaper.
    public event Action<string>? WallpaperChanged;

    // True while a switch runs or waits for its turn. The phone's long-press menu entries read
    // it, so a tap while the wallpaper is already changing makes no second change (issue #203,
    // step 6).
    public bool IsSwitching { get; }

    // Each switch returns the source path it set, or null when there was nothing to set (no
    // wallpapers, or the file is gone). A wallpaper the device refuses throws
    // WallpaperRefusedException and leaves the current wallpaper as it was.
    public Task<string?> SwitchToNextAsync();
    public Task<string?> SwitchToRandomAsync();
    public Task<string?> SwitchToSpecificAsync(string path);

    // True when the download folder holds at least one picture that is not banned.
    public Task<bool> HasWallpapersAsync();
}

// The device did not accept the wallpaper (IWallpaperApplier.SetWallpaper returned false).
// Nothing was recorded: the previous wallpaper stays current.
public sealed class WallpaperRefusedException : Exception
{
    public WallpaperRefusedException(string wallpaperPath)
        : base("The device did not accept the new wallpaper.")
    {
        WallpaperPath = wallpaperPath;
    }

    // The source picture that was refused.
    public string WallpaperPath { get; }
}

internal sealed class SwitchWallpaper : ISwitchWallpaper, IAddSingleton<ISwitchWallpaper>
{
    // The processed wallpaper written to the data folder, as PNG or JPEG.
    internal const string CurrentPngName = "current.png";
    internal const string CurrentJpgName = "current.jpg";

    private readonly ILogger<SwitchWallpaper> _logger;
    private readonly IWallpaperApplier _wallpaperApplier;
    private readonly ISettingsStore _settingsStore;
    private readonly string _dataFolder;
    private readonly IDeviceSettingRules _deviceRules;

    // One switch at a time: the window, the tray, the slideshow and the phone's buttons can
    // ask together, and each switch reads the current wallpaper, writes the processed file and
    // records the new one, so two at once would race on the file and on the settings.
    private readonly SemaphoreSlim _switchLock = new(1);

    // How many switches are running or waiting for the lock
    private int _switchesInProgress;

    public event Action<string>? WallpaperChanged;

    public bool IsSwitching => Volatile.Read(ref _switchesInProgress) > 0;

    public SwitchWallpaper(
        ILogger<SwitchWallpaper> logger,
        IWallpaperApplier wallpaperApplier,
        ISettingsStore settingsStore,
        IAppFolders appFolders,
        IDeviceSettingRules deviceRules)
    {
        _deviceRules = deviceRules.ThrowIfNull();
        _logger = logger.ThrowIfNull();
        _wallpaperApplier = wallpaperApplier.ThrowIfNull();
        _settingsStore = settingsStore.ThrowIfNull();
        _dataFolder = appFolders.ThrowIfNull().DataFolder;
    }

    // The processed current wallpaper inside dataFolder, PNG preferred, or null when neither
    // file exists. The desktop's settings window shows it as its preview.
    public static string? FindCurrentImage(string dataFolder)
    {
        var pngPath = Path.Combine(dataFolder, CurrentPngName);
        var jpgPath = Path.Combine(dataFolder, CurrentJpgName);
        var currentImage = File.Exists(pngPath) ? pngPath : File.Exists(jpgPath) ? jpgPath : null;
        return currentImage;
    }

    public async Task<bool> HasWallpapersAsync()
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        if (!settings.IsConfigured)
            return false;

        var bannedSet = new HashSet<string>(settings.BannedWallpapers, StringComparer.OrdinalIgnoreCase);
        var any = GetWallpaperFiles(settings.Download.Folder).Any(f => !bannedSet.Contains(f.FullName));
        return any;
    }

    public Task<string?> SwitchToNextAsync() => RunSwitchAsync(SwitchToNextLockedAsync);

    public Task<string?> SwitchToRandomAsync() => RunSwitchAsync(SwitchToRandomLockedAsync);

    public Task<string?> SwitchToSpecificAsync(string path) => RunSwitchAsync(() => SwitchToSpecificLockedAsync(path));

    // Runs one switch under the lock. The count goes up before the first await, so a caller
    // that asks right after this one already reads IsSwitching as true.
    private async Task<string?> RunSwitchAsync(Func<Task<string?>> lockedSwitch)
    {
        Interlocked.Increment(ref _switchesInProgress);
        try
        {
            using (await _switchLock.EnterAsync().ConfigureAwait(false))
            {
                return await lockedSwitch().ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _switchesInProgress);
        }
    }

    private async Task<string?> SwitchToSpecificLockedAsync(string path)
    {
        if (!File.Exists(path))
            return null;
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        return await ApplyWallpaperAsync(path, settings).ConfigureAwait(false);
    }

    // Advances to the next wallpaper according to the configured slideshow order.
    // Banned wallpapers are excluded from the candidate pool. In Random order the
    // current wallpaper is excluded from candidates (unless it is the only one) so the
    // same image is not shown twice in a row. For sequential orders the list wraps around.
    private async Task<string?> SwitchToNextLockedAsync()
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
    private async Task<string?> SwitchToRandomLockedAsync()
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
    // to the processed current file, sets the device wallpaper, and persists the
    // current path to settings so the next run knows where it left off.
    //
    // Write to a fixed current file in the host's data folder so the original files are never modified.
    // Apply the title overlay here rather than at download time to preserve source image quality.
    // Save as PNG; if it exceeds 16 MB fall back to JPEG stepping quality down by 3% from 97%.
    // When the device wants a canvas (the phone), the picture is fitted to it; the phone draws
    // no title (IDeviceSettingRules.DrawsWallpaperTitle).
    // A wallpaper the device refuses is not recorded: WallpaperRefusedException is thrown
    // before the settings are saved or WallpaperChanged is raised.
    private async Task<string?> ApplyWallpaperAsync(string next, WallpaperNexusSettings settings)
    {
        // Strip the URL-stem suffix (" - <urlfile>") that was added during download to recover the human-readable title
        var title = Path.GetFileNameWithoutExtension(next);
        var separatorIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex >= 0)
            title = title[..separatorIndex];

        // A device that draws no title (the phone) never gets one, whatever the settings say
        var drawsTitle = _deviceRules.DrawsWallpaperTitle && settings.AnnotateWallpaper;
        var annotation = drawsTitle ? BuildAnnotation(title, settings) : null;

        var canvas = _wallpaperApplier.GetCanvas();
        var fillStyle = settings.Slideshow.FillStyle;

        // Decoding, drawing and encoding a 4K image is CPU-bound and synchronous; run it on
        // the thread pool so a switch started from the settings window does not freeze the UI.
        using var encoded = await Task.Run(() => WallpaperRenderer.Render(next, annotation, canvas, fillStyle)).ConfigureAwait(false);
        // Debug mode's detail: how the wallpaper was made, which no other entry says
        _logger.LogDebug(
            "Rendered {Path} as {Format} ({Bytes} bytes) for {Target}, fill style {FillStyle}, title {Title}.",
            next,
            encoded.Format,
            encoded.Data.Length,
            canvas is null ? "the picture's own size" : $"a {canvas.Width}x{canvas.Height} canvas",
            fillStyle,
            annotation is null ? "none" : $"'{annotation.Title}' at size {annotation.FontSize}");

        Directory.CreateDirectory(_dataFolder);
        var pngPath = Path.Combine(_dataFolder, CurrentPngName);
        var jpgPath = Path.Combine(_dataFolder, CurrentJpgName);
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

        _wallpaperApplier.ApplyFillStyle(fillStyle);
        var wallpaperSet = _wallpaperApplier.SetWallpaper(currentPath);
        if (!wallpaperSet)
        {
            // Nothing is recorded, so the previous wallpaper stays current and a later switch
            // starts from it again
            _logger.LogWarning("The device did not accept the wallpaper {Path}; the current wallpaper is unchanged.", next);
            throw new WallpaperRefusedException(next);
        }
        _logger.LogInformation("Switching wallpaper to: {Path}", next);

        // Persist the original source path (not the processed current.* path) so ordering is stable across restarts.
        // Only the current wallpaper is written: the settings read at the start of the switch
        // are seconds old by now, and anything saved meanwhile (a favorite, a ban) must stay.
        await _settingsStore.UpdateAsync(latest => latest.CurrentWallpaperPath = next).ConfigureAwait(false);
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

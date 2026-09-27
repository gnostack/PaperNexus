using Newtonsoft.Json;

namespace PaperNexus.Core;

// The only way into settings.json. Every consumer receives this by constructor injection,
// so a test can point the whole app at a temporary directory and no code path can reach
// the real file by accident.
public interface ISettingsStore
{
    // Reads the settings, filling defaults for missing or invalid fields. Returns a fresh
    // default instance when the file does not exist or is corrupt.
    public Task<WallpaperNexusSettings> LoadAsync();

    // Writes the settings atomically, so the file is never left half-written.
    public Task SaveAsync(WallpaperNexusSettings settings);
}

public sealed class SettingsStore : ISettingsStore, IAddSingleton<ISettingsStore>
{
    public const string FileName = "settings.json";

    private readonly string _directory;

    // The production store: settings live in the default install directory regardless of
    // where the executable itself was installed.
    public SettingsStore()
        : this(PlatformPaths.DefaultInstallDirectory)
    {
    }

    // Internal so the container only ever sees the parameterless constructor; tests use
    // this to root a store in their own temporary directory.
    internal SettingsStore(string directory)
    {
        _directory = directory.ThrowIfNull();
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    // Reads settings.json and deserialises it; fills in defaults for any missing or
    // invalid fields introduced by schema migrations. Returns a fresh default instance
    // if the file does not yet exist or is corrupted.
    public async Task<WallpaperNexusSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = await File.ReadAllTextAsync(FilePath);
                var settings = JsonConvert.DeserializeObject<WallpaperNexusSettings>(json, WallpaperNexusSettings.JsonFormat)
                    ?? new WallpaperNexusSettings();
                ApplyDefaults(settings);
                return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
        return new WallpaperNexusSettings();
    }

    // Guards against settings files written by older versions that omitted certain fields,
    // or that contain zero/empty values that would break the scheduler or file paths.
    private static void ApplyDefaults(WallpaperNexusSettings settings)
    {
        // Ensure slideshow sub-object exists and that its computed fields are valid
        var defaultSlideshow = new SlideshowSettings();
        settings.Slideshow ??= defaultSlideshow;
        if (string.IsNullOrWhiteSpace(settings.Slideshow.CronExpression))
            settings.Slideshow.CronExpression = defaultSlideshow.CronExpression;

        // Migrate deprecated interval modes
        if (settings.Slideshow.ScheduleMode == SlideshowScheduleMode.IntervalMinutes)
        {
            settings.Slideshow.ScheduleMode = SlideshowScheduleMode.Interval;
            settings.Slideshow.IntervalType = IntervalType.Minutes;
        }
        else if (settings.Slideshow.ScheduleMode == SlideshowScheduleMode.IntervalHours)
        {
            settings.Slideshow.ScheduleMode = SlideshowScheduleMode.Interval;
            settings.Slideshow.IntervalType = IntervalType.Hours;
        }

        if (settings.Slideshow.Interval <= 0)
            settings.Slideshow.Interval = defaultSlideshow.Interval;

        // Ensure download sub-object exists; RetentionDays of 0 would delete everything immediately
        var defaultDownload = new DownloadSettings();
        settings.Download ??= defaultDownload;
        if (string.IsNullOrWhiteSpace(settings.Download.Folder))
            settings.Download.Folder = defaultDownload.Folder;
        if (settings.Download.RetentionDays <= 0)
            settings.Download.RetentionDays = defaultDownload.RetentionDays;

        // Initialise reference-type properties that may be null after JSON deserialisation
        settings.CurrentWallpaperPath ??= string.Empty;
        settings.Annotation ??= new AnnotationSettings();
        // Guard annotation sub-fields that the rendering pipeline uses directly: a null or empty
        // FontFamily would be passed straight to the font lookup; a FontSize of 0 creates a
        // degenerate font; a null Color would be logged as invalid on every switch.
        var defaultAnnotation = new AnnotationSettings();
        if (string.IsNullOrWhiteSpace(settings.Annotation.FontFamily))
            settings.Annotation.FontFamily = defaultAnnotation.FontFamily;
        if (settings.Annotation.FontSize <= 0)
            settings.Annotation.FontSize = defaultAnnotation.FontSize;
        if (string.IsNullOrWhiteSpace(settings.Annotation.Color))
            settings.Annotation.Color = defaultAnnotation.Color;
        settings.FavoriteWallpapers ??= [];
        settings.BannedWallpapers ??= [];
        settings.DiscoveredEasterEggs ??= [];
        // A weight of 0 or less would make favorite priority a no-op
        if (settings.Slideshow.FavoritePriorityWeight <= 0)
            settings.Slideshow.FavoritePriorityWeight = 3;

        // Ensure the sources list is never null after deserialisation.
        // An empty list is valid — it means the user intentionally removed all sources —
        // so we do NOT restore the built-in defaults here. A brand-new WallpaperNexusSettings
        // instance (created when no file exists or the file is corrupt) already carries
        // DefaultSources via the property initialiser, so first-run defaults still apply.
        settings.Sources ??= WallpaperNexusSettings.DefaultSources;
    }

    // Writes to a temporary file then renames it over the real settings path.
    // This ensures the settings file is never left in a partial/corrupt state if the
    // process is killed mid-write; the old file is preserved until the rename succeeds.
    public async Task SaveAsync(WallpaperNexusSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var tempPath = Path.Combine(_directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonConvert.SerializeObject(settings, WallpaperNexusSettings.JsonFormat);
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }
}

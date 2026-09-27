using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PaperNexus.Core;

public enum WallpaperFillStyle
{
    Fill,
    Fit,
    Stretch,
    Tile,
    Center,
    Span,
}

public enum SlideshowOrder
{
    Alphabetical,
    Random,
    OldestFirst,
    NewestFirst,
}

public enum SlideshowScheduleMode
{
    CronExpression,
    IntervalMinutes, // deprecated — migrated to Interval on load
    IntervalHours,   // deprecated — migrated to Interval on load
    Interval,
}

public enum IntervalType
{
    Seconds,
    Minutes,
    Hours,
    Days,
    Weeks,
    Months,
    Years,
}

public class SlideshowSettings
{
    public bool Enabled { get; set; } = true;
    public SlideshowScheduleMode ScheduleMode { get; set; } = SlideshowScheduleMode.Interval;
    public double Interval { get; set; } = 30;
    public IntervalType IntervalType { get; set; } = IntervalType.Minutes;
    public string CronExpression { get; set; } = "*/30 * * * *";
    public SlideshowOrder Order { get; set; } = SlideshowOrder.NewestFirst;
    public WallpaperFillStyle FillStyle { get; set; } = WallpaperFillStyle.Fill;
    public bool FavoritePriorityEnabled { get; set; }
    public int FavoritePriorityWeight { get; set; } = 3;
}

public enum WallpaperSourceType
{
    HttpJson,
}

public class WallpaperSource : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public WallpaperSourceType Type { get; set; } = WallpaperSourceType.HttpJson;
    public string Url { get; set; } = string.Empty;
    public string ImageUrlJPath { get; set; } = "$[*].imageUrl";
    public string TitleJPath { get; set; } = "$[*].title";
    public string CronExpression { get; set; } = "0 */8 * * *";
    public DateTimeOffset? LastDownloadUtc { get; set; }

    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

public enum AnnotationPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public class AnnotationSettings
{
    public string FontFamily { get; set; } = BundledFonts.DefaultFontFamily;
    public int FontSize { get; set; } = 18;
    public string Color { get; set; } = "#F5F5F5";
    public AnnotationPosition Position { get; set; } = AnnotationPosition.TopLeft;
    public bool OutlineEnabled { get; set; } = true;
}

public class DownloadSettings
{
    public string Folder { get; set; } = Path.Combine(
        PaperNexus.Core.Platform.PlatformPaths.DefaultPicturesDirectory, "PaperNexus");
    public int ResolutionWidth { get; set; } = 0;
    public int ResolutionHeight { get; set; } = 0;
    public int RetentionDays { get; set; } = 365;
}

public class WallpaperNexusSettings
{
    public static readonly string SettingsFilePath = Path.Combine(
        PaperNexus.Core.Platform.PlatformPaths.DefaultInstallDirectory, "settings.json");

    public SlideshowSettings Slideshow { get; set; } = new();
    public DownloadSettings Download { get; set; } = new();

    public string CurrentWallpaperPath { get; set; } = string.Empty;
    public bool AnnotateWallpaper { get; set; } = true;
    public AnnotationSettings Annotation { get; set; } = new();
    public bool RunOnStartup { get; set; } = true;
    public bool AutoUpdatesEnabled { get; set; } = true;
    public bool DebugMode { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public List<string> FavoriteWallpapers { get; set; } = [];
    public List<string> BannedWallpapers { get; set; } = [];

    // Ids of easter eggs the user has triggered, backing the checklist. Ids come from
    // EasterEggCatalog; unknown entries are ignored rather than pruned, so downgrading the
    // app does not lose progress on eggs a newer version added.
    public List<string> DiscoveredEasterEggs { get; set; } = [];

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<WallpaperSource> Sources { get; set; } = DefaultSources;

    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Download.Folder);

    public static List<WallpaperSource> DefaultSources =>
    [
        new() { Name = "Bing Daily 4k", Url = "https://peapix.com/bing/feed?country=us" },
        new() { Name = "Spotlight Daily 4k", Url = "https://peapix.com/spotlight/feed" },
    ];

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() },
    };

    // Reads settings.json and deserialises it; fills in defaults for any missing or
    // invalid fields introduced by schema migrations. Returns a fresh default instance
    // if the file does not yet exist or is corrupted.
    public static async Task<WallpaperNexusSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<WallpaperNexusSettings>(json, JsonSettings)
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
        settings.Sources ??= DefaultSources;
    }

    // Writes to a temporary file then renames it over the real settings path.
    // This ensures the settings file is never left in a partial/corrupt state if the
    // process is killed mid-write; the old file is preserved until the rename succeeds.
    public async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(SettingsFilePath)!;
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, JsonConvert.SerializeObject(this, JsonSettings));
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }
}

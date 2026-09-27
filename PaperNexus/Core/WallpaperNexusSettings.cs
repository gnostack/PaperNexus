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

// Plain data: the contents of settings.json. Reading and writing the file belongs to
// ISettingsStore, so nothing can reach the file without being handed the store.
public class WallpaperNexusSettings
{
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

    // The on-disk JSON shape of this class. Shared by SettingsStore and by the install
    // screen's first-install write, so both produce the same file format.
    public static readonly JsonSerializerSettings JsonFormat = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() },
    };
}

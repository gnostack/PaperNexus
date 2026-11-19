using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// The download settings both devices show: the resolution pictures are saved at and how long
// they are kept. The desktop's download folder is the desktop's own (DesktopSettingsViewModel).
public sealed partial class DownloadSettingsViewModel : SettingsGroupViewModel
{
    public const int RetentionMinimum = 1;
    public const int RetentionMaximum = 3650;
    public const int RetentionStep = 30;

    // Used when the retention box is emptied.
    public const int DefaultRetentionDays = 365;

    public static readonly IReadOnlyList<ResolutionOption> ResolutionOptions = new[]
    {
        new ResolutionOption("Native",                 "Use the image's original resolution, no resampling",              0,    0),
        new ResolutionOption("HD (1280×720)",          "Low resolution, good for slow connections or limited storage", 1280,  720),
        new ResolutionOption("Full HD (1920×1080)",    "Standard HD, suitable for most 1080p displays",               1920, 1080),
        new ResolutionOption("2K (2560×1440)",         "Quad HD, ideal for 1440p displays",                           2560, 1440),
        new ResolutionOption("4K (3840×2160)",         "Ultra HD, best for large 4K displays",                        3840, 2160),
        new ResolutionOption("5K (5120×2880)",         "5K, for high-DPI displays and Apple Studio Display",          5120, 2880),
        new ResolutionOption("8K (7680×4320)",         "8K, maximum quality, larger file sizes",                      7680, 4320),
    };

    [ObservableProperty]
    private ResolutionOption _selectedResolution = ResolutionOptions[0];

    [ObservableProperty]
    private int? _retentionDays = DefaultRetentionDays;

    public DownloadSettingsViewModel(ISettingsStore settingsStore)
        : base(settingsStore)
    {
    }

    partial void OnSelectedResolutionChanged(ResolutionOption value) => SaveSoon();

    partial void OnRetentionDaysChanged(int? value) => SaveSoon();

    // A stored size that matches no option shows as Native.
    protected override void ReadFrom(WallpaperNexusSettings settings)
    {
        var download = settings.Download;
        SelectedResolution = ResolutionOptions.FirstOrDefault(
            r => r.Width == download.ResolutionWidth && r.Height == download.ResolutionHeight)
            ?? ResolutionOptions[0];
        RetentionDays = download.RetentionDays;
    }

    protected override Action<WallpaperNexusSettings>? CaptureChange()
    {
        var resolution = SelectedResolution ?? ResolutionOptions[0];
        var retentionDays = RetentionDays ?? DefaultRetentionDays;
        return latest =>
        {
            latest.Download.ResolutionWidth = resolution.Width;
            latest.Download.ResolutionHeight = resolution.Height;
            latest.Download.RetentionDays = retentionDays;
        };
    }
}

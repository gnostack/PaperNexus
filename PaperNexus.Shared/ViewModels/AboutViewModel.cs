using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

// The app's version, the version egg, and the links to report a bug and to the project, as both
// devices show them.
public sealed partial class AboutViewModel
{
    // Taps on the version that play its easter egg, and the longest gap between two of them.
    public const int VersionTapsForEgg = 5;
    public static readonly TimeSpan VersionTapGap = TimeSpan.FromSeconds(3);

    private readonly IAppInfo _appInfo;
    private readonly IShellOpener _shellOpener;
    private readonly EasterEggRequests _easterEggs;
    private readonly TimeProvider _clock;

    private int _versionTaps;
    private DateTimeOffset _lastVersionTap = DateTimeOffset.MinValue;

    public AboutViewModel(IAppInfo appInfo, IShellOpener shellOpener, EasterEggRequests easterEggs)
        : this(appInfo, shellOpener, easterEggs, TimeProvider.System)
    {
    }

    // Tests pass a clock they move themselves.
    internal AboutViewModel(IAppInfo appInfo, IShellOpener shellOpener, EasterEggRequests easterEggs, TimeProvider clock)
    {
        _appInfo = appInfo.ThrowIfNull();
        _shellOpener = shellOpener.ThrowIfNull();
        _easterEggs = easterEggs.ThrowIfNull();
        _clock = clock.ThrowIfNull();
    }

    // A link that could not be opened, for the screen's status line.
    public event Action<SettingsStatus>? StatusReported;

    public static string BrowserFailedText(string reason) => $"Could not open the browser: {reason}";

    // The version label, e.g. "v12".
    public string Version => _appInfo.Version;

    // The address a bug report opens: a new issue on the project with the version filled in.
    public string BugReportUrl
    {
        get
        {
            var body = $"**App Version:** {Version}\n\n**Describe the bug:**\n\n\n**Steps to reproduce:**\n\n\n**Expected behavior:**\n\n";
            var url = GitHubRepository.Current.WebUrl + "/issues/new"
                + "?assignees=claude&labels=bug&title=Bug+Report"
                + "&body=" + Uri.EscapeDataString(body);
            return url;
        }
    }

    // A tap or click on the version. Five in a row, each within three seconds of the one
    // before, play the version egg; the count then starts again.
    public void TapVersion()
    {
        var now = _clock.GetUtcNow();
        if (now - _lastVersionTap > VersionTapGap)
            _versionTaps = 0;
        _lastVersionTap = now;
        _versionTaps++;

        if (_versionTaps < VersionTapsForEgg)
            return;
        _versionTaps = 0;
        _easterEggs.Request(EasterEggShows.Version());
    }

    [RelayCommand]
    private void ReportBug()
    {
        OpenInBrowser(BugReportUrl);
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        var homepage = GitHubRepository.Current.WebUrl;
        OpenInBrowser(homepage);
    }

    // Opens a web address; a failure (no browser, or the device refusing) is reported on the
    // status line rather than thrown from the button.
    private void OpenInBrowser(string url)
    {
        try
        {
            _shellOpener.Open(url);
        }
        catch (Exception ex)
        {
            var status = new SettingsStatus(BrowserFailedText(ex.Message), StatusTone.Error);
            StatusReported?.Invoke(status);
        }
    }
}

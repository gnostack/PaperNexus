using CommunityToolkit.Mvvm.ComponentModel;

namespace PaperNexus.ViewModels;

// Debug mode and Factory Reset, as both devices show them.
//
// Turning debug mode on asks seventeen increasingly absurd questions first; turning it off asks
// nothing. While it is on the log holds debug entries (LogLevelSwitch), the wallpaper title
// carries the switch time, and the Factory Reset button shows. Factory Reset asks twice, then
// the host deletes the app's data and starts the app again (IFactoryReset).
//
// The group asks its questions one at a time through PendingQuestion; the screen shows each in
// its own way (a window on the desktop) and passes the answer to Answer. Because the question
// lives here rather than in a view, a screen created again finds it still open.
public sealed partial class DebugSettingsViewModel : SettingsGroupViewModel
{
    public const string EnabledSentence = "Debug mode enabled. You absolute legend.";
    public const string FactoryResetTitle = "Factory Reset";
    public const string FactoryResetQuestion = "This will delete ALL application data (settings, logs, timers) and restart.\n\nYour downloaded wallpapers folder will NOT be deleted.\n\nAre you sure?";
    public const string FactoryResetFinalTitle = "Factory Reset - Final Confirmation";
    public const string FactoryResetFinalQuestion = "This cannot be undone. Seriously. Last chance to back out.";

    // How long the debug mode lines stay, as on the desktop.
    public const int DebugLineDurationMs = 5000;

    public static readonly IReadOnlyList<string> DebugConfirmations =
    [
        "Are you sure you want to enable debug mode?",
        "Are you really sure?",
        "Debug mode is for developers. Are you a developer?",
        "Do you pinky promise you know what you're doing?",
        "Have you consulted your system administrator?",
        "Have you backed up your wallpapers? (You should.)",
        "On a scale of 1 to 10, is your certainty at least 7?",
        "Did you eat breakfast today? Mental clarity is important for decisions like this.",
        "Have you considered that your wallpapers might judge you for this?",
        "If a tree falls in a forest and no one hears it, should debug mode still be enabled?",
        "The last person who enabled debug mode was never seen again. Continue?",
        "We've notified your emergency contacts. Proceed anyway?",
        "This action may void your wallpaper warranty. Accept?",
        "NASA has been consulted and they recommend against it. Override NASA?",
        "The prophecy foretold of one who would enable debug mode. Is it you, The Chosen One?",
        "Final answer? No take-backs. This is legally binding in 47 countries.",
        "Okay fine. But we warned you. SEVENTEEN TIMES. Enable debug mode?",
    ];

    private readonly IAppInfo _appInfo;
    private readonly LogLevelSwitch _logLevels;
    private readonly IFactoryReset _factoryReset;

    // Which questions are being asked, and how many of them were answered yes so far.
    private QuestionRun _run;
    private int _answered;

    [ObservableProperty]
    private bool _debugMode;

    // The question waiting for an answer, or null.
    [ObservableProperty]
    private SettingsQuestion? _pendingQuestion;

    public DebugSettingsViewModel(
        ISettingsStore settingsStore,
        IAppInfo appInfo,
        LogLevelSwitch logLevels,
        IFactoryReset factoryReset)
        : base(settingsStore)
    {
        _appInfo = appInfo.ThrowIfNull();
        _logLevels = logLevels.ThrowIfNull();
        _factoryReset = factoryReset.ThrowIfNull();
    }

    private enum QuestionRun
    {
        None,
        DebugMode,
        FactoryReset,
    }

    public static string CancelledSentence(int answered) =>
        $"Debug mode cancelled. You only made it through {answered} of {DebugConfirmations.Count} confirmations.";

    public static string FactoryResetFailedSentence(string reason) => $"Factory reset failed: {reason}";

    public static SettingsQuestion DebugQuestion(int index) =>
        new($"Confirmation {index + 1} of {DebugConfirmations.Count}", DebugConfirmations[index]);

    // The person flipped the switch. Off takes effect at once; on starts the questions and the
    // switch stays off until the last one is answered yes.
    public void RequestDebugMode(bool turnOn)
    {
        if (!turnOn)
        {
            CloseQuestions();
            DebugMode = false;
            return;
        }

        if (DebugMode)
            return;
        _run = QuestionRun.DebugMode;
        _answered = 0;
        PendingQuestion = DebugQuestion(0);
    }

    // The Factory Reset button: asks twice before resetting.
    public void RequestFactoryReset()
    {
        _run = QuestionRun.FactoryReset;
        _answered = 0;
        PendingQuestion = new SettingsQuestion(FactoryResetTitle, FactoryResetQuestion);
    }

    // The answer to PendingQuestion.
    public async Task AnswerAsync(bool yes)
    {
        if (PendingQuestion is null)
            return;

        switch (_run)
        {
            case QuestionRun.DebugMode:
                AnswerDebugQuestion(yes);
                break;
            case QuestionRun.FactoryReset:
                await AnswerFactoryResetQuestionAsync(yes);
                break;
            default:
                CloseQuestions();
                break;
        }
    }

    private void AnswerDebugQuestion(bool yes)
    {
        if (!yes)
        {
            var answered = _answered;
            CloseQuestions();
            Report(CancelledSentence(answered), StatusTone.Neutral, DebugLineDurationMs);
            return;
        }

        _answered++;
        if (_answered < DebugConfirmations.Count)
        {
            PendingQuestion = DebugQuestion(_answered);
            return;
        }

        CloseQuestions();
        DebugMode = true;
        Report(EnabledSentence, StatusTone.Neutral, DebugLineDurationMs);
    }

    private async Task AnswerFactoryResetQuestionAsync(bool yes)
    {
        if (!yes)
        {
            CloseQuestions();
            return;
        }

        _answered++;
        if (_answered == 1)
        {
            PendingQuestion = new SettingsQuestion(FactoryResetFinalTitle, FactoryResetFinalQuestion);
            return;
        }

        CloseQuestions();
        try
        {
            await _factoryReset.ResetAsync();
        }
        catch (Exception ex)
        {
            Report(FactoryResetFailedSentence(ex.Message), StatusTone.Neutral, DebugLineDurationMs);
        }
    }

    private void CloseQuestions()
    {
        _run = QuestionRun.None;
        _answered = 0;
        PendingQuestion = null;
    }

    // The log follows the switch at once, whether the person or a load changed it.
    partial void OnDebugModeChanged(bool value)
    {
        _logLevels.SetDebugMode(value);
        SaveSoon();
    }

    // Starting the desktop with --debug shows the switch on without saving it.
    protected override void ReadFrom(WallpaperNexusSettings settings)
    {
        DebugMode = settings.DebugMode || _appInfo.StartedInDebugMode;
        _logLevels.SetDebugMode(settings.DebugMode);
    }

    protected override Action<WallpaperNexusSettings>? CaptureChange()
    {
        var debugMode = DebugMode;
        return latest => latest.DebugMode = debugMode;
    }
}

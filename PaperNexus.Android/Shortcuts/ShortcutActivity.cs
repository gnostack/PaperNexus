using Android.Content;
using Android.OS;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using PaperNexus.ViewModels;

namespace PaperNexus;

// What the launcher's long-press menu entries "Next Wallpaper" and "Random Wallpaper" start
// (Resources/xml/shortcuts.xml, issue #203 step 6), and the home screen icons dragged out of
// them. An entry can only start an activity, and the person should stay on the home screen, so
// this one shows nothing: a transparent window, no animation, never in Recents, in a task of its
// own so the app's own screen is neither brought forward nor destroyed (a static entry always
// starts its activity with NEW_TASK and CLEAR_TASK).
//
// While it is in front, a tap on the home screen does nothing. Measured on the Android 16
// emulator (issue #203, step 6): Android puts an input sink (ActivityRecordInputSink) under every
// activity, which takes the touches meant for the app behind it, so even a window that takes no
// touches and is fully transparent did not let a tap through to the launcher below.
//
// It asks the shared BackgroundWork for the change, through MainApplication's container, and
// stays in front until the change has ended (about a second and a half from a cold start), so
// Android keeps the process running for it and counts the tap as the person using the app.
// Then it shows the pop-up the outcome calls for (ShortcutSentence), or opens the app when there
// are no pictures yet, and finishes.
//
// Declared in AndroidManifest.xml by hand rather than by the [Activity] attribute: its
// configChanges must include assetsPaths, which the manifest generator drops. A new wallpaper
// rebuilds the system's color overlays, which would otherwise recreate this activity in the
// middle of its own change. The attribute only fixes the Java name, which the entries name and
// which must never change (a dragged-out icon names it too).
[Activity(Name = "com.gnostack.papernexus.ShortcutActivity")]
public sealed class ShortcutActivity : global::Android.App.Activity
{
    // The entries' actions and ids, as Resources/xml/shortcuts.xml names them
    internal const string NextAction = "com.gnostack.papernexus.action.NEXT_WALLPAPER";
    internal const string RandomAction = "com.gnostack.papernexus.action.RANDOM_WALLPAPER";
    internal const string NextShortcutId = "next_wallpaper";
    internal const string RandomShortcutId = "random_wallpaper";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // A screen Android recreated mid-change would otherwise ask for a second change
        if (savedInstanceState is not null)
        {
            FinishQuietly();
            return;
        }

        var random = Intent?.Action == RandomAction;
        _ = ChangeAsync(random);
    }

    private async Task ChangeAsync(bool random)
    {
        var started = SystemClock.ElapsedRealtime();
        var outcome = ShortcutChangeOutcome.Failed;
        try
        {
            ReportUsed(random);
            var application = (MainApplication)Application!;
            var work = application.Services.GetRequiredService<BackgroundWork>();
            outcome = await Task.Run(() => work.RunShortcutChangeAsync(random));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("PaperNexus", $"The long-press menu change could not run: {ex}");
        }

        var finished = SystemClock.ElapsedRealtime();
        var processAge = finished - global::Android.OS.Process.StartElapsedRealtime;
        global::Android.Util.Log.Info("PaperNexus",
            $"Shortcut change ({(random ? "random" : "next")}): {outcome}, {finished - started} ms in the activity, {processAge} ms since the process started.");

        ShowOutcome(outcome);
        FinishQuietly();
    }

    // The pop-up for a failure or a phone that forbids changes; the app itself when there are
    // no pictures yet, as the icon opens it (its Current tab downloads on first open)
    private void ShowOutcome(ShortcutChangeOutcome outcome)
    {
        if (outcome == ShortcutChangeOutcome.NoPictures)
        {
            var launch = PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
            StartActivity(launch);
            return;
        }

        var sentence = ShortcutSentence.For(outcome);
        if (sentence is not null)
            Toast.MakeText(ApplicationContext, sentence, ToastLength.Short)!.Show();
    }

    // Tells the launcher the entry was used, for the history it ranks entries by
    private void ReportUsed(bool random)
    {
        var shortcuts = (global::Android.Content.PM.ShortcutManager?)GetSystemService(ShortcutService);
        var id = random ? RandomShortcutId : NextShortcutId;
        shortcuts?.ReportShortcutUsed(id);
    }

    // Ends the invisible screen with no closing animation
    private void FinishQuietly()
    {
        Finish();
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            OverrideActivityTransition(OverrideTransition.Close, 0, 0);
        else
            OverridePendingTransition(0, 0);
    }
}

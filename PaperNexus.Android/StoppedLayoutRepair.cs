using Android.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PaperNexus;

// A repair for Avalonia 12.1.1 on Android. While the activity is stopped, Android takes its drawing
// surface away and Avalonia lays the screen out against a one-pixel size whenever something on it
// changes size; when the activity returns, Avalonia measures again at the real size but does not
// arrange again, so the tab bar, the picture and the rest stay collapsed (issue #203, step 7; see
// docs/android.md, "Android 12"). When the surface comes back after it was lost, this marks the
// measure and arrange of every control on the screen invalid, once, so the next layout pass lays
// the whole screen out again at its real size: every tab, an open form, Secrets, a question or a
// picker, and the easter egg overlay.
//
// Re-check it whenever Avalonia is updated: once a return lays the screen out correctly without
// it, it can go.
internal sealed class StoppedLayoutRepair : Java.Lang.Object, ISurfaceHolderCallback
{
    private readonly Func<Visual?> _screen;
    private bool _surfaceWasLost;

    // screen gives the control the activity shows, whose top level is laid out again
    public StoppedLayoutRepair(Func<Visual?> screen)
    {
        _screen = screen;
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
    }

    // The surface is back at its real size. Avalonia's own callback on this surface was added
    // first, so it has already taken the size; the screen is laid out again after the layout
    // pass that size starts, at background priority.
    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
    {
        if (!_surfaceWasLost)
            return;
        _surfaceWasLost = false;
        Dispatcher.UIThread.Post(LayOutAgain, DispatcherPriority.Background);
    }

    // Measured: Avalonia reports a one-pixel size from here until the surface is back.
    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _surfaceWasLost = true;
    }

    private void LayOutAgain()
    {
        var screen = _screen();
        var topLevel = screen is null ? null : TopLevel.GetTopLevel(screen);
        if (topLevel is null)
            return;
        var controls = topLevel.GetSelfAndVisualDescendants().OfType<Layoutable>().ToList();
        foreach (var control in controls)
        {
            control.InvalidateMeasure();
            control.InvalidateArrange();
        }
        global::Android.Util.Log.Debug("PaperNexus", $"Screen laid out again after its surface came back ({controls.Count} controls).");
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PaperNexus.Core;

namespace PaperNexus.Views;

// The phone's whole screen: the app's pages, with the easter egg overlay above them, so an egg
// covers the tab bar and the system bars' strip as the desktop's covers its whole window. Any
// screen's view model asks for an egg through EasterEggRequests; this plays it.
//
// The pages keep receiving the system bars' insets and Android's back: the page host reads them
// from the window itself, and this panel takes no inset padding of its own. While an egg plays,
// back skips it (as Esc does on the desktop) before any page sees it, because this panel joins
// the window's back event before the page host inside it does. A tap anywhere skips it too.
public sealed class EasterEggStage : Panel
{
    // The line under an egg's message, worded for touch (the desktop's says "CLICK OR PRESS ESC").
    public const string DismissHint = "TAP OR PRESS BACK";

    private readonly EasterEggRequests _requests;
    private readonly EasterEggPlayback _playback;
    private readonly EasterEggOverlay _overlay;
    private TopLevel? _topLevel;

    public EasterEggStage(Control pages, EasterEggRequests requests, EasterEggProgress progress, EasterEggPlayback playback)
    {
        _requests = requests.ThrowIfNull();
        _playback = playback.ThrowIfNull();
        // The page host pads each page for the system bars itself
        SetValue(TopLevel.AutoSafeAreaPaddingProperty, false);
        Children.Add(pages.ThrowIfNull());
        _overlay = new EasterEggOverlay { DismissHint = DismissHint };
        _overlay.UseProgress(progress.ThrowIfNull());
        Children.Add(_overlay);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _requests.Requested += OnRequested;
        _overlay.Dismissed += OnDismissed;
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.BackRequested += OnBackRequested;
        // A show the screen before this one was playing continues, once the panel has its size
        Dispatcher.UIThread.Post(ContinueShow, DispatcherPriority.Loaded);
    }

    // A dead screen plays nothing; the show stays recorded for the next screen.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _requests.Requested -= OnRequested;
        _overlay.Dismissed -= OnDismissed;
        if (_topLevel is not null)
            _topLevel.BackRequested -= OnBackRequested;
        _topLevel = null;
        _overlay.Dismiss();
    }

    // Raised on the thread that asked.
    private void OnRequested(EasterEggShow show)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _playback.Start(show);
            _overlay.Play(show);
        });
    }

    private void ContinueShow()
    {
        var show = _playback.Current;
        if (show is null || _overlay.IsPlaying || _topLevel is null)
            return;
        var frame = _playback.ElapsedFrame;
        if (frame >= EasterEggAnimation.TotalFrames)
        {
            _playback.Clear();
            return;
        }
        _overlay.Play(show, frame);
    }

    private void OnDismissed()
    {
        _playback.Clear();
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || !_overlay.IsPlaying)
            return;
        _overlay.Dismiss();
        e.Handled = true;
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PaperNexus.Core;

namespace PaperNexus.Views;

// Full-window overlay that plays a short pixel animation for an easter egg, then dismisses
// itself. Positions come from EasterEggAnimation, which is a pure function of frame number,
// so this class only creates shapes and moves them - there is no animation state here to get
// out of step with what the tests assert.
public partial class EasterEggOverlay : UserControl
{
    // One entry per sprite pixel, so a frame update is a straight loop of Canvas.SetLeft/Top
    // rather than rebuilding the visual tree.
    private readonly List<(Rectangle Shape, int OffsetX, int OffsetY, int Particle)> _pixels = [];

    private DispatcherTimer? _timer;
    private int _frame;
    private EasterEggShow? _show;

    // Supplied by the hosting window, because Avalonia creates this control from markup and
    // it cannot receive constructor arguments. Null only in a host that never provided one,
    // in which case the animation still plays and nothing is recorded.
    private EasterEggProgress? _progress;

    public EasterEggOverlay()
    {
        InitializeComponent();
        // Clicking anywhere skips the rest of the animation.
        PointerPressed += (_, _) => Dismiss();
    }

    // Called once by the hosting window with the container's recorder.
    public void UseProgress(EasterEggProgress progress)
    {
        _progress = progress.ThrowIfNull();
    }

    // Starts an egg. A show already playing is replaced rather than queued, so mashing a
    // trigger restarts the animation instead of stacking overlays.
    public void Play(EasterEggShow show)
    {
        Stop();

        _show = show;
        _frame = 0;
        MessageText.Text = show.Message;
        IsVisible = true;

        BuildScanlines();
        BuildSprites(show);

        // Recording here rather than at each trigger means a new egg cannot be added and
        // silently left out of the checklist.
        if (_progress is not null)
            _ = _progress.RecordAsync(show.Id);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / EasterEggAnimation.FramesPerSecond),
        };
        _timer.Tick += OnTick;
        _timer.Start();

        // Draw frame zero immediately so the overlay never appears empty for one tick.
        RenderFrame();
    }

    // Horizontal lines every few pixels, drawn once per show because the control size does
    // not change while one is playing.
    private void BuildScanlines()
    {
        ScanlineCanvas.Children.Clear();

        var height = Bounds.Height > 0 ? Bounds.Height : 600;
        var width = Bounds.Width > 0 ? Bounds.Width : 900;
        var brush = new SolidColorBrush(Color.Parse("#20000000"));

        for (var y = 0.0; y < height; y += EasterEggAnimation.PixelSize)
        {
            var line = new Rectangle
            {
                Width = width,
                Height = 1,
                Fill = brush,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, 0);
            Canvas.SetTop(line, y);
            ScanlineCanvas.Children.Add(line);
        }
    }

    // Creates one Rectangle per filled pixel of every particle's sprite. The sprites are
    // small (about 30 filled pixels each), so even 28 particles stays under a thousand
    // rectangles, which Avalonia handles comfortably for a three second animation.
    private void BuildSprites(EasterEggShow show)
    {
        SpriteCanvas.Children.Clear();
        _pixels.Clear();

        var filled = EasterEggSprites.FilledPixels(show.Sprite).ToList();

        for (var particle = 0; particle < show.ParticleCount; particle++)
        {
            // One colour per particle, so each sprite reads as a single solid object.
            var colour = Color.Parse(show.Palette[particle % show.Palette.Length]);
            var brush = new SolidColorBrush(colour);

            foreach (var (px, py) in filled)
            {
                var rect = new Rectangle
                {
                    Fill = brush,
                    IsHitTestVisible = false,
                };
                SpriteCanvas.Children.Add(rect);
                _pixels.Add((rect, px, py, particle));
            }
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _frame++;
        if (_frame > EasterEggAnimation.TotalFrames)
        {
            Dismiss();
            return;
        }
        RenderFrame();
    }

    private void RenderFrame()
    {
        if (_show is null)
            return;

        var width = Bounds.Width > 0 ? Bounds.Width : 900;
        var height = Bounds.Height > 0 ? Bounds.Height : 600;

        // Frames are computed per particle, not per pixel, so the sprite stays rigid.
        var frames = new SpriteFrame[_show.ParticleCount];
        for (var i = 0; i < frames.Length; i++)
            frames[i] = EasterEggAnimation.Frame(_show.Motion, i, _frame, width, height);

        foreach (var (shape, offsetX, offsetY, particle) in _pixels)
        {
            var frame = frames[particle];
            var size = EasterEggAnimation.PixelSize * frame.Scale / 2.0;

            shape.Width = size;
            shape.Height = size;
            shape.Opacity = frame.Opacity;
            Canvas.SetLeft(shape, frame.X + offsetX * size);
            Canvas.SetTop(shape, frame.Y + offsetY * size);
        }
    }

    // Esc is routed here from the hosting window, which owns the key handling.
    public void Dismiss()
    {
        Stop();
        IsVisible = false;
        SpriteCanvas.Children.Clear();
        ScanlineCanvas.Children.Clear();
        _pixels.Clear();
        _show = null;
    }

    private void Stop()
    {
        if (_timer is null)
            return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    public bool IsPlaying => _timer is not null;
}

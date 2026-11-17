using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PaperNexus.Core.Imaging;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Shows the current wallpaper's source picture full width. The picture is scaled to the
// screen's own pixel width by the shared imaging layer rather than shown at its full size: a 4K
// picture would otherwise take about 33 MB of memory to show at a third of its resolution.
public partial class CurrentWallpaperView : UserControl
{
    private CurrentWallpaperViewModel? _viewModel;
    private string? _shownPath;
    private int _shownWidth;

    public CurrentWallpaperView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as CurrentWallpaperViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowPicture();
    }

    // The width is known only after the first layout, so a picture that arrived before it is
    // shown now.
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ShowPicture();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurrentWallpaperViewModel.PicturePath))
            ShowPicture();
    }

    // Scales the view model's picture to this view's width in pixels on a background thread
    // and shows it, releasing the previous one. Runs again when the width changes: the first
    // layout passes measure the screen at a placeholder size before its real one.
    private async void ShowPicture()
    {
        var path = _viewModel?.PicturePath;
        var topLevel = TopLevel.GetTopLevel(this);
        if (path is null || topLevel is null || Bounds.Width < 1)
            return;
        var pixelWidth = Math.Max(1, (int)Math.Round(Bounds.Width * topLevel.RenderScaling));
        if (path == _shownPath && pixelWidth == _shownWidth)
            return;
        _shownPath = path;
        _shownWidth = pixelWidth;

        Bitmap bitmap;
        try
        {
            var png = await Task.Run(() => ImageResizing.CreatePngOfWidth(path, pixelWidth));
            using var stream = new MemoryStream(png);
            bitmap = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            // An unreadable picture leaves the previous one shown; the next change tries again
            global::Android.Util.Log.Warn("PaperNexus", $"Could not show the picture {path}: {ex.Message}");
            _shownPath = null;
            _shownWidth = 0;
            return;
        }

        // A newer picture or width may have been asked for while this one was scaled
        if (_shownPath != path || _shownWidth != pixelWidth)
        {
            bitmap.Dispose();
            return;
        }

        var previous = Picture.Source as Bitmap;
        Picture.Source = bitmap;
        previous?.Dispose();
    }
}

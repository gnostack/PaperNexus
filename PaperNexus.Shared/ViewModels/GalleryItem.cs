using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

public partial class GalleryItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteToolTip))]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isBanned;

    // True while one of this card's actions runs. The phone dims the card's buttons
    // meanwhile; the desktop's cards do not show it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(CanSetAsCurrent))]
    private bool _isBusy;

    // False on a device that forbids changing the wallpaper; the set-as-current button then
    // stays dimmed. Set when the card is created.
    public bool AllowsSetAsCurrent { get; init; } = true;

    public bool CanAct => !IsBusy;

    public bool CanSetAsCurrent => AllowsSetAsCurrent && !IsBusy;

    // What the heart does when tapped, as its label says it.
    public string FavoriteToolTip => IsFavorite ? "Remove from favorites" : "Add to favorites";

    public string FilePath { get; }
    public string DisplayName { get; }

    private readonly Func<GalleryItem, Task> _setAsCurrent;
    private readonly Func<GalleryItem, Task> _toggleFavorite;
    private readonly Func<GalleryItem, Task> _toggleBan;
    private readonly Func<GalleryItem, Task> _delete;

    // Stores the action delegates as callbacks so the item can trigger ViewModel operations
    // without holding a direct reference to the ViewModel (avoids tight coupling).
    public GalleryItem(
        string path,
        bool isFavorite,
        bool isBanned,
        Func<GalleryItem, Task> setAsCurrent,
        Func<GalleryItem, Task> toggleFavorite,
        Func<GalleryItem, Task> toggleBan,
        Func<GalleryItem, Task> delete)
    {
        FilePath = path;
        DisplayName = WallpaperDisplayName.FromPath(path);
        _isFavorite = isFavorite;
        _isBanned = isBanned;
        _setAsCurrent = setAsCurrent;
        _toggleFavorite = toggleFavorite;
        _toggleBan = toggleBan;
        _delete = delete;
    }

    [RelayCommand]
    private Task SetAsCurrent() => RunAsync(_setAsCurrent);

    [RelayCommand]
    private Task ToggleFavorite() => RunAsync(_toggleFavorite);

    [RelayCommand]
    private Task ToggleBan() => RunAsync(_toggleBan);

    [RelayCommand]
    private Task Delete() => RunAsync(_delete);

    // Marks the card busy while the action runs, so the phone dims its buttons.
    private async Task RunAsync(Func<GalleryItem, Task> action)
    {
        IsBusy = true;
        try
        {
            await action(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private CancellationTokenSource? _loadCts;

    // Starts an async thumbnail load for this item, pixelWidth pixels wide (the desktop's
    // 600 unless the screen asks for its own width). Cancels and disposes any previous
    // in-flight load first so the old CancellationTokenSource does not leak.
    public async Task LoadAsync(int pixelWidth = ImageResizing.ThumbnailWidth)
    {
        // Swap in a fresh CTS and dispose the old one to avoid a handle leak.
        var oldCts = _loadCts;
        oldCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        oldCts?.Dispose();

        var bmp = await LoadThumbnailAsync(FilePath, pixelWidth);
        if (!ct.IsCancellationRequested)
            Thumbnail = bmp;
        else
            bmp?.Dispose();
    }

    // Cancels any in-flight load, disposes the CancellationTokenSource, and clears
    // the thumbnail bitmap to free both managed and unmanaged memory.
    public void DisposeThumbnail()
    {
        var cts = _loadCts;
        _loadCts = null;
        cts?.Cancel();
        cts?.Dispose();

        var bmp = Thumbnail;
        Thumbnail = null;
        bmp?.Dispose();
    }

    // Decodes the image at path, scales it to pixelWidth (height proportional), and
    // returns an Avalonia Bitmap. The semaphore limits concurrent decodes to 4 to
    // avoid exhausting memory when the gallery is large. Returns null on any error.
    internal static async Task<Bitmap?> LoadThumbnailAsync(string path, int pixelWidth = ImageResizing.ThumbnailWidth)
    {
        try
        {
            await _thumbnailSemaphore.WaitAsync();
            try
            {
                // Decoding is synchronous and CPU-bound; keep it off the UI thread that
                // populates the gallery.
                var png = await Task.Run(() => ImageResizing.CreatePngOfWidth(path, pixelWidth));
                using var ms = new MemoryStream(png);
                return new Bitmap(ms);
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }
        catch
        {
            return null;
        }
    }

    private static readonly SemaphoreSlim _thumbnailSemaphore = new(4, 4);
}

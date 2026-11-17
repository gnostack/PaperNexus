using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

public partial class GalleryItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isBanned;

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
        DisplayName = GetDisplayName(path);
        _isFavorite = isFavorite;
        _isBanned = isBanned;
        _setAsCurrent = setAsCurrent;
        _toggleFavorite = toggleFavorite;
        _toggleBan = toggleBan;
        _delete = delete;
    }

    [RelayCommand]
    private Task SetAsCurrent() => _setAsCurrent(this);

    [RelayCommand]
    private Task ToggleFavorite() => _toggleFavorite(this);

    [RelayCommand]
    private Task ToggleBan() => _toggleBan(this);

    [RelayCommand]
    private Task Delete() => _delete(this);

    private CancellationTokenSource? _loadCts;

    // Starts an async thumbnail load for this item. Cancels and disposes any previous
    // in-flight load first so the old CancellationTokenSource does not leak.
    public async Task LoadAsync()
    {
        // Swap in a fresh CTS and dispose the old one to avoid a handle leak.
        var oldCts = _loadCts;
        oldCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        oldCts?.Dispose();

        var bmp = await LoadThumbnailAsync(FilePath);
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

    // Decodes the image at path, scales it to 600 px wide (height proportional), and
    // returns an Avalonia Bitmap. The semaphore limits concurrent decodes to 4 to
    // avoid exhausting memory when the gallery is large. Returns null on any error.
    internal static async Task<Bitmap?> LoadThumbnailAsync(string path)
    {
        try
        {
            await _thumbnailSemaphore.WaitAsync();
            try
            {
                // Decoding is synchronous and CPU-bound; keep it off the UI thread that
                // populates the gallery.
                var png = await Task.Run(() => ImageResizing.CreateThumbnailPng(path));
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

    private static string GetDisplayName(string path)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var lastSep = nameWithoutExt.LastIndexOf(" - ", StringComparison.Ordinal);
        return lastSep > 0 ? nameWithoutExt[..lastSep] : nameWithoutExt;
    }
}

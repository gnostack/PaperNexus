using Avalonia.Controls;
using PaperNexus.Core.Imaging;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Loads a gallery card's thumbnail when the list builds the card and releases it when the
// list clears the card, as the desktop gallery does, so the list only holds the pictures near
// the screen. The thumbnail is scaled by the shared imaging layer to the screen's own pixel
// width rather than kept at the picture's 4K size.
internal static class CardThumbnails
{
    public static void Load(Control list, ContainerPreparedEventArgs e)
    {
        var pixelWidth = PixelWidth(list);
        CardThumbnailHolder.Prepared(e.Container, pixelWidth);
    }

    public static void Release(ContainerClearingEventArgs e)
    {
        CardThumbnailHolder.Clearing(e.Container);
    }

    // A tab's page leaves the visual tree when another tab is chosen (measured on the
    // emulator), but its list keeps its cards; their thumbnails are released then, so only the
    // tab on screen holds pictures.
    public static void ReleaseAll(ItemsControl list)
    {
        foreach (var container in list.GetRealizedContainers())
        {
            if (container.DataContext is GalleryItem item)
                item.DisposeThumbnail();
        }
    }

    // When the tab is chosen again the kept cards are not built anew, so their thumbnails
    // are loaded here.
    public static void LoadRealized(ItemsControl list)
    {
        var pixelWidth = PixelWidth(list);
        foreach (var container in list.GetRealizedContainers())
        {
            if (container.DataContext is GalleryItem item && item.Thumbnail is null)
                _ = item.LoadAsync(pixelWidth);
        }
    }

    // The card's width in screen pixels: the list's width when it has been laid out, the
    // screen's width before that.
    private static int PixelWidth(Control list)
    {
        var topLevel = TopLevel.GetTopLevel(list);
        var scaling = topLevel?.RenderScaling ?? 1;
        var width = list.Bounds.Width >= 1 ? list.Bounds.Width : topLevel?.Bounds.Width ?? 0;
        var pixels = (int)Math.Round(width * scaling);
        return pixels >= 1 ? pixels : ImageResizing.ThumbnailWidth;
    }
}

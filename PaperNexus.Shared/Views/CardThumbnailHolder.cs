using System.Runtime.CompilerServices;
using Avalonia.Controls;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// Holds a gallery card's thumbnail exactly while a list shows the card, for both apps'
// galleries. A virtualizing list builds only the cards near the screen and reuses their
// containers for other pictures as the person scrolls; by the time it reports a container
// cleared, the container no longer says which picture it showed. So this remembers the picture
// each container shows (ContainerPictures), and releases a picture's thumbnail when its
// container is cleared or reused for another picture, unless another container shows it by then.
//
// Measured on the Android emulator before this existed: after scrolling 55 pictures and
// leaving the gallery, all 55 thumbnails were still held though the list had built 3 cards.
public static class CardThumbnailHolder
{
    private static readonly ContainerPictures Shown = new();

    // A list prepared container to show a picture: the picture it showed before (if another,
    // and shown nowhere else) is let go, and this one's thumbnail is loaded pixelWidth wide.
    public static void Prepared(Control container, int pixelWidth)
    {
        if (container.DataContext is not GalleryItem item)
            return;
        Shown.Show(container, item)?.DisposeThumbnail();
        _ = item.LoadAsync(pixelWidth);
    }

    // A list cleared container: the picture it showed is let go.
    public static void Clearing(Control container)
    {
        var shown = Shown.Clear(container) ?? container.DataContext as GalleryItem;
        shown?.DisposeThumbnail();
    }
}

// Which picture each container shows, and which pictures to let go as containers are reused
// or cleared. Weak on both sides, so it keeps neither a discarded container nor a picture alive.
internal sealed class ContainerPictures
{
    private readonly ConditionalWeakTable<object, GalleryItem> _pictureIn = new();
    private readonly ConditionalWeakTable<GalleryItem, object> _containerOf = new();

    // The container now shows item. Returns the picture it showed before when that picture is
    // no longer shown anywhere, or null.
    public GalleryItem? Show(object container, GalleryItem item)
    {
        GalleryItem? released = null;
        if (_pictureIn.TryGetValue(container, out var previous) && !ReferenceEquals(previous, item))
            released = LetGo(container, previous);

        _pictureIn.AddOrUpdate(container, item);
        _containerOf.AddOrUpdate(item, container);
        return released;
    }

    // The container shows nothing now. Returns the picture it showed when that picture is no
    // longer shown anywhere, or null.
    public GalleryItem? Clear(object container)
    {
        if (!_pictureIn.TryGetValue(container, out var item))
            return null;
        _pictureIn.Remove(container);
        return LetGo(container, item);
    }

    // The picture's thumbnail may go unless another container shows it now.
    private GalleryItem? LetGo(object container, GalleryItem item)
    {
        if (_containerOf.TryGetValue(item, out var holder) && !ReferenceEquals(holder, container))
            return null;
        _containerOf.Remove(item);
        return item;
    }
}

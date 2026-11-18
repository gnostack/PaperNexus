using Avalonia;
using Avalonia.Controls;

namespace PaperNexus.Views;

// Holds a gallery card's picture in a 16:9 box as wide as the card. Every card is then the
// same height whether or not its thumbnail has loaded, so the list does not jump as pictures
// arrive while scrolling, and the virtualizing list can estimate the rows it has not built.
// A picture of another shape is fitted inside the box (Stretch="Uniform" on the image).
public class PictureFrame : Decorator
{
    private const double AspectRatio = 16.0 / 9.0;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var size = new Size(width, width / AspectRatio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = new Size(finalSize.Width, finalSize.Width / AspectRatio);
        Child?.Arrange(new Rect(size));
        return size;
    }
}

namespace PaperNexus.ViewModels;

// A list view model that keeps how far its list was scrolled, in the view's units, so a view
// built for a recreated phone screen can return to the same place.
public interface IScrollPlace
{
    public double ScrollOffset { get; set; }
}

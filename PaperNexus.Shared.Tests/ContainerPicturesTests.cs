using PaperNexus.ViewModels;
using PaperNexus.Views;
using Xunit;

namespace PaperNexus.Tests;

// The rule that frees gallery thumbnails as a virtualizing list reuses its card containers:
// a picture's thumbnail goes when the container that showed it is cleared or reused, unless
// another container shows it by then.
public class ContainerPicturesTests
{
    private static GalleryItem Picture(string name) =>
        new($"/tmp/{name} - x.png", false, false, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);

    [Fact]
    public void Reused_ForAnotherPicture_LetsTheFirstGo()
    {
        var shown = new ContainerPictures();
        var container = new object();
        var a = Picture("A");
        var b = Picture("B");

        Assert.Null(shown.Show(container, a));
        Assert.Same(a, shown.Show(container, b));
    }

    [Fact]
    public void ShownAgainInTheSameContainer_KeepsIt()
    {
        var shown = new ContainerPictures();
        var container = new object();
        var a = Picture("A");

        shown.Show(container, a);

        Assert.Null(shown.Show(container, a));
    }

    [Fact]
    public void Cleared_LetsThePictureGo_Once()
    {
        var shown = new ContainerPictures();
        var container = new object();
        var a = Picture("A");
        shown.Show(container, a);

        Assert.Same(a, shown.Clear(container));
        Assert.Null(shown.Clear(container));
    }

    // The list moved the picture to another container before reusing the first one: the
    // picture is still on screen, so its thumbnail stays.
    [Fact]
    public void PictureMovedToAnotherContainer_IsKept()
    {
        var shown = new ContainerPictures();
        var first = new object();
        var second = new object();
        var a = Picture("A");
        var b = Picture("B");
        shown.Show(first, a);
        shown.Show(second, a);

        Assert.Null(shown.Show(first, b));
        Assert.Same(b, shown.Clear(first));
        Assert.Same(a, shown.Clear(second));
    }

    // Scrolling a long list through three containers lets go of every picture that left.
    [Fact]
    public void ScrollingThroughThreeContainers_HoldsOnlyThreePictures()
    {
        var shown = new ContainerPictures();
        var containers = new[] { new object(), new object(), new object() };
        var pictures = Enumerable.Range(0, 55).Select(i => Picture($"P{i}")).ToList();
        var held = new HashSet<GalleryItem>();

        for (var i = 0; i < pictures.Count; i++)
        {
            var released = shown.Show(containers[i % 3], pictures[i]);
            if (released is not null)
                held.Remove(released);
            held.Add(pictures[i]);
        }

        Assert.Equal(3, held.Count);
        Assert.Equal(pictures.TakeLast(3), held.OrderBy(p => pictures.IndexOf(p)));
    }
}

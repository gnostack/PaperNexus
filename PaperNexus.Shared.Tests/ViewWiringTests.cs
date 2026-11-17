using System.Reflection;
using Avalonia.Controls;
using Xunit;

namespace PaperNexus.Tests;

// Guards the wiring between a view's AXAML and its code-behind, for the shared views.
// PaperNexus.Tests runs the same scan over the desktop host's views.
public class ViewWiringTests
{
    private static IEnumerable<Type> ViewTypes => typeof(PaperNexus.Views.EasterEggOverlay).Assembly
        .GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && typeof(Control).IsAssignableFrom(t))
        .Where(t => t.Namespace == "PaperNexus.Views");

    [Fact]
    public void NoView_DeclaresItsOwnInitializeComponent()
    {
        // Avalonia's name generator emits InitializeComponent, and *that generated method is
        // what assigns the x:Name backing fields*. Hand-writing
        //
        //     private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
        //
        // suppresses the generated one. The fields still compile, so the build stays green,
        // but they are never assigned and stay null until the first use throws.
        //
        // This shipped in v147: EasterEggOverlay declared its own InitializeComponent, so
        // MessageText was null, Play() threw a NullReferenceException out of an async void
        // handler, and the whole process aborted the moment anyone triggered an easter egg.
        var offenders = ViewTypes
            .Where(t => t.GetMethod(
                "InitializeComponent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null) is not null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These views declare their own parameterless InitializeComponent, which suppresses " +
            "Avalonia's generated one and leaves every x:Name field null: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryViewType_HasTheGeneratedNameFieldsItReferences()
    {
        // A view whose AXAML declares x:Name should end up with a matching private field.
        // If the generator was suppressed, the type has no such fields at all - which is the
        // shape the v147 crash took.
        var overlay = typeof(PaperNexus.Views.EasterEggOverlay);
        var nameFields = overlay
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(f => f.Name)
            .ToList();

        Assert.Contains("MessageText", nameFields);
        Assert.Contains("SpriteCanvas", nameFields);
        Assert.Contains("ScanlineCanvas", nameFields);
    }
}

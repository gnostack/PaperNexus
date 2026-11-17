using System.Reflection;
using Avalonia.Controls;
using Xunit;

namespace PaperNexus.Tests;

// Guards the wiring between a view's AXAML and its code-behind, for the desktop host's views.
// PaperNexus.Shared.Tests runs the same check over the shared views.
public class ViewWiringTests
{
    private static IEnumerable<Type> ViewTypes => typeof(App).Assembly
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
}

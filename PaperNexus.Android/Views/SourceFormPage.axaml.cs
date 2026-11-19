using Avalonia.Controls;
using Avalonia.Input;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// The full-screen add or edit source form. MainView opens it over the tabs as a modal page with
// the sources view model's open form as its data, and closes it when the form is done. Android's
// back button or gesture cancels the form, as the desktop dialog's Cancel does.
//
// The soft keyboard never covers the field being typed in or the buttons (KeyboardLift): the
// page is drawn above the part of the screen the keyboard covers, and the field that has the
// focus is scrolled into view with its label and hint. The keyboard's Next key moves to the
// next field.
public partial class SourceFormPage : ContentPage
{
    public SourceFormPage()
    {
        InitializeComponent();
        // Lives as long as the page: it is held by the page's own event handlers
        _ = new KeyboardLift(this, Layout);
        TextBox[] order = [NameBox, UrlBox, ImageUrlJPathBox, TitleJPathBox, CronBox];
        for (var index = 0; index < order.Length; index++)
        {
            var next = index + 1 < order.Length ? order[index + 1] : null;
            order[index].KeyDown += (_, e) => OnFieldKeyDown(e, next);
        }
    }

    // Back cancels the form; the form being done closes this page.
    protected override bool OnSystemBackButtonPressed()
    {
        if (DataContext is SourceFormViewModel form)
            form.CancelCommand.Execute(null);
        return true;
    }

    // Enter is what the keyboard's Next key sends: it moves to the next field. On the last
    // field the key is Done, which the keyboard handles itself.
    private static void OnFieldKeyDown(KeyEventArgs e, TextBox? next)
    {
        if (e.Key != Key.Enter || next is null)
            return;
        e.Handled = true;
        next.Focus();
    }
}

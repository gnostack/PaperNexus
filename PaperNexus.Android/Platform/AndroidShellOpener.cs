using Android.Content;
using PaperNexus.Core;

namespace PaperNexus;

// Opens a web address in the phone's browser (Report a bug, the project link). A failure (no
// app on the phone opens web addresses) reaches the caller, as on the desktop.
internal sealed class AndroidShellOpener : IShellOpener, IAddSingleton<IShellOpener>
{
    public void Open(string target)
    {
        var context = global::Android.App.Application.Context;
        var view = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(target));
        view.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(view);
    }

    // The phone's folders are the app's own storage, which no file manager shows (issue #203,
    // decision 10), and the phone has no folder setting to open.
    public void OpenFolder(string folderPath) =>
        throw new NotSupportedException("The phone does not show the app's folders.");
}

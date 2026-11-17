namespace PaperNexus.Core;

// Hands a file, folder or web address to the device's default handler.
public interface IShellOpener
{
    // Opens a file or web address in its default application.
    public void Open(string target);

    // Shows a folder in the device's file manager.
    public void OpenFolder(string folderPath);
}

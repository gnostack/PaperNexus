namespace PaperNexus.ViewModels;

// The name a person sees for a downloaded wallpaper.
public static class WallpaperDisplayName
{
    // The file name without its extension and without the " - <url file>" suffix the
    // downloader appends to keep same-titled pictures apart.
    public static string FromPath(string path)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var lastSep = nameWithoutExt.LastIndexOf(" - ", StringComparison.Ordinal);
        var name = lastSep > 0 ? nameWithoutExt[..lastSep] : nameWithoutExt;
        return name;
    }
}

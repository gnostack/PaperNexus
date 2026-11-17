namespace PaperNexus;

// Sets the device wallpaper. The host supplies the implementation; tests use a no-op
// double so they never change the real wallpaper.
public interface IWallpaperApplier
{
    public bool SetWallpaper(string wallpaperPath);
    public void ApplyFillStyle(WallpaperFillStyle style);
}

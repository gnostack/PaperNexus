using Android;

// INTERNET: the wallpaper feeds and the pictures they list. Debug builds add it on their own
// for the debugger; a Release build has it only through this line.
[assembly: UsesPermission(Manifest.Permission.Internet)]

// SET_WALLPAPER: WallpaperManager.SetStream. A normal permission, granted at install.
[assembly: UsesPermission(Manifest.Permission.SetWallpaper)]

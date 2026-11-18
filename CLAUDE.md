# CLAUDE.md - AI Assistant Guide for PaperNexus

## Project Overview

.NET 10.0 Avalonia app for automated wallpaper rotation on Windows and Linux, with an Android app in progress (issue #203). Solution: `PaperNexus.sln`, three projects plus two test projects:

- `PaperNexus.Shared` - class library (`net10.0`) with everything both devices use: settings model and store, wallpaper sources and downloads, wallpaper switching, the imaging layer, the view models, easter eggs, logging, DI helpers. No `Avalonia.Desktop` and no Windows/Linux-only API, so the Android host references it unchanged.
- `PaperNexus` - the desktop host (Windows/Linux exe): `Program`, the tray `App`, every window and dialog, install, auto-update, single instance, startup registration and the platform layer. The project, assembly and exe keep the name `PaperNexus`, because the release asset names, `WM_CLASS` and `StartupWMClass` depend on it.
- `PaperNexus.Android` - the Android host (`net10.0-android`, Android 12 / API 31 and later, application ID `com.gnostack.papernexus`): the Android application and activity, the phone's own Avalonia `App`, its screens, and its implementations of the shared host interfaces. It opens on five bottom tabs; the Current tab shows the current wallpaper and its Next and Random buttons change the home screen and lock screen wallpaper (step 3 of issue #203); Gallery and Favorites list the pictures and Current deletes one (step 4, part 1); Sources adds, edits, tests, turns on and off and deletes wallpaper sources and downloads now (step 4, part 2); Settings shows only its name until step 4's last part. Building it needs the .NET `android` workload and the `ANDROID_HOME` and `JAVA_HOME` environment variables, so a solution-level build does too; see `docs/android.md`.
- `PaperNexus.Shared.Tests` covers `PaperNexus.Shared`; `PaperNexus.Tests` covers the desktop host. Each references only the project it covers.

## Quick Reference

```bash
dotnet restore PaperNexus.sln
dotnet build PaperNexus.sln --configuration Release
dotnet run --project PaperNexus --configuration Release -- --debug
dotnet test --configuration Release
```

The solution build includes `PaperNexus.Android`, so it needs `ANDROID_HOME` and `JAVA_HOME` exported; `dotnet build PaperNexus` builds the desktop host alone without them. Android build, install and emulator commands are in `docs/android.md`.

**Always use `--debug` when running locally.** Without it, `dotnet run` triggers the auto-install path (copies exe to `%LOCALAPPDATA%\PaperNexus\`) and exits immediately - the window never appears directly.

After launching, monitor the background task output for runtime errors. Watch for unhandled exceptions, AVLN binding errors, or service failures.

## Repository Structure

```
PaperNexus/
├── PaperNexus.sln                       # Solution file
├── PaperNexus.Desktop.slnf              # Solution filter: everything except PaperNexus.Android
├── CLAUDE.md                            # AI assistant guide (this file)
├── .editorconfig                        # C# code style rules & diagnostics
├── .gitignore                           # Standard Visual Studio .gitignore
├── .claude/                             # Claude Code configuration
├── .github/
│   └── workflows/
│       ├── pull-request.yml             # PR build verification (desktop, shared, tests)
│       ├── pull-request-android.yml     # PR build of the Android host
│       ├── deploy-wallpaper-service.yml # Release builds + code signing
│       └── deploy-website.yml           # Azure Static Web Apps deploy (website/ only)
├── docs/
│   ├── ui-style-guide.md               # Avalonia AXAML patterns reference
│   ├── platform-support.md             # Windows/Linux platform layer reference; local desktop verification (scratch build, screenshots, AT-SPI)
│   └── android.md                      # Android host: build, emulator, Current/Gallery/Favorites/Sources tabs, keyboard, files, wallpaper canvas
├── PaperNexus.Shared/                   # Shared class library (everything both devices use)
│   ├── PaperNexus.Shared.csproj         # Declares every package; embeds RepositoryUrl
│   ├── GlobalUsings.cs                  # Project-wide usings (copy of the host's minus Platform)
│   ├── DownloadWallpapers.cs            # Scheduled wallpaper downloader (IDownloadWallpapers); DownloadOutcome
│   ├── HttpWallpaperSourceService.cs    # HTTP feed client + WallpaperImage DTO
│   ├── SwitchWallpaper.cs               # Wallpaper switching logic + job wrapper
│   ├── WallpaperLibrary.cs              # IWallpaperLibrary: list, heart, ban, delete pictures; Changed
│   ├── WallpaperSources.cs              # IWallpaperSources: list, add, edit, delete, on/off, check, test sources; Changed
│   ├── IWallpaperApplier.cs             # Sets the device wallpaper (host implements); WallpaperCanvas
│   ├── ICheckForUpdates.cs              # Self-update check (only a self-updating host implements)
│   ├── Assets/                          # Cinzel.ttf (embedded) + its OFL licence
│   ├── Core/                            # DI, logging, scheduling, settings, easter eggs
│   │   ├── Imaging/                    # All SkiaSharp code: decode/encode, resize, annotation, fonts, tray icons
│   │   ├── IAppFolders.cs              # Host interface: settings, data and default download folders
│   │   ├── IAppInfo.cs                 # Host interface: version label, started in debug mode
│   │   ├── IShellOpener.cs             # Host interface: open a file/URL/folder
│   │   ├── IStartupRegistration.cs     # Host interface: run at sign-in (optional)
│   │   ├── Bootstrapper.cs             # DI helpers, IAddSingleton<T>, AddServicesFrom()
│   │   ├── Extensions.cs               # Utility extension methods
│   │   ├── FileLogger.cs               # File-based ILogger implementation (async queue)
│   │   ├── GitHubRepository.cs         # Owner/name parsed from the build-embedded RepositoryUrl
│   │   ├── ScheduledService.cs         # ScheduledJobService base, IScheduleScopedJob, JobTimerStore
│   │   ├── SettingsStore.cs            # ISettingsStore: the only reader/writer of settings.json
│   │   └── WallpaperNexusSettings.cs   # Settings model and enums (plain data, no file access)
│   ├── ViewModels/
│   │   ├── WallpaperConfigViewModel.cs # The desktop settings window's view model (CommunityToolkit.Mvvm)
│   │   ├── CurrentWallpaperViewModel.cs # The phone's Current tab
│   │   ├── PictureListViewModel.cs     # The phone's Gallery and Favorites tabs' shared base
│   │   ├── GalleryViewModel.cs         # The phone's Gallery tab
│   │   ├── FavoritesViewModel.cs       # The phone's Favorites tab
│   │   ├── SourcesViewModel.cs         # The phone's Sources tab: rows, Download now, the open form
│   │   ├── SourceFormViewModel.cs      # The add/edit source form (desktop dialog and phone page)
│   │   ├── SourceItem.cs               # One source row (both apps)
│   │   ├── DownloadSentence.cs         # What Download now says, from DownloadOutcome (both apps)
│   │   ├── StatusLine.cs               # The phone tabs' three-second confirmation line
│   │   ├── IScrollPlace.cs             # A list view model that keeps its scroll place
│   │   ├── WallpaperDisplayName.cs     # A wallpaper's shown name from its file name
│   │   └── GalleryItem.cs              # Gallery item view model
│   └── Views/
│       ├── EasterEggOverlay.axaml(.cs) # Easter egg animation control (used by MainWindow)
│       └── CardThumbnailHolder.cs      # Gallery thumbnails held only while a list shows the card (both apps)
├── PaperNexus.Shared.Tests/             # Tests for PaperNexus.Shared (xUnit)
├── PaperNexus/                          # Desktop host (Windows/Linux exe)
│   ├── PaperNexus.csproj                # References PaperNexus.Shared + Avalonia.Desktop
│   ├── App.axaml(.cs)                   # App root, tray icon, startup, DI composition
│   ├── Program.cs                       # Entry point, auto-install, single-instance, IPC
│   ├── AutoUpdateService.cs             # Silent auto-update via GitHub Releases + job wrapper
│   ├── DesktopAppInfo.cs                # IAppInfo for the desktop (App.AppVersion, --debug)
│   ├── NativeMethods.cs                 # WallpaperApplier + Windows wallpaper backend (P/Invoke)
│   ├── Assets/                          # logo.ico, logo.png
│   ├── Core/
│   │   └── Platform/                   # All Windows/Linux differences live here
│   │       ├── PlatformPaths.cs        # Executable name, install dir, Pictures, path comparison; DesktopAppFolders
│   │       ├── SingleInstance.cs       # Mutex+event (Windows) / Unix socket (Linux)
│   │       ├── StartupRegistration.cs  # Run key (Windows) / XDG autostart (Linux); DesktopStartupRegistration
│   │       ├── IWallpaperBackend.cs    # Per-OS wallpaper backend contract
│   │       ├── LinuxWallpaperBackend.cs # KDE / GNOME / feh-xwallpaper-swaybg
│   │       ├── LinuxDesktop.cs         # Desktop detection + helper process runner
│   │       ├── DesktopEntry.cs         # XDG launcher + icon so the app can be pinned
│   │       └── ShellOpener.cs          # explorer.exe / xdg-open, app relaunch; DesktopShellOpener
│   └── Views/
│       ├── MainWindow.axaml(.cs)       # Settings window
│       ├── SplashScreen.axaml(.cs)     # Startup splash
│       ├── InstallScreen.axaml(.cs)    # First-run install screen
│       ├── EasterEggListDialog.axaml(.cs) # Secrets checklist
│       ├── WallpaperSourceDialog.axaml(.cs)  # Add/edit wallpaper source dialog (shows SourceFormViewModel)
│       └── NonScrollableComboBox.cs    # ComboBox that ignores scroll unless dropdown is open
├── PaperNexus.Tests/                    # Tests for the desktop host (xUnit)
└── PaperNexus.Android/                  # Android host (net10.0-android)
    ├── PaperNexus.Android.csproj        # References PaperNexus.Shared + Avalonia.Android; links the desktop logo
    ├── MainApplication.cs               # [Application]: label, icon, theme; builds the service container
    ├── MainActivity.cs                  # Launcher activity (AvaloniaMainActivity); fixed Java name
    ├── AndroidManifest.xml              # Hand-written activity entry: launcher, configChanges incl. assetsPaths
    ├── App.axaml(.cs)                   # The phone's Avalonia App: MainView in a bar-less NavigationPage inside PageNavigationHost
    ├── Platform/
    │   ├── AndroidAppFolders.cs        # IAppFolders: files (settings) and no_backup (everything else)
    │   ├── AndroidWallpaperApplier.cs  # IWallpaperApplier: WallpaperManager, both screens; the canvas
    │   └── AndroidPermissions.cs       # INTERNET and SET_WALLPAPER
    ├── Views/
    │   ├── MainView.axaml(.cs)         # TabbedPage: Current, Gallery, Favorites, Sources, Settings
    │   ├── CurrentWallpaperView.axaml(.cs) # The Current tab
    │   ├── GalleryView.axaml(.cs)      # The Gallery tab
    │   ├── FavoritesView.axaml(.cs)    # The Favorites tab
    │   ├── SourcesView.axaml(.cs)      # The Sources tab
    │   ├── SourceFormPage.axaml(.cs)   # Full-screen add/edit source form (modal page); keeps above the keyboard
    │   ├── PictureListStyles.axaml     # Card and picture-button styles
    │   ├── StatusLineStyles.axaml      # Confirmation-line styles (every tab)
    │   ├── PictureFrame.cs             # 16:9 picture box so every card has the same height
    │   ├── CardThumbnails.cs           # Load/release card thumbnails at the screen's pixel width
    │   ├── MainScreenState.cs          # Selected tab, kept for the process's lifetime
    │   ├── ListPlace.cs                # Keeps a list's scroll place in its view model
    │   └── ConfirmationDialog.axaml(.cs) # Reusable in-page question (dimmed page, centered box)
    └── Resources/
        ├── values/styles.xml           # Android theme (AppCompat, no action bar, #2B2B2B window)
        └── mipmap-anydpi-v26/icon.xml  # Adaptive launcher icon around the linked logo
```

## Key Architecture

- **MVVM** with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
- **Scheduled Jobs (preferred):** `IScheduleScopedJob` - separate business logic from scheduling. Job wrapper delegates to injected interface. Run on the process's own timer by `AddInProcessSchedules()`, which the desktop calls and the phone does not (the phone's slideshow is Android's to schedule, issue #203 step 5).
- **Scheduled Jobs (legacy):** `ScheduledJobService` base class - `DownloadWallpapers` extends directly. Registered via `IAddHostedSingleton<T>`.
- **DI:** `AddServicesFrom(assembly)` auto-discovers `IAddSingleton<T>` and `IAddHostedSingleton<T>` implementations; `AddInProcessSchedules(assembly)` wraps each `IScheduleScopedJob` in a hosted scheduler. The desktop `App` calls both on the `PaperNexus.Shared` assembly, then on the desktop assembly; the phone's `MainApplication` calls only `AddServicesFrom`, on the shared assembly then its own. `timers.json` belongs to the `JobTimerStore` singleton, which both scheduler types receive by constructor. Screens are per-request registrations listed explicitly in `App`'s service setup (`MainWindow`, `SplashScreen`, `EasterEggListDialog`, `WallpaperConfigViewModel`, plus a `Func<EasterEggListDialog>` factory); `App` resolves them from its host instead of calling `new`. These windows have no parameterless constructor, so `AVLN3001` is suppressed in the csproj and the IDE markup previewer cannot open them.
- **Host interfaces (`PaperNexus.Shared/Core/`):** shared code never calls a desktop-only type; it receives what only the host knows by constructor injection. `IAppFolders` (the folder holding `settings.json`, the data folder for `current.png`, `timers.json` and `logs/`, and the default download folder), `IAppInfo` (version label, started in debug mode), `IShellOpener` (open a file, URL or folder), `IStartupRegistration` (run at sign-in), plus `IWallpaperApplier` and `ICheckForUpdates`. The desktop implementations (`DesktopAppFolders`, `DesktopAppInfo`, `DesktopShellOpener`, `DesktopStartupRegistration`, `WallpaperApplier`, `AutoUpdateService`) and the phone's (`AndroidAppFolders`, `AndroidWallpaperApplier`) register through `IAddSingleton<T>`. Shared code never uses `AppContext.BaseDirectory` or a Pictures folder itself: those are `DesktopAppFolders` answers. `IStartupRegistration`, `ICheckForUpdates`, `ISwitchWallpaper` and `IDownloadWallpapers` are optional view model parameters: a host without the capability registers nothing and the view model skips or reports it. A host uses only the shared project's `public` types; `InternalsVisibleTo` is granted to the test projects only.
- **Settings store (`Core/SettingsStore.cs`):** `ISettingsStore` (`LoadAsync` / `UpdateAsync(change)`) is a container singleton rooted at `IAppFolders.SettingsFolder` (the default install directory on the desktop, the app's files folder on the phone). It also fills an empty download folder with `IAppFolders.DefaultDownloadFolder`, so the settings model names no folder itself. **Nothing reaches `settings.json` except through `ISettingsStore`**, received by constructor injection; tests root a store in a temp directory via `TestHelpers.CreateSettingsStore()`. The one exception is `InstallScreen`, which runs before the container exists and writes a fresh file directly to the `PlatformPaths` location. **Every change goes through `UpdateAsync`**, which reads the latest settings, applies the caller's change and writes them back under one lock; each writer sets only the fields it owns (the switcher the current wallpaper, the downloader each source's download time and the retention pruning, the sources service the list of sources, the settings window the settings it edits, the favorite/ban/delete commands their lists). There is deliberately no way to save a whole settings object read earlier: a writer that worked for seconds between its read and its save used to overwrite favorites, bans and the current wallpaper saved meanwhile. Tests put a prepared file in place with the internal `SettingsStore.WriteAsync`.
- **Platform Layer (`PaperNexus/Core/Platform/`, desktop host only):** Every Windows/Linux difference is isolated here - no other file may call a Windows-only API, hardcode `.exe`, or compare paths case-insensitively. See `docs/platform-support.md`.
- **Imaging Layer (`PaperNexus.Shared/Core/Imaging/`):** All image decoding, encoding and drawing lives here - resize-to-fit, gallery thumbnails, icon scaling, the tray menu icons, wallpaper annotation, the PNG/JPEG encode, and font lookup. No other file in either project may reference SkiaSharp (the desktop host calls `ImageResizing` and `MenuIcons` for its tray and launcher icons); callers pass paths, streams and settings in and get bytes back. Tests may use SkiaSharp only to build fixture images or read dimensions.
- **Auto-Update:** Queries GitHub Releases API for the repository returned by `GitHubRepository.Current`, compares `vN` tag as integer against `Assembly.Version.Major`, downloads the per-platform asset (`PaperNexus.exe` / `PaperNexus-linux-x64`), verifies it (Authenticode on Windows, published SHA-256 on Linux), swaps via self-deleting script (`.bat` / `.sh`) with rollback.
- **Repository location (`PaperNexus.Shared/Core/GitHubRepository.cs`):** Never hardcoded. The GitHub repository is derived from the git `origin` remote at build time (`PublishRepositoryUrl` in `PaperNexus.Shared.csproj` embeds it as `AssemblyMetadata("RepositoryUrl")` in the shared assembly; the `RequireRepositoryUrl` target there fails the build without one, which also fails a desktop build), and `GitHubRepository` parses owner/name for the auto-updater and the Report Bug / project links. Build from a clone with an `origin` remote.
- **Auto-Install:** First run copies exe to the chosen install directory (default `%LOCALAPPDATA%\PaperNexus\`), migrates settings, writes a `.installed` sentinel file alongside the exe, then relaunches. On subsequent launches, `IsRunningFromInstallLocation` detects the sentinel file so custom install paths (not equal to the default AppData path) are recognised correctly and the install flow is not re-triggered.
- **Single Instance:** Named `Mutex` + `EventWaitHandle` for IPC (signals running instance to show UI).
- **Tray-only:** `ShutdownMode.OnExplicitShutdown`. Menu: "Open Settings", "Next Wallpaper", "Random Wallpaper", "Exit". Each item has a programmatically-drawn icon (`Core/Imaging/MenuIcons.cs`).
- **Wallpaper Processing:** Writes to `current.png`/`.jpg` in `IAppFolders.DataFolder`. `WallpaperRenderer` draws the title overlay (`WallpaperAnnotator`, SkiaSharp with HarfBuzz shaping for kerning) at switch time. PNG (8-bit RGB) preferred; JPEG fallback stepping quality down from 97 if >16 MB. When `IWallpaperApplier.GetCanvas()` returns a `WallpaperCanvas` (the phone), the picture is first drawn onto the canvas for the fill style (`WallpaperFitting`; Span draws as Fill) and the title is placed inside the canvas's title area, the part the screen shows clear of the system bars; with `null` (the desktop) the picture keeps its size and the operating system fits it.
- **`ISwitchWallpaper`:** Exposes `WallpaperChanged`, `SwitchToNextAsync()`, `SwitchToRandomAsync()`, `SwitchToSpecificAsync()` and `HasWallpapersAsync()`. Switches run one at a time. A wallpaper the device refuses (`SetWallpaper` returns false) throws `WallpaperRefusedException`; it is not recorded as current and `WallpaperChanged` is not raised.
- **Downloads:** one run at a time. Each picture is written as `<name>.download` and renamed into place when complete, the way the settings store saves; each run deletes partial files a stopped process left behind, and nothing lists a `.download` file as a picture.
- **Wallpaper Sources (JPath-based):** `HttpWallpaperSourceService` uses Newtonsoft `SelectTokens` with `ImageUrlJPath`/`TitleJPath`. **The rules for sources are `IWallpaperSources` (`PaperNexus.Shared/WallpaperSources.cs`), for both devices**: listing, adding, editing (a source that keeps its name keeps its last download time), deleting, turning on and off, the checks a source must pass (name, an https address, both JPaths, a 5-field cron; a blank cron is every hour), and Test (the same address and JPath checks, then a feed read). Names are unique ignoring case after trimming. A source is known by its `SourceKey` (name plus which of the same-named sources it is), which each `SourceItem` row carries, so an older file holding two sources of one name loads as it is and each row's edit, switch and delete act on exactly its own source. Each change is saved at once through `ISettingsStore.UpdateAsync` touching only `Sources`, and `Changed` is raised after it. **No view model or view edits `settings.Sources` itself, and the desktop settings window's save never writes sources.** The add/edit form is `SourceFormViewModel`, shown by the desktop's `WallpaperSourceDialog` and the phone's `SourceFormPage`; rows are `SourceItem`, with the edit and delete buttons on each row. Download now is `IDownloadWallpapers.DownloadAllAsync`, which returns a `DownloadOutcome` (sources tried, sources failed); `DownloadSentence` words it for both devices.
- **`NonScrollableComboBox`:** Suppresses scroll wheel unless dropdown is open - prevents accidental changes while scrolling the settings page.
- **Picture library (`PaperNexus.Shared/WallpaperLibrary.cs`):** `IWallpaperLibrary` holds the rules for listing the gallery (newest first by last write time) and the favorites (hearted order), hearting, banning and deleting pictures, for both devices. **No view model reads or writes `FavoriteWallpapers`/`BannedWallpapers` or deletes a picture file itself**; each asks the library and shows the result. One delete rule: the file, its favorite and its ban go, and deleting the current wallpaper sets the next one. The library raises `Changed` after every save (and `PicturesChanged` after a download run), so every open screen follows changes made on another; view models apply it on the thread that created them. Setting a picture as the wallpaper stays with `ISwitchWallpaper`.
- **Favorites:** Heart toggle, stored in `settings.json`, excluded from retention cleanup. The Favorites tab shows one `GalleryItem` card per favorite (thumbnail, title, set-as-current and remove buttons), built by the ViewModel's `FavoriteWallpapers` collection and sharing the Gallery's thumbnail load/dispose handlers.
- **Easter eggs (`Core/EasterEggs.cs`, `EasterEggSprites.cs`, `EasterEggAnimation.cs`, `PaperNexus.Shared/Views/EasterEggOverlay.axaml`):** Eggs play a retro pixel animation in a full-window overlay - auto-dismisses after ~3s, click or Esc skips. Sprites are `'X'`/`'.'` bitmasks; motion is a pure function of (particle, frame) with no `Random` and no accumulated state, so the visuals are unit tested and can be rendered offscreen for inspection. The overlay only creates rectangles and moves them. The ViewModel raises `EasterEggTriggered`; `MainWindow` owns the overlay. Currently: Konami (burst), version-label ×5 (rise), favorite milestones (rise), word-spelling annotation hex colours (fall), plus an occasional alternate splash line. Each show carries a catalog `Id`, and `EasterEggOverlay.Play` records discovery through the injected `EasterEggProgress` service (handed to the overlay by `MainWindow`, since Avalonia creates the overlay from markup), so a new egg cannot be left out of the checklist. The **Secrets** button beside the version label opens `EasterEggListDialog`, a checklist of `EasterEggCatalog.All` with progress; discovery ids persist in `settings.json` as `DiscoveredEasterEggs` and **must never be renamed** (that resets the egg to unfound for everyone). **Check these files before adding one** - the Konami egg already existed in `MainWindow.axaml.cs` and was nearly duplicated.
- **Startup at login:** Registry key at `HKCU\...\Run\PaperNexus` on Windows; `~/.config/autostart/PaperNexus.desktop` on Linux.
- **Linux launcher:** `DesktopEntry` writes `~/.local/share/applications/PaperNexus.desktop` and a hicolor icon on every launch. Required for dock pinning: without it the shell synthesises a temporary entry that vanishes when the app exits. `WM_CLASS`, `StartupWMClass`, and the `.desktop` basename must all stay `PaperNexus`.
- **Wallpaper on Linux:** No cross-desktop API - dispatches to KDE Plasma (`evaluateScript` over D-Bus), GNOME (`gsettings`), or `feh`/`xwallpaper`/`swaybg`. SteamOS Desktop Mode (KDE Plasma) is the Linux deployment target.

## Dependencies

Every package is declared once, in `PaperNexus.Shared.csproj`; the desktop host gets them through its project reference and declares only `Avalonia.Desktop` 12.1.1 (which also brings the Linux SkiaSharp/HarfBuzz native libraries via `Avalonia.Skia`). `PaperNexus.Shared.Tests` declares `Avalonia.Skia` 12.1.1 for the same reason: the test runner needs those native libraries on Linux, and `PaperNexus.Shared` carries only the managed SkiaSharp. Keep its version equal to Avalonia's; Dependabot does not group the Avalonia packages, so update them together by hand when one moves. The xUnit packages are declared in both test projects.

Avalonia 12.1.1, Avalonia.Themes.Fluent 12.1.1, Avalonia.Fonts.Inter 12.1.1, CommunityToolkit.Mvvm 8.4.2, Cronos 0.13.0, CronExpressionDescriptor 2.51.0, Microsoft.Extensions.Hosting 10.0.11, Newtonsoft.Json 13.0.4, SkiaSharp 3.119.4 (the version Avalonia 12.1.1 renders with, so its native libraries already ship in both binaries; Dependabot ignores it, and it is updated together with Avalonia), SkiaSharp.HarfBuzz 3.119.2 (text shaping for the annotation's kerning; pinned at 3.119.2 in Dependabot because later versions require a newer HarfBuzzSharp than the 8.3.1.3 native library Avalonia 12.1.1 ships - lift the pin when Avalonia moves). The Android host adds only Avalonia.Android 12.1.1, which moves with Avalonia; its Skia and HarfBuzz native libraries resolve to the same versions.

No `Avalonia.Diagnostics` reference: it has no 12.x release, and `AttachDevTools` was never called. Adding the visual-tree inspector back would mean pinning Avalonia to 11.x.

## Settings

`PaperNexus.Shared/Core/WallpaperNexusSettings.cs` (plain data) → `%LOCALAPPDATA%\PaperNexus\settings.json` (`~/.local/share/PaperNexus/settings.json` on Linux, the app's files folder on Android), read and written only by `SettingsStore`. Load fills defaults and repairs empty or invalid fields, and returns a fresh default instance when the file is missing or corrupt; every update writes a temp file then renames it over `settings.json`. `WallpaperNexusSettings.JsonFormat` is the on-disk JSON shape.
- `SlideshowSettings` - `Enabled` flag, schedule mode, interval (double) + `IntervalType` (Seconds/Minutes/Hours/Days/Weeks/Months/Years), cron expression, order (alphabetical/random/oldest/newest), fill style, `FavoritePriorityEnabled`, `FavoritePriorityWeight` (default: 3)
- `DownloadSettings` - folder path (empty in the model; the store fills it with the host's default: `%USERPROFILE%\Pictures\PaperNexus` on the desktop, from `PlatformPaths.DefaultPicturesDirectory`; the app's no-backup `Wallpapers` folder on the phone), `ResolutionWidth`/`ResolutionHeight`, retention days (default: 365)
- `List<WallpaperSource>` - name, URL, `ImageUrlJPath`, `TitleJPath`, cron, `IsEnabled`, `LastDownloadUtc`; defaults: "Bing Daily 4k" + "Spotlight Daily 4k"
- `AnnotationSettings` - font (default Cinzel, bundled; the picker lists every installed system family, never a hardcoded name list), size (default 18, UI range 8–200 - an absolute point size, so a title sized for 1080p is tiny on 4K), color (#F5F5F5), position, `OutlineEnabled` (stroke width `fontSize/12` with a 1px floor, drawn as a stroke pass then a fill pass so the outline does not thin small glyphs)
- `FavoriteWallpapers`, `BannedWallpapers`, window position/size, `RunOnStartup`, `AutoUpdatesEnabled`, `DebugMode`, `AnnotateWallpaper`, `MinimizeToTray`, `CurrentWallpaperPath`

## Code Style

Enforced via `.editorconfig`: .NET 10, C#, file-scoped namespaces, 4-space indent, CRLF, `var` everywhere, Allman braces (optional for single-line), PascalCase, explicit access modifiers, expression-bodied for properties/accessors/lambdas (not constructors), pattern matching preferred, `is null` preferred, `readonly` fields, no `this.`, `using` over `Dispose()`, no collection initializers.

**Button tooltips required:** Every `Button` in AXAML must have `ToolTip.Tip`. Tray `NativeMenuItem`s exempt.

**Action buttons belong on the item they act on:** Buttons or controls that require a selection (e.g. "Set as current", "Remove") should live on the row/card of the item they modify, not in a separate toolbar. Toolbars are for global actions only.

**Comments for intended behavior:** Use comments to document *why* and *what* a method or block is intended to do - especially for non-obvious logic, edge cases, and side-effect sequences.

**Local vars over inline wrapping:** Extract method call arguments into named local variables rather than nesting inline. E.g., `var current = Path.GetFullPath(x); var install = Path.GetFullPath(y); string.Equals(current, install, ...)` over `string.Equals(Path.GetFullPath(x), Path.GetFullPath(y), ...)`.

**Body blocks over expression bodies for multi-step logic:** Use `{ }` body blocks with local variables when a method or property involves more than one step. Reserve expression-bodied (`=>`) syntax for genuinely trivial single-expression members. Prefer readability over terseness.

**Suppressed diagnostics:** CS8600-CS8604, CS8618-CS8619, CA1806, CA1835, CA1848 (all `none`). `<Nullable>enable</Nullable>` is set but warnings silenced. Don't add `#nullable` annotations unless asked.

## Build & CI/CD

- **PR workflow (desktop):** `pull-request.yml` - set up .NET → restore → build (Release) → test (continue-on-error) of `PaperNexus.Desktop.slnf`, the solution filter holding the desktop host, the shared library and both test projects. Supplies the required `build` check. GitHub-hosted runner (`ubuntu-latest`), `actions/checkout@v7`.
- **PR workflow (Android):** `pull-request-android.yml` - a separate workflow whose `build-android` job installs the `android` workload (`dotnet workload restore`; the runner image already has the Android SDK and a JDK) and builds `PaperNexus.Android` (Release). It starts on every pull request, but the job is skipped unless the pull request changes `PaperNexus.Android/`, `PaperNexus.Shared/`, the linked logo or the workflow itself. `build-android` is not in the ruleset's required checks.
- **Deploy workflow:** push to `main`/tags/manual → restore and build `PaperNexus/PaperNexus.csproj` only (it publishes nothing from the Android host, so it needs no workload) → publish win-x64 and linux-x64 single-file → sign the exe, checksum the Linux binary → GitHub Release with three assets. GitHub-hosted runner (`ubuntu-latest`); signing uses `openssl` + `osslsigncode` (Linux equivalents of `New-SelfSignedCertificate`/`signtool.exe`).
- **Version:** Default `0.0.0`, CI sets `-p:Version=$buildNum.0.0`. Tags use `vN` format. Auto-updater compares `Version.Major` as integer.
- **Code signing:** Self-signed cert, auto-generated on first run, stored as `SIGNING_CERTIFICATE`/`SIGNING_CERTIFICATE_PASSWORD` secrets. Requires `GH_PAT` for persistence. 5-year validity, auto-renews at 30 days remaining.
- **Publishing:** the desktop host is published; `PaperNexus.Shared.dll` is bundled inside the single-file binary, so the release assets are unchanged. `dotnet publish PaperNexus/PaperNexus.csproj -c Release -r <win-x64|linux-x64> --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false`. The Linux output is renamed to `PaperNexus-linux-x64` because the auto-updater looks the asset up by that exact name.
- **Website workflow:** `deploy-website.yml` deploys `website/` to Azure Static Web Apps on `ubuntu-latest`. Triggered only by changes under `website/`.
- **Runners:** All workflows run on GitHub-hosted `ubuntu-latest` (switched from a self-hosted runner 2026-08-24; the repo is public, so hosted minutes are free and fork pull requests never execute on private hardware). `osslsigncode` is installed per run via apt.
- **Actions maintenance:** 30-day cycle. Currently `actions/checkout@v7`, `actions/setup-dotnet@v6`. **Next update: August 29, 2026.**

## Guidelines

- .NET 10.0, Avalonia UI (not WPF), root namespace `PaperNexus` / `PaperNexus.Core` in both projects (moved files kept their namespaces)
- **Where new code goes:** anything the phone also needs goes in `PaperNexus.Shared`; anything that calls a desktop-only API, opens a `Window`, or touches install/update/tray goes in `PaperNexus`. When shared code needs a host capability, add an interface in `PaperNexus.Shared/Core/` (or a question on an existing one) and an implementation in each host registered with `IAddSingleton<T>` (the phone's live in `PaperNexus.Android/Platform/`)
- **Platform differences go in `PaperNexus/Core/Platform/`** (desktop host; `PaperNexus.Shared` has none) - read `docs/platform-support.md` before touching wallpaper setting, startup registration, single-instance, install/update paths, or any path comparison
- AXAML assets: `avares://PaperNexus/Assets/...`
- New scheduled jobs: use `IScheduleScopedJob` pattern, not `ScheduledJobService`
- Never commit secrets. Check `dotnet list package --vulnerable`.
- **Update `CLAUDE.md`** after structural/pattern changes
- **No Linear issues** for this repo
- "Remember" = update `CLAUDE.md` (not just the memory directory)
- **Merging:** Land every PR with `gh pr merge --squash --auto`. Every commit reaching `main` is signed regardless of local setup, because GitHub creates the squash commit itself and signs it with its web-flow key. Branch commits are deliberately *not* required to be signed: the ruleset's `required_signatures` rule was removed, since PRs are mandatory and the squash commit is signed either way. While that rule was active it refused the merge outright - even with `--auto` armed and all checks green - because it evaluates the branch commits before GitHub ever creates the signed squash commit.
- **Branch protection on `main`:** ruleset requires PRs, squash-only, linear history, and both `build` and `CodeQL` status checks; no bypass actors (owner cannot bypass)
- **CodeQL:** code scanning default setup (`actions` + `csharp`) supplies the required `CodeQL` check. It only attaches to PRs opened *after* it was enabled. Closing and reopening an older PR does not trigger it. Push a new commit to the branch, an empty one is enough, for the check to appear.
- **Always start work on a feature branch** - never commit directly to `main`; create a descriptive branch (e.g., `feature/wallpaper-preview`, `fix/auto-update`) before making any changes
- **Comment addition tasks:** Add comments in small, focused batches (1–3 files at a time) rather than delegating all files to a single agent. Large batches risk code corruption.
- **Android host:** read `docs/android.md` before touching `PaperNexus.Android`, the Android build or CI steps, the phone's folders or wallpaper canvas, or driving the emulator; update it when the prerequisites, the commands, the app's structure or a measured Android behavior change. Verify Android changes on the emulator from a screenshot of the real screen.
- **Verify Linux changes against the real desktop**, not the app log - read the desktop's own state (e.g. `gsettings get org.gnome.desktop.background picture-uri`). Setup is in `docs/platform-support.md`. To capture or drive a desktop window (a scratch build under a throwaway `HOME`, GdkX11 screenshots, AT-SPI), read the "Verifying a change locally" section of `docs/platform-support.md`.
- **No system-level calls in unit tests:** Tests must never invoke real OS APIs (e.g., `NativeMethods`, registry writes, `SetDesktopWallpaper`). Use injectable interfaces with no-op test doubles (e.g., `NoOpWallpaperApplier`) to isolate from the system.

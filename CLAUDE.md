# CLAUDE.md - AI Assistant Guide for PaperNexus

## Project Overview

.NET 10.0 Avalonia desktop app for automated wallpaper rotation on Windows and Linux. Solution: `PaperNexus.sln`.

## Quick Reference

```bash
dotnet restore PaperNexus.sln
dotnet build PaperNexus.sln --configuration Release
dotnet run --project PaperNexus --configuration Release -- --debug
dotnet test --configuration Release
```

**Always use `--debug` when running locally.** Without it, `dotnet run` triggers the auto-install path (copies exe to `%LOCALAPPDATA%\PaperNexus\`) and exits immediately - the window never appears directly.

After launching, monitor the background task output for runtime errors. Watch for unhandled exceptions, AVLN binding errors, or service failures.

## Repository Structure

```
PaperNexus/
├── PaperNexus.sln                       # Solution file
├── CLAUDE.md                            # AI assistant guide (this file)
├── .editorconfig                        # C# code style rules & diagnostics
├── .gitignore                           # Standard Visual Studio .gitignore
├── .claude/                             # Claude Code configuration
├── .github/
│   └── workflows/
│       ├── pull-request.yml             # PR build verification
│       ├── deploy-wallpaper-service.yml # Release builds + code signing
│       └── deploy-website.yml           # Azure Static Web Apps deploy (website/ only)
├── docs/
│   ├── ui-style-guide.md               # Avalonia AXAML patterns reference
│   └── platform-support.md             # Windows/Linux platform layer reference
├── PaperNexus.Tests/                    # Unit tests (xUnit + NSubstitute)
└── PaperNexus/                          # Main project
    ├── PaperNexus.csproj
    ├── App.axaml(.cs)                   # App root, tray icon, startup
    ├── Program.cs                       # Entry point, auto-install, single-instance, IPC
    ├── AutoUpdateService.cs             # Silent auto-update via GitHub Releases + job wrapper
    ├── DownloadWallpapers.cs            # Scheduled wallpaper downloader
    ├── HttpWallpaperSourceService.cs    # HTTP feed client + WallpaperImage DTO
    ├── NativeMethods.cs                 # P/Invoke for Windows wallpaper API
    ├── SwitchWallpaper.cs               # Wallpaper switching logic + job wrapper
    ├── Assets/                          # logo.ico, logo.png, bundled fonts
    ├── Core/                            # DI, logging, scheduling, settings
    │   ├── Platform/                   # All Windows/Linux differences live here
    │   │   ├── PlatformPaths.cs        # Executable name, install dir, path comparison
    │   │   ├── SingleInstance.cs       # Mutex+event (Windows) / Unix socket (Linux)
    │   │   ├── StartupRegistration.cs  # Run key (Windows) / XDG autostart (Linux)
    │   │   ├── IWallpaperBackend.cs    # Per-OS wallpaper backend contract
    │   │   ├── LinuxWallpaperBackend.cs # KDE / GNOME / feh-xwallpaper-swaybg
    │   │   ├── LinuxDesktop.cs         # Desktop detection + helper process runner
    │   │   ├── DesktopEntry.cs         # XDG launcher + icon so the app can be pinned
    │   │   └── ShellOpener.cs          # explorer.exe / xdg-open, app relaunch
    │   ├── Bootstrapper.cs             # DI helpers, IAddSingleton<T>, AddServicesFrom()
    │   ├── Extensions.cs               # Utility extension methods
    │   ├── FileLogger.cs               # File-based ILogger implementation (async queue)
    │   ├── ScheduledService.cs         # ScheduledJobService base, IScheduleScopedJob
    │   └── WallpaperNexusSettings.cs   # Settings model, enums, LoadAsync/SaveAsync
    ├── ViewModels/
    │   ├── WallpaperConfigViewModel.cs # MVVM ViewModel (CommunityToolkit.Mvvm)
    │   └── GalleryItem.cs              # Gallery item view model
    └── Views/
        ├── MainWindow.axaml(.cs)       # Settings window
        ├── SplashScreen.axaml(.cs)     # Startup splash
        ├── WallpaperSourceDialog.axaml(.cs)  # Add/edit wallpaper source dialog
        └── NonScrollableComboBox.cs    # ComboBox that ignores scroll unless dropdown is open
```

## Key Architecture

- **MVVM** with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
- **Scheduled Jobs (preferred):** `IScheduleScopedJob` - separate business logic from scheduling. Job wrapper delegates to injected interface. Auto-discovered by `AddServicesFrom()`.
- **Scheduled Jobs (legacy):** `ScheduledJobService` base class - `DownloadWallpapers` extends directly. Registered via `IAddHostedSingleton<T>`.
- **DI:** `AddServicesFrom(assembly)` auto-discovers `IAddSingleton<T>`, `IAddHostedSingleton<T>`, and `IScheduleScopedJob` implementations.
- **Platform Layer (`Core/Platform/`):** Every Windows/Linux difference is isolated here - no other file may call a Windows-only API, hardcode `.exe`, or compare paths case-insensitively. See `docs/platform-support.md`.
- **Auto-Update:** Queries GitHub Releases API, compares `vN` tag as integer against `Assembly.Version.Major`, downloads the per-platform asset (`PaperNexus.exe` / `PaperNexus-linux-x64`), verifies it (Authenticode on Windows, published SHA-256 on Linux), swaps via self-deleting script (`.bat` / `.sh`) with rollback.
- **Auto-Install:** First run copies exe to the chosen install directory (default `%LOCALAPPDATA%\PaperNexus\`), migrates settings, writes a `.installed` sentinel file alongside the exe, then relaunches. On subsequent launches, `IsRunningFromInstallLocation` detects the sentinel file so custom install paths (not equal to the default AppData path) are recognised correctly and the install flow is not re-triggered.
- **Single Instance:** Named `Mutex` + `EventWaitHandle` for IPC (signals running instance to show UI).
- **Tray-only:** `ShutdownMode.OnExplicitShutdown`. Menu: "Open Settings", "Next Wallpaper", "Random Wallpaper", "Exit". Each item has a programmatically-drawn SixLabors icon.
- **Wallpaper Processing:** Writes to `current.png`/`.jpg`. Title overlay via SixLabors at switch time. PNG preferred; JPEG fallback if >16 MB.
- **`ISwitchWallpaper`:** Exposes `WallpaperChanged` event, `SwitchToNextAsync()`, and `SwitchToRandomAsync()`.
- **Wallpaper Sources (JPath-based):** `HttpWallpaperSourceService` uses Newtonsoft `SelectTokens` with `ImageUrlJPath`/`TitleJPath`. Sources edited via `WallpaperSourceDialog` (name, URL, JPath, cron, enabled toggle, live Test button).
- **`NonScrollableComboBox`:** Suppresses scroll wheel unless dropdown is open - prevents accidental changes while scrolling the settings page.
- **Favorites:** Heart toggle, stored in `settings.json`, excluded from retention cleanup.
- **Easter eggs (`Core/EasterEggs.cs`, `EasterEggSprites.cs`, `EasterEggAnimation.cs`, `Views/EasterEggOverlay.axaml`):** Eggs play a retro pixel animation in a full-window overlay - auto-dismisses after ~3s, click or Esc skips. Sprites are `'X'`/`'.'` bitmasks; motion is a pure function of (particle, frame) with no `Random` and no accumulated state, so the visuals are unit tested and can be rendered offscreen for inspection. The overlay only creates rectangles and moves them. The ViewModel raises `EasterEggTriggered`; `MainWindow` owns the overlay. Currently: Konami (burst), version-label ×5 (rise), favorite milestones (rise), word-spelling annotation hex colours (fall), plus an occasional alternate splash line. Each show carries a catalog `Id`, and `EasterEggOverlay.Play` records discovery, so a new egg cannot be left out of the checklist. The **Secrets** button beside the version label opens `EasterEggListDialog`, a checklist of `EasterEggCatalog.All` with progress; discovery ids persist in `settings.json` as `DiscoveredEasterEggs` and **must never be renamed** (that resets the egg to unfound for everyone). **Check these files before adding one** - the Konami egg already existed in `MainWindow.axaml.cs` and was nearly duplicated.
- **Startup at login:** Registry key at `HKCU\...\Run\PaperNexus` on Windows; `~/.config/autostart/PaperNexus.desktop` on Linux.
- **Linux launcher:** `DesktopEntry` writes `~/.local/share/applications/PaperNexus.desktop` and a hicolor icon on every launch. Required for dock pinning: without it the shell synthesises a temporary entry that vanishes when the app exits. `WM_CLASS`, `StartupWMClass`, and the `.desktop` basename must all stay `PaperNexus`.
- **Wallpaper on Linux:** No cross-desktop API - dispatches to KDE Plasma (`evaluateScript` over D-Bus), GNOME (`gsettings`), or `feh`/`xwallpaper`/`swaybg`. SteamOS Desktop Mode (KDE Plasma) is the Linux deployment target.

## Dependencies

Avalonia 12.1.1, CommunityToolkit.Mvvm 8.4.2, Cronos 0.13.0, CronExpressionDescriptor 2.51.0, Microsoft.Extensions.Hosting 10.0.5, Newtonsoft.Json 13.0.4, SixLabors.ImageSharp 3.1.12 + Drawing 2.1.7

No `Avalonia.Diagnostics` reference: it has no 12.x release, and `AttachDevTools` was never called. Adding the visual-tree inspector back would mean pinning Avalonia to 11.x.

## Settings

`Core/WallpaperNexusSettings.cs` → `%LOCALAPPDATA%\PaperNexus\settings.json`:
- `SlideshowSettings` - `Enabled` flag, schedule mode, interval (double) + `IntervalType` (Seconds/Minutes/Hours/Days/Weeks/Months/Years), cron expression, order (alphabetical/random/oldest/newest), fill style, `FavoritePriorityEnabled`, `FavoritePriorityWeight` (default: 3)
- `DownloadSettings` - folder path (default: `%USERPROFILE%\Pictures\PaperNexus`), `ResolutionWidth`/`ResolutionHeight`, retention days (default: 365)
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

- **PR workflow:** set up .NET → restore → build (Release) → test (continue-on-error). GitHub-hosted runner (`ubuntu-latest`), `actions/checkout@v7`.
- **Deploy workflow:** push to `main`/tags/manual → publish win-x64 and linux-x64 single-file → sign the exe, checksum the Linux binary → GitHub Release with three assets. GitHub-hosted runner (`ubuntu-latest`); signing uses `openssl` + `osslsigncode` (Linux equivalents of `New-SelfSignedCertificate`/`signtool.exe`).
- **Version:** Default `0.0.0`, CI sets `-p:Version=$buildNum.0.0`. Tags use `vN` format. Auto-updater compares `Version.Major` as integer.
- **Code signing:** Self-signed cert, auto-generated on first run, stored as `SIGNING_CERTIFICATE`/`SIGNING_CERTIFICATE_PASSWORD` secrets. Requires `GH_PAT` for persistence. 5-year validity, auto-renews at 30 days remaining.
- **Publishing:** `dotnet publish PaperNexus/PaperNexus.csproj -c Release -r <win-x64|linux-x64> --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false`. The Linux output is renamed to `PaperNexus-linux-x64` because the auto-updater looks the asset up by that exact name.
- **Website workflow:** `deploy-website.yml` deploys `website/` to Azure Static Web Apps on `ubuntu-latest`. Triggered only by changes under `website/`.
- **Runners:** All workflows run on GitHub-hosted `ubuntu-latest` (switched from a self-hosted runner 2026-08-24; the repo is public, so hosted minutes are free and fork pull requests never execute on private hardware). `osslsigncode` is installed per run via apt.
- **Actions maintenance:** 30-day cycle. Currently `actions/checkout@v7`, `actions/setup-dotnet@v6`. **Next update: August 29, 2026.**

## Guidelines

- .NET 10.0, Avalonia UI (not WPF), root namespace `PaperNexus` / `PaperNexus.Core`
- **Platform differences go in `Core/Platform/`** - read `docs/platform-support.md` before touching wallpaper setting, startup registration, single-instance, install/update paths, or any path comparison
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
- **Verify Linux changes against the real desktop**, not the app log - read the desktop's own state (e.g. `gsettings get org.gnome.desktop.background picture-uri`). Setup is in `docs/platform-support.md`.
- **No system-level calls in unit tests:** Tests must never invoke real OS APIs (e.g., `NativeMethods`, registry writes, `SetDesktopWallpaper`). Use injectable interfaces with no-op test doubles (e.g., `NoOpWallpaperApplier`) to isolate from the system.

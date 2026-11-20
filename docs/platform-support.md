# Platform Support

PaperNexus runs on Windows and Linux from one `net10.0` desktop host, `PaperNexus`, built on
the shared class library `PaperNexus.Shared`. Every place the two operating systems differ is
isolated in the desktop host's `PaperNexus/Core/Platform/`; no other file may call a
Windows-only API, hardcode `.exe`, or compare paths case-insensitively.

## The shared library and its host interfaces

`PaperNexus.Shared` holds the code both devices use (it is also the base of the Android
host `PaperNexus.Android`, issue #203; see `docs/android.md`). It has no operating-system-specific code and never references
`Avalonia.Desktop`. What only a host knows reaches it through interfaces it receives by
constructor injection; each host's implementations live beside the code they wrap and
register through `IAddSingleton<T>` (the phone's are in `PaperNexus.Android/Platform/`):

| Shared interface | Used by | Desktop implementation | Phone implementation |
| --- | --- | --- | --- |
| `IAppFolders` | `SettingsStore`, `SwitchWallpaper`, `JobTimerStore`, `WallpaperConfigViewModel`; the logger's folder | `DesktopAppFolders` (`PlatformPaths.cs`): settings in the default install directory, `current.png`/`timers.json`/`logs/` beside the executable (`AppContext.BaseDirectory`), downloads in `Pictures/PaperNexus` | `AndroidAppFolders`: settings in the files folder, everything else in the no-backup folder |
| `IAppInfo` | `WallpaperConfigViewModel`, `AboutViewModel`, `DebugSettingsViewModel` | `DesktopAppInfo`: `App.AppVersion`, `--debug` | `AndroidAppInfo`: "v" and the major number of the package's version name (`AppVersionLabel`); never started in debug mode |
| `IShellOpener` | `WallpaperConfigViewModel`, `AboutViewModel`, `DesktopSettingsViewModel` | `DesktopShellOpener` (`ShellOpener.cs`) | `AndroidShellOpener`: a web address opens in the browser; no folders |
| `IStartupRegistration` (optional) | `DesktopSettingsViewModel` | `DesktopStartupRegistration` (`StartupRegistration.cs`) | none (decision 8) |
| `IDeviceSettingRules` | `SwitchWallpaper` (whether to draw a title), `SlideshowSettingsViewModel` (the fill styles, the interval units and their numbers, the fastest schedule) | `DesktopSettingRules`: draws the title, all six fill styles, every interval unit with `SlideshowSchedule.IntervalRules`' ranges, no fastest schedule | `AndroidSettingRules`: never draws a title, five fill styles (no Span), minutes picked from 15, 20 and 30 then hours to years, nothing faster than every 15 minutes |
| `IFactoryReset` | `DebugSettingsViewModel` | `DesktopFactoryReset`: deletes `settings.json`, `current.*`, `timers.json` and `logs/`, relaunches with the same arguments except `--startup`, so the new copy shows its window | `AndroidFactoryReset`: deletes the same files (`AppDataReset`), keeps the pictures, restarts the launcher task in a new process |
| `IDeviceEasterEggs` | `SecretsViewModel` | `DesktopEasterEggs`: the whole catalog | `AndroidDeviceEasterEggs`: three eggs (no Fleeting Words, no Hexspeak); the Konami and version hints worded for the phone |
| `IWallpaperApplier` | `SwitchWallpaper`, `CurrentWallpaperViewModel` | `WallpaperApplier` (`NativeMethods.cs`): no canvas, always allowed | `AndroidWallpaperApplier`: `WallpaperManager`, both screens, a screen-shaped canvas |
| `ICheckForUpdates` (optional) | `WallpaperConfigViewModel` | `AutoUpdateService` | none (decision 8) |
| `IBackgroundScheduler` (optional) | `BackgroundWork` | none: the slideshow and the downloads run on timers in its own process (`AddInProcessSchedules`) | `AndroidBackgroundScheduler`: the slideshow's alarm and the download job (issue #203, step 5) |

`ISettingsStore` (shared, not a host interface) raises `Saved` after each successful update; the
download work is `IDownloadWallpapers` (all sources, or only the due ones) and its in-process timer
`DownloadSchedule`, which the desktop runs through `AddInProcessSchedules`.

An optional interface is one a host may lack: the phone has no run-at-sign-in and updates
through the store, so it registers neither, and the view model skips the call or reports the
service as unavailable.

`IWallpaperApplier.GetCanvas()` says who fits the picture to the screen. The desktop answers
`null`: the switcher writes the picture at its own size with the title against its edges, and
the operating system fits it from the fill style, as it always has. The phone answers with a
`WallpaperCanvas`, and the switcher draws the picture onto it for the fill style; the phone draws
no title (see `docs/android.md`). A wallpaper the device refuses (`SetWallpaper`
returns false) is never recorded as current, on either device.

## The platform layer

| File | Concern | Windows | Linux |
| --- | --- | --- | --- |
| `PlatformPaths.cs` | Executable name, install directory, path comparison, execute bit | `PaperNexus.exe`, `%LOCALAPPDATA%`, case-insensitive compare | `PaperNexus`, `~/.local/share`, case-sensitive compare, `chmod +x` |
| `SingleInstance.cs` | One running copy, plus "show the window" from a second launch | Named `Mutex` + named `EventWaitHandle` | Unix domain socket in `$XDG_RUNTIME_DIR` |
| `StartupRegistration.cs` | Launch at login | `HKCU\...\CurrentVersion\Run` | `~/.config/autostart/PaperNexus.desktop` |
| `IWallpaperBackend.cs`, `LinuxWallpaperBackend.cs`, `NativeMethods.cs` | Setting the desktop wallpaper and its fill style | `SystemParametersInfo` + `HKCU\Control Panel\Desktop` | KDE Plasma / GNOME / generic setters |
| `LinuxDesktop.cs` | Desktop environment detection, running helper commands | not used | `XDG_CURRENT_DESKTOP` etc., then process-name inference |
| `DesktopEntry.cs` | Application launcher and icon so the app can be pinned | not used (icon comes from the executable's own resources) | `~/.local/share/applications/PaperNexus.desktop` + hicolor icon |
| `ShellOpener.cs` | Opening folders, URLs, and relaunching the app | `explorer.exe`, shell execute | `xdg-open`, shell execute |

`WallpaperApplier` remains the single type registered for `IWallpaperApplier` (the interface
lives in `PaperNexus.Shared`), so the
`IAddSingleton<T>` auto-discovery in `Bootstrapper` still resolves exactly one
implementation; it selects a backend in its constructor rather than being replaced.

## Setting the wallpaper on Linux

There is no cross-desktop API, so `LinuxWallpaperBackend` dispatches on the detected desktop:

- **KDE Plasma** (the SteamOS Desktop Mode shell) - a JavaScript snippet passed to
  `org.kde.PlasmaShell.evaluateScript` over D-Bus, tried against `qdbus6`, `qdbus-qt6`,
  `qdbus`, then `qdbus-qt5`. Falls back to `plasma-apply-wallpaperimage`, which sets the
  image but not the fill mode.
- **GNOME** - `gsettings` keys under `org.gnome.desktop.background`. Both `picture-uri` and
  `picture-uri-dark` are written, otherwise the wallpaper appears unchanged under the dark
  colour scheme.
- **Anything else** - `feh`, then `xwallpaper`, then `swaybg` if installed.

When a switch fails the backend logs a warning naming the detected desktop, and each
attempted mechanism logs at debug level (which `--debug` enables) saying whether the helper
was found and whether it succeeded. That matters because the failure mode is otherwise
silent: the wallpaper simply does not change. Log growth is bounded regardless - `FileLogger`
rotates at 5 MB and keeps one previous generation, so the logs directory cannot exceed
about 10 MB.

Both KDE and GNOME only repaint when the stored value actually changes, and PaperNexus always
writes the same `current.png`, so each backend clears the key before writing the real path.
`swaybg` runs for as long as the wallpaper is displayed, so the backend keeps its process and
terminates the old instance only after the replacement is running.

## Pinning to the dock or task manager on Linux

A dock can only pin a `.desktop` file it knows about. With none installed, GNOME Shell and
the KDE task manager synthesise a temporary entry from the running window, and that entry
disappears when the app exits - so a pinned PaperNexus showed an icon only while running.

`DesktopEntry.Install` therefore writes both halves on every launch:

- `~/.local/share/applications/PaperNexus.desktop`, carrying `StartupWMClass=PaperNexus`.
- `~/.local/share/icons/hicolor/256x256/apps/papernexus.png`, downscaled from the bundled
  logo (the source asset is several megabytes and icon caches load every entry eagerly).

Three names must stay in agreement or the shell opens a second dock item beside the pinned
one: the window's `WM_CLASS`, the `StartupWMClass` key, and the `.desktop` basename. All
three are `PaperNexus`, and `DesktopEntryTests` asserts they cannot drift apart.

Installing on every launch is deliberate - it repairs installs made before the launcher
existed and corrects the `Exec` line if the executable moves. `StartupRegistration`'s
autostart entry mirrors the same icon and `StartupWMClass` so an autostarted window
resolves to the same launcher.

To verify a change here, check the desktop's own resolution rather than the file contents,
with the app closed:

```python
from gi.repository import Gio
Gio.DesktopAppInfo.new("PaperNexus.desktop")   # must not be None
```

## Auto-update

The updater looks up a release asset named for the running platform: `PaperNexus.exe` on
Windows, `PaperNexus-linux-x64` on Linux. Integrity is verified before the binary is ever
executed:

- Windows - Authenticode signature with subject `CN=PaperNexus`.
- Linux - SHA-256 digest read from the `PaperNexus-linux-x64.sha256` sidecar asset.
  Authenticode is a PE-only format, so it cannot be used. **An update with no checksum asset
  is refused**, so the deploy workflow must keep publishing that file.

The binary swap runs from a self-deleting script - a `.bat` driven by `cmd.exe` on Windows,
a `.sh` driven by `/bin/sh` on Linux - with the same back up, move, relaunch, verify,
roll back sequence on both.

## Building and releasing

The deploy workflow publishes a self-contained single-file binary per runtime identifier
(`win-x64`, `linux-x64`) and attaches three assets to the release: the signed Windows exe,
the Linux binary, and the Linux checksum.

## Verifying a change locally

This repository is developed on Ubuntu with GNOME; SteamOS Desktop Mode (KDE Plasma) is the
Linux deployment target. To drive the real desktop from a non-graphical shell, export the
session's variables before launching:

```bash
export XDG_RUNTIME_DIR=/run/user/$(id -u)
export DBUS_SESSION_BUS_ADDRESS=unix:path=$XDG_RUNTIME_DIR/bus
export DISPLAY=:0 XAUTHORITY=$(pgrep -a Xwayland | grep -o '/run/user/[0-9]*/\.mutter-Xwaylandauth\.[A-Za-z0-9]*')
dotnet run --project PaperNexus -c Release -- --debug
```

Confirm the wallpaper actually changed by reading the desktop's own state rather than the
app's log - `gsettings get org.gnome.desktop.background picture-uri` on GNOME.

### A scratch build beside an installed copy

To run a build without touching the installed app or its files, build it in a separate clone (with
an `origin` remote) and launch it with a throwaway home: set `HOME` to an empty folder, unset
`XDG_DATA_HOME` and `XDG_CONFIG_HOME`, and point `XDG_RUNTIME_DIR` at another empty folder so its
single-instance socket does not hand the launch over to a running copy. Keep
`DBUS_SESSION_BUS_ADDRESS` on the real session bus so GNOME and accessibility still answer.
`settings.json`, the downloads, the autostart entry and the launcher then all land under the
throwaway home; a wallpaper switch still changes the real GNOME keys, so record them first (or
turn the slideshow off in the throwaway `settings.json`).

### Screenshots and driving the window

GNOME refuses screenshots over D-Bus, but the app's windows are X11 clients of Xwayland and can be
captured one window at a time from Python, with nothing installed beyond the system's GTK and
AT-SPI bindings:

- Run Python with `GDK_BACKEND=x11` (and the session variables above), so GDK opens the X display
  rather than the Wayland one.
- Find the window through Xlib (`ctypes` on `libX11`): read the root window's `_NET_CLIENT_LIST`,
  and for each window its `_NET_WM_PID` (to pick the app's process) and `_NET_WM_NAME` (to pick
  the settings window, "Paper Nexus", or a dialog such as "Wallpaper Source").
- Capture it with `GdkX11.X11Window.foreign_new_for_display(display, xid)` and
  `Gdk.pixbuf_get_from_window(window, 0, 0, width, height)`, then `savev(path, "png", [], [])`.
  `Gdk.property_get` cannot be called from Python, hence the Xlib lookup.

Drive the window through AT-SPI (`gi.require_version('Atspi', '2.0')`): find the application whose
`get_process_id()` is the app's, walk its children, and

- press a button with `get_action_iface().do_action(0)`; a button's tooltip is its accessible
  description, and a checkbox's label its name;
- type into a text box with `Atspi.EditableText.set_text_contents(node.get_editable_text_iface(), text)`;
- read a status or refusal line from the labels' names.

Two limits: pointer clicks sent through XTest do not reach the window under GNOME on Wayland, and
rows a list adds after the window opened are missing from the accessibility tree (Avalonia
12.1.1), so act on rows that were there when the window opened.

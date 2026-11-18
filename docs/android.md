# Android Host

`PaperNexus.Android` is the Android app (issue #203). It targets `net10.0-android` (API 36) with
Android 12 (API 31) as the minimum, application ID `com.gnostack.papernexus`, label `PaperNexus`.
It references `PaperNexus.Shared`, as the desktop host does; the phone's screens live in this
project and its logic and view models in the shared project.

## What it is made of

| File | Job |
| --- | --- |
| `MainApplication.cs` | The Android application object (`[Application]`, deriving `AvaloniaAndroidApplication<App>`). Sets the label, the launcher icon (`@mipmap/icon`) and the theme (`@style/PaperNexusTheme`), and adds the Inter font as the desktop does. It is the phone's composition root: `OnCreate` builds the service container and starts it before Avalonia creates `App`. |
| `MainActivity.cs` | The launcher activity (`AvaloniaMainActivity`). Shows the view `App` supplies. |
| `App.axaml(.cs)` | The phone's own Avalonia `App` (Fluent theme, dark). The desktop `App` owns the tray and the windows, so it stays in the desktop host. `OnFrameworkInitializationCompleted` sets `IActivityApplicationLifetime.MainViewFactory`, which resolves a new `MainView` from `MainApplication`'s services for each activity. |
| `Views/MainView.axaml(.cs)` | A `TabbedPage` with five `ContentPage`s: Current, Gallery, Favorites, Sources, Settings. It receives the `CurrentWallpaperViewModel`, `GalleryViewModel` and `FavoritesViewModel` by constructor, tells Gallery and Favorites to list their pictures the first time their tab is chosen, and disposes all three when it leaves the screen. Sources and Settings show only their names until their parts of step 4. |
| `Views/CurrentWallpaperView.axaml(.cs)` | The Current tab: the current wallpaper's picture full width, its title, the heart, Next, Random and delete, the confirmation line, and the delete question. |
| `Views/GalleryView.axaml(.cs)`, `Views/FavoritesView.axaml(.cs)` | The Gallery and Favorites tabs (see "Gallery and Favorites"). |
| `Views/PictureListStyles.axaml` | The card, picture-button, ban-icon and confirmation-line styles both lists and the Current tab share. |
| `Views/PictureFrame.cs` | Holds a card's picture in a 16:9 box as wide as the card, so every card has the same height. |
| `Views/CardThumbnails.cs` | Loads a card's thumbnail at the screen's pixel width when the list builds the card, and releases it when the list clears the card or the tab leaves the screen. |
| `Views/ConfirmationDialog.axaml(.cs)` | The reusable in-page question: the page dimmed, a box in the middle, cancel and confirm buttons, a destructive confirm in the delete color. `await ShowAsync(...)` answers true or false; a tap on the dimmed page cancels. |
| `Platform/AndroidAppFolders.cs` | `IAppFolders` for the phone (see "Where the files are"). |
| `Platform/AndroidWallpaperApplier.cs` | `IWallpaperApplier` for the phone (see "Setting the wallpaper"). |
| `Platform/AndroidPermissions.cs` | `INTERNET` and `SET_WALLPAPER`, as assembly attributes. |
| `Resources/values/styles.xml` | The Android theme and its colors. |
| `Resources/mipmap-anydpi-v26/icon.xml` | The adaptive launcher icon. |

There is no `AndroidManifest.xml` in the project: the build generates it from the attributes on
`MainApplication` and `MainActivity` and from the csproj (`ApplicationId`,
`SupportedOSPlatformVersion`). The generated file is
`PaperNexus.Android/obj/<Configuration>/net10.0-android/android/AndroidManifest.xml`.

Decisions that are easy to undo by accident:

- **`PageNavigationHost` wraps `MainView`.** Android 15 and later always draw an app edge to edge,
  under the status bar and the navigation bar. `PageNavigationHost` passes the system bars' insets
  to the page, and `TabbedPage` then keeps its tab bar above the navigation bar. Without it the
  back, home and recents buttons sit on top of the tabs.
- **The tab colors are overridden** in `MainView.axaml`. With Fluent's defaults the selected tab is
  the plain accent color, which is darker on the #2B2B2B background than the unselected grey. The
  selected tab uses `SystemAccentColorLight1` and the others `SystemBaseMediumLowColor`.
- **The pages' background is #2B2B2B**, the desktop window color, set by a style in `MainView`;
  the theme's own page background is black.
- **The Android theme descends from `Theme.AppCompat.NoActionBar`.** Avalonia's activity is an
  AppCompat activity and refuses to start under any other theme. Its window background (#2B2B2B)
  is also what Android 12 and later show, with the launcher icon, as the launch screen, so no
  splash screen package is needed.
- **The root namespace is `PaperNexus`**, not `PaperNexus.Android`, because code inside a
  `PaperNexus.Android` namespace resolves `Android.*` names against it rather than the Android
  SDK. `App` derives from `Avalonia.Application` written in full, because the implicit usings also
  bring in `Android.App.Application`.
- **The launcher icon is the desktop logo, linked** from `PaperNexus/Assets/logo.png` into
  `Resources/drawable-nodpi/logo.png` (`nodpi` stops Android scaling the 2048 px image up for the
  screen density). The adaptive icon insets it by the sixth the launcher masks off each side, on
  the logo's own light green (#DBE7D3). The image is 5.4 MB of the app package.

## The Current tab

`CurrentWallpaperViewModel` (in `PaperNexus.Shared`) decides what the tab shows:

| State | When | What the person sees | Next and Random |
| --- | --- | --- | --- |
| Showing | There is a current wallpaper | Its picture full width, its title, the heart (outline, or filled #E06C75 for a favorite) | enabled |
| Downloading | No pictures, a download is running | "No wallpapers yet. Downloading…" in a #1E1E1E placeholder | dimmed |
| Download failed | The download ended with no pictures | "Could not download wallpapers. Check your connection." and a "Try again" button | dimmed |
| No wallpaper | Pictures but none current, while the first is being set | the empty placeholder | dimmed while it works |

- On opening with no current wallpaper the tab makes one itself: it downloads when there are
  no pictures, then sets the next picture in slideshow order (the same rule as Next), with no
  tap.
- Next and Random both dim while either runs, and the heart stays usable.
- A change the phone does not take shows "Could not change the wallpaper. Try again." in red
  under the buttons, keeps the current wallpaper shown, and the buttons work again at once.
- A phone that forbids changing the wallpaper (`IWallpaperApplier.CanSetWallpaper` is false)
  shows "This phone does not allow apps to change the wallpaper." in red, and Next and Random
  stay dimmed.
- The picture is the downloaded source picture, scaled to the screen's pixel width by
  `ImageResizing.CreatePngOfWidth` (the shared imaging layer) rather than held at 4K.
- Each activity gets a new view and view model. A wallpaper set by an older copy of the tab
  reaches the new one through `ISwitchWallpaper.WallpaperChanged`; the old view model stops
  listening when its view leaves the screen.
- The heart says "Added to favorites." or "Removed from favorites." in a line under the
  buttons for three seconds (green; a failure is red, "Error updating favorites: …").
- The delete button (the desktop's trash icon, after Random) asks "Delete this wallpaper file
  from disk?" in the in-page dialog, with "Cancel" and "Delete". On Delete the picture leaves
  the phone, its favorite and its ban, and the next picture in slideshow order is set: "Deleted
  and switched to: …". After the last picture the line says "Wallpaper deleted. No more
  wallpapers." and the tab downloads new ones, as on first open.
- A heart set, or the current picture deleted, on another tab shows here at once (the
  library's `Changed`).

## Gallery and Favorites

The rules for listing, hearting, banning and deleting pictures are `IWallpaperLibrary`
(`PaperNexus.Shared/WallpaperLibrary.cs`), which the desktop's settings window uses too;
setting a picture as the wallpaper is `ISwitchWallpaper.SwitchToSpecificAsync`. The phone's
`GalleryViewModel` and `FavoritesViewModel` share `PictureListViewModel`.

| Tab | Lists | Buttons on each card | Empty |
| --- | --- | --- | --- |
| Gallery | every downloaded picture, newest first by the file's last write time | set as current, heart, ban, delete | "No wallpapers yet." |
| Favorites | the favorites, in the order they were hearted | set as current, remove from favorites | "No favorites yet. Tap the heart on a wallpaper to add one." |

- A card is the picture in a 16:9 frame the width of the screen, its title beneath (two lines
  at most), and its buttons on the picture's bottom corners as on the desktop's card: 48 dp
  buttons on a dark see-through square. A banned picture is tinted red (`#AA660000`) and its
  ban icon is orange; Favorites cards, like the desktop's, show no tint.
- Every tap shows a line over the bottom of the list for three seconds: green when it worked,
  red when it failed, white ("Switching wallpaper...") while a switch waits its turn. The
  card's buttons dim while its action runs. Gallery delete asks nothing, as on the desktop.
- A change made on any tab shows on the others at once: the library raises `Changed` after
  every save, and each view model applies it on the screen's thread. A finished download run
  raises `PicturesChanged`, which adds the new pictures to an open Gallery.
- On a phone that forbids changing the wallpaper, set as current stays dimmed.

Measured on the Android 16 emulator (issue #203, step 4):

- `TabbedPage.SelectionChanged` is raised for the first selection (the Current tab, as the
  view attaches) and for every tab change, so the lists load on it.
- A tab's page leaves the visual tree when another tab is chosen, but its list keeps its
  cards; `CardThumbnails` releases their thumbnails then and reloads the cards in view when the
  tab returns.
- Setting the wallpaper makes Android 12 and later rebuild the system's wallpaper-based color
  overlays, which recreates the activity: the app reopens on the Current tab with new view
  models, so the Gallery's place and the "Switched to: …" or "Deleted and switched to: …" line
  are lost. The new wallpaper itself is set and shown on Current. Not yet decided: the
  manifest's `configChanges="assetsPaths"` would keep the activity, but the .NET Android
  manifest generator drops `ConfigChanges.AssetsPaths` from the activity attribute.
- With 55 pictures (4K JPEGs), a Debug build used about 215 MB at rest, 236 MB with the
  Gallery open, up to 523 MB while flinging through the whole list, and stayed near 380 MB
  after leaving the tab (two more scroll rounds stayed between 370 and 400 MB, so this is
  memory kept by the allocator after up to four full-size decodes at once, not a growing
  leak). A Release build: 177 MB at rest, 466 MB peak, 335 MB after.

## Composition and lifecycle

- `MainApplication.OnCreate` builds the container once per process with `new HostBuilder()`:
  the shared services (`AddServicesFrom` on the shared assembly), then this host's
  implementations (`AddServicesFrom` on the Android assembly), the screens as transient
  registrations, and the file logger. It does not call `AddInProcessSchedules`, so the
  slideshow does not run on a timer inside the app (issue #203, step 5 brings it).
- The scheduled downloader starts with the host: its first run begins at once and later runs
  follow each source's schedule while Android keeps the process.
- The log is `no_backup/logs/app.log` (`Assembly.GetEntryAssembly()` is null on Android, so
  the logger's fallback name is used). Read it with
  `adb shell run-as com.gnostack.papernexus cat no_backup/logs/app.log` (Debug builds only).

## Where the files are

All inside the app's own storage (decision 10):

| What | Where | Why |
| --- | --- | --- |
| `settings.json` | `files/` (`Context.FilesDir`) | Android's automatic backup copies it to a new phone |
| Downloaded wallpapers | `no_backup/Wallpapers/` | Android stops backing up an app whose data passes 25 MB, and the pictures alone pass it; they are downloaded again |
| `current.png`, `timers.json`, `logs/` | `no_backup/` | Working files of this phone |

The manifest sets no `android:installLocation`, so the app stays on internal storage
(`internalOnly`) and the absolute paths `settings.json` records never move.

Measured on the emulator: `AppContext.BaseDirectory` is `files/` in both Debug and Release
and is writable, and `SpecialFolder.MyPictures` is `files/Pictures`. Shared code uses neither:
the folders come from `IAppFolders`.

## Setting the wallpaper

`AndroidWallpaperApplier.SetWallpaper` calls
`WallpaperManager.SetStream(stream, null, true, FLAG_SYSTEM | FLAG_LOCK)`: one picture for
both screens (decision 3). It returns the new wallpaper's id, or 0 when Android did not take
it.

Android does not fit a picture the way the app needs, so the app fits it itself.
`GetCanvas()` returns a `WallpaperCanvas` for the screen in its upright orientation, and the
shared `WallpaperRenderer` draws the picture onto it for the fill style and then draws the
title inside its title area. Measured on the Android 16 emulator (1080x2400), issue #203
step 3:

- A wide picture (3840x2160) given as is was scaled to the launcher's desired size
  (`getDesiredMinimumWidth/Height`, 4800x2400) and its left part shown.
- Android enlarges a still wallpaper by about 1.1 on both the home screen and the lock
  screen, so the screen shows only the middle 1/1.1 of it. The factor is the system value
  `config_wallpaperMaxScale` (1.1 here), which is not public API; `AndroidWallpaperApplier`
  reads it by name and takes 1.0 when it cannot.
- A picture 1.1 times the screen size (1188x2640) shows at one picture pixel per screen pixel,
  centred. That is the canvas.
- The status bar is 136 px and the navigation bar 126 px. The title area leaves them out, so
  the title is never cut off or covered: on this phone (54, 256) to (1134, 2394) of the canvas.
- At the bottom positions the launcher's own dock icons can still sit over the title on the
  home screen; the lock screen shows it whole.
- Fill styles are drawn on the whole canvas, as a monitor shows them; at rest the screen shows
  its middle 91%, so Fit's sides lose about 4.5%. Span draws as Fill.
- `dumpsys wallpaper` after a change shows `System wallpaper state: User 0: id=N: mWhich=3`
  and an empty `Lock wallpaper state`: one image serves both screens.
  `getWallpaperId(FLAG_LOCK)` returns -1 while the lock screen shares the home screen's
  picture, so check `mWhich=3` rather than a lock id.

## Prerequisites

- The .NET `android` workload (`dotnet workload list` shows it).
- A JDK, version 17 to 21 (the range the workload accepts), at `JAVA_HOME`.
- The Android SDK at `ANDROID_HOME`, with `platform-tools`, `platforms;android-36` and
  `build-tools;36.0.0`; for the emulator also `emulator` and a system image such as
  `system-images;android-36;default;x86_64`, plus `/dev/kvm`.

The Android build reads `ANDROID_HOME` and `JAVA_HOME` from the environment; nothing in the
repository names a path. **Both must be exported in the shell that builds the solution**: without
them every solution-level build and test fails with `XA5300: The Android SDK directory could not
be found`. The desktop project alone (`dotnet build PaperNexus`) builds without them.

A Microsoft OpenJDK whose version has four parts (for example `21.0.13.0.2`) makes the build print
warning `XA0033` ("Failed to get the Java SDK version"); the build still succeeds.

The Android host has its own pull request workflow, `.github/workflows/pull-request-android.yml`:
it gets the workload with `dotnet workload restore` and builds only `PaperNexus.Android`; the
GitHub runner image already provides `ANDROID_HOME`, `JAVA_HOME` (JDK 17) and the SDK packages.
Its build job is skipped when a pull request changes nothing the app is built from. The desktop
pull request workflow builds `PaperNexus.Desktop.slnf` (everything except this project) and the
deploy workflow builds only the desktop project; neither needs the workload.

## Build, install and launch

```bash
export JAVA_HOME=<JDK directory> ANDROID_HOME=<Android SDK directory>
export PATH=$PATH:$ANDROID_HOME/platform-tools
dotnet build PaperNexus.Android -t:Run
```

`-t:Run` builds the Debug configuration, installs it on the running emulator (or the one connected
device) and starts it. The first build takes about 40 seconds.

A Release build (`dotnet build PaperNexus.Android -c Release`, also part of the solution build)
writes a package signed with the SDK's debug key:
`PaperNexus.Android/bin/Release/net10.0-android/com.gnostack.papernexus-Signed.apk`. Install it with
`adb install -r <that path>`. A clean Release build of the Android project takes about a minute.
There is no store signing or release asset yet (issue #203, step 7).

## Driving the emulator

```bash
$ANDROID_HOME/emulator/emulator -list-avds
$ANDROID_HOME/emulator/emulator -avd <name> -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot &
until [ "$(adb shell getprop sys.boot_completed | tr -d '\r')" = 1 ]; do sleep 2; done
```

- Clean install: `adb uninstall com.gnostack.papernexus`, then install as above.
- Open the app the way a person does: `adb shell input keyevent KEYCODE_HOME`, swipe up for the
  app drawer (`adb shell input swipe 540 1800 540 600 300` on a 1080x2400 screen) and tap the
  PaperNexus icon. Without the launcher:
  `adb shell monkey -p com.gnostack.papernexus -c android.intent.category.LAUNCHER 1`.
- Screenshot: `adb exec-out screencap -p > screen.png`, then read the image.
- Tap: `adb shell input tap X Y` in screen pixels. On a 1080x2400 screen the five tabs are at
  y = 2200 and x = 108, 324, 540, 756, 972.
- Crashes and start time: `adb logcat -d | grep -E "FATAL|AndroidRuntime|Displayed com.gnostack"`.
- Tap a button by its tooltip, which Avalonia exposes as the accessibility description:
  `adb shell uiautomator dump /sdcard/ui.xml`, find the node whose `content-desc` is the
  tooltip (for example `Change to the next wallpaper`), and tap the middle of its `bounds`.
- The lock screen is off on the virtual phone (`adb shell locksettings get-disabled` is
  `true`). To see it: `adb shell locksettings set-disabled false`, then
  `adb shell input keyevent KEYCODE_SLEEP` and `KEYCODE_WAKEUP`; set it back to `true`
  afterwards.
- Change a setting the phone has no screen for yet by editing it in place on a Debug build:
  `adb shell "run-as com.gnostack.papernexus sed -i 's/\"FillStyle\": \"[A-Za-z]*\"/\"FillStyle\": \"Fit\"/' files/settings.json"`.
  The app reads the file on every change, so the next tap uses it.
- No network: `adb shell svc wifi disable` and `adb shell svc data disable` (`enable` to undo).
- Shut the emulator down when finished: `adb emu kill`.

The app drawer on a 5-column launcher shortens the label to "PaperNe..."; the label itself is
`PaperNexus`.

## Tests

There is no Android test project: a `net10.0` test project cannot reference a
`net10.0-android` project. The logic the phone relies on lives in `PaperNexus.Shared` and is
tested there: the canvas math (`WallpaperCanvas.ForScreen`), fitting and rendering for a
canvas (`WallpaperFittingTests`), the switcher with a canvas or a refusal
(`SwitchWallpaperDeviceTests`), the Current tab's states (`CurrentWallpaperViewModelTests`),
the picture library (`WallpaperLibraryTests`) and the Gallery and Favorites tabs
(`PictureListViewModelTests`).
The Android classes only pass Android's numbers and calls through; they are verified on the
emulator.

# Android Host

`PaperNexus.Android` is the Android app (issue #203). It targets `net10.0-android` (API 36) with
Android 12 (API 31) as the minimum, application ID `com.gnostack.papernexus`, label `PaperNexus`.
It references `PaperNexus.Shared`, as the desktop host does; the phone's screens live in this
project and its logic and view models in the shared project.

## What it is made of

| File | Job |
| --- | --- |
| `MainApplication.cs` | The Android application object (`[Application]`, deriving `AvaloniaAndroidApplication<App>`). Sets the label, the launcher icon (`@mipmap/icon`) and the theme (`@style/PaperNexusTheme`), and adds the Inter font as the desktop does. It is the phone's composition root: `OnCreate` builds the service container and starts it before Avalonia creates `App`. |
| `MainActivity.cs` | The launcher activity (`AvaloniaMainActivity`). Shows the view `App` supplies. Its `[Activity]` attribute only fixes its Java name, `com.gnostack.papernexus.MainActivity`; the activity is declared in `AndroidManifest.xml`. |
| `AndroidManifest.xml` | The hand-written part of the manifest: the activity, its launcher intent filter, and the configuration changes it handles itself (see "Keeping the screen"). |
| `App.axaml(.cs)` | The phone's own Avalonia `App` (Fluent theme, dark). The desktop `App` owns the tray and the windows, so it stays in the desktop host. `OnFrameworkInitializationCompleted` sets `IActivityApplicationLifetime.MainViewFactory`, which resolves a new `MainView` from `MainApplication`'s services for each activity and puts it in a `NavigationPage` with no bar and its swipe recognizer off (see "Dragging a switch"), inside a `PageNavigationHost`, inside an `EasterEggStage` (see "Easter eggs"). |
| `Views/MainView.axaml(.cs)` | A `TabbedPage` with five `ContentPage`s: Current, Gallery, Favorites, Sources, Settings. It receives the `CurrentWallpaperViewModel`, `GalleryViewModel`, `FavoritesViewModel`, `SourcesViewModel` and `MainScreenState` (all held for the process's lifetime) by constructor, binds the tabs to them when it reaches the screen and unbinds when it leaves, returns to the tab shown last, and tells Gallery, Favorites and Sources to list their contents the first time their tab is chosen. It also opens and closes the Sources tab's form (see "Sources"), receives the `SettingsViewModel` and reads the settings the first time the Settings tab is chosen (see "Settings"), and passes Android's back to the Settings tab. |
| `Views/MainScreenState.cs` | The selected tab, kept for the process's lifetime. |
| `Views/ListPlace.cs` | Records a list's scroll place in its view model (`IScrollPlace`) and returns to it when the list reaches the screen again. |
| `Views/CurrentWallpaperView.axaml(.cs)` | The Current tab: the current wallpaper's picture full width, its title, the heart, Next, Random and delete, the confirmation line, and the delete question. |
| `Views/GalleryView.axaml(.cs)`, `Views/FavoritesView.axaml(.cs)` | The Gallery and Favorites tabs (see "Gallery and Favorites"). |
| `Views/SourcesView.axaml(.cs)`, `Views/SourceFormPage.axaml(.cs)` | The Sources tab and its full-screen add or edit form (see "Sources"). |
| `Views/SettingsView.axaml(.cs)` | The Settings tab (see "Settings"). |
| `Views/ChoiceDialog.axaml(.cs)` | The in-page list to choose one value from: the page dimmed, a box with a title, one row per choice (its label and the line that explains it) with the chosen one marked, and Cancel. A tap on the dimmed page cancels. The Settings tab's pickers use it. |
| `Views/SecretsPage.axaml(.cs)` | The Secrets list as a full-screen modal page (see "Easter eggs"). |
| `Views/EasterEggStage.cs`, `Views/EasterEggPlayback.cs` | The screen's root: the pages with the easter egg overlay over them, and the egg playing, kept for the process (see "Easter eggs"). |
| `Views/KeyboardLift.cs` | Keeps the keyboard off what is being typed: lifts a page's content by the part of the keyboard above the navigation bar and scrolls the focused field (with its hint) into view. The Sources form and the Settings tab use it (see "Sources"). |
| `Views/PictureListStyles.axaml` | The card, picture-button and ban-icon styles both picture lists and the Current tab share. |
| `Views/StatusLineStyles.axaml` | The confirmation line's look (white, green, red) for every tab, which each tab's `StatusLine` drives. |
| `Views/PictureFrame.cs` | Holds a card's picture in a 16:9 box as wide as the card, so every card has the same height. |
| `Views/CardThumbnails.cs` | Loads a card's thumbnail at the screen's pixel width when the list builds the card, and releases it when the list clears or reuses the card (through the shared `CardThumbnailHolder`) or the tab leaves the screen. |
| `Views/ConfirmationDialog.axaml(.cs)` | The reusable in-page question: the page dimmed, a box in the middle (as wide as `ChoiceDialog`'s whatever the question, long text wrapping inside) with an optional title line, cancel and confirm buttons, a destructive confirm in the delete color. `await ShowAsync(...)` answers true or false; a tap on the dimmed page cancels. |
| `Platform/AndroidAppFolders.cs` | `IAppFolders` for the phone (see "Where the files are"). |
| `Platform/AndroidWallpaperApplier.cs` | `IWallpaperApplier` for the phone (see "Setting the wallpaper"). |
| `Platform/AndroidAppInfo.cs` | `IAppInfo` for the phone: the version label is "v" and the major number of the package's version name. No workflow sets the version yet, so every build is 1.0.0 and shows "v1" (step 7 sets it). |
| `Platform/AndroidShellOpener.cs` | `IShellOpener` for the phone: opens a web address in the browser (Report a bug, the project link). Measured: the emulator's browser (WebView Browser Tester) opened the project page. |
| `Platform/AndroidFactoryReset.cs` | `IFactoryReset` for the phone (see "Factory Reset"). |
| `Platform/AndroidDeviceEasterEggs.cs` | `IDeviceEasterEggs` for the phone: the catalog without Fleeting Words (no startup screen with words) and Hexspeak (no title colour box), three eggs, with the Konami hint "Every arcade player knew this one by heart. Swipe it here in Settings." and the version hint "The version number in About does not look tappable. Try anyway. Keep going." |
| `Platform/AndroidSettingRules.cs` | `IDeviceSettingRules` for the phone: it draws no title on the wallpaper (see "No title on the phone's wallpaper"); the fill styles are Fill, Fit, Stretch, Tile and Center. A stored Span shows as Fill, draws as Fill, and stays in the file until another fill style is picked. |
| `Platform/AndroidPermissions.cs` | `INTERNET` and `SET_WALLPAPER`, as assembly attributes. |
| `Resources/values/styles.xml` | The Android theme and its colors. |
| `Resources/mipmap-anydpi-v26/icon.xml` | The adaptive launcher icon. |

The build generates the manifest from the attributes on `MainApplication` and the permission
attributes, from the csproj (`ApplicationId`, `SupportedOSPlatformVersion`), and merges in
`AndroidManifest.xml`, which declares the activity by hand: for an activity the hand-written
file declares, the generator adds nothing, so that entry carries the launcher intent filter and
`exported` itself. The generated file is
`PaperNexus.Android/obj/<Configuration>/net10.0-android/android/AndroidManifest.xml`.

Decisions that are easy to undo by accident:

- **`PageNavigationHost` wraps `MainView`.** Android 15 and later always draw an app edge to edge,
  under the status bar and the navigation bar. `PageNavigationHost` passes the system bars' insets
  to the page, and `TabbedPage` then keeps its tab bar above the navigation bar. Without it the
  back, home and recents buttons sit on top of the tabs. It also passes Android's back button and
  back gesture to the page tree.
- **A `NavigationPage` with no bar sits between the host and `MainView`.** It is what shows the
  Sources tab's form over the tabs as a modal page (`NavigationPage.SetHasNavigationBar(mainView,
  false)` keeps its own title bar away). Measured on the emulator after adding it: the tab bar
  still sits above the navigation bar, the status bar inset is unchanged, and a wallpaper change
  still does not recreate the activity.
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
- Each activity gets a new view; the view model lives as long as the process (see "Keeping
  the screen"), so the first-open work runs once (`EnsureLoadedAsync`).
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
- Memory with 55 pictures (4K JPEGs), total PSS from `dumpsys meminfo`, after the two fixes
  below:

  | Build | At rest | Gallery open | Peak while flinging the whole list twice | After leaving the Gallery |
  | --- | --- | --- | --- | --- |
  | Debug | 198 MB | 225 MB | 343 MB | 222 MB |
  | Release | 154 MB | 173 MB | 282 MB | 184 MB |

  Before them the Debug peak was 523 MB and 381 MB stayed after leaving. The growth was native
  heap still in use, not memory kept by the allocator: every thumbnail the list had ever shown
  was still held (55 of 55 with 3 cards built), because a virtualizing list reuses a card's
  container without saying which picture it showed. `CardThumbnailHolder` (shared with the
  desktop gallery, which had the same leak at 600 px) now remembers that and lets each
  picture's thumbnail go. Thumbnails are also decoded at reduced size
  (`ImageCodec.DecodeFileAtLeast`: a JPEG decodes straight to the smallest eighth of its size
  that is wide enough; PNG has no scaled decode in Skia's codec and decodes whole); on its own
  that lowered the Debug peak from 523 to 486 MB.

## Sources

The rules for sources (listing, adding, editing, deleting, turning on and off, the checks a
source must pass, and testing a feed) are `IWallpaperSources`
(`PaperNexus.Shared/WallpaperSources.cs`), which the desktop's settings window and source dialog
use too. Download now is `IDownloadWallpapers.DownloadAllAsync`, which reports how many enabled
sources it tried and how many failed (`DownloadOutcome`); `DownloadSentence` turns that into the
sentence both devices show. The phone's `SourcesViewModel` lists the rows (`SourceItem`, shared
with the desktop window) and holds the open form (`SourceFormViewModel`, shared with the desktop
dialog).

- **The tab:** "Add" and "Download now" across the top; one row per source with its name, an
  on/off switch, and edit and delete buttons (48 dp) on the row. With no sources: "No sources
  yet. Tap Add to create one."
- **Every tap shows a line** over the bottom of the list for three seconds, the Gallery's strip:
  "Settings saved." (green) after a save, a switch or a delete; "Error saving settings: …" (red);
  "Downloading wallpapers..." (white) while Download now runs, then "Download complete.", "Download
  failed for N of M sources.", "Download failed: check your connection." or "No enabled sources to
  download from." Download now is dimmed while it runs. Delete asks nothing, as on the desktop.
- **The switch saves at once** (the desktop's checkbox saved with the window's settings save, half
  a second later); a failed save puts it back.
- **The form** covers the whole screen: the title and the Enabled switch, then Name, URL, Image URL
  JPath, Title JPath and Download Schedule (Cron) with the desktop dialog's labels, placeholders
  and hints, then Cancel, Test and Save at the bottom with the red refusal or the green test result
  just above them. A refusal keeps the form open with what was typed. Test shows the desktop's
  text: how many pictures the feed lists, then each picture's title and address.
- **Back** with the form open closes the form without saving, as Cancel does; the first back
  closes the keyboard if it is open. Back on the tabs leaves the app, as before.
- **Names are unique**, compared ignoring case after trimming ("A source named … already exists.").
  An older `settings.json` that already holds two sources with one name loads and lists both. A
  source is known by its `SourceKey` (its name and which of the same-named sources it is), so the
  switch, edit and delete of each row act on exactly that row's source. Either twin can be edited
  keeping its own name; renaming one to another source's name is refused; nothing is renamed by
  itself.

Measured on the Android 16 emulator (1080x2400, the on-screen keyboard shown):

- The app is drawn edge to edge and Android does not make the window shorter for the keyboard.
  `SourceFormPage` (through `KeyboardLift`) lifts its content by the part of the keyboard above the navigation bar
  (`InputPane.OccludedRect` less the page's safe area padding), so the bottom buttons sit on the
  keyboard's top edge and the focused field, with its hint, is scrolled into view, the lowest
  (the schedule) included.
- The keyboard is taller on fields that show a suggestion strip (Name, URL) than on the others.
  Its height changing while it stays open raises no `InputPane.StateChanged`, only
  `InsetsManager.SafeAreaChanged`, so the page follows both.
- The keyboard's Next key moves to the next field; the schedule shows Done.

### Text input options in Avalonia 12.1.1

Measured with `adb shell dumpsys input_method` (the focused field's `inputType`), so they need
not be measured again until Avalonia changes:

- `TextInputOptions.ContentType="Url"` reaches Android as `inputType=0x10`: the URI variation
  without the text class. The keyboard shows its ordinary layout, not the web-address one.
- `TextInputOptions.ShowSuggestions="False"` on that same URL field is not passed on: no
  no-suggestions flag, and the keyboard shows its suggestion strip.
- The two JPath fields and the schedule (`ShowSuggestions="False"`, `AutoCapitalization="False"`)
  reach Android as `inputType=0x80081`: text class, no-suggestions flag, and the password
  variation (`0x80`). The text stays visible in the field, but the keyboard treats it as a
  password field (no suggestion strip, nothing learned).
- `TextInputOptions.ContentType="Number"` reaches Android as `inputType=0x2` (the number keypad);
  `"Digits"` reaches it as `0x3`, the phone keypad. The Settings tab's number boxes use Number.
- The emulator reports a hardware keyboard, so Android shows no on-screen keyboard until
  `adb shell settings put secure show_ime_with_hard_keyboard 1`; set it back to `0` afterwards,
  and power the emulator off with `adb shell reboot -p` rather than `adb emu kill`: a kill right
  after the change lost it once, and the next boot read `1` again.

### Dragging a switch

`NavigationPage` (Avalonia 12.1.1) adds a horizontal `SwipeGestureRecognizer` in its constructor
for its swipe back. Once a finger moves sideways by more than half a tap anywhere on the page, the
recognizer captures the pointer, and the touch device then sends the moves and the lift only to
it. A `ToggleSwitch` being dragged never hears the finger lift: its knob stops where the capture
happened and the value does not change. `NavigationPage.IsGestureEnabled = false` does not help;
it is read only when the swipe ends, not by the recognizer. Measured on the emulator: a sideways
drag on a Sources row switch left the knob halfway, with `IsGestureEnabled = false` set.

`App` turns the recognizer off (`SwipeGestureRecognizer.IsEnabled = false`, found in the
navigation page's `GestureRecognizers`), which the app can afford: it pushes no page a swipe
could go back from (the Sources form is modal). Measured after: a drag turned the Settings
switches (Prioritize Favorites, Enabled, Debug mode, which then asks question 1 and stays off)
and a Sources row switch on and off, also when the finger ran past the switch's end.

A drag that moves more up or down than sideways in a list that can scroll still scrolls the list:
its `ScrollGestureRecognizer` captures the pointer the same way, the value stays, and the knob is
left slightly short of its end until the next tap. `ToggleSwitch` does not handle losing the
pointer, so this stays as it is.

## Settings

The Settings tab is the desktop window's settings groups, shared from `PaperNexus.Shared`
(`DownloadSettingsViewModel`, `SlideshowSettingsViewModel`, `DebugSettingsViewModel`), in the
other tabs' dark boxes, then About. `SettingsViewModel` holds the three groups, `AboutViewModel`,
`SecretsViewModel`, the open picker, whether Secrets is open, and the confirmation line. Each group writes only its own fields, half a second after the last change,
through `ISettingsStore.UpdateAsync`; the tab never writes the desktop-only settings (start at
login, minimize to tray, automatic updates, the folder) or the title fields.

- **Download Settings:** Saved Resolution (a picker with the desktop's eight choices and their
  lines) and Retention Period (days), a number box with the number keypad.
- **Slideshow Settings:** the Enabled switch in the heading; Schedule (Interval or Cron
  expression); the interval number and its unit (a picker) or the cron box, with the schedule's
  description ("Every 2 hours") under it; Order; Prioritize Favorites and, while it is on, the
  weight; Fill Style with the phone's five choices. A cron expression that does not parse shows
  "Invalid cron expression." in red and is not saved; the slideshow's other settings still are.
- **General Settings:** Debug mode, "Write more detail to the log" (the desktop's hint without
  the title part). Turning it on asks the desktop's seventeen questions with their titles
  ("Confirmation 5 of 17") and No and Yes; the switch stays off until the last Yes, and a No
  shows "Debug mode cancelled. You only made it through N of 17 confirmations." Turning it off
  takes effect at once. The switch is set from the group's value in code, as on the desktop: a
  two-way binding left it showing on after a No. While debug mode is on, the red Factory Reset
  button shows below it and asks the desktop's two questions.
- **Pickers** open a `ChoiceDialog` with the current value marked; a choice sets it and closes
  the box; Cancel, a tap outside it or Android's back closes it without a change.
- **The confirmation line** ("Settings saved.", green; errors red) sits over the bottom of the
  list for three seconds; a focused number box is kept above it and above the keyboard.
- **Debug entries** reach `no_backup/logs/app.log` from the moment debug mode is turned on; a
  wallpaper change then writes one with the rendered size, the canvas, the fill style and the
  title.
- **About:** the heading with the version at its right ("v1", faint, as on the desktop), then
  three 48 dp rows with the desktop's icons and tooltips: "Secrets", "Report a bug" and
  "PaperNexus on GitHub". The two links open the browser through `IShellOpener`; a failure shows
  "Could not open the browser: …" in red on the line (the desktop's status bar shows it too).
  Report a bug opens GitHub's new-issue page, which asks a signed-out person to sign in first.

## Easter eggs

The phone has three of the five (`AndroidDeviceEasterEggs`): the Konami code swiped on the
Settings tab, five taps on the version in About, and favorite milestones. No Hexspeak (no title
colour box) and no Fleeting Words (no startup screen with words).

- **The overlay** is the desktop's `EasterEggOverlay`, over the whole screen: `App` puts the
  page host inside an `EasterEggStage`, a panel with the overlay above it, so an egg covers the
  tab bar and the system bars' strips as the desktop's covers its window. Any view model asks
  through `EasterEggRequests`. The line under the message reads "TAP OR PRESS BACK"; a tap
  anywhere skips the egg (without pressing what is under it), and so does Android's back, which
  the stage answers before the pages while an egg plays. An egg lasts three seconds; back after
  it has ended does what it always does. The stage takes no inset padding, so the pages still get
  the system bars' insets from the page host, and back still reaches them (measured: a picker and
  the Sources form close on back, and back on a tab leaves the app).
- **A recreated screen continues the egg:** `EasterEggPlayback` (a container singleton) holds the
  egg and when it started, and the new stage plays it from the frame it had reached. Measured: a
  screenshot taken 0.8 s after a font scale change created the new activity showed the egg.
- **Konami by swipes:** the Settings view watches every stroke (tunnel-phase press and release,
  handled ones too, never marking anything handled). Strokes that start on a button, switch, text
  box or number box, or while a list or question is open, are ignored. `KonamiGestures.Classify`
  makes a stroke a swipe (at least 48 dp along one axis and twice as far as across it, in the
  direction the finger moved) or a tap (under 8 dp in under 300 ms); the strokes feed the
  desktop's `KonamiSequence`, the two taps standing for B and A. A vertical swipe the list can
  follow scrolls it, and the list then takes the finger, so the release never arrives; the stroke
  is then the list's scroll gestures added up (`ScrollGesture` and `ScrollGestureEnded`, which
  still bubble to the tab). Horizontal swipes reach the tab because no horizontal recognizer is
  left (see "Dragging a switch"). Measured: up, up, down, down, left, right, left, right and two
  taps, each started on a label or hint, played "+30 LIVES GRANTED"; the down swipes scrolled
  the list to its top.
- **Version taps:** five taps on the version, each within three seconds of the last.
- **Favorite milestones:** the 1st, 10th, 25th, 50th and 100th favorite made with the Current
  tab's or a Gallery card's heart play the egg instead of "Added to favorites.", as on the
  desktop.
- **Secrets** (the About row) opens a full-screen modal page with the desktop's layout and text:
  "SECRETS", "Hidden things scattered through the app. Find them all.", "N / 3 found" (or "3 / 3
  found - all of them. Nothing left to hide.") with the yellow bar, one row per egg with a box
  that is filled when found, its name and the phone's hint, and Close (tooltip "Close the
  secrets list"). Back closes it too. It reads the found eggs each time it opens; whether it is
  open is kept in `SettingsViewModel`, so a recreated screen opens it again. Found eggs are
  `DiscoveredEasterEggs` in `settings.json`, which Factory Reset deletes with the rest.


## Keeping the screen

Whatever the person is looking at survives Android destroying and recreating the activity
(font size, language, display size):

- The view models of the Current, Gallery, Favorites and Sources tabs and `MainScreenState` (the
  selected tab) are container singletons, created on the screen's thread when the first view
  asks for them. Each activity gets a new `MainView`, which binds to them when it reaches the
  screen and sets the tabs' `DataContext` to null when it leaves, so a dead view holds no
  subscription on a view model and is never updated by one. The view models subscribe to the
  switcher and the library once, for the process.
- A recreated screen opens on the same tab, with the same cards (not listed again), the same
  confirmation line while it is within its three seconds, and the list at the same place
  (`ListPlace`; rows whose titles wrap to two lines can put it a fraction of a row off).
- Thumbnails are still released when a tab leaves the screen, and loaded again for the cards in
  view when it returns.
- The Settings tab's view model and its three groups are container singletons too, so a recreated
  screen shows the same scroll place, an open picker and an open debug or Factory Reset question
  (the group's `PendingQuestion`). Measured with a font scale change: the middle of the list, the
  open Order picker and question 5 of 17 were all back.
- An open add or edit form is the sources view model's `Form`, so a recreated screen opens it
  again with what was typed, the refusal line and the test result (a test still running finishes
  into it). Measured with a font scale change: the form, the typed schedule and the test result
  were all back. The keyboard focus and the form's scroll place are not kept: the new screen opens
  the form at its top with the keyboard closed.

A wallpaper change does not recreate the activity at all. On Android 12 and later every new
wallpaper makes the system rebuild its wallpaper-based color overlays, which is an asset paths
configuration change; the activity declares `configChanges="...|assetsPaths"` in
`AndroidManifest.xml` and keeps running (the .NET Android manifest generator drops
`ConfigChanges.AssetsPaths` from the `[Activity]` attribute, hence the hand-written entry). So
set as current and delete stay on their tab and show their line. Measured on the emulator
(Debug and Release): the activity object is the same before and after a wallpaper change, and
a font scale change still creates a new one. The system's event log shows each new activity
object: `adb logcat -b events -d | grep wm_on_create_called` (the activity record number in
`dumpsys activity` stays the same when the activity is recreated, so it does not tell). The tab accent color, which Avalonia takes from
the system's wallpaper-based accent, still follows each new wallpaper without the rebuild.
Rotation and dark/light mode changes still keep the activity, as before.

## Composition and lifecycle

- `MainApplication.OnCreate` builds the container once per process with `new HostBuilder()`:
  the shared services (`AddServicesFrom` on the shared assembly), then this host's
  implementations (`AddServicesFrom` on the Android assembly), the screens as transient
  registrations, and the file logger. It does not call `AddInProcessSchedules`, so the
  slideshow does not run on a timer inside the app (issue #203, step 5 brings it).
- The scheduled downloader starts with the host: its first run begins at once and later runs
  follow each source's schedule while Android keeps the process.
- The logger reads its level from a `LogLevelSwitch` that `MainApplication` creates and registers:
  debug entries while the debug mode setting is on, from the moment it changes (`DebugLogLevel`
  applies the stored setting when the host starts). The Settings tab's Debug mode switch changes
  it.
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
`GetCanvas()` returns a `WallpaperCanvas` for the screen in its upright orientation (the screen
size times the wallpaper zoom), and the shared `WallpaperRenderer` draws the picture onto it for
the fill style. Measured on the Android 16 emulator (1080x2400), issue #203 step 3:

- A wide picture (3840x2160) given as is was scaled to the launcher's desired size
  (`getDesiredMinimumWidth/Height`, 4800x2400) and its left part shown.
- Android enlarges a still wallpaper by about 1.1 on both the home screen and the lock
  screen, so the screen shows only the middle 1/1.1 of it. The factor is the system value
  `config_wallpaperMaxScale` (1.1 here), which is not public API; `AndroidWallpaperApplier`
  reads it by name and takes 1.0 when it cannot.
- A picture 1.1 times the screen size (1188x2640) shows at one picture pixel per screen pixel,
  centred. That is the canvas.
- Fill styles are drawn on the whole canvas, as a monitor shows them; at rest the screen shows
  its middle 91%, so Fit's sides lose about 4.5%. Span draws as Fill.
- `dumpsys wallpaper` after a change shows `System wallpaper state: User 0: id=N: mWhich=3`
  and an empty `Lock wallpaper state`: one image serves both screens.
  `getWallpaperId(FLAG_LOCK)` returns -1 while the lock screen shares the home screen's
  picture, so check `mWhich=3` rather than a lock id.

## Factory Reset

`AndroidFactoryReset` keeps the desktop's meaning: it deletes the app's own data with the shared
`AppDataReset` (`files/settings.json`, and `current.*`, `timers.json` and `logs/` in `no_backup/`)
and keeps the downloaded pictures in `no_backup/Wallpapers/`. It then starts the launcher activity
as a new task that replaces the current one (`Intent.makeRestartActivityTask`) and ends the process
(`Process.killProcess`), so nothing the old process held in memory survives.

The Settings tab's Factory Reset button (shown while debug mode is on) starts it after the two
questions. Measured on the Android 16 emulator, Debug and Release (first with a temporary
trigger, then from the button): the old process died, a new process started for the activity Android was asked to show, and the Current
tab appeared about a second later. The settings were back to their defaults (no favorites, both
default sources), the pictures were kept, and with the network off the tab set the next existing
picture without downloading, as on first open with pictures.

`ActivityManager.clearApplicationUserData` was not used: it erases the pictures as well, and its
documentation does not say the app is started again.

## No title on the phone's wallpaper

The phone never draws the picture's title on the wallpaper, whatever `settings.json` says
(`IDeviceSettingRules.DrawsWallpaperTitle` is false; `SwitchWallpaper` builds no title then). The
owner decided it in step 4 after the measurement: the launcher's search bar and its dock and app
icons cover a title on the home screen, and the lock screen's clock and unlock icon cover it there,
at every position and size tried, and the wallpaper cannot know where they are.

So the phone's Settings tab (step 4, its last part) has no "Title on Wallpaper" group: no
`AnnotationSettingsViewModel` on the phone and no font picker page. Its Secrets list leaves out
Hexspeak, which is typed into the title's colour box ("N / 3 found").

A fresh phone `settings.json` still holds `"AnnotateWallpaper": true`, the model's default; it is
harmless, because only the switcher reads it and the phone's switcher draws no title.

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
- Change a setting the phone has no screen for by editing it in place on a Debug build:
  `adb shell "run-as com.gnostack.papernexus sed -i 's/\"FillStyle\": \"[A-Za-z]*\"/\"FillStyle\": \"Fit\"/' files/settings.json"`.
  The app reads the file on every change, so the next tap uses it.
- No network: `adb shell svc wifi disable` and `adb shell svc data disable` (`enable` to undo).
- Font size (recreates the activity): `adb shell settings put system font_scale 1.15`, and `1.0`
  to undo.
- Shut the emulator down when finished: `adb emu kill`.

The app drawer on a 5-column launcher shortens the label to "PaperNe..."; the label itself is
`PaperNexus`.

## Tests

There is no Android test project: a `net10.0` test project cannot reference a
`net10.0-android` project. The logic the phone relies on lives in `PaperNexus.Shared` and is
tested there: the canvas math (`WallpaperCanvas.ForScreen`), fitting and rendering for a
canvas (`WallpaperFittingTests`), the switcher with a canvas or a refusal
(`SwitchWallpaperDeviceTests`), the Current tab's states (`CurrentWallpaperViewModelTests`),
the picture library (`WallpaperLibraryTests`), the Gallery and Favorites tabs
(`PictureListViewModelTests`), the source rules (`WallpaperSourcesTests`), the add or edit form
(`SourceFormViewModelTests`), the Sources tab (`SourcesViewModelTests`), Download now's
sentences (`DownloadOutcomeTests`), the Settings tab (`SettingsViewModelTests`, with the
shared groups' own tests) and the Konami strokes (`KonamiGesturesTests`).
The Android classes only pass Android's numbers and calls through; they are verified on the
emulator.

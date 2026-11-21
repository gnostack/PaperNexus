# Android Host

`PaperNexus.Android` is the Android app (issue #203). It targets `net10.0-android` (API 36) with
Android 12 (API 31) as the minimum, application ID `com.gnostack.papernexus`, label `PaperNexus`.
It references `PaperNexus.Shared`, as the desktop host does; the phone's screens live in this
project and its logic and view models in the shared project.

## What it is made of

| File | Job |
| --- | --- |
| `MainApplication.cs` | The Android application object (`[Application]`, deriving `AvaloniaAndroidApplication<App>`). Sets the label, the launcher icon (`@mipmap/icon`) and the theme (`@style/PaperNexusTheme`), and adds the Inter font as the desktop does. It is the phone's composition root: `OnCreate` builds the service container and starts it before Avalonia creates `App`. |
| `MainActivity.cs` | The launcher activity (`AvaloniaMainActivity`). Shows the view `App` supplies. Its `[Activity]` attribute only fixes its Java name, `com.gnostack.papernexus.MainActivity`; the activity is declared in `AndroidManifest.xml`. It tells `AndroidExactTimingPermission` when it resumes, pauses and ends, so turning Exact timing on can read the answer from the phone's "Alarms & reminders" screen, and joins `StoppedLayoutRepair` to Avalonia's drawing surface. |
| `StoppedLayoutRepair.cs` | Lays the whole screen out again when its drawing surface comes back after the activity was stopped: a repair for Avalonia 12.1.1 (see "The screen after a return"). |
| `AndroidManifest.xml` | The hand-written part of the manifest: the activity, its launcher intent filter, the configuration changes it handles itself (see "Keeping the screen") and the long-press entries' file; and `ShortcutActivity` (see "Long-press menu"). |
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
| `Views/IconToggleSwitchTheme.axaml` | The look of a switch with no words beside it (see "Switches without words"). |
| `Views/KeyboardLift.cs` | Keeps the keyboard off what is being typed: lifts a page's content by the part of the keyboard above the navigation bar and scrolls the focused field (with its hint) into view, and sets the lift again from where the keyboard is when the page comes back on screen. The Sources form and the Settings tab use it (see "Sources"). |
| `Views/PictureListStyles.axaml` | The card, picture-button and ban-icon styles both picture lists and the Current tab share. |
| `Views/StatusLineStyles.axaml` | The confirmation line's look (white, green, red) for every tab, which each tab's `StatusLine` drives. |
| `Views/PictureFrame.cs` | Holds a card's picture in a 16:9 box as wide as the card, so every card has the same height. |
| `Views/CardThumbnails.cs` | Loads a card's thumbnail at the screen's pixel width when the list builds the card, and releases it when the list clears or reuses the card (through the shared `CardThumbnailHolder`) or the tab leaves the screen. |
| `Views/ConfirmationDialog.axaml(.cs)` | The reusable in-page question: the page dimmed, a box in the middle (as wide as `ChoiceDialog`'s whatever the question, long text wrapping inside) with an optional title line, cancel and confirm buttons, a destructive confirm in the delete color. `await ShowAsync(...)` answers true or false; a tap on the dimmed page cancels. |
| `Platform/AndroidAppFolders.cs` | `IAppFolders` for the phone (see "Where the files are"). |
| `Platform/AndroidWallpaperApplier.cs` | `IWallpaperApplier` for the phone (see "Setting the wallpaper"). |
| `Platform/AndroidAppInfo.cs` | `IAppInfo` for the phone: the version label is "v" and the major number of the package's version name. The package workflow builds version name N.0.0 from the desktop release's build number N, so the phone shows "vN" as that release does; a local build is 0.0.0 and shows "v0" (see "Versions and the store package"). |
| `Platform/AndroidShellOpener.cs` | `IShellOpener` for the phone: opens a web address in the browser (Report a bug, the project link). Measured: the emulator's browser (WebView Browser Tester) opened the project page. |
| `Platform/AndroidFactoryReset.cs` | `IFactoryReset` for the phone (see "Factory Reset"). |
| `Platform/AndroidDeviceEasterEggs.cs` | `IDeviceEasterEggs` for the phone: the catalog without Fleeting Words (no startup screen with words) and Hexspeak (no title colour box), three eggs, with the Konami hint "Every arcade player knew this one by heart. Swipe it here in Settings." and the version hint "The version number in About does not look tappable. Try anyway. Keep going." |
| `Platform/AndroidSettingRules.cs` | `IDeviceSettingRules` for the phone: it draws no title on the wallpaper (see "No title on the phone's wallpaper"); the fill styles are Fill, Fit, Stretch, Tile and Center. A stored Span shows as Fill, draws as Fill, and stays in the file until another fill style is picked. The fastest slideshow is every 15 minutes; the interval units are Minutes (15, 20 or 30, picked from a list), Hours, Days, Weeks, Months and Years (see "Settings"). |
| `Platform/AndroidPermissions.cs` | `INTERNET`, `SET_WALLPAPER`, `RECEIVE_BOOT_COMPLETED` and `ACCESS_NETWORK_STATE`, as assembly attributes. |
| `Platform/AndroidBackgroundScheduler.cs` | `IBackgroundScheduler` for the phone: the slideshow's alarms and the download job (see "Background slideshow and downloads"). |
| `Platform/AndroidExactTimingPermission.cs` | `IExactTimingPermission` for the phone: whether exact alarms are allowed (`canScheduleExactAlarms`), and opening the phone's "Alarms & reminders" screen to ask (see "Settings"). |
| `Background/SlideshowAlarmReceiver.cs`, `Background/DownloadJobService.cs`, `Background/ScheduleRestoreReceiver.cs` | What Android starts the app for: a slideshow change, a scheduled download run, and a restart, app update or clock change that needs the work armed again. |
| `Shortcuts/ShortcutActivity.cs` | What the icon's long-press entries "Next Wallpaper" and "Random Wallpaper" start: an activity that shows nothing, changes the wallpaper and finishes (see "Long-press menu"). |
| `Resources/xml/shortcuts.xml`, `Resources/values/strings.xml`, `Resources/drawable/shortcut_*.xml` | The long-press entries, their labels and their icons. |
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
- After a background slideshow change failed (the phone refused the picture, or it could not be
  read), the tab shows "The slideshow could not change the wallpaper. Tap Next to try again." in
  red under the buttons when it opens, or at once if it is open. The next change that works - a
  background one, Next, Random or set as current - takes it away. The fact is the slideshow's
  entry in `timers.json` (`BackgroundWork`, read through `IScheduledWorkFailures`).
- A picture that cannot be read (a damaged file) leaves the placeholder's empty #1E1E1E box in its
  place, so the title and the buttons stay where they are; the picture shown before is released.
- A change made while another tab is shown (set as current on the Gallery, a long-press entry, the
  slideshow) shows its picture when the Current tab comes back: the view scales the picture to its
  own width, which it can only read on the screen, so it shows the picture again when it is
  attached (measured before the fix: the new title over the old picture).
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
- **A failed scheduled download** (some sources' feeds could not be read) shows "The last
  scheduled download failed for N of M sources." in red on the line when the tab is first shown,
  held until another line replaces it. The next scheduled run that works takes it away, and so
  does a Download now in which no source failed: it downloads every enabled source, so it
  supersedes the failed run. The fact is the scheduled download's entry in `timers.json`, with one
  writer: `BackgroundWork` writes it after each scheduled run that tried a source
  (`RunDownloadsAsync`) and clears it after a Download now that worked (`RecordDownloadNowAsync`,
  which the Sources tab calls with Download now's outcome).
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

### Switches without words

Fluent's switch template (Avalonia 12.1.1) keeps a fixed 12 dp column after the track for the
words beside it (`<ColumnDefinition Width="12" MaxWidth="12" />`), with no property or resource to
remove it, so a switch with no words ended 12 dp short of the pickers and number boxes. The
`IconToggleSwitch` theme (`Views/IconToggleSwitchTheme.axaml`, merged into `App`'s resources) is
Fluent's switch theme with that template copied from
`src/Avalonia.Themes.Fluent/Controls/ToggleSwitch.xaml` and the columns removed; every part name is
kept and the theme is based on Fluent's, so the on, off, pressed, disabled and dragging looks still
apply. The switches with no words use it (`Theme="{StaticResource IconToggleSwitch}"`): Prioritize
Favorites and Debug mode on the Settings tab, and the Sources rows (whose right margin grew by the
same 12 dp, so their track did not move). The two switches with "Enabled" beside them keep the
built-in look: their word already ends on the edge. **Re-check this theme against Fluent's
`ToggleSwitch.xaml` whenever Avalonia is updated**: a changed template there is not picked up here.

Measured on the emulator (Debug and Release): the Settings switch tracks end at pixel column 1013,
the pickers at 1012 and the number box at 1013; a Sources row's track is at 647 to 751 and its
edit button starts at 794, as before. Tap and drag work on each switch.

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
- **Exact timing** (issue #203, step 5; `SlideshowSettings.ExactTiming`, off by default; only a device
  whose `IDeviceSettingRules.OffersExactTiming` is true shows or writes it): a row after the
  schedule's description, "Exact timing" with the switch with no words (tooltip "Change the
  wallpaper on the minute") and the hint "Change on the minute. Uses more battery." The switch is
  set from the saved value in code and only asks to change it (`RequestExactTimingAsync`), as Debug
  mode's does, so it never shows a value that is not saved. Off takes effect at once. On, when the
  phone allows exact alarms (Android 12 and 13 from the start), turns on and says "Settings saved.";
  when it does not (Android 14 and later until allowed), the phone's "Alarms & reminders" screen
  opens, and the answer is read when the app resumes: allowed turns it on; not allowed leaves it off
  with "Exact timing needs the phone's Alarms & reminders permission." in red. Saved on while the
  phone no longer allows it (the permission was taken away while the app was closed): the tab shows
  it off with that sentence held on the line, and off is saved. Factory Reset deletes the setting
  (off again); the phone's permission stays as the person left it, since an app cannot take it
  back, so turning Exact timing on again turns it on without the screen.
- **The 15-minute rule** (issue #203, step 5; `IDeviceSettingRules.ShortestSlideshowGap` and
  `IntervalTypes`): the unit picker lists Minutes, Hours, Days, Weeks, Months and Years (no
  Seconds). For Minutes the number box is replaced, in the same place, by a picker button showing
  the number; it opens a `ChoiceDialog` titled "Minutes" with 15, 20 and 30 (tooltip "Choose the
  interval"). Choosing Minutes from a larger unit moves the number to the nearest of the three. The
  other units keep the desktop's number box and ranges. A schedule that can change faster than every
  15 minutes (a typed cron such as `*/5 * * * *`, or a minute count that leaves a shorter gap before
  the hour, such as every 50 minutes) is not saved and shows "The phone can change the wallpaper at
  most every 15 minutes." in red; the slideshow's other settings still are. A stored schedule that
  is too fast (only a development phone can have one) shows as it is, with that sentence held on
  the line until the next one, and nothing is rewritten; a stored unit the phone does not offer
  (Seconds) shows as stored with the number box. A stored minute count not in the list that keeps
  15 minutes apart (45) shows as it is on the button, with nothing marked in the list.
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
- **About:** the heading with the version at its right ("vN", faint, as on the desktop), then
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
  registrations, and the file logger. It does not call `AddInProcessSchedules`: the slideshow
  and the scheduled downloads run no timer inside the app's process, which Android can stop at
  any time. Android starts the process for each change and each download run (see "Background
  slideshow and downloads").
- `OnCreate` runs whatever starts the process: the screen, the slideshow's alarm, the download
  job or a broadcast. Starting the host applies the stored log level (`DebugLogLevel`) and arms
  the background work (`BackgroundWork`); it starts no download and no wallpaper change itself.
  The tabs' view models are created only when a screen asks for them, so the Current tab's
  first-open download runs only when the app is opened.
- The logger reads its level from a `LogLevelSwitch` that `MainApplication` creates and registers:
  debug entries while the debug mode setting is on, from the moment it changes (`DebugLogLevel`
  applies the stored setting when the host starts). The Settings tab's Debug mode switch changes
  it.
- The log is `no_backup/logs/app.log` (`Assembly.GetEntryAssembly()` is null on Android, so
  the logger's fallback name is used). Read it with
  `adb shell run-as com.gnostack.papernexus cat no_backup/logs/app.log` (Debug builds only).

## Background slideshow and downloads (issue #203, step 5)

Built in four changes (issue #203, step 5). The design: the slideshow is a
one-shot alarm set for the next change and set again after every change, a 10-minute window
alarm with Exact timing off, an exact allow-while-idle alarm plus the same window alarm as a
backup with it on; downloads are one persisted JobScheduler job that waits for a network. What is
armed is always derived from the settings (`BackgroundPlan.For`, after each `ISettingsStore.Saved`
and at every process start), never stored. No package is added: AlarmManager and JobScheduler are
the platform's own.

**How it runs (change 3, Exact timing off):**

- **`BackgroundWork`** (shared) plans when the process starts (it is a hosted service), after every
  settings save (`ISettingsStore.Saved`), and after every change and download run. It arms each
  of the two only when its time differs from what is armed, through `IBackgroundScheduler`
  (`AndroidBackgroundScheduler`). The slideshow switched off cancels its alarm; an edited schedule
  arms the new time; Factory Reset starts a new process, which plans from the defaults.
- **The slideshow** is a one-shot `AlarmManager.SetWindow(RtcWakeup, time, 10 minutes)` alarm whose
  pending intent carries the scheduled time. `SlideshowAlarmReceiver` handles it with `goAsync`:
  `BackgroundWork.RunSlideshowAsync` changes to the next picture in slideshow order (the desktop
  slideshow's rule), records the run in `timers.json` (`PaperNexus.BackgroundSlideshow`: when it
  ran, the time it was for, whether it failed), and arms the next change at least 15 minutes
  after the time this one was due (not when it ran: counted from when it ran, a change a moment
  after 12:15 put 12:30 inside the floor, and every 15 minutes became every 30; measured and fixed
  in issue #203, step 7). So 15 minutes always separate the scheduled times; a change Android
  delivers late (a window alarm up to 10 minutes) can come closer to the next one, which keeps its
  own time. A change made by hand (Next, Random, a long-press entry) records nothing and does not
  move the schedule. A second alarm for a time already handled, or one arriving with the slideshow
  off, changes nothing. A spent alarm is always armed again, even for the same time. A last change
  due after now (the clock was set back) sets no floor; an entry without the time it was due
  counts from when it ran.
- **Downloads** are one JobScheduler job (id 1, `DownloadJobService`): one-shot, persisted, needing a
  network, with a minimum latency up to the earliest time a source is due. `RunDownloadsAsync`
  calls `IDownloadWallpapers.DownloadDueAsync`, records the run (`PaperNexus.BackgroundDownloads`:
  when it ran, sources tried and failed), finishes the job, then arms the next. While a run is in
  progress its job is not armed again (scheduling the running job's id stops it). A source that
  failed waits for its next occurrence; a run that tried nothing records nothing.
- **Restoring:** `ScheduleRestoreReceiver` plans again after a restart (`BOOT_COMPLETED`), an app
  update (`MY_PACKAGE_REPLACED`) and a clock or time zone change. A change whose time was skipped
  by a clock change is not made up: the next one follows the new clock.
- **The two failure lines** (`IScheduledWorkFailures`): the Current tab's "The slideshow could not
  change the wallpaper. Tap Next to try again." and the Sources tab's "The last scheduled download
  failed for N of M sources." (see those sections).
- `dotnet build -t:Install` of a Debug build force-stops the app after installing it, which
  cancels its alarms and job until the app is opened (measured: `pi_cancelled` in `dumpsys
  alarm`); `adb install -r` of a package, which is what an update does, keeps them.

Measured on the Android 16 emulator (Release, issue #203 step 5, change 3):

| What | Result |
| --- | --- |
| A real change with the app closed and never opened in that process | Armed for 20:00 with a 10-minute window; Android started the process for `SlideshowAlarmReceiver` at 20:10:00.005; the wallpaper id went from 74 to 75; the next change was armed for 20:30; no activity was created. |
| A cold change, start of the process to the end of the change | 1699 ms (1086 ms in the receiver); a second cold one 1477 ms (975 ms). A warm one 1166 ms. Well inside the receiver's 10 seconds, so no job is needed for the change. |
| Doze (forced deep idle, unplugged, screen off) | The window alarm for 21:00 was held (`dumpsys alarm`: `device_idle=+55m`); after leaving Doze it was delivered at the end of its window, 21:10:01. |
| Restart, app not opened | The boot broadcast started the process and armed the change; the next change came with no activity created. |
| App update (`adb install -r`, twice) | The alarm and the job stayed; `MY_PACKAGE_REPLACED` started the process and planned again. |
| Factory Reset | The new process armed the defaults (every 30 minutes) and the download job, which ran at once and then armed the next. |
| A picture that cannot be read | The change failed (`timers.json`: `LastExecutionSucceeded: false`), the wallpaper stayed, the Current tab showed the red line; Next with the picture repaired changed the wallpaper and took the line away. |
| A feed that cannot be reached (forced with `adb shell cmd jobscheduler run -f com.gnostack.papernexus 1`) | The run recorded 3 tried, 1 failed; the Sources tab showed "The last scheduled download failed for 1 of 3 sources."; the next run that worked cleared it. |

**Exact timing (change 4):** with `SlideshowSettings.ExactTiming` on and exact alarms allowed,
`BackgroundWork` plans the change on the minute and `AndroidBackgroundScheduler` arms two alarms
for the same time: `setExactAndAllowWhileIdle` (request code 2) and the window alarm (request code
1) as a backup. Whichever arrives first changes the wallpaper and arms the next pair, replacing the
other; both arriving together change it once (`RunSlideshowAsync` runs one change at a time and a
time already recorded changes nothing). Revoking the permission ends the app's process and deletes
the exact alarm; the backup still changes the wallpaper. The permission broadcast
(`SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED`, `ScheduleRestoreReceiver`) plans again after a
grant outside the app; the switch stays as saved.

Measured on the Android 16 emulator (Release, issue #203 step 5, change 4):

| What | Result |
| --- | --- |
| Turning it on (Android 16) | The phone's "Alarms & reminders" screen opened (`com.android.settings/.spa.SpaActivity`). Back without allowing: the switch stayed off with the red sentence. Allowing, then back: on, "Settings saved.", and `dumpsys alarm` showed the 21:00 pair (`window=0 exactAllowReason=permission` and `window=+10m0s0ms`). |
| A real change with the app closed | Android started the process for `SlideshowAlarmReceiver` at 21:00:00.005; the change finished at 21:00:01.121; the wallpaper id went 80 to 81, once; the next pair was armed for 21:30; no activity was created. |
| Both alarms due together (clock moved past the window) | Two receivers ran for the same time: one changed the wallpaper (732 ms), the other changed nothing (2 ms); the id went up by one. |
| Forced deep Doze | Still idle, the change for 22:00 finished at 22:00:00.953. |
| Revoked with the app closed | "Killing … schedule_exact_alarm revoked"; only the window alarm was left; the next change still came (id 83 to 84) through it; Settings then showed the switch off with the red sentence, and the next open showed it off with no sentence (off was saved). |
| Granted outside the app | The broadcast started the process; with the switch saved off only the window alarm stayed armed. Turning it on then needed no screen. |
| Restart, app not opened | The boot broadcast armed the pair; the exact change for 21:30 finished at 21:30:00.951. |
| Factory Reset | Exact timing off and only the window alarm armed. The phone's permission stayed allowed: an app cannot take it back, so turning Exact timing on again needs no screen. Nothing is shown for it. |

Change 1 (shared groundwork, nothing on the phone behaves differently yet): the downloader's
scheduled run is `IDownloadWallpapers.DownloadDueAsync`, the in-process timer is `DownloadSchedule`,
the store raises `Saved`, and the schedule rules are `SlideshowSchedule.ShortestGap`,
`SlideshowSchedule.IsFasterThan` (a gap is counted between consecutive occurrences in UTC, over
four years and a day from the start of 2028, so February 29 is inside it), `SlideshowSchedule.NextChange`
(the first occurrence at or after the later of now and the time the last change was due plus the
device's fastest slideshow) and `BackgroundPlan.For` (a due download source is tried once, then waits for its next
occurrence).

Android's rules this rests on, read in the Android documentation and measured on the Android 16
emulator with a throwaway probe app (issue #203, step 5 plan):

- `SCHEDULE_EXACT_ALARM` is denied at install on Android 14 and later (measured on 16:
  `canScheduleExactAlarms()` false) and granted at install on 12 and 13. Google Play allows
  `USE_EXACT_ALARM` only for alarm, timer and calendar apps, so the app declares
  `SCHEDULE_EXACT_ALARM`.
- Revoking it kills the app's process and deletes its exact alarms, with no broadcast; an
  inexact alarm set beside the exact one stays and fires (measured). Granting it sends
  `SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED` (measured, about 7 ms after the grant).
- Alarms are cleared by a restart (the boot broadcast reaches only an app opened at least once)
  and kept by an app update (measured with `adb install -r`; `MY_PACKAGE_REPLACED` started the
  process).
- An inexact `setAndAllowWhileIdle` alarm got a window of 75% of its delay and fired at the end of
  it (measured). Android 12 and later deliver inexact alarms within an hour outside battery saver
  and Doze. During Doze only the allow-while-idle alarms fire, at most once every nine minutes per
  app; an exact one set for 19:00:01.137 fired at 19:00:01.140 in forced deep Doze (measured).
- A broadcast receiver's work, `goAsync` included, must end within 10 seconds. The Release app
  with its screen starts cold in about 2 seconds (`am start -W`, 2061 ms).
- **An app nobody uses for a number of days goes into the restricted standby bucket: 8 days on
  Android 13 and later, 45 days on Android 12 and 12L** (the restricted bucket itself is new in
  Android 12; time the phone is off does not count). There it gets one alarm a day, jobs once a
  day and no network, even while charging. The slideshow then changes about once a day and downloads
  stop until the person uses the app again; setting the app's battery use to "Unrestricted" lifts
  it. On battery the frequent and rare buckets allow 2 and 1 alarms an hour. The owner accepted
  this as it is (issue #203, step 5): nothing new is shown for it. The background slideshow alone
  never counts as use. A tap on a long-press menu entry, or on an entry dragged out as its own home
  screen icon, does count and starts the count of days again, because its activity comes to the
  front (see "Long-press menu"). There is no home screen widget (issue #203, step 6 replaced it
  with the long-press menu).
- Force-stop cancels every alarm and job until the person opens the app again (Android 15 and
  later then send the boot broadcast). A tap on a long-press menu entry starts the process too,
  which arms them again (measured: after `dotnet build -t:Install` force-stopped the app, a tap
  armed the next change).
- This emulator image cannot show standby buckets: App Standby is off (`mAppIdleEnabled=false`),
  it always reports charging, and every app is in bucket 5 (the Android 12 image reports the same
  and puts PaperNexus in bucket 10, `adb shell am get-standby-bucket`). Deep Doze is off until
  `adb shell dumpsys deviceidle enable deep`; put it back with `disable deep`.

## Long-press menu (issue #203, step 6)

Long-pressing the PaperNexus icon shows "Next Wallpaper" and "Random Wallpaper" (the desktop tray
menu's words) after the launcher's own "App info". A tap changes the wallpaper on the home and
lock screen without opening the app. The person can drag either entry onto the home screen as its
own icon, labelled "Next" or "Random" (the short labels) with the PaperNexus icon as a badge; it
works the same way. The decision replaced the home screen widget.

| What happened | What the person sees |
| --- | --- |
| The wallpaper changed | The new wallpaper; nothing else |
| The phone refused the picture, or it could not be read | The pop-up "Could not change the wallpaper." |
| The phone forbids wallpaper changes | The pop-up "This phone does not allow apps to change the wallpaper." (the Current tab's sentence) |
| There are no pictures yet | PaperNexus opens, as from its icon, and the Current tab downloads (its first-open work) |
| A change is already running (another tap, the slideshow, the app's buttons) | Nothing: no second change |

**How it works.** The entries are static app shortcuts (`Resources/xml/shortcuts.xml`): they never
change, exist from install, and Android publishes them again on every update. An entry can only
start an activity, so each starts `ShortcutActivity`, which shows nothing: a transparent theme
(`PaperNexusShortcutTheme`, no animation, no starting window), its own task (empty
`taskAffinity`, so the CLEAR_TASK flag a static entry always carries never touches the app's own
screen), `excludeFromRecents`, `noHistory`, not exported. It asks the shared
`BackgroundWork.RunShortcutChangeAsync` for the change through `MainApplication`'s container (the
same switcher as the tabs), stays in front until the change has ended, shows the pop-up
`ShortcutSentence` gives for the outcome or opens the app, and finishes. It reports the use to the
launcher (`reportShortcutUsed`). Nothing is recorded: the pop-up says a failure at once and no
screen reads it later; a change that works still clears the slideshow's failure line.

**One change at a time.** A tap while any change is running (`ISwitchWallpaper.IsSwitching`) makes
none; a second entry tap while an entry's change runs waits for it, so its screen stays in front
and the process keeps running, and shows nothing.

**Never rename** the ids `next_wallpaper` and `random_wallpaper`, their actions, or the Java name
`com.gnostack.papernexus.ShortcutActivity`: dragged-out icons are recorded by them, and an update
that drops an entry disables its icons. `ShortcutActivity` is declared in `AndroidManifest.xml` by
hand because its `configChanges` must include `assetsPaths`: each new wallpaper is an asset paths
change, which would otherwise recreate it in the middle of its own change.

**Standby.** Each tap brings an activity of the app to the front, which Android counts as using
the app (the App Standby documentation: an app is active when it launches an activity; AOSP's
`AppStandbyController` treats an activity coming to the front as use and resets the usage time),
so a tap starts the count of days to the restricted bucket again (8 days, 45 on Android 12).
Measured: `dumpsys usagestats` records `ACTIVITY_RESUMED` for `ShortcutActivity` and a new
`lastTimeUsed` at each tap, plus a `SHORTCUT_INVOCATION`. The emulator cannot show the bucket itself
(App Standby is off; `app_standby_enabled` does not turn it on).

Measured on the Android 16 emulator (Debug and Release, issue #203 step 6):

| What | Result |
| --- | --- |
| The menu (AOSP Launcher3) | "App info", then "Next Wallpaper" and "Random Wallpaper" with their icons (the long labels) |
| A tap with the app closed | The wallpaper id went up by one (`mWhich=3`); only `ShortcutActivity` was created; it was in front about 1.0 to 1.4 s, 1.6 to 2.4 s after the tap from a cold start; the menu stays open with the entry highlighted meanwhile, then closes; no card in Recents; the launcher in front after |
| A dragged-out icon | The same, from the home screen |
| The app open on Settings, then a tap from the home screen | The app's activity was neither recreated nor destroyed; opening the app showed Settings |
| A truncated picture | The pop-up "Could not change the wallpaper." for about 2 s; the wallpaper stayed; Next with the picture repaired changed it |
| `no_set_wallpaper` user restriction (as root) | The pop-up with the Current tab's sentence, on two lines with the app icon; no change |
| No pictures (the folder moved aside) | PaperNexus opened on the Current tab, downloaded, and set the first wallpaper |
| A tap while a slideshow change ran (clock moved past the alarm) | "AlreadyRunning"; the id went up by one |
| A restart, app not opened; an app update (`adb install -r`) | The menu and the dragged-out icons kept working |
| Another app starting `ShortcutActivity` (`adb shell am start`) | Refused: not exported |

**What the person may notice.** While the change runs (about a second), the home screen does not
answer taps that land on it: Android puts an input sink under every activity
(`ActivityRecordInputSink`), so even a window that takes no touches and is fully transparent did
not let a tap through to the launcher (measured, three runs each way). A tap made before the
activity's window is up (within its start, about 1 s from cold and 0.3 s warm) does reach the
launcher, which acts on it once the activity ends: a double tap on an entry that quick makes two
changes, one after the other.

## Android 12 (issue #203, step 7)

Android 12 (API 31) is the oldest version the app supports (decision 6). Every tab and every
behavior of steps 3 to 6 was checked on the Android 12 emulator (`papernexus-phone-12`,
1080x2400, 420 dpi), Release unless the row says Debug (the Debug build is needed for `run-as`
and the log), from the build of commit `ffd57ae`.

| What | Result on Android 12 |
| --- | --- |
| Install and cold start (Release) | `Displayed` after 1625 to 1820 ms; no crash in the log |
| Launch screen | Tapping the icon shows the launcher icon on #2B2B2B, as on Android 16; a start from the shell shows the plain #2B2B2B window |
| First open (no pictures) | Downloaded, then set the first wallpaper with no tap |
| Next, Random, the heart, delete with its question | Work as on Android 16; the first favorite plays the milestone egg; "Deleted and switched to: …" |
| Delete of the last picture (Debug, the others moved aside) | "Wallpaper deleted. No more wallpapers.", "No wallpapers yet. Downloading…", then a new first wallpaper |
| No network and no pictures | "Could not download wallpapers. Check your connection." with "Try again"; with the network back, Try again downloaded and set a wallpaper |
| A picture that cannot be read (Debug, files cut to 100 bytes) | "Could not change the wallpaper. Try again." in red, the wallpaper stayed. The current wallpaper's own file cut: the Current tab keeps the empty placeholder box in the picture's place (after the fix below; before it the title and buttons moved to the top). A picture cut to 20 KB is not refused: Skia decodes what is there and the rest is black |
| Changing the wallpaper is forbidden (`no_set_wallpaper` as root) | "This phone does not allow apps to change the wallpaper." in red, Next and Random dimmed; the long-press entry shows the same sentence as a pop-up |
| Home screen and lock screen | Both show the picture (screenshots); `dumpsys wallpaper` lists no lock wallpaper. Android 12's dump has no `mWhich`, and its wallpaper id sometimes goes up by 2 for one change (one "Switching wallpaper" line in the log), so a change is judged by the id changing |
| Wallpaper zoom and fill styles | The canvas is 1188x2640 (Debug log), so Android 12 enlarges the wallpaper by 1.1 as Android 16 does. Fit, Stretch, Tile and Center read on both screens from screenshots look as on Android 16 |
| A wallpaper change while the app is open | The activity is not recreated (no `wm_on_create_called`); a font size change recreates it |
| Gallery and Favorites | Set as current, heart, ban (red tint, "Banned from rotation."), unban, delete, remove from favorites |
| Memory with 56 pictures (total PSS) | Debug: 206 MB at rest, 228 MB on Gallery, 271 MB peak flinging the list twice, 238 MB after leaving. Release: 159, 180, 217 and 189 MB |
| Sources | Edit and Test (the success line, "7 image(s) found.", and each title and address), a duplicate name refused ("A source named bing daily 4k already exists."), add and save, the row switch (tap and drag), delete, Download now. Field `inputType`s as on Android 16: Name `0x1`, URL `0x10`, the JPaths `0x80081`, the schedule `0x80081` with Done |
| The keyboard on the form | The buttons sit on the keyboard's top edge and the focused field stays in view; back closes the keyboard, then the form |
| Settings | The pickers, the Minutes list (15, 20, 30), the number box (step 30, number keypad `0x2`), a 5-minute cron refused ("The phone can change the wallpaper at most every 15 minutes."), the 17 debug questions, About "v1" (the pre-Android-13 `GetPackageInfo` path), Report a bug and the GitHub page open the browser |
| Easter eggs | Konami by swipes ("+30 LIVES GRANTED"), five version taps, Secrets "3 / 3 found" |
| Exact timing | On with no system screen, "Settings saved."; the exact alarm and the window alarm armed (`exactAllowReason=permission`). Revoked with the app in the background: "Killing … schedule_exact_alarm revoked", only the window alarm left, and the next open showed it off with "Exact timing needs the phone's Alarms & reminders permission.". On while denied: the phone's "Alarms & reminders" screen opened; back without allowing kept it off; allowing turned it on. Granted outside the app with the switch off: the broadcast started the process and only the window alarm was armed |
| A real background change, Exact timing off, app closed | Window alarm for 15:20; Android started the process for `SlideshowAlarmReceiver` at 15:24:28; the wallpaper id went 18 to 19; 15:40 armed next; no activity created |
| A real exact change, app closed, after a restart with the app not opened | The boot broadcast armed the pair; the process started at 15:40:00.003 and the change finished at 15:40:01.854; id 19 to 20, once; the next pair armed |
| Forced deep Doze, unplugged, screen off (Debug) | The exact alarm was delivered at 16:00:00.004 |
| A background change that failed (Debug, pictures cut to 100 bytes) | Recorded in `timers.json` as failed; the Current tab showed "The slideshow could not change the wallpaper. Tap Next to try again."; Next with the pictures put back changed the wallpaper and took the line away |
| Scheduled download with one feed unreachable (Debug, sources made due, job forced) | 2 tried, 1 failed; the Sources tab showed "The last scheduled download failed for 1 of 2 sources."; Download now took it away |
| An app update (`adb install -r`) | `MY_PACKAGE_REPLACED` started the process; the alarms and the job stayed |
| Long-press menu | 12's launcher shows App info as an (i) icon above "Next Wallpaper" and "Random Wallpaper". A tap with the app closed changed the wallpaper with only `ShortcutActivity` created; the failure and "does not allow" pop-ups show with the app icon; with no pictures it opened the app; a dragged-out icon, "Next" with the app badge, works the same |
| Back on the tabs | Leaves the app; Android 12 keeps the activity, and the next open returns to the same tab |
| Factory Reset | A new process on the Current tab, the defaults (Exact timing off, every 30 minutes), the pictures kept |
| Standby | The emulator cannot show it: App Standby off, always charging, bucket 10 |

**What differs from Android 16 on screen:**

- The status bar and the navigation bar are dark see-through strips over the app, #1A1A1A over
  the app's #2B2B2B. On Android 16 the app's color runs under both.
- The selected tab's accent is always the same bright blue (#00B9FA) on Android 12; on Android 16
  it follows the wallpaper's colors.
- The launcher's long-press menu shows App info as an icon, not a row of text.
- An easter egg covers the app's area only, not the status bar.

- The status bar and the navigation bar cannot be given the app's color through the theme
  (`android:statusBarColor`, `android:navigationBarColor`) on Android 12 to 14: Avalonia's
  `PageNavigationHost` draws the app edge to edge, and Avalonia then sets the window's translucent
  status and navigation flags (measured in `dumpsys window`: `TRANSLUCENT_STATUS
  TRANSLUCENT_NAVIGATION`), under which Android draws its own dark see-through bars and ignores the
  theme's colors; Avalonia's own `SystemBarColor` is ignored while edge to edge. Measured: the two
  theme lines left both bars #1A1A1A.

### The screen after a return

**What happened.** A wallpaper change made while the app was in the background on the Current tab
(a long-press entry, the slideshow) left the Current tab broken when the app was opened again: no
picture, no tab bar, the title cut off at the right and the buttons pushed to the right, until the
activity was recreated. Both versions; on the unfixed build, with the title changing between one
and two lines, Android 12 broke 3 times of 3 and Android 16 3 times of 3.

**Why** (measured with a diagnostic build logging the layout at each step of the activity's life):

- When the activity stops, Android takes its drawing surface away. From then Avalonia 12.1.1's
  Android view reports a size of one pixel (0.381 x 0.381 at scaling 2.625), while
  `TopLevel.ClientSize` keeps 411 x 914 and raises no change.
- A change that alters the height of what is on the screen (the Current tab's title wrapping to a
  different number of lines, or a picture of another size) makes the next layout pass measure the
  whole screen against that one pixel: the root's desired size became 0.38 x 0.38, the tab bar was
  arranged 0 high, the picture 0.68 x 0.38.
- When the activity returns, the surface comes back, Avalonia measures again at the real size (the
  tab bar's desired size is 48 high again), but does not arrange again: the root's rectangle is
  unchanged, so the tab bar stayed 0 high and the picture 0.68 x 0.38, marked valid.
- Ruled out: the picture's decoding (always 1080 x 608 for a width of 1080; with the picture's
  loading skipped, the title alone still broke it); the wallpaper's configuration change (Android
  12 delivered none; a wallpaper set by another app while PaperNexus was stopped caused no layout
  pass and no breakage; Android 16 delivers one on return in broken and unbroken runs alike);
  layout passes while stopped as such (36 passes while the confirmation line faded, none of which
  measured the whole screen: no breakage). Android 16 broke less often in the first count only
  because its titles happened to keep the same number of lines.

**The repair** (`StoppedLayoutRepair`): it listens to the Android surface Avalonia draws on. When the
surface is destroyed and then comes back at its size (`surfaceChanged`), it marks the measure and
arrange of every control under the top level invalid, once, at background priority, so the next
layout pass lays the whole screen out at the real size: each tab, an open form, Secrets, a question
or a picker, and the easter egg overlay. Re-check it whenever Avalonia is updated: once a return
lays the screen out correctly without it, it can go.

Measured with the repair (Release; 10 pictures whose titles alternate one line and two lines in
slideshow order, sources off, so every change moves the buttons, read from the Random button's
position on screen):

| What | Android 12 | Android 16 |
| --- | --- | --- |
| Long-press entry change, app stopped on the Current tab | 0 broken of 5 | 0 broken of 5 |
| Left on Gallery, on Sources with the form open, on Settings with the order picker open, on Secrets, with an easter egg playing (one run each) | 0 broken of 5; the Current tab then showed the new title and its picture | the same |
| A real slideshow change (Exact timing, every 15 minutes), app stopped on the Current tab | laid out (00:15:00) | laid out (00:30:00) |
| A plain leave and return, frames every 50 to 160 ms after the return | complete screen from the first frame at +101 ms, no blank or broken frame | complete screen from +106 ms, the same |
| The repair ran | once per return, 68 ms after the restart | once per return, 33 ms after the restart |

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
  `build-tools;36.0.0`; for the emulators also `emulator`, the system images
  `system-images;android-36;default;x86_64` (Android 16) and
  `system-images;android-31;default;x86_64` (Android 12, the oldest version supported), plus
  `/dev/kvm`.

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
deploy workflow builds only the desktop project; neither needs the workload. The store package is
built by `.github/workflows/package-android.yml` (see "Versions and the store package").

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
It also writes the App Bundle, `com.gnostack.papernexus-Signed.aab`.

## Versions and the store package (issue #203, step 7)

**The version.** The phone and the desktop made from one commit carry one number N, the desktop
release's build number (`.github/scripts/build-number.sh`: a `vN` tag gives N, any other ref the
desktop run's number). The package is built with `ApplicationDisplayVersion` N.0.0 (the version
name; About shows "vN" through `AppVersionLabel`) and `ApplicationVersion` N (the version code,
which Google Play requires to rise with every upload; the run number only rises). A build made on
a development machine has the csproj's defaults, 0.0.0 and 1, and shows "v0", as the desktop's
local build does. Measured: a local publish with both set to 999 read `versionCode="999"
versionName="999.0.0"` in the `.aab` (the workload's `bundletool dump manifest`) and in the `.apk`
(`aapt2 dump badging`), and About showed "v999" on the Android 16 emulator; a plain local build
read 1 and 0.0.0 and showed "v0".

**The workflow** (`package-android.yml`, "Package Android App"):

| Started by | Builds | Number | Artifact |
| --- | --- | --- | --- |
| A desktop release run ("Build Paper Nexus") that succeeded (`workflow_run`) | That run's commit | From that run's ref name and run number | `PaperNexus-android-vN` |
| Run workflow (`workflow_dispatch`) | The latest successful desktop release run on main, again | That run's | `PaperNexus-android-vN` |
| A pull request that changes the workflow or `build-number.sh` (a rehearsal) | The pull request as it would merge | The latest successful desktop release run on main's | `PaperNexus-android-vN-rehearsal` |

It sets up .NET, runs `dotnet workload restore`, publishes `PaperNexus.Android` in Release with
the two version properties, and keeps `PaperNexus-android-vN.aab` (for Google Play) and
`PaperNexus-android-vN.apk` (to install on a phone or the emulators with `adb install`) as one
artifact of the run, for the repository's artifact retention (90 days). It never makes a GitHub
Release asset: the phone app is updated by the store (decision 2), and the desktop's release keeps
exactly its three assets. A failed desktop release run makes no package, so every package has a
desktop release with its number. Find the files on the run's page in the Actions tab
(https://github.com/gnostack/PaperNexus/actions/workflows/package-android.yml), under Artifacts,
or with `gh run download <run id> -n PaperNexus-android-vN`.

**Signing.** Both files are signed with the Android SDK's debug key, which the build creates afresh
on the runner for every run. Google Play refuses a debug-signed bundle, and an `.apk` from one run
cannot update one from another run (or a local build) without uninstalling first, which deletes the
app's settings and pictures. An uploadable package needs the upload key, which needs the Google
Play developer account: the publish step then passes `AndroidKeyStore=true`,
`AndroidSigningKeyStore`, `AndroidSigningStorePass`, `AndroidSigningKeyAlias` and
`AndroidSigningKeyPass` from repository secrets. Not set up.

**What the owner supplies for the store** (nothing of this is built yet):

- [ ] A Google Play developer account (personal accounts must run a closed test with 12 testers
      for 14 days before production).
- [ ] The upload key and Play App Signing enrollment, and the five signing properties above as
      repository secrets.
- [ ] A privacy policy at a public address: Play requires one to complete the Data safety form,
      even for an app that collects no data. It could be a page under `website/`.
- [ ] The Data safety answers: the developer collects nothing; the phone requests only the feed
      addresses and image hosts the person sets up.
- [ ] The store listing: the short and full descriptions, a 512 x 512 icon (from
      `PaperNexus/Assets/logo.png`), a 1024 x 500 feature graphic, and phone screenshots (the
      emulators can take them).
- [ ] The content rating questionnaire, the target audience (not children), and a contact address.
- [ ] Uploading to Play (the Play Developer API and its service account), not part of this
      workflow.

## Driving the emulator

```bash
$ANDROID_HOME/emulator/emulator -list-avds
$ANDROID_HOME/emulator/emulator -avd <name> -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot &
until [ "$(adb shell getprop sys.boot_completed | tr -d '\r')" = 1 ]; do sleep 2; done
```

- The virtual phones are `papernexus-phone` (Android 16) and `papernexus-phone-12` (Android 12),
  both 1080x2400. Run one at a time.
- Clean install: `adb uninstall com.gnostack.papernexus`, then install as above.
- A Debug package installed with `adb install` does not start ("No assemblies found … Assuming
  this is part of Fast Deployment"): a Debug build keeps its assemblies outside the package and
  only `-t:Install` or `-t:Run` copies them. After the package was replaced or removed by `adb`,
  `-t:Install` can still believe they are on the phone and skip them (measured: it finished in 2
  seconds and the app would not start); build with `-p:EmbedAssembliesIntoApk=true` to put them
  in the package, which `adb install -r` can then update while keeping the app's data.
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
  to undo (`adb shell settings delete system font_scale` where it was never set, as on the
  Android 12 phone).
- Shut the emulator down when finished with `adb shell reboot -p`, not `adb emu kill`: a kill can
  lose a setting changed just before it (see "Text input options").

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
shared groups' own tests), the Konami strokes (`KonamiGesturesTests`), and the long-press menu's
change and its pop-up sentences (`BackgroundWorkTests`, `SwitchWallpaperDeviceTests`).
The Android classes only pass Android's numbers and calls through; they are verified on the
emulator.

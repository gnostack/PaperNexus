# Android Host

`PaperNexus.Android` is the Android app (issue #203). It targets `net10.0-android` (API 36) with
Android 12 (API 31) as the minimum, application ID `com.gnostack.papernexus`, label `PaperNexus`.
It references `PaperNexus.Shared`, as the desktop host does; the phone's screens live in this
project and its logic and view models in the shared project.

## What it is made of

| File | Job |
| --- | --- |
| `MainApplication.cs` | The Android application object (`[Application]`, deriving `AvaloniaAndroidApplication<App>`). Sets the label, the launcher icon (`@mipmap/icon`) and the theme (`@style/PaperNexusTheme`), and adds the Inter font as the desktop does. |
| `MainActivity.cs` | The launcher activity (`AvaloniaMainActivity`). Shows the view `App` supplies. |
| `App.axaml(.cs)` | The phone's own Avalonia `App` (Fluent theme, dark). The desktop `App` owns the tray and the windows, so it stays in the desktop host. `OnFrameworkInitializationCompleted` sets `IActivityApplicationLifetime.MainViewFactory`. |
| `Views/MainView.axaml(.cs)` | A `TabbedPage` with five `ContentPage`s: Current, Gallery, Favorites, Sources, Settings. |
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

The pull request workflow gets the workload with `dotnet workload restore`; the GitHub runner image
already provides `ANDROID_HOME`, `JAVA_HOME` (JDK 17) and the SDK packages. The deploy workflow
builds only the desktop project and does not need the workload.

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
- Shut the emulator down when finished: `adb emu kill`.

The app drawer on a 5-column launcher shortens the label to "PaperNe..."; the label itself is
`PaperNexus`.

## Tests

The project has no test project yet: it holds only markup and framework base classes, and a
`net10.0` test project cannot reference a `net10.0-android` project. Verification is on the
emulator. Logic the phone adds (its implementations of the shared host interfaces) gets tests
when it arrives.

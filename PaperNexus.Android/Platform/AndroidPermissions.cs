using Android;

// INTERNET: the wallpaper feeds and the pictures they list. Debug builds add it on their own
// for the debugger; a Release build has it only through this line.
[assembly: UsesPermission(Manifest.Permission.Internet)]

// SET_WALLPAPER: WallpaperManager.SetStream. A normal permission, granted at install.
[assembly: UsesPermission(Manifest.Permission.SetWallpaper)]

// RECEIVE_BOOT_COMPLETED: Android clears every alarm when the phone restarts; the boot broadcast
// starts the app so the slideshow is armed again (ScheduleRestoreReceiver), and the download
// job is kept across the restart (JobInfo.Builder.SetPersisted needs it). Issue #203, step 5.
[assembly: UsesPermission(Manifest.Permission.ReceiveBootCompleted)]

// SCHEDULE_EXACT_ALARM: the slideshow's exact alarm while Exact timing is on (issue #203,
// step 5). The person allows it on the phone's "Alarms & reminders" screen (Android 14 and later;
// 12 and 13 allow it from the start). Not USE_EXACT_ALARM: Google Play keeps that one for alarm,
// timer and calendar apps.
[assembly: UsesPermission(Manifest.Permission.ScheduleExactAlarm)]

// ACCESS_NETWORK_STATE: Android 14 and later refuse a job that waits for a network without it
// (the scheduled download run, AndroidBackgroundScheduler).
[assembly: UsesPermission(Manifest.Permission.AccessNetworkState)]

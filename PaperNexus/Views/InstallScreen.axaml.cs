using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PaperNexus.Core;

namespace PaperNexus.Views;

public partial class InstallScreen : Window
{
    public InstallScreen()
    {
        InitializeComponent();
        InstallPathText.Text = Program.InstallDir;
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var results = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose install folder",
                AllowMultiple = false,
            });

            if (results is [var folder])
            {
                var selected = folder.Path.LocalPath;

                // If the exe is already in the selected folder, install in-place
                // rather than nesting into a redundant "PaperNexus" subfolder that
                // leaves the original exe behind and causes an install loop.
                var currentDir = Path.GetDirectoryName(Path.GetFullPath(Program.CurrentExePath));
                var selectedFull = Path.GetFullPath(selected);
                if (currentDir is not null && PlatformPaths.PathEquals(selectedFull, currentDir))
                    InstallPathText.Text = selected;
                else
                    InstallPathText.Text = Path.Combine(selected, "PaperNexus");
            }
        }
        catch { /* picker cancelled or unavailable - leave path unchanged */ }
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        // Prevent double-click and give visual feedback while copying.
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        InstallButton.Content = "Installing\u2026";

        // Fall back to the default AppData path if the user cleared the field.
        var installDir = InstallPathText.Text?.Trim();
        if (string.IsNullOrEmpty(installDir))
            installDir = Program.InstallDir;
        var installExePath = Path.Combine(installDir, PlatformPaths.ExecutableName);

        try
        {
            // Run the file copy off the UI thread to avoid blocking the window.
            var success = await Task.Run(() =>
                Program.TryInstall(Program.CurrentExePath, installDir, installExePath));

            if (!success)
            {
                // Install failed - re-enable so the user can try again.
                InstallButton.Content = "Install";
                InstallButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
                return;
            }

            // Persist the startup preference to AppData so the installed app finds it on first launch.
            // Settings always live in the default install directory regardless of exe location.
            // Only write fresh settings when none were migrated from alongside the source exe.
            // This runs during first-time install, before the service container (and so the
            // settings store) exists. That is a separate lifecycle from the running app, so it
            // keeps its own direct check and write, taking the path from PlatformPaths. No
            // existing file is ever overwritten here, so the store's atomic rename is not needed.
            var settingsDir = PlatformPaths.DefaultInstallDirectory;
            var settingsPath = Path.Combine(settingsDir, "settings.json");
            if (!File.Exists(settingsPath))
            {
                var settings = new WallpaperNexusSettings
                {
                    RunOnStartup = RunOnStartupCheckBox.IsChecked == true,
                };
                var json = JsonConvert.SerializeObject(settings, WallpaperNexusSettings.JsonFormat);
                Directory.CreateDirectory(settingsDir);
                await File.WriteAllTextAsync(settingsPath, json);
            }

            // Always rewrite the startup registration during install so a stale entry from
            // a previous install at a different path doesn't survive and launch the wrong
            // (possibly missing) executable at login. The installed path is passed
            // explicitly because Environment.ProcessPath is still the un-installed copy.
            StartupRegistration.Update(RunOnStartupCheckBox.IsChecked == true, installExePath);

            // Launch the newly-installed copy and exit the installer process.
            ShellOpener.LaunchExecutable(installExePath);
            Environment.Exit(0);
        }
        catch
        {
            // Restore interactive state so the user can retry or cancel.
            InstallButton.Content = "Install";
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            BrowseButton.IsEnabled = true;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }
}

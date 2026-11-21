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

            // Save the Run on startup choice; the installed copy applies it to the sign-in
            // entry when it starts a moment later, replacing any entry a previous install left
            // at another path. This copy is not the installed one, so it never writes the
            // sign-in entry itself (issue #211). The install screen runs before the service
            // container exists, so it builds its own SettingsStore over the same folders the
            // container's store uses, and writes through it like everything else.
            var settingsStore = new SettingsStore(new DesktopAppFolders());
            await SaveRunOnStartupChoiceAsync(settingsStore, RunOnStartupCheckBox.IsChecked == true);

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

    // Records the install screen's Run on startup box in the settings, keeping every other
    // saved field (settings migrated from beside the downloaded executable, or left by an
    // earlier install); with no settings file yet it writes the defaults with this choice.
    internal static Task SaveRunOnStartupChoiceAsync(ISettingsStore settingsStore, bool runOnStartup)
    {
        return settingsStore.UpdateAsync(latest => latest.RunOnStartup = runOnStartup);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }
}

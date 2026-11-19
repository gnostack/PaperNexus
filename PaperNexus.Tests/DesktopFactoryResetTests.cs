using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The desktop's Factory Reset deletes exactly the app's own data: settings.json in the settings
// folder, and current.*, timers.json and the logs folder in the data folder. Everything else,
// above all a build's libraries, the install sentinel and the downloaded wallpapers, stays.
public class DesktopFactoryResetTests : IDisposable
{
    private readonly string _root;
    private readonly ResetFolders _folders;

    public DesktopFactoryResetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"PaperNexus_Reset_{Guid.NewGuid():N}");
        _folders = new ResetFolders(Path.Combine(_root, "settings"), Path.Combine(_root, "data"), Path.Combine(_root, "Pictures"));
        Directory.CreateDirectory(_folders.SettingsFolder);
        Directory.CreateDirectory(_folders.DataFolder);
        Directory.CreateDirectory(_folders.DefaultDownloadFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Touch(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void DeleteAppData_DeletesOnlyTheAppsData()
    {
        var settings = Touch(_folders.SettingsFolder, "settings.json");
        var currentPng = Touch(_folders.DataFolder, "current.png");
        var currentJpg = Touch(_folders.DataFolder, "current.jpg");
        var timers = Touch(_folders.DataFolder, "timers.json");
        Touch(_folders.DataFolder, Path.Combine("logs", "PaperNexus.log"));
        var logs = Path.Combine(_folders.DataFolder, "logs");

        var executable = Touch(_folders.DataFolder, "PaperNexus");
        var library = Touch(_folders.DataFolder, "PaperNexus.Shared.dll");
        var sentinel = Touch(_folders.DataFolder, ".installed");
        var otherSettingsFile = Touch(_folders.SettingsFolder, "PaperNexus");
        var picture = Touch(_folders.DefaultDownloadFolder, "lake.jpg");

        var deleted = DesktopFactoryReset.DeleteAppData(_folders);

        Assert.Equal(
            new[] { settings, currentJpg, currentPng, timers, logs }.Order(),
            deleted.Order());
        Assert.False(File.Exists(settings));
        Assert.False(File.Exists(currentPng));
        Assert.False(File.Exists(currentJpg));
        Assert.False(File.Exists(timers));
        Assert.False(Directory.Exists(logs));

        Assert.True(File.Exists(executable));
        Assert.True(File.Exists(library));
        Assert.True(File.Exists(sentinel));
        Assert.True(File.Exists(otherSettingsFile));
        Assert.True(File.Exists(picture));
    }

    [Fact]
    public void DeleteAppData_WithNothingToDeleteDeletesNothing()
    {
        var deleted = DesktopFactoryReset.DeleteAppData(_folders);

        Assert.Empty(deleted);
    }

    private sealed class ResetFolders : IAppFolders
    {
        public ResetFolders(string settingsFolder, string dataFolder, string downloadFolder)
        {
            SettingsFolder = settingsFolder;
            DataFolder = dataFolder;
            DefaultDownloadFolder = downloadFolder;
        }

        public string SettingsFolder { get; }

        public string DataFolder { get; }

        public string DefaultDownloadFolder { get; }
    }
}

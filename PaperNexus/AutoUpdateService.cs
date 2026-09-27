using Cronos;
using PaperNexus.Core;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PaperNexus;

internal interface ICheckForUpdates
{
    public Task CheckAsync(bool forceUpdate = false, IProgress<string>? progress = null);
}

internal sealed class AutoUpdateService : ICheckForUpdates, IAddSingleton<ICheckForUpdates>
{
    private const string GitHubRepo = "0Keith/PaperNexus";

    // Each platform ships its own release asset. The Windows build is Authenticode-signed,
    // which is a PE-only format, so the Linux build's integrity is verified against a
    // SHA-256 digest published alongside it instead.
    private static string AssetName => OperatingSystem.IsWindows()
        ? "PaperNexus.exe"
        : "PaperNexus-linux-x64";

    private static string ChecksumAssetName => AssetName + ".sha256";

    private readonly ILogger<AutoUpdateService> _logger;

    // Two separate HttpClient instances, both long-lived singletons on this service:
    //   _apiClient  - short 30-second timeout for small GitHub API JSON responses.
    //   _downloadClient - 10-minute timeout for streaming the full exe binary (50+ MB).
    // Using a single 30-second client caused reliable timeouts on slow connections during
    // the body-streaming phase even though headers arrived quickly, because HttpClient's
    // Timeout counts from the moment the request is initiated, not just the header wait.
    // HttpClient is thread-safe for concurrent use; creating one per call drains ephemeral
    // ports because disposed clients leave sockets in TIME_WAIT for several minutes.
    private readonly HttpClient _apiClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _downloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public AutoUpdateService(ILogger<AutoUpdateService> logger)
    {
        _logger = logger.ThrowIfNull();
        _apiClient.DefaultRequestHeaders.UserAgent.ParseAdd("PaperNexus-AutoUpdater");
        _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("PaperNexus-AutoUpdater");
    }

    // Checks GitHub Releases for a newer build and, if found (or if forceUpdate is set),
    // downloads the signed exe, verifies its Authenticode signature, then launches a
    // self-deleting batch script that swaps the binary while the app is not running.
    // The current process calls Environment.Exit(0) after launching the updater script.
    public async Task CheckAsync(bool forceUpdate = false, IProgress<string>? progress = null)
    {
        var currentVersion = typeof(AutoUpdateService).Assembly.GetName().Version;
        if (currentVersion is null)
        {
            _logger.LogError("Cannot determine current assembly version.");
            throw new InvalidOperationException("Cannot determine current assembly version.");
        }

        // Version scheme is vN where N maps to Version.Major
        var currentBuild = currentVersion.Major;
        _logger.LogInformation("Checking for updates. Current build: v{Build}", currentBuild);
        progress?.Report($"Checking for updates (v{currentBuild})...");

        string json;
        try
        {
            json = await _apiClient.GetStringAsync(
                $"https://api.github.com/repos/{GitHubRepo}/releases/latest");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Update check failed: {Message}", ex.Message);
            throw;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement))
        {
            _logger.LogWarning("No tag_name found in GitHub release response.");
            throw new InvalidOperationException("No version tag found in latest release.");
        }

        var tag = tagElement.GetString();
        if (tag is null)
        {
            _logger.LogWarning("Release tag_name is null.");
            throw new InvalidOperationException("Release version tag is empty.");
        }

        // Tags are "vN" - strip the leading 'v' and parse as an integer build number
        var versionStr = tag.TrimStart('v');
        if (!int.TryParse(versionStr, out var latestBuild))
        {
            _logger.LogWarning("Cannot parse release tag '{Tag}' as build number.", tag);
            throw new InvalidOperationException($"Cannot parse release tag '{tag}' as a version number.");
        }

        if (latestBuild <= currentBuild && !forceUpdate)
        {
            _logger.LogInformation("Already up to date (v{Build})", currentBuild);
            return;
        }

        if (forceUpdate && latestBuild <= currentBuild)
            _logger.LogInformation("Forcing re-install of current version (v{Build})", currentBuild);

        _logger.LogInformation("Update available: v{Latest} (current: v{Current})", latestBuild, currentBuild);

        if (!root.TryGetProperty("assets", out var assetsElement))
        {
            _logger.LogWarning("No assets found in release {Tag}.", tag);
            throw new InvalidOperationException($"No assets found in release {tag}.");
        }

        // Find the download URL for this platform's binary, plus its checksum sidecar
        // if the release publishes one (used for integrity verification on Linux).
        string? downloadUrl = null;
        string? checksumUrl = null;
        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl)
                || !asset.TryGetProperty("browser_download_url", out var urlEl))
                continue;

            var assetName = nameEl.GetString();
            if (assetName == AssetName)
                downloadUrl = urlEl.GetString();
            else if (assetName == ChecksumAssetName)
                checksumUrl = urlEl.GetString();
        }

        if (downloadUrl is null)
        {
            _logger.LogWarning("Asset '{Asset}' not found in release {Tag}.", AssetName, tag);
            throw new InvalidOperationException($"Update file '{AssetName}' not found in release {tag}.");
        }

        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, AssetName);
        // Stage the download beside the running exe; the batch script will move it into place
        var newExePath = exePath + ".new";
        var backupPath = exePath + ".bak";
        // Use a unique script name to avoid collisions if a previous update was interrupted
        var scriptExtension = OperatingSystem.IsWindows() ? "bat" : "sh";
        var scriptPath = Path.Combine(Path.GetDirectoryName(exePath)!, $"update-{Guid.NewGuid():N}.{scriptExtension}");

        _logger.LogInformation("Downloading v{Latest} from {Url}", latestBuild, downloadUrl);
        progress?.Report($"Downloading {tag}...");

        try
        {
            // Stream directly to disk rather than buffering the full exe in memory (50+ MB).
            // _downloadClient has a 10-minute timeout; the 30-second API timeout would expire
            // before the body finishes streaming on any connection slower than ~13 Mbps.
            using var response = await _downloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var fileStream = new FileStream(newExePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
            await response.Content.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Download failed: {Message}", ex.Message);
            // Remove any partially-written staging file so it does not linger on disk.
            // FileMode.Create would overwrite it on retry, but a stale partial binary
            // could confuse manual inspection or a future update path check.
            try { File.Delete(newExePath); } catch { }
            throw;
        }

        // Verify the download before it is ever executed, so a tampered or truncated
        // binary is discarded rather than swapped into place.
        if (!await VerifyDownloadAsync(newExePath, checksumUrl))
        {
            File.Delete(newExePath);
            _logger.LogWarning("Update integrity verification failed. Update aborted.");
            throw new InvalidOperationException("Downloaded update failed integrity verification.");
        }

        if (OperatingSystem.IsWindows())
        {
            // Remove the Zone.Identifier alternate data stream so Smart App Control
            // does not treat the downloaded file as untrusted internet content.
            try
            {
                File.Delete(newExePath + ":Zone.Identifier");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Zone.Identifier removal skipped: {Message}", ex.Message);
            }
        }
        else
        {
            // The download arrives without the execute bit, which the swap script needs.
            PlatformPaths.EnsureExecutable(newExePath);
        }

        // The swap script (same steps on both platforms, written in each one's shell):
        //  1. Waits 2 s for the current process to exit
        //  2. Backs up the running binary
        //  3. Moves the new binary into place
        //  4. Launches the updated app
        //  5. Waits 8 s and checks the process is running; if not, rolls back from backup
        //  6. Deletes the backup and self-deletes
        try
        {
            if (OperatingSystem.IsWindows())
                await WriteWindowsSwapScriptAsync(scriptPath, exePath, newExePath, backupPath);
            else
                await WriteUnixSwapScriptAsync(scriptPath, exePath, newExePath, backupPath);

            LaunchSwapScript(scriptPath);
        }
        catch (Exception ex)
        {
            // Clean up the staged binary and script so the install directory is not
            // left with orphaned files. The update will be retried at the next scheduled check.
            _logger.LogWarning("Failed to launch updater script: {Message}", ex.Message);
            try { File.Delete(newExePath); } catch { }
            try { File.Delete(scriptPath); } catch { }
            throw;
        }

        _logger.LogInformation("Update downloaded. Restarting to apply v{Latest}...", latestBuild);
        progress?.Report("Restarting...");
        await Task.Delay(500); // brief pause so the UI can display "Restarting..." before exit
        Environment.Exit(0);
    }

    private static Task WriteWindowsSwapScriptAsync(string scriptPath, string exePath, string newExePath, string backupPath)
    {
        var processName = Path.GetFileName(exePath);
        return File.WriteAllTextAsync(scriptPath,
            $"""
            @echo off
            timeout /t 2 /nobreak > nul
            copy /y "{exePath}" "{backupPath}" > nul
            if errorlevel 1 exit /b 1
            move /y "{newExePath}" "{exePath}"
            if errorlevel 1 (
                del "{backupPath}" 2>nul
                exit /b 1
            )
            start "" "{exePath}" --updated
            timeout /t 8 /nobreak > nul
            tasklist /fi "imagename eq {processName}" /fo csv 2>nul | findstr /i "{Path.GetFileNameWithoutExtension(processName)}" > nul
            if errorlevel 1 (
                copy /y "{backupPath}" "{exePath}" > nul
                start "" "{exePath}"
            )
            del "{backupPath}" 2>nul
            del "%~f0"
            """);
    }

    private static async Task WriteUnixSwapScriptAsync(string scriptPath, string exePath, string newExePath, string backupPath)
    {
        // setsid detaches the relaunched app from this script's process group so it
        // survives the script exiting. pgrep -f matches the full command line because the
        // Linux binary has no extension for pgrep's default name match to key on.
        var script = $"""
            #!/bin/sh
            sleep 2
            cp -f '{exePath}' '{backupPath}' || exit 1
            if ! mv -f '{newExePath}' '{exePath}'; then
                rm -f '{backupPath}'
                exit 1
            fi
            chmod +x '{exePath}'
            setsid '{exePath}' --updated >/dev/null 2>&1 &
            sleep 8
            if ! pgrep -f '{exePath}' >/dev/null 2>&1; then
                cp -f '{backupPath}' '{exePath}'
                chmod +x '{exePath}'
                setsid '{exePath}' >/dev/null 2>&1 &
            fi
            rm -f '{backupPath}'
            rm -f "$0"
            """;

        await File.WriteAllTextAsync(scriptPath, script.ReplaceLineEndings("\n"));
        PlatformPaths.EnsureExecutable(scriptPath);
    }

    private static void LaunchSwapScript(string scriptPath)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            : new ProcessStartInfo("/bin/sh", scriptPath);
        startInfo.CreateNoWindow = true;
        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
    }

    // Confirms the staged download is the artifact the release published, before it is
    // executed. Windows builds carry an Authenticode signature; Linux builds are matched
    // against the SHA-256 digest published as a sidecar asset in the same release.
    private async Task<bool> VerifyDownloadAsync(string filePath, string? checksumUrl)
    {
        if (OperatingSystem.IsWindows())
            return VerifyAuthenticodeSignature(filePath);

        if (string.IsNullOrEmpty(checksumUrl))
        {
            _logger.LogWarning("Release publishes no '{Asset}' checksum; refusing to install an unverifiable update.", ChecksumAssetName);
            return false;
        }

        string expected;
        try
        {
            // The sidecar is in `sha256sum` format: "<hex digest>  <filename>".
            var contents = await _apiClient.GetStringAsync(checksumUrl);
            expected = contents.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not fetch update checksum: {Message}", ex.Message);
            return false;
        }

        string actual;
        await using (var stream = File.OpenRead(filePath))
        {
            var digest = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
            actual = Convert.ToHexString(digest);
        }

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Update checksum mismatch. Expected {Expected}, got {Actual}.", expected, actual);
            return false;
        }
        return true;
    }

    // Checks that the file at filePath carries a valid Authenticode signature whose
    // subject CN matches "PaperNexus". Returns false (rather than throwing) if the
    // certificate is missing, invalid, or signed by an unexpected issuer.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private bool VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057 // No non-obsolete API for Authenticode cert extraction yet
            using var x509 = X509Certificate2.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            if (!x509.Subject.Contains("CN=PaperNexus", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Update signed by unexpected subject: {Subject}", x509.Subject);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Authenticode verification failed: {Message}", ex.Message);
            return false;
        }
    }
}

internal sealed class AutoUpdateJob : IScheduleScopedJob
{
    private readonly ICheckForUpdates _checkForUpdates;
    private readonly ISettingsStore _settingsStore;

    public AutoUpdateJob(ICheckForUpdates checkForUpdates, ISettingsStore settingsStore)
    {
        _checkForUpdates = checkForUpdates.ThrowIfNull();
        _settingsStore = settingsStore.ThrowIfNull();
    }

    // Returns an empty config (no schedule) in debug mode or when auto-updates are disabled.
    // Otherwise schedules a daily check at 03:00 and also runs once on startup.
    public async Task<JobConfig> GetJobConfigAsync()
    {
        if (Program.IsDebugMode)
            return new JobConfig();
        var settings = await _settingsStore.LoadAsync();
        if (!settings.AutoUpdatesEnabled)
            return new JobConfig();
        return new JobConfig(
            CronExpression: CronExpression.Parse("0 3 * * *"),
            ExecuteOnStartup: true);
    }

    public Task ExecuteAsync() => _checkForUpdates.CheckAsync(forceUpdate: false, progress: null);
}

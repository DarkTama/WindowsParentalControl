using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace ParentalControl.Core.Services;

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseTitle,
    string ReleaseNotes,
    string? PublishedAt,
    string? DownloadUrl,
    long? FileSizeBytes,
    string? Error = null);

public static class UpdateService
{
    private static readonly ILogger _logger = Log.ForContext(typeof(UpdateService));
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "WindowsParentalControl-Updater");
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVerStr = AppVersion.Current;
        try
        {
            var url = $"https://api.github.com/repos/{AppVersion.GitHubRepo}/releases/latest";
            var response = await HttpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.Information("No published releases found on GitHub repo {Repo} (404 NotFound).", AppVersion.GitHubRepo);
                    var clean = currentVerStr.TrimStart('v', 'V').Trim();
                    return new UpdateCheckResult(
                        false,
                        clean,
                        clean,
                        "Versi Terbaru",
                        "Belum ada rilis baru yang dipublikasikan di repositori GitHub.",
                        null,
                        null,
                        null,
                        Error: null);
                }

                var err = $"GitHub API returned {response.StatusCode}";
                _logger.Warning("Update check failed: {Error}", err);
                return new UpdateCheckResult(false, currentVerStr, currentVerStr, "", "", null, null, null, err);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonNode.Parse(json);
            if (doc == null)
            {
                return new UpdateCheckResult(false, currentVerStr, currentVerStr, "", "", null, null, null, "Failed to parse release metadata.");
            }

            var tagName = doc["tag_name"]?.ToString() ?? "";
            var releaseTitle = doc["name"]?.ToString() ?? tagName;
            var body = doc["body"]?.ToString() ?? "";
            var publishedAt = doc["published_at"]?.ToString();

            var cleanLatest = tagName.TrimStart('v', 'V').Trim();
            var cleanCurrent = currentVerStr.TrimStart('v', 'V').Trim();

            var hasUpdate = IsNewerVersion(cleanLatest, cleanCurrent);

            string? downloadUrl = null;
            long? fileSize = null;

            var assets = doc["assets"]?.AsArray();
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    var name = asset?["name"]?.ToString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset?["browser_download_url"]?.ToString();
                        fileSize = asset?["size"]?.GetValue<long>();
                        break;
                    }
                }
            }

            return new UpdateCheckResult(
                hasUpdate,
                cleanCurrent,
                cleanLatest,
                releaseTitle,
                body,
                publishedAt,
                downloadUrl,
                fileSize);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception while checking for updates from GitHub Releases");
            return new UpdateCheckResult(false, currentVerStr, currentVerStr, "", "", null, null, null, ex.Message);
        }
    }

    public static async Task<(bool Success, string? InstallerPath, string Message)> DownloadUpdateAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return (false, null, "URL unduhan pembaruan tidak valid.");
        }

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ParentalControlUpdate");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            var installerPath = Path.Combine(tempDir, "ParentalControlSetup_Update.exe");
            if (File.Exists(installerPath))
            {
                try { File.Delete(installerPath); } catch { }
            }

            _logger.Information("Downloading update installer from {Url} to {Path}", downloadUrl, installerPath);

            using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                    totalRead += read;
                    if (totalBytes > 0 && progress != null)
                    {
                        var pct = (int)((double)totalRead / totalBytes * 100);
                        progress.Report(pct);
                    }
                }
            }

            _logger.Information("Installer downloaded ({Bytes} bytes) to {Path}", new FileInfo(installerPath).Length, installerPath);
            return (true, installerPath, "File pembaruan berhasil diunduh.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to download update installer");
            return (false, null, $"Gagal mengunduh file pembaruan: {ex.Message}");
        }
    }

    public static (bool Success, string Message) LaunchInstaller(string installerPath, bool silent = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            {
                return (false, "File installer tidak ditemukan.");
            }

            _logger.Information("Launching update installer (Silent={Silent}): {Path}", silent, installerPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = silent ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" : "",
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            return (true, "Installer berhasil dijalankan.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to launch update installer");
            return (false, $"Gagal menjalankan installer: {ex.Message}");
        }
    }

    public static async Task<(bool Success, string Message)> DownloadAndApplyUpdateAsync(string downloadUrl, bool silent = true, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var (downloaded, installerPath, msg) = await DownloadUpdateAsync(downloadUrl, progress, cancellationToken);
        if (!downloaded || string.IsNullOrWhiteSpace(installerPath))
        {
            return (false, msg);
        }

        var (launched, launchMsg) = LaunchInstaller(installerPath, silent);
        if (!launched)
        {
            return (false, launchMsg);
        }

        return (true, silent
            ? "Pembaruan berhasil diunduh dan instalasi otomatis telah dimulai."
            : "Pembaruan berhasil diunduh dan wizard instalasi telah dibuka.");
    }

    public static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var vLatest) && Version.TryParse(current, out var vCurrent))
        {
            return vLatest > vCurrent;
        }

        // Fallback simple string comparison
        return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
    }
}

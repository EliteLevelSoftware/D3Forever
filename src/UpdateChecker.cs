using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace D3Downloader
{
    public sealed class UpdateInfo
    {
        public bool HasUpdate { get; init; }
        public string CurrentVersion { get; init; } = "";
        public string LatestVersion { get; init; } = "";
        public string DownloadUrl { get; init; } = "";
        public string ChecksumUrl { get; init; } = "";
        public long AssetSize { get; init; }
        public string ReleaseNotes { get; init; } = "";
        public string HtmlUrl { get; init; } = "";
    }

    public static class UpdateChecker
    {
        public const string CurrentVersion = "1.4.4";
        private const string GitHubApiReleasesLatest = "https://api.github.com/repos/EliteLevelSoftware/D3Forever/releases/latest";

        public static async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.All
                };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Diablo3Forever-Updater/1.0");

                using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiReleasesLatest);
                using var res = await http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    return new UpdateInfo { HasUpdate = false, CurrentVersion = CurrentVersion };
                }

                string json = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
                string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : "";

                string cleanLatest = tagName.TrimStart('v', 'V').Trim();
                string cleanCurrent = CurrentVersion.TrimStart('v', 'V').Trim();

                bool isNewer = false;
                if (Version.TryParse(cleanLatest, out var vLatest) && Version.TryParse(cleanCurrent, out var vCurrent))
                {
                    isNewer = vLatest > vCurrent;
                }
                else
                {
                    isNewer = !string.Equals(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(cleanLatest);
                }

                string downloadUrl = "";
                string checksumUrl = "";
                long assetSize = 0;

                if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsElem.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        string url = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : "";

                        if (name.Equals("Diablo3Forever.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = url;
                            assetSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        }
                        else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            checksumUrl = url;
                        }
                    }
                }

                return new UpdateInfo
                {
                    HasUpdate = isNewer && !string.IsNullOrEmpty(downloadUrl),
                    CurrentVersion = CurrentVersion,
                    LatestVersion = cleanLatest,
                    DownloadUrl = downloadUrl,
                    ChecksumUrl = checksumUrl,
                    AssetSize = assetSize,
                    ReleaseNotes = body,
                    HtmlUrl = htmlUrl
                };
            }
            catch
            {
                return new UpdateInfo { HasUpdate = false, CurrentVersion = CurrentVersion };
            }
        }

        private static bool IsTrustedUpdateUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Paths are injected into a batch file, so anything that could break cmd.exe quoting is rejected.
        /// </summary>
        private static bool IsBatchSafePath(string path) =>
            !string.IsNullOrWhiteSpace(path)
            && path.IndexOfAny(new[] { '"', '%', '&', '|', '<', '>', '^', '!', '\r', '\n', '\0' }) < 0;

        private static async Task<string> DownloadChecksumAsync(string checksumUrl, CancellationToken ct)
        {
            if (!IsTrustedUpdateUrl(checksumUrl))
            {
                throw new InvalidOperationException("The release does not expose a trusted SHA256SUMS.txt asset; aborting update.");
            }

            using var handler = new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Diablo3Forever-Updater/1.0");

            string sums = await http.GetStringAsync(checksumUrl, ct);
            foreach (string rawLine in sums.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                int sep = line.IndexOf(' ');
                if (sep <= 0) continue;
                string hash = line.Substring(0, sep).Trim();
                string fileName = line.Substring(sep + 1).Trim().TrimStart('*').Trim();
                if (Path.GetFileName(fileName).Equals("Diablo3Forever.exe", StringComparison.OrdinalIgnoreCase)
                    && hash.Length == 64 && hash.All(Uri.IsHexDigit))
                {
                    return hash.ToLowerInvariant();
                }
            }

            throw new InvalidDataException("SHA256SUMS.txt does not contain an entry for Diablo3Forever.exe; aborting update.");
        }

        public static async Task ApplyUpdateAsync(string downloadUrl, string checksumUrl, Action<double, string> onProgress, CancellationToken ct)
        {
            if (!IsTrustedUpdateUrl(downloadUrl))
            {
                throw new InvalidOperationException("Refusing to download an update from an untrusted URL.");
            }

            string expectedSha256 = await DownloadChecksumAsync(checksumUrl, ct);

            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath))
            {
                throw new InvalidOperationException("Could not determine current executable path.");
            }

            if (!IsBatchSafePath(currentExePath))
            {
                throw new InvalidOperationException("The current executable path contains characters that are unsafe for the updater script.");
            }

            string tempExe = Path.Combine(Path.GetTempPath(), $"Diablo3Forever_vUpdate_{Guid.NewGuid():N}.exe");
            string updaterBat = Path.Combine(Path.GetTempPath(), $"d3forever_updater_{Guid.NewGuid():N}.bat");

            if (!IsBatchSafePath(tempExe) || !IsBatchSafePath(updaterBat))
            {
                throw new InvalidOperationException("The temporary folder path contains characters that are unsafe for the updater script.");
            }

            try
            {
                using (var handler = new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Diablo3Forever-Updater/1.0");
                    using var res = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    res.EnsureSuccessStatusCode();

                    long totalBytes = res.Content.Headers.ContentLength ?? -1L;
                    await using (var src = await res.Content.ReadAsStreamAsync(ct))
                    await using (var dst = File.Create(tempExe))
                    {
                        byte[] buf = new byte[128 * 1024];
                        long readTotal = 0;
                        int read;
                        while ((read = await src.ReadAsync(buf, ct)) > 0)
                        {
                            await dst.WriteAsync(buf.AsMemory(0, read), ct);
                            readTotal += read;
                            if (totalBytes > 0)
                            {
                                double pct = (double)readTotal / totalBytes * 100.0;
                                onProgress?.Invoke(pct, $"{readTotal / 1048576.0:F1} MB / {totalBytes / 1048576.0:F1} MB");
                            }
                        }
                    }
                }

                onProgress?.Invoke(100, "Verifying SHA-256 signature...");
                string actualSha256;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(tempExe))
                {
                    actualSha256 = Convert.ToHexString(await sha.ComputeHashAsync(fs, ct)).ToLowerInvariant();
                }

                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Update integrity check failed (expected SHA-256 {expectedSha256}, got {actualSha256}). The download was discarded.");
                }

                // Write the updater batch script to swap the executable and restart
                string batScript = $@"@echo off
timeout /t 1 /nobreak >nul
:retry
del ""{currentExePath}"" >nul 2>&1
if exist ""{currentExePath}"" (
    timeout /t 1 /nobreak >nul
    goto retry
)
move /y ""{tempExe}"" ""{currentExePath}"" >nul 2>&1
start """" ""{currentExePath}""
del ""%~f0""
";
                await File.WriteAllTextAsync(updaterBat, batScript, System.Text.Encoding.ASCII, ct);

                // Launch updater batch detached
                var psi = new ProcessStartInfo
                {
                    FileName = updaterBat,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                // Exit current application to release file lock
                Environment.Exit(0);
            }
            catch
            {
                try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
                try { if (File.Exists(updaterBat)) File.Delete(updaterBat); } catch { }
                throw;
            }
        }
    }
}

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
        public long AssetSize { get; init; }
        public string ReleaseNotes { get; init; } = "";
        public string HtmlUrl { get; init; } = "";
    }

    public static class UpdateChecker
    {
        public const string CurrentVersion = "1.4.0";
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
                long assetSize = 0;

                if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsElem.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.Equals("Diablo3Forever.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : "";
                            assetSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                            break;
                        }
                    }
                }

                return new UpdateInfo
                {
                    HasUpdate = isNewer && !string.IsNullOrEmpty(downloadUrl),
                    CurrentVersion = CurrentVersion,
                    LatestVersion = cleanLatest,
                    DownloadUrl = downloadUrl,
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

        public static async Task ApplyUpdateAsync(string downloadUrl, Action<double, string> onProgress, CancellationToken ct)
        {
            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath))
            {
                throw new InvalidOperationException("Could not determine current executable path.");
            }

            string tempExe = Path.Combine(Path.GetTempPath(), $"Diablo3Forever_vUpdate_{Guid.NewGuid():N}.exe");
            string updaterBat = Path.Combine(Path.GetTempPath(), $"d3forever_updater_{Guid.NewGuid():N}.bat");

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
    }
}

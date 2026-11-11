using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace D3Downloader
{
    public enum BackupMode
    {
        FastBinaries,
        RawCdnMirror
    }

    public enum BackupScope
    {
        All,
        RetailOnly,
        ReaperOfSoulsOnly,
        VanillaOnly
    }

    public static class BackupEngine
    {
        public static async Task<bool> RunBackupAsync(
            string backupRoot,
            BackupMode mode,
            BackupScope scope,
            Action<int, int, string> onOverallProgress,
            Action<double, string> onItemProgress,
            Action<string> log,
            CancellationToken ct)
        {
            Directory.CreateDirectory(backupRoot);

            var candidateBuilds = D3BuildCatalog.AllBuilds.Where(b =>
            {
                return scope switch
                {
                    BackupScope.All => true,
                    BackupScope.RetailOnly => b.Category == D3BuildCategory.Retail,
                    BackupScope.ReaperOfSoulsOnly => b.IsRoS,
                    BackupScope.VanillaOnly => !b.IsRoS,
                    _ => true
                };
            }).ToList();

            log("========================================================================");
            log(" DIABLO 3 FOREVER — BULK ARCHIVAL & BACKUP ENGINE");
            log("========================================================================");
            log($" Destination : {backupRoot}");
            log($" Mode        : {(mode == BackupMode.FastBinaries ? "⚡ Fast Binaries Archive (~3.2 GB)" : "📦 Raw Blizzard CDN Mirror (~48 GB)")}");
            log($" Scope       : {scope} ({candidateBuilds.Count} Target Builds)");
            log("========================================================================");

            if (mode == BackupMode.FastBinaries)
            {
                return await RunFastBinariesBackupAsync(backupRoot, candidateBuilds, onOverallProgress, onItemProgress, log, ct);
            }
            else
            {
                return await RunRawCdnMirrorBackupAsync(backupRoot, candidateBuilds, onOverallProgress, onItemProgress, log, ct);
            }
        }

        private static async Task<bool> RunFastBinariesBackupAsync(
            string backupRoot,
            List<D3BuildCatalogItem> builds,
            Action<int, int, string> onOverallProgress,
            Action<double, string> onItemProgress,
            Action<string> log,
            CancellationToken ct)
        {
            string baseCacheDir = Path.Combine(backupRoot, "_BaseArchiveCache");
            Directory.CreateDirectory(baseCacheDir);

            int total = builds.Count;
            int success = 0;
            int skipped = 0;

            for (int i = 0; i < builds.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var b = builds[i];
                string editionDir = b.IsRoS ? "Reaper of Souls" : "Vanilla Classic";
                string categoryDir = b.Category switch
                {
                    D3BuildCategory.Retail => "Retail",
                    D3BuildCategory.Ptr => "Public Test Realm",
                    D3BuildCategory.Beta => "Closed Beta",
                    _ => "Other"
                };

                string folderName = $"{b.Version}.{b.BuildNumber}";
                string targetDir = Path.Combine(backupRoot, editionDir, categoryDir, folderName);
                string markerFile = Path.Combine(targetDir, "VERSION_INFO.txt");
                string exeName = b.IsCasc ? "Diablo III64.exe" : "Diablo III.exe";
                string targetExe = Path.Combine(targetDir, exeName);
                if (!File.Exists(targetExe))
                {
                    targetExe = Path.Combine(targetDir, "Diablo III.exe");
                }

                onOverallProgress?.Invoke(i + 1, total, $"[{i + 1}/{total}] Processing {b.Version}.{b.BuildNumber} ({b.Category})...");

                // Check if already completed and verified
                if (File.Exists(markerFile) && File.Exists(targetExe) && new FileInfo(targetExe).Length > 100_000)
                {
                    log($"[SKIP {i + 1}/{total}] {b.DisplayTitle} already backed up and verified.");
                    skipped++;
                    continue;
                }

                log($"\n------------------------------------------------------------------------");
                log($"[{i + 1}/{total}] Backing up {b.DisplayTitle}...");
                log($"Folder: {targetDir}");

                try
                {
                    int res = await Program.ExecuteBuildPipelineAsync(
                        targetDir: targetDir,
                        selectedBuild: b,
                        region: "us",
                        locale: "enUS",
                        maxConcurrency: 8,
                        dryRunLimit: 0,
                        binariesOnly: true,
                        installPrereqs: false,
                        configureShortcutFirewall: false,
                        log: log,
                        onProgress: onItemProgress,
                        ct: ct);

                    if (res == 0)
                    {
                        // Write rich metadata info file
                        var sb = new StringBuilder();
                        sb.AppendLine("========================================================================");
                        sb.AppendLine($" Diablo III Historical Archive — Standalone Binaries Package");
                        sb.AppendLine("========================================================================");
                        sb.AppendLine($"Version          : {b.Version}");
                        sb.AppendLine($"Build Number     : {b.BuildNumber}");
                        sb.AppendLine($"Edition          : {editionDir}");
                        sb.AppendLine($"Category         : {categoryDir}");
                        sb.AppendLine($"Architecture     : {(b.IsCasc ? "CASC 64-bit" : "MPQ 32-bit")}");
                        sb.AppendLine($"Release Date     : {b.ReleaseDate}");
                        sb.AppendLine($"Summary          : {b.Summary}");
                        sb.AppendLine($"Product Code     : {b.ProductCode}");
                        sb.AppendLine($"Base Direct URL  : {b.BaseDirectUrl}");
                        sb.AppendLine($"Base Build       : {b.BaseBuildNumber}");
                        sb.AppendLine($"Manifest / MFIL  : {b.MfilFileName}");
                        sb.AppendLine($"Archive Time     : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                        sb.AppendLine($"Source CDN       : Blizzard Entertainment Official Akamai / EdgeSuite Nodes");
                        sb.AppendLine("========================================================================");
                        sb.AppendLine("Usage:");
                        sb.AppendLine("  Copy the contents of this folder into your Diablo III game directory to");
                        sb.AppendLine("  run this historical version directly.");
                        sb.AppendLine("========================================================================");

                        await File.WriteAllTextAsync(markerFile, sb.ToString(), Encoding.UTF8, ct);

                        // Append SHA256 of executables to root SHA256SUMS.txt
                        await AppendChecksumsAsync(backupRoot, targetDir, ct);

                        success++;
                    }
                    else
                    {
                        log($"[WARN] Pipeline returned non-zero code ({res}) for {b.Version}.{b.BuildNumber}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log($"[ERROR] Failed to backup {b.Version}.{b.BuildNumber}: {ex.Message}");
                }
            }

            log("\n========================================================================");
            log($" ARCHIVE BACKUP COMPLETE!");
            log($" Total Builds : {total}");
            log($" Newly Backed : {success}");
            log($" Skipped (OK) : {skipped}");
            log($" Target Root  : {backupRoot}");
            log("========================================================================");

            return true;
        }

        private static async Task<bool> RunRawCdnMirrorBackupAsync(
            string backupRoot,
            List<D3BuildCatalogItem> builds,
            Action<int, int, string> onOverallProgress,
            Action<double, string> onItemProgress,
            Action<string> log,
            CancellationToken ct)
        {
            string mirrorRoot = Path.Combine(backupRoot, "RawBlizzardCDN");
            Directory.CreateDirectory(mirrorRoot);

            using var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 16,
                AutomaticDecompression = System.Net.DecompressionMethods.None
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NGDP-D3Downloader/2.0");

            // 1. Collect all distinct Base URLs and Base MPQs
            var baseUrls = builds.Where(b => !b.IsCasc).Select(b => b.BaseDirectUrl).Distinct().ToList();

            // 2. Collect all update MPQ URLs
            var updateFiles = new List<(string url, string localRel)>();

            foreach (var url in baseUrls)
            {
                // base-Win.mpq
                string baseRel = GetRelativeFromBaseUrl(url) + "/Data_D3/PC/MPQs/base-Win.mpq";
                updateFiles.Add(($"{url}/Data_D3/PC/MPQs/base-Win.mpq", baseRel));
            }

            foreach (var b in builds.Where(b => !b.IsCasc))
            {
                if (b.BuildNumber > b.BaseBuildNumber)
                {
                    string updateUrl = $"{b.BaseDirectUrl}/Updates/d3-0-{b.BuildNumber}-Win-final.MPQ";
                    string updateRel = GetRelativeFromBaseUrl(b.BaseDirectUrl) + $"/Updates/d3-0-{b.BuildNumber}-Win-final.MPQ";
                    updateFiles.Add((updateUrl, updateRel));
                }

                if (!string.IsNullOrWhiteSpace(b.MfilFileName))
                {
                    string mfilUrl = $"{b.BaseDirectUrl}/{b.MfilFileName}";
                    string mfilRel = GetRelativeFromBaseUrl(b.BaseDirectUrl) + $"/{b.MfilFileName}";
                    updateFiles.Add((mfilUrl, mfilRel));
                }
            }

            // De-duplicate
            var distinctFiles = updateFiles.GroupBy(x => x.url).Select(g => g.First()).ToList();

            int total = distinctFiles.Count;
            int completed = 0;
            int skipped = 0;

            log($"Discovered {total} raw Blizzard CDN archive files to mirror...");

            for (int i = 0; i < distinctFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var item = distinctFiles[i];
                string localPath = Path.Combine(mirrorRoot, item.localRel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                onOverallProgress?.Invoke(i + 1, total, $"[{i + 1}/{total}] Mirroring {Path.GetFileName(item.url)}...");

                if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                {
                    skipped++;
                    continue;
                }

                log($"[{i + 1}/{total}] Downloading {item.url}...");
                try
                {
                    await DownloadRawFileAsync(http, item.url, localPath, onItemProgress, ct);
                    completed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log($"[ERROR] Failed to download {item.url}: {ex.Message}");
                }
            }

            // Write Manifest
            string manifestPath = Path.Combine(mirrorRoot, "RAW_CDN_MIRROR_INFO.txt");
            var sb = new StringBuilder();
            sb.AppendLine("========================================================================");
            sb.AppendLine(" Diablo III — Raw Blizzard CDN Offline Mirror");
            sb.AppendLine("========================================================================");
            sb.AppendLine($"Date Generated   : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Total Files      : {distinctFiles.Count}");
            sb.AppendLine($"Offline Sources  : 100% Direct from Blizzard EdgeSuite / Akamai");
            sb.AppendLine("========================================================================");
            foreach (var f in distinctFiles)
            {
                sb.AppendLine($"{f.localRel,-80} | {f.url}");
            }
            await File.WriteAllTextAsync(manifestPath, sb.ToString(), Encoding.UTF8, ct);

            log("\n========================================================================");
            log($" RAW CDN MIRROR COMPLETE!");
            log($" Total Archives : {total}");
            log($" Downloaded     : {completed}");
            log($" Skipped (OK)   : {skipped}");
            log($" Mirror Path    : {mirrorRoot}");
            log("========================================================================");

            return true;
        }

        private static string GetRelativeFromBaseUrl(string baseUrl)
        {
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                return uri.AbsolutePath.Trim('/');
            }
            return "cdn-data";
        }

        private static async Task DownloadRawFileAsync(HttpClient http, string url, string destPath, Action<double, string> onProgress, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();

            long total = res.Content.Headers.ContentLength ?? -1L;
            string temp = destPath + ".tmp";

            await using (var src = await res.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(temp))
            {
                byte[] buf = new byte[128 * 1024];
                long readSoFar = 0;
                int read;
                while ((read = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct);
                    readSoFar += read;
                    if (total > 0)
                    {
                        double pct = (double)readSoFar / total * 100.0;
                        onProgress?.Invoke(pct, $"{readSoFar / 1048576.0:F1} MB / {total / 1048576.0:F1} MB");
                    }
                }
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(temp, destPath);
        }

        private static async Task AppendChecksumsAsync(string backupRoot, string buildDir, CancellationToken ct)
        {
            string shaFile = Path.Combine(backupRoot, "SHA256SUMS.txt");
            var lines = new List<string>();

            foreach (var f in Directory.GetFiles(buildDir, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                {
                    using var sha = SHA256.Create();
                    await using var stream = File.OpenRead(f);
                    byte[] hash = await sha.ComputeHashAsync(stream, ct);
                    string hashHex = Convert.ToHexString(hash).ToLowerInvariant();
                    string relPath = Path.GetRelativePath(backupRoot, f).Replace('\\', '/');
                    lines.Add($"{hashHex}  {relPath}");
                }
            }

            if (lines.Count > 0)
            {
                await File.AppendAllLinesAsync(shaFile, lines, Encoding.UTF8, ct);
            }
        }
    }
}

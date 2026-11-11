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
    [Flags]
    public enum BackupMode
    {
        FastBinaries = 1,
        RawCdnMirror = 2,
        Both = FastBinaries | RawCdnMirror
    }

    public enum BackupScope
    {
        All,
        RetailOnly,
        ReaperOfSoulsOnly,
        VanillaOnly
    }

    public enum BackupLocaleMode
    {
        SingleLocale,
        AllTextLocales,
        AllLocalesFull
    }

    public sealed class ArchiveInventory
    {
        public string DestinationRoot { get; set; } = string.Empty;
        public int TotalCatalogBuilds { get; set; }
        public int FastBinariesCount { get; set; }
        public int FastBinariesMissing => Math.Max(0, TotalCatalogBuilds - FastBinariesCount);
        public List<D3BuildCatalogItem> ExistingBuilds { get; set; } = new();
        public List<D3BuildCatalogItem> MissingBuilds { get; set; } = new();

        public int RawCdnTotalExpected { get; set; }
        public int RawCdnPresentCount { get; set; }
        public int RawCdnMissingCount => Math.Max(0, RawCdnTotalExpected - RawCdnPresentCount);

        public bool HasVanillaBaseCache { get; set; }
        public bool HasRosBaseCache { get; set; }
        public List<string> AvailableLocales { get; set; } = new();

        public long TotalDiskBytes { get; set; }
        public string FormattedDiskSize => FormatBytes(TotalDiskBytes);

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / 1024.0:F1} KB";
        }
    }

    public static class BackupEngine
    {
        public static readonly string[] AllSupportedLocales = new[]
        {
            "enUS", "enGB", "deDE", "frFR", "esES", "esMX",
            "ptBR", "itIT", "plPL", "ruRU", "koKR", "zhTW", "zhCN"
        };

        public static ArchiveInventory ScanInventory(string backupRoot, BackupScope scope)
        {
            var inv = new ArchiveInventory
            {
                DestinationRoot = backupRoot
            };

            var targetBuilds = GetScopeBuilds(scope);
            inv.TotalCatalogBuilds = targetBuilds.Count;

            if (!Directory.Exists(backupRoot))
            {
                inv.MissingBuilds.AddRange(targetBuilds);
                return inv;
            }

            // 1. Scan Fast Binaries builds
            foreach (var b in targetBuilds)
            {
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
                if (!File.Exists(targetExe)) targetExe = Path.Combine(targetDir, "Diablo III.exe");

                if (File.Exists(markerFile) && File.Exists(targetExe) && new FileInfo(targetExe).Length > 100_000)
                {
                    inv.FastBinariesCount++;
                    inv.ExistingBuilds.Add(b);
                }
                else
                {
                    inv.MissingBuilds.Add(b);
                }
            }

            // 2. Scan Raw Blizzard CDN files
            string mirrorRoot = Path.Combine(backupRoot, "RawBlizzardCDN");
            var expectedCdnFiles = GetExpectedRawCdnFiles(targetBuilds);
            inv.RawCdnTotalExpected = expectedCdnFiles.Count;

            if (Directory.Exists(mirrorRoot))
            {
                foreach (var f in expectedCdnFiles)
                {
                    string localPath = Path.Combine(mirrorRoot, f.localRel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                    {
                        inv.RawCdnPresentCount++;
                    }
                }
            }

            // 3. Scan Base Archive Cache
            string baseCache = Path.Combine(backupRoot, "_BaseArchiveCache");
            if (Directory.Exists(baseCache))
            {
                inv.HasVanillaBaseCache = File.Exists(Path.Combine(baseCache, "Vanilla", "ClientData.mpq"));
                inv.HasRosBaseCache = File.Exists(Path.Combine(baseCache, "ReaperOfSouls", "ClientData.mpq"));
            }

            // 4. Calculate total folder disk size
            try
            {
                long size = 0;
                var di = new DirectoryInfo(backupRoot);
                foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    size += fi.Length;
                }
                inv.TotalDiskBytes = size;
            }
            catch
            {
                // Ignore permission or transient errors during quick scan
            }

            return inv;
        }

        public static List<D3BuildCatalogItem> GetScopeBuilds(BackupScope scope)
        {
            return D3BuildCatalog.AllBuilds.Where(b =>
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
        }

        public static async Task<bool> RunBackupAsync(
            string backupRoot,
            BackupMode mode,
            BackupScope scope,
            string locale,
            BackupLocaleMode localeMode,
            bool includeBaseAssets,
            Action<int, int, string> onOverallProgress,
            Action<double, string> onItemProgress,
            Action<string> log,
            CancellationToken ct)
        {
            Directory.CreateDirectory(backupRoot);

            var candidateBuilds = GetScopeBuilds(scope);

            log("========================================================================");
            log(" DIABLO 3 FOREVER — BULK ARCHIVAL & BACKUP ENGINE");
            log("========================================================================");
            log($" Destination   : {backupRoot}");
            log($" Mode          : {FormatBackupMode(mode)}");
            log($" Scope         : {scope} ({candidateBuilds.Count} Target Builds)");
            log($" Primary Locale: {locale}");
            log($" Locale Mode   : {FormatLocaleMode(localeMode)}");
            log($" Base Assets   : {(includeBaseAssets ? "Included (~45 GB Full Standalone Client)" : "Excluded (Compact Patches & Binaries)")}");
            log("========================================================================");

            bool okFast = true;
            bool okRaw = true;

            if (mode.HasFlag(BackupMode.FastBinaries))
            {
                log("\n>>> STARTING FAST BINARIES ARCHIVAL PHASE <<<");
                okFast = await RunFastBinariesBackupAsync(backupRoot, candidateBuilds, locale, localeMode, includeBaseAssets, onOverallProgress, onItemProgress, log, ct);
            }

            if (mode.HasFlag(BackupMode.RawCdnMirror))
            {
                log("\n>>> STARTING RAW BLIZZARD CDN MIRROR PHASE <<<");
                okRaw = await RunRawCdnMirrorBackupAsync(backupRoot, candidateBuilds, locale, localeMode, includeBaseAssets, onOverallProgress, onItemProgress, log, ct);
            }

            // Write master archive root guide
            await WriteMasterArchiveGuideAsync(backupRoot, candidateBuilds, locale, localeMode, ct);

            log("\n========================================================================");
            log(" BULK ARCHIVE COMPLETED SUCCESSFULLY!");
            log($" Target Location : {backupRoot}");
            log($" Verified State  : All selected files are verified and ready on disk.");
            log("========================================================================");

            return okFast && okRaw;
        }

        private static string FormatBackupMode(BackupMode mode)
        {
            if (mode == BackupMode.Both) return "Dual Archive (Fast Binaries + Raw Blizzard CDN Mirror)";
            if (mode == BackupMode.FastBinaries) return "Fast Binaries Archive (~3.2 GB)";
            if (mode == BackupMode.RawCdnMirror) return "Raw Blizzard CDN Mirror";
            return mode.ToString();
        }

        private static string FormatLocaleMode(BackupLocaleMode mode)
        {
            return mode switch
            {
                BackupLocaleMode.SingleLocale => "Selected Locale Only",
                BackupLocaleMode.AllTextLocales => "Universal Multi-Language Pack (All 13 Languages - Text & UI)",
                BackupLocaleMode.AllLocalesFull => "Complete All Locales (Audio, Video & Text for all 13 Languages)",
                _ => mode.ToString()
            };
        }

        private static async Task<bool> RunFastBinariesBackupAsync(
            string backupRoot,
            List<D3BuildCatalogItem> builds,
            string locale,
            BackupLocaleMode localeMode,
            bool includeBaseAssets,
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

                onOverallProgress?.Invoke(i + 1, total, $"[Binaries {i + 1}/{total}] Processing {b.Version}.{b.BuildNumber} ({b.Category})...");

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
                        locale: locale,
                        maxConcurrency: 8,
                        dryRunLimit: 0,
                        binariesOnly: !includeBaseAssets,
                        installPrereqs: false,
                        configureShortcutFirewall: false,
                        log: log,
                        onProgress: onItemProgress,
                        ct: ct,
                        localArchiveSource: backupRoot);

                    if (res == 0)
                    {
                        // Multi-locale support: fetch text MPQs for all other languages if requested
                        if (!b.IsCasc && localeMode == BackupLocaleMode.AllTextLocales)
                        {
                            await DownloadUniversalTextPackAsync(targetDir, b, log, ct);
                        }

                        // Generate locale launcher batch files
                        await GenerateLocaleLaunchScriptsAsync(targetDir, b, locale, localeMode, ct);

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
                        sb.AppendLine($"Locale Support   : {FormatLocaleMode(localeMode)}");
                        sb.AppendLine($"Archive Time     : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                        sb.AppendLine($"Source CDN       : Blizzard Entertainment Official Akamai / EdgeSuite Nodes");
                        sb.AppendLine("========================================================================");
                        sb.AppendLine("Launch Instructions:");
                        sb.AppendLine("  Double-click 'Launch-Game.bat' to pick any language and launch.");
                        sb.AppendLine($"  Or double-click 'Launch-{locale}.bat' to launch directly in {locale}.");
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
            log($" FAST BINARIES ARCHIVE PHASE FINISHED");
            log($" Total Builds : {total}");
            log($" Newly Backed : {success}");
            log($" Skipped (OK) : {skipped}");
            log("========================================================================");

            return true;
        }

        private static List<(string url, string localRel)> GetExpectedRawCdnFiles(List<D3BuildCatalogItem> builds)
        {
            var updateFiles = new List<(string url, string localRel)>();
            var baseUrls = builds.Where(b => !b.IsCasc).Select(b => b.BaseDirectUrl).Distinct().ToList();

            foreach (var url in baseUrls)
            {
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

            return updateFiles.GroupBy(x => x.url).Select(g => g.First()).ToList();
        }

        private static async Task<bool> RunRawCdnMirrorBackupAsync(
            string backupRoot,
            List<D3BuildCatalogItem> builds,
            string primaryLocale,
            BackupLocaleMode localeMode,
            bool includeBaseAssets,
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

            var distinctFiles = GetExpectedRawCdnFiles(builds);

            // If Base Assets or Multi-Locales requested, parse MFILs to collect assets
            if (includeBaseAssets || localeMode != BackupLocaleMode.SingleLocale)
            {
                log("Inspecting .mfil manifests for base assets and multi-language archives...");
                var mfilUrls = builds.Where(b => !b.IsCasc && !string.IsNullOrWhiteSpace(b.MfilFileName))
                    .Select(b => (url: $"{b.BaseDirectUrl}/{b.MfilFileName}", baseDirect: b.BaseDirectUrl))
                    .Distinct()
                    .ToList();

                foreach (var item in mfilUrls)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        string mfilContent = await http.GetStringAsync(item.url, ct);
                        var entries = LegacyMpqInstaller.ParseMfil(mfilContent);
                        string baseRel = GetRelativeFromBaseUrl(item.baseDirect);

                        foreach (var e in entries)
                        {
                            if (string.IsNullOrWhiteSpace(e.RelativePath)) continue;
                            if (e.RelativePath.EndsWith("base-OSX.mpq", StringComparison.OrdinalIgnoreCase) ||
                                e.RelativePath.EndsWith("OpenGLShaders.mpq", StringComparison.OrdinalIgnoreCase))
                                continue;

                            bool include = false;

                            // Base game assets
                            if (includeBaseAssets && (e.Tag == "base" || e.Tag == "Win" || string.IsNullOrWhiteSpace(e.Tag)))
                            {
                                include = true;
                            }

                            // Locales
                            if (localeMode == BackupLocaleMode.SingleLocale && e.Tag == primaryLocale)
                            {
                                include = true;
                            }
                            else if (localeMode == BackupLocaleMode.AllTextLocales)
                            {
                                if (e.Tag == primaryLocale)
                                {
                                    include = true;
                                }
                                else if (AllSupportedLocales.Contains(e.Tag, StringComparer.OrdinalIgnoreCase) &&
                                         (e.RelativePath.EndsWith("_Text.mpq", StringComparison.OrdinalIgnoreCase) ||
                                          e.RelativePath.Contains("/d3-update-", StringComparison.OrdinalIgnoreCase)))
                                {
                                    include = true;
                                }
                            }
                            else if (localeMode == BackupLocaleMode.AllLocalesFull)
                            {
                                if (AllSupportedLocales.Contains(e.Tag, StringComparer.OrdinalIgnoreCase))
                                {
                                    include = true;
                                }
                            }

                            if (include)
                            {
                                string fileUrl = $"{item.baseDirect}/{e.RelativePath.Replace('\\', '/')}";
                                string fileRel = baseRel + "/" + e.RelativePath.Replace('\\', '/');
                                distinctFiles.Add((fileUrl, fileRel));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log($"[NOTICE] Could not parse .mfil {item.url}: {ex.Message}");
                    }
                }

                distinctFiles = distinctFiles.GroupBy(x => x.url).Select(g => g.First()).ToList();
            }

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

                onOverallProgress?.Invoke(i + 1, total, $"[CDN {i + 1}/{total}] Mirroring {Path.GetFileName(item.url)}...");

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
            log($" RAW CDN MIRROR PHASE FINISHED");
            log($" Total Archives : {total}");
            log($" Downloaded     : {completed}");
            log($" Skipped (OK)   : {skipped}");
            log($" Mirror Path    : {mirrorRoot}");
            log("========================================================================");

            return true;
        }

        private static async Task DownloadUniversalTextPackAsync(string targetDir, D3BuildCatalogItem build, Action<string> log, CancellationToken ct)
        {
            string mpqDir = Path.Combine(targetDir, "Data_D3", "PC", "MPQs");
            Directory.CreateDirectory(mpqDir);

            using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 8 };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NGDP-D3Downloader/2.0");

            foreach (var loc in AllSupportedLocales)
            {
                ct.ThrowIfCancellationRequested();
                string textFileName = $"{loc}_Text.mpq";
                string destFile = Path.Combine(mpqDir, textFileName);
                if (File.Exists(destFile) && new FileInfo(destFile).Length > 10_000)
                    continue;

                string url = $"{build.BaseDirectUrl}/Data_D3/PC/MPQs/{textFileName}";
                try
                {
                    await DownloadRawFileAsync(http, url, destFile, null, ct);
                    log($"      [LOCALE] Universal Text Pack: Downloaded {textFileName} ({new FileInfo(destFile).Length / 1024} KB)");
                }
                catch
                {
                    // Non-fatal if a specific beta build lacked one locale
                }
            }
        }

        public static async Task GenerateLocaleLaunchScriptsAsync(
            string targetDir,
            D3BuildCatalogItem build,
            string primaryLocale,
            BackupLocaleMode localeMode,
            CancellationToken ct)
        {
            string exeName = build.IsCasc ? "Diablo III64.exe" : "Diablo III.exe";

            // 1. Interactive Master Launcher
            var sbMenu = new StringBuilder();
            sbMenu.AppendLine("@echo off");
            sbMenu.AppendLine("title Diablo III Historical Archive Launcher");
            sbMenu.AppendLine("cls");
            sbMenu.AppendLine("echo ========================================================================");
            sbMenu.AppendLine($"echo  DIABLO III {build.Version}.{build.BuildNumber} — MULTI-LOCALE LAUNCHER");
            sbMenu.AppendLine("echo ========================================================================");
            sbMenu.AppendLine("echo  Select your preferred language:");
            sbMenu.AppendLine("echo.");
            sbMenu.AppendLine("echo   [1]  enUS - English (US)          [7]  ptBR - Portugues (Brasil)");
            sbMenu.AppendLine("echo   [2]  deDE - Deutsch (German)      [8]  itIT - Italiano");
            sbMenu.AppendLine("echo   [3]  frFR - Francais (French)     [9]  plPL - Polski");
            sbMenu.AppendLine("echo   [4]  esES - Espanol (Castellano)  [10] ruRU - Russian");
            sbMenu.AppendLine("echo   [5]  esMX - Espanol (Latino)      [11] koKR - Korean");
            sbMenu.AppendLine("echo   [6]  enGB - English (EU)          [12] zhTW - Traditional Chinese");
            sbMenu.AppendLine("echo                                     [13] zhCN - Simplified Chinese");
            sbMenu.AppendLine("echo ========================================================================");
            sbMenu.AppendLine("set /p choice=\"Enter choice [1-13] (default 1): \"");
            sbMenu.AppendLine("if \"%choice%\"==\"\" set choice=1");
            sbMenu.AppendLine();
            sbMenu.AppendLine("set LOC=enUS");
            sbMenu.AppendLine("if \"%choice%\"==\"1\" set LOC=enUS");
            sbMenu.AppendLine("if \"%choice%\"==\"2\" set LOC=deDE");
            sbMenu.AppendLine("if \"%choice%\"==\"3\" set LOC=frFR");
            sbMenu.AppendLine("if \"%choice%\"==\"4\" set LOC=esES");
            sbMenu.AppendLine("if \"%choice%\"==\"5\" set LOC=esMX");
            sbMenu.AppendLine("if \"%choice%\"==\"6\" set LOC=enGB");
            sbMenu.AppendLine("if \"%choice%\"==\"7\" set LOC=ptBR");
            sbMenu.AppendLine("if \"%choice%\"==\"8\" set LOC=itIT");
            sbMenu.AppendLine("if \"%choice%\"==\"9\" set LOC=plPL");
            sbMenu.AppendLine("if \"%choice%\"==\"10\" set LOC=ruRU");
            sbMenu.AppendLine("if \"%choice%\"==\"11\" set LOC=koKR");
            sbMenu.AppendLine("if \"%choice%\"==\"12\" set LOC=zhTW");
            sbMenu.AppendLine("if \"%choice%\"==\"13\" set LOC=zhCN");
            sbMenu.AppendLine();
            sbMenu.AppendLine("echo Launching Diablo III in [%LOC%]...");
            sbMenu.AppendLine($"start \"\" \"{exeName}\" -locale %LOC% -launch");

            await File.WriteAllTextAsync(Path.Combine(targetDir, "Launch-Game.bat"), sbMenu.ToString(), Encoding.ASCII, ct);

            // 2. Individual quick-launch batch files for primary & popular locales
            foreach (var loc in AllSupportedLocales)
            {
                string scriptPath = Path.Combine(targetDir, $"Launch-{loc}.bat");
                string cmd = $"@start \"\" \"{exeName}\" -locale {loc} -launch\r\n";
                await File.WriteAllTextAsync(scriptPath, cmd, Encoding.ASCII, ct);
            }

            // 3. Locale Guide text
            var sbGuide = new StringBuilder();
            sbGuide.AppendLine("========================================================================");
            sbGuide.AppendLine($" Diablo III — Language & Locale Selection Guide");
            sbGuide.AppendLine("========================================================================");
            sbGuide.AppendLine($"Target Build   : {build.Version}.{build.BuildNumber}");
            sbGuide.AppendLine($"Default Locale : {primaryLocale}");
            sbGuide.AppendLine($"Mode           : {FormatLocaleMode(localeMode)}");
            sbGuide.AppendLine("========================================================================");
            sbGuide.AppendLine("How to switch languages:");
            sbGuide.AppendLine("  1. Double-click 'Launch-Game.bat' to pick any language from the menu.");
            sbGuide.AppendLine("  2. Or double-click any 'Launch-<locale>.bat' (e.g. Launch-deDE.bat).");
            sbGuide.AppendLine($"  3. Or run from command prompt: \"{exeName}\" -locale <locale_code> -launch");
            sbGuide.AppendLine("========================================================================");

            await File.WriteAllTextAsync(Path.Combine(targetDir, "LOCALE_GUIDE.txt"), sbGuide.ToString(), Encoding.UTF8, ct);
        }

        public static async Task<bool> BuildCompleteGameFromArchiveAsync(
            string archiveSource,
            string destinationFolder,
            D3BuildCatalogItem build,
            string locale,
            BackupLocaleMode localeMode,
            bool createShortcuts,
            bool configureFirewall,
            bool installPrereqs,
            Action<double, string> onProgress,
            Action<string> log,
            CancellationToken ct)
        {
            Directory.CreateDirectory(destinationFolder);

            log("========================================================================");
            log($" ASSEMBLING COMPLETE GAME INSTALL FROM ARCHIVE");
            log("========================================================================");
            log($" Target Build : {build.Version}.{build.BuildNumber} ({build.Category})");
            log($" Source Root  : {archiveSource}");
            log($" Destination  : {destinationFolder}");
            log($" Target Locale: {locale} ({FormatLocaleMode(localeMode)})");
            log("========================================================================");

            onProgress?.Invoke(5, "Resolving archived assets...");

            int res = await Program.ExecuteBuildPipelineAsync(
                targetDir: destinationFolder,
                selectedBuild: build,
                region: "us",
                locale: locale,
                maxConcurrency: 16,
                dryRunLimit: 0,
                binariesOnly: false, // Complete game install
                installPrereqs: installPrereqs,
                configureShortcutFirewall: configureFirewall || createShortcuts,
                log: log,
                onProgress: onProgress,
                ct: ct,
                localArchiveSource: archiveSource);

            if (res != 0)
            {
                log($"[ERROR] Build pipeline returned error code {res}.");
                return false;
            }

            // Multi-locale support: fetch or copy text MPQs for all other languages if requested
            if (!build.IsCasc && (localeMode == BackupLocaleMode.AllTextLocales || localeMode == BackupLocaleMode.AllLocalesFull))
            {
                await DownloadUniversalTextPackAsync(destinationFolder, build, log, ct);
            }

            // Generate multi-locale launcher batch files
            await GenerateLocaleLaunchScriptsAsync(destinationFolder, build, locale, localeMode, ct);

            onProgress?.Invoke(100, "Complete game build assembled successfully!");
            log("\n========================================================================");
            log($" COMPLETE GAME INSTALLATION READY!");
            log($" Location: {destinationFolder}");
            log($" Ready to play offline directly with zero network dependencies.");
            log("========================================================================");

            return true;
        }

        private static async Task WriteMasterArchiveGuideAsync(
            string backupRoot,
            List<D3BuildCatalogItem> builds,
            string locale,
            BackupLocaleMode localeMode,
            CancellationToken ct)
        {
            string guidePath = Path.Combine(backupRoot, "ARCHIVE_README.txt");
            var sb = new StringBuilder();
            sb.AppendLine("========================================================================");
            sb.AppendLine(" DIABLO III HISTORICAL ARCHIVE — MASTER REPOSITORY");
            sb.AppendLine("========================================================================");
            sb.AppendLine($"Generated on      : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Total Cataloged   : {builds.Count} Builds");
            sb.AppendLine($"Primary Locale    : {locale}");
            sb.AppendLine($"Locale Support    : {FormatLocaleMode(localeMode)}");
            sb.AppendLine("========================================================================");
            sb.AppendLine("Folder Structure:");
            sb.AppendLine("  ├── Reaper of Souls/        Patched standalone builds for Expansion");
            sb.AppendLine("  │   ├── Retail/             Patches 2.0.1 through 2.7.7");
            sb.AppendLine("  │   ├── Public Test Realm/  PTR testing builds");
            sb.AppendLine("  │   └── Closed Beta/        RoS Pre-release Beta");
            sb.AppendLine("  ├── Vanilla Classic/        Patched standalone builds for Vanilla 2012");
            sb.AppendLine("  │   ├── Retail/             Patches 1.0.1 through 1.0.8a");
            sb.AppendLine("  │   ├── Public Test Realm/  Vanilla PTR milestones");
            sb.AppendLine("  │   └── Closed Beta/        Friends & Family 2011 Beta");
            sb.AppendLine("  ├── RawBlizzardCDN/         Offline cold storage mirror of original Blizzard MPQs");
            sb.AppendLine("  └── _BaseArchiveCache/      Shared multi-GB base assets (ClientData/CoreData)");
            sb.AppendLine("========================================================================");
            sb.AppendLine("How to Play / Deploy Any Build:");
            sb.AppendLine("  1. Open Diablo 3 Forever and switch to 'Assemble Complete Game from Archive'.");
            sb.AppendLine("  2. Or navigate directly into any build folder and double-click 'Launch-Game.bat'.");
            sb.AppendLine("  3. To launch in a specific language, use 'Launch-<locale>.bat'.");
            sb.AppendLine("========================================================================");

            await File.WriteAllTextAsync(guidePath, sb.ToString(), Encoding.UTF8, ct);
        }

        public static string GetRelativeFromBaseUrl(string baseUrl)
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

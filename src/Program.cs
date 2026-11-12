using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace D3Downloader
{
    internal static class Program
    {
        private static readonly string[] DefaultPatchHosts =
        {
            "https://us.patch.battle.net/d3",
            "https://us.version.battle.net/v2/products/d3",
            "https://eu.patch.battle.net/d3",
            "https://eu.version.battle.net/v2/products/d3"
        };

        // Only these Blizzard-controlled domains may ever be used as a download source.
        private static readonly string[] TrustedCdnHostSuffixes =
        {
            ".blizzard.com",
            ".battle.net",
            ".akamaihd.net",
            ".edgesuite.net",
            ".llnwd.net"
        };

        private const string DefaultSharedCdnConfigKey = "6547fc64ca796c7c1b25864145814ace";

        private static HttpClient _http;
        private static List<string> _cdnHosts = new();
        private static string _cdnPath = "tpr/diablo3";
        private static string _cdnConfigPath = "tpr/configs/data";

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        /// <summary>
        /// Resolves a manifest-supplied relative path inside <paramref name="root"/> and fails
        /// if the result would escape it (path traversal / absolute path injection).
        /// </summary>
        internal static string SafePathUnder(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A target root directory is required.", nameof(root));
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("A relative path is required.", nameof(relativePath));

            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) || normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new InvalidDataException($"Refusing unsafe path '{relativePath}'.");

            string rootFull = Path.GetFullPath(root);
            string combined = Path.GetFullPath(Path.Combine(rootFull, normalized));
            string rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Path '{relativePath}' escapes the target directory.");

            return combined;
        }

        /// <summary>
        /// Same as <see cref="SafePathUnder"/> but returns null instead of throwing.
        /// </summary>
        internal static string TrySafePathUnder(string root, string relativePath)
        {
            try { return SafePathUnder(root, relativePath); }
            catch (Exception) { return null; }
        }

        internal static bool IsTrustedCdnHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (Uri.CheckHostName(host.Trim()) != UriHostNameType.Dns) return false;
            return TrustedCdnHostSuffixes.Any(s => host.Trim().EndsWith(s, StringComparison.OrdinalIgnoreCase));
        }

        internal static string ComputeMd5Hex(byte[] data)
        {
            using var md5 = MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(data));
        }

        internal static bool HashMatchesMd5(byte[] data, string expectedMd5Hex) =>
            !string.IsNullOrWhiteSpace(expectedMd5Hex) &&
            string.Equals(ComputeMd5Hex(data), expectedMd5Hex, StringComparison.OrdinalIgnoreCase);

        internal static bool IsValidHash32(string value) =>
            !string.IsNullOrEmpty(value) && value.Length == 32 && value.All(Uri.IsHexDigit);

        internal static bool IsSupportedLocale(string locale) =>
            !string.IsNullOrWhiteSpace(locale)
            && (locale.Equals("allText", StringComparison.OrdinalIgnoreCase)
                || locale.Equals("allLocales", StringComparison.OrdinalIgnoreCase)
                || BackupEngine.AllSupportedLocales.Contains(locale, StringComparer.OrdinalIgnoreCase));

        [STAThread]
        public static int Main(string[] args)
        {
            string targetDir = @"C:\Program Files (x86)\Diablo III";
            string region = "us";
            string locale = "enUS";
            string versionArg = "live";
            int maxConcurrency = 16;
            int dryRunLimit = -1; // -1 = full download
            bool binariesOnly = false;
            bool listVersions = false;
            string exeName = Process.GetCurrentProcess().MainModule?.ModuleName ?? string.Empty;
            bool fullDeploy = exeName.Contains("FullInstaller", StringComparison.OrdinalIgnoreCase)
                           || exeName.Contains("AutoDeploy", StringComparison.OrdinalIgnoreCase);
            bool launchGui = args.Length == 0 && !fullDeploy && !exeName.Contains("D3Downloader", StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--gui", StringComparison.OrdinalIgnoreCase))
                {
                    launchGui = true;
                }
                else if (arg.Equals("--list-versions", StringComparison.OrdinalIgnoreCase) || arg.Equals("--versions", StringComparison.OrdinalIgnoreCase))
                {
                    listVersions = true;
                }
                else if (arg.Equals("--snapshot-ros", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string outSnap = args[++i];
                    System.Windows.Forms.Application.EnableVisualStyles();
                    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                    using var f = new VersionSelectorForm();
                    f.Show();
                    f.CaptureSnapshot(outSnap, expandRos: true, expandVanilla: false);
                    return 0;
                }
                else if (arg.Equals("--snapshot-vanilla", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string outSnap = args[++i];
                    System.Windows.Forms.Application.EnableVisualStyles();
                    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                    using var f = new VersionSelectorForm();
                    f.Show();
                    f.CaptureSnapshot(outSnap, expandRos: false, expandVanilla: true);
                    return 0;
                }
                else if ((arg.Equals("--version", StringComparison.OrdinalIgnoreCase) || arg.Equals("--build", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    versionArg = args[++i];
                    launchGui = false;
                }
                else if (arg.Equals("--binaries-only", StringComparison.OrdinalIgnoreCase))
                {
                    binariesOnly = true;
                    launchGui = false;
                }
                else if (arg.Equals("--deploy", StringComparison.OrdinalIgnoreCase) || arg.Equals("--full-deploy", StringComparison.OrdinalIgnoreCase))
                {
                    fullDeploy = true;
                    launchGui = false;
                }
                else if (arg.Equals("--no-deploy", StringComparison.OrdinalIgnoreCase))
                {
                    fullDeploy = false;
                }
                else if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) || arg.Equals("--limit", StringComparison.OrdinalIgnoreCase))
                {
                    launchGui = false;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int lim))
                    {
                        dryRunLimit = lim;
                        i++;
                    }
                    else
                    {
                        dryRunLimit = 25;
                    }
                }
                else if (arg.Equals("--region", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    region = args[++i].ToLowerInvariant();
                }
                else if (arg.Equals("--locale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    locale = args[++i];
                }
                else if (arg.Equals("--threads", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out int th))
                {
                    maxConcurrency = Math.Clamp(th, 1, 64);
                }
                else if (arg.Equals("--backup-all", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string backupDest = args[++i];
                    BackupMode mode = BackupMode.FastBinaries;
                    BackupScope scope = BackupScope.All;
                    BackupLocaleMode locMode = BackupLocaleMode.AllTextLocales;
                    bool includeBase = false;

                    for (int j = i + 1; j < args.Length; j++)
                    {
                        if (args[j].Equals("--backup-mode", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                        {
                            string mStr = args[++j];
                            if (mStr.Equals("raw", StringComparison.OrdinalIgnoreCase) || mStr.Equals("cdn", StringComparison.OrdinalIgnoreCase))
                                mode = BackupMode.RawCdnMirror;
                            else if (mStr.Equals("both", StringComparison.OrdinalIgnoreCase) || mStr.Equals("all", StringComparison.OrdinalIgnoreCase))
                                mode = BackupMode.Both;
                            else if (mStr.Equals("fast", StringComparison.OrdinalIgnoreCase) || mStr.Equals("binaries", StringComparison.OrdinalIgnoreCase))
                                mode = BackupMode.FastBinaries;
                        }
                        else if (args[j].Equals("--backup-scope", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                        {
                            string sStr = args[++j];
                            if (sStr.Equals("retail", StringComparison.OrdinalIgnoreCase)) scope = BackupScope.RetailOnly;
                            else if (sStr.Equals("ros", StringComparison.OrdinalIgnoreCase)) scope = BackupScope.ReaperOfSoulsOnly;
                            else if (sStr.Equals("vanilla", StringComparison.OrdinalIgnoreCase)) scope = BackupScope.VanillaOnly;
                        }
                        else if (args[j].Equals("--locale-mode", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                        {
                            string lm = args[++j];
                            if (lm.Equals("single", StringComparison.OrdinalIgnoreCase)) locMode = BackupLocaleMode.SingleLocale;
                            else if (lm.Equals("all-text", StringComparison.OrdinalIgnoreCase) || lm.Equals("text", StringComparison.OrdinalIgnoreCase)) locMode = BackupLocaleMode.AllTextLocales;
                            else if (lm.Equals("all-full", StringComparison.OrdinalIgnoreCase) || lm.Equals("full", StringComparison.OrdinalIgnoreCase)) locMode = BackupLocaleMode.AllLocalesFull;
                        }
                        else if (args[j].Equals("--include-base-assets", StringComparison.OrdinalIgnoreCase) || args[j].Equals("--include-base", StringComparison.OrdinalIgnoreCase))
                        {
                            includeBase = true;
                        }
                    }

                    AttachConsole(-1);
                    try
                    {
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }

                    var ok = BackupEngine.RunBackupAsync(
                        backupRoot: backupDest,
                        mode: mode,
                        scope: scope,
                        locale: locale,
                        localeMode: locMode,
                        includeBaseAssets: includeBase,
                        onOverallProgress: (curr, tot, msg) => Console.WriteLine($"[OVERALL {curr}/{tot}] {msg}"),
                        onItemProgress: (pct, msg) => { },
                        log: Console.WriteLine,
                        ct: CancellationToken.None).GetAwaiter().GetResult();

                    return ok ? 0 : 1;
                }
                else if (arg.Equals("--build-from-archive", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string archiveSrc = args[++i];
                    string targetFolder = @"C:\Games\Diablo III";
                    int targetBuildNum = 16603;
                    string targetLocale = "enUS";

                    for (int j = i + 1; j < args.Length; j++)
                    {
                        if (args[j].Equals("--dest", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                            targetFolder = args[++j];
                        else if (args[j].Equals("--build", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length && int.TryParse(args[++j], out int bn))
                            targetBuildNum = bn;
                        else if (args[j].Equals("--locale", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                            targetLocale = args[++j];
                    }

                    var b = D3BuildCatalog.AllBuilds.FirstOrDefault(x => x.BuildNumber == targetBuildNum)
                        ?? D3BuildCatalog.AllBuilds.First();

                    AttachConsole(-1);
                    try
                    {
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }

                    var ok = BackupEngine.BuildCompleteGameFromArchiveAsync(
                        archiveSource: archiveSrc,
                        destinationFolder: targetFolder,
                        build: b,
                        locale: targetLocale,
                        localeMode: BackupLocaleMode.AllTextLocales,
                        createShortcuts: true,
                        configureFirewall: true,
                        installPrereqs: true,
                        onProgress: (pct, msg) => Console.WriteLine($"[{pct:F1}%] {msg}"),
                        log: Console.WriteLine,
                        ct: CancellationToken.None).GetAwaiter().GetResult();

                    return ok ? 0 : 1;
                }
                else if (arg.Equals("--rebuild-casc", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string installRoot = Path.GetFullPath(args[++i]);
                    bool keepShmem = i + 1 < args.Length && args[i + 1].Equals("--keep-shmem", StringComparison.OrdinalIgnoreCase);
                    if (keepShmem) i++;
                    AttachConsole(-1);
                    try
                    {
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }

                    string cascDir = Path.Combine(installRoot, "Data", "data");
                    Console.WriteLine($"Rebuilding CASC indexes from {cascDir} ...");
                    try
                    {
                        int records = CascStorageWriter.RebuildFromExisting(cascDir, Console.WriteLine, keepShmem);
                        Console.WriteLine($"Rebuilt {records:N0} chunk records, 16 .idx files and shmem.");
                        string buildInfo = Path.Combine(installRoot, ".build.info");
                        if (File.Exists(buildInfo))
                        {
                            string[] lines = File.ReadAllLines(buildInfo);
                            if (lines.Length >= 2)
                            {
                                string[] cols = lines[1].Split('|');
                                if (cols.Length >= 5)
                                {
                                    cols[4] = "";
                                    lines[1] = string.Join('|', cols);
                                    File.WriteAllLines(buildInfo, lines, Encoding.ASCII);
                                    Console.WriteLine("Cleared the Install Key in .build.info (matches Blizzard deployments).");
                                }
                            }
                        }
                        try
                        {
                            NgdpConsistency.Apply(installRoot, Console.WriteLine);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WARNING] CDN catalog repair failed: {ex.Message}");
                        }
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                        return 1;
                    }
                }
                else if (arg.Equals("--verify-casc", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string installRoot = Path.GetFullPath(args[++i]);
                    AttachConsole(-1);
                    try
                    {
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }
                    try
                    {
                        return VerifyCascAsync(installRoot).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                        return 1;
                    }
                }
                else if (arg.Equals("--fix-ngdp", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string installRoot = Path.GetFullPath(args[++i]);
                    AttachConsole(-1);
                    try
                    {
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }
                    try
                    {
                        var result = NgdpConsistency.Apply(installRoot, Console.WriteLine);
                        Console.WriteLine(result.Changed
                            ? "CDN catalog rewritten; the retail client no longer needs the retired artefacts."
                            : "CDN catalog is already consistent.");
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                        return 1;
                    }
                }
                else if (arg.Equals("--check-update", StringComparison.OrdinalIgnoreCase) || arg.Equals("--update", StringComparison.OrdinalIgnoreCase))
                {
                    AttachConsole(-1);
                    try
                    {
                        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                        Console.SetOut(stdOut);
                        var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                        Console.SetError(stdErr);
                    }
                    catch { }

                    var uInfo = UpdateChecker.CheckForUpdatesAsync().GetAwaiter().GetResult();
                    if (uInfo.HasUpdate)
                    {
                        Console.WriteLine($"[UPDATE AVAILABLE] Version {uInfo.LatestVersion} is available (Current: {uInfo.CurrentVersion})");
                        Console.WriteLine($"Download URL: {uInfo.DownloadUrl}");
                        Console.WriteLine($"Changelog:\n{uInfo.ReleaseNotes}");
                    }
                    else
                    {
                        Console.WriteLine($"[UP TO DATE] Diablo 3 Forever is running the latest version (v{uInfo.CurrentVersion}).");
                    }
                    return 0;
                }
                else if (!arg.StartsWith("-"))
                {
                    targetDir = Path.GetFullPath(arg);
                    launchGui = false;
                }
            }

            if (launchGui && Environment.UserInteractive && !listVersions)
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                System.Windows.Forms.Application.Run(new VersionSelectorForm());
                return 0;
            }

            AttachConsole(-1);
            try
            {
                var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdOut);
                var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                Console.SetError(stdErr);
            }
            catch { }

            if (listVersions)
            {
                Console.WriteLine("======================================================================================================================");
                Console.WriteLine($" Available Official Blizzard CDN Diablo III Builds ({D3BuildCatalog.AllBuilds.Count} Total Supported Builds Across Retail, PTR & Beta)");
                Console.WriteLine("======================================================================================================================");
                foreach (var b in D3BuildCatalog.AllBuilds)
                {
                    string edition = b.IsRoS ? "Reaper of Souls" : "Vanilla Classic";
                    string catStr = b.Category switch
                    {
                        D3BuildCategory.Retail => "Retail",
                        D3BuildCategory.Ptr => "PTR",
                        D3BuildCategory.Beta => "Beta",
                        _ => "Other"
                    };
                    Console.WriteLine($"  {b.Version,-8} | Build {b.BuildNumber,-6} | {catStr,-6} | {(b.IsCasc ? "CASC 64-bit" : "MPQ  32-bit"),-11} | {edition,-16} | {b.ReleaseDate,-10} | {b.Summary}");
                }
                Console.WriteLine("======================================================================================================================");
                return 0;
            }

            if (!IsSupportedLocale(locale))
            {
                Console.Error.WriteLine($"[ERROR] Unsupported locale '{locale}'. Use one of: {string.Join(", ", BackupEngine.AllSupportedLocales)}, allText, allLocales.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(region) || !region.All(char.IsLetterOrDigit))
            {
                Console.Error.WriteLine($"[ERROR] Invalid region '{region}'.");
                return 1;
            }

            var resolvedBuild = D3BuildCatalog.Resolve(versionArg);
            if (resolvedBuild == null)
            {
                Console.Error.WriteLine($"[ERROR] Unknown version or build '{versionArg}'. Run with --list-versions to see all {D3BuildCatalog.AllBuilds.Count} supported builds.");
                return 1;
            }

            return ExecuteBuildPipelineAsync(
                targetDir: targetDir,
                selectedBuild: resolvedBuild,
                region: region,
                locale: locale,
                maxConcurrency: maxConcurrency,
                dryRunLimit: dryRunLimit,
                binariesOnly: binariesOnly,
                installPrereqs: fullDeploy && dryRunLimit <= 0,
                configureShortcutFirewall: fullDeploy,
                log: Console.WriteLine,
                onProgress: null,
                ct: CancellationToken.None).GetAwaiter().GetResult();
        }

        public static async Task<int> ExecuteBuildPipelineAsync(
            string targetDir,
            D3BuildCatalogItem selectedBuild,
            string region,
            string locale,
            int maxConcurrency,
            int dryRunLimit,
            bool binariesOnly,
            bool installPrereqs,
            bool configureShortcutFirewall,
            Action<string> log,
            Action<double, string> onProgress = null,
            CancellationToken ct = default,
            string localArchiveSource = null)
        {
            if (!IsSupportedLocale(locale))
            {
                throw new ArgumentException($"Unsupported locale '{locale}'.", nameof(locale));
            }
            if (string.IsNullOrWhiteSpace(region) || !region.All(char.IsLetterOrDigit))
            {
                throw new ArgumentException($"Invalid region '{region}'.", nameof(region));
            }

            log("========================================================================");
            log(" Diablo III Official Blizzard CDN Downloader & Multi-Version Builder");
            log("========================================================================");
            log($" Selected Build   : {selectedBuild.Version}.{selectedBuild.BuildNumber} ({(selectedBuild.IsCasc ? "NGDP/CASC" : "Legacy MPQ + PTCH")})");
            log($" Target Directory : {targetDir}");
            log($" Region / Locale  : {region} / {locale}");
            log($" Parallel Streams : {maxConcurrency}");
            if (!string.IsNullOrWhiteSpace(localArchiveSource))
            {
                log($" Archive Cache    : {localArchiveSource}");
            }
            if (dryRunLimit > 0)
            {
                log($" Mode             : DRY-RUN (First {dryRunLimit} install + CASC files)");
            }
            else if (binariesOnly)
            {
                log(" Mode             : BINARIES ONLY (Extract & Patch Executables/DLLs)");
            }
            else if (installPrereqs || configureShortcutFirewall)
            {
                log(" Mode             : ALL-IN-ONE FULL DEPLOY");
            }
            else
            {
                log(" Mode             : FULL PRODUCTION INSTALL");
            }
            log("========================================================================");

            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = Math.Max(64, maxConcurrency * 2),
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(20)
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NGDP-D3Downloader/2.0");

            try
            {
                if (installPrereqs && dryRunLimit <= 0)
                {
                    await InstallSystemPrerequisitesAsync();
                }

                if (!selectedBuild.IsCasc)
                {
                    await LegacyMpqInstaller.InstallLegacyMpqBuildAsync(
                        _http,
                        targetDir,
                        selectedBuild,
                        locale,
                        binariesOnly,
                        maxConcurrency,
                        log,
                        onProgress,
                        ct,
                        localArchiveSource);

                    if (configureShortcutFirewall)
                    {
                        ConfigureShortcutAndFirewall(targetDir);
                    }

                    log("========================================================================");
                    log($" SUCCESS: Diablo III {selectedBuild.Version}.{selectedBuild.BuildNumber} completed with 0 errors!");
                    log("========================================================================");
                    return 0;
                }

                if (selectedBuild.RequiresOfflineArchive && string.IsNullOrWhiteSpace(localArchiveSource))
                {
                    log("========================================================================");
                    log($"[ERROR] Diablo III {selectedBuild.Version}.{selectedBuild.BuildNumber} (64-bit CASC) data chunks are retired/offline.");
                    log("        Blizzard purged historical CASC data chunks from the live CDN when Patch 2.8.0 launched.");
                    log("        To install this version, specify an offline archive directory via --archive-cache <path>.");
                    log("========================================================================");
                    return 1;
                }

                Directory.CreateDirectory(targetDir);
                string dataDir = Path.Combine(targetDir, "Data");
                string configDir = Path.Combine(dataDir, "config");
                string indicesDir = Path.Combine(dataDir, "indices");
                string cascDataDir = Path.Combine(dataDir, "data");
                Directory.CreateDirectory(configDir);
                Directory.CreateDirectory(indicesDir);
                Directory.CreateDirectory(cascDataDir);

                onProgress?.Invoke(2.0, $"Initializing Blizzard CASC & NGDP engine ({selectedBuild.ProductCode})...");

                // Step 1: Query NGDP versions & cdns (or initialize historical CASC manifests)
                string prodCode = string.IsNullOrWhiteSpace(selectedBuild?.ProductCode) ? "d3" : selectedBuild.ProductCode;
                Console.WriteLine($"[1/7] Initializing Blizzard NGDP configuration ({prodCode})...");

                VersionRecord versionInfo;
                CdnRecord cdnInfo;

                if (!string.IsNullOrWhiteSpace(selectedBuild.BuildConfigHash))
                {
                    log($"[CASC Hybrid Engine] Historical Build Selected: {selectedBuild.Version}.{selectedBuild.BuildNumber}");
                    log($"[CASC Hybrid Engine] Using verified BuildConfig: {selectedBuild.BuildConfigHash}");

                    versionInfo = new VersionRecord
                    {
                        Region = region,
                        BuildConfig = selectedBuild.BuildConfigHash.ToLowerInvariant(),
                        CdnConfig = !string.IsNullOrWhiteSpace(selectedBuild.CdnConfigHash) ? selectedBuild.CdnConfigHash.ToLowerInvariant() : "6547fc64ca796c7c1b25864145814ace",
                        BuildId = selectedBuild.BuildNumber.ToString(),
                        VersionsName = $"{selectedBuild.Version}.{selectedBuild.BuildNumber}",
                        ProductConfig = ""
                    };

                    try
                    {
                        var cdnsText = await QueryNgdpEndpointAsync("cdns", prodCode);
                        cdnInfo = ParseCdns(cdnsText, region);
                    }
                    catch
                    {
                        cdnInfo = new CdnRecord
                        {
                            Name = region,
                            Path = "tpr/diablo3",
                            Hosts = new[] { "us.cdn.blizzard.com", "level3.blizzard.com", "cdn.blizzard.com", "blzddist1-a.akamaihd.net" },
                            Servers = "https://us.cdn.blizzard.com/?maxhosts=4",
                            ConfigPath = "tpr/diablo3/config"
                        };
                    }
                }
                else
                {
                    var versionsText = await QueryNgdpEndpointAsync("versions", prodCode);
                    var cdnsText = await QueryNgdpEndpointAsync("cdns", prodCode);
                    versionInfo = ParseVersions(versionsText, region);
                    cdnInfo = ParseCdns(cdnsText, region);
                }

                _cdnPath = cdnInfo.Path.Trim('/');
                _cdnConfigPath = string.IsNullOrWhiteSpace(cdnInfo.ConfigPath) ? "tpr/configs/data" : cdnInfo.ConfigPath.Trim('/');
                _cdnHosts = new[] { "us.cdn.blizzard.com", "cdn.blizzard.com", "level3.blizzard.com", "blzddist1-a.akamaihd.net" }
                    .Concat(cdnInfo.Hosts ?? Array.Empty<string>())
                    .Select(h => h?.Trim())
                    .Where(h => IsTrustedCdnHost(h))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (_cdnHosts.Count == 0)
                {
                    _cdnHosts.Add("us.cdn.blizzard.com");
                    _cdnHosts.Add("blzddist1-a.akamaihd.net");
                }

                Console.WriteLine($"      Selected Build : {versionInfo.VersionsName} (Build ID {versionInfo.BuildId})");
                Console.WriteLine($"      BuildConfig    : {versionInfo.BuildConfig}");
                Console.WriteLine($"      CDNConfig      : {versionInfo.CdnConfig}");
                Console.WriteLine($"      CDN Hosts      : {string.Join(", ", _cdnHosts)}");

                onProgress?.Invoke(5.0, "Downloading BuildConfig and CDNConfig manifests...");
                // Step 2: Download BuildConfig, CDNConfig, ProductConfig
                Console.WriteLine("[2/7] Downloading BuildConfig and CDNConfig manifests...");
                byte[] buildCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.BuildConfig);
                
                byte[] cdnCfgBytes;
                try
                {
                    cdnCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.CdnConfig);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      [Note] Historical CDNConfig unavailable ({ex.Message}), falling back to live shared archive catalog...");
                    versionInfo.CdnConfig = "6547fc64ca796c7c1b25864145814ace";
                    cdnCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.CdnConfig);
                }

                if (!string.IsNullOrWhiteSpace(versionInfo.ProductConfig))
                {
                    try
                    {
                        await DownloadConfigFileAsync(configDir, _cdnConfigPath, versionInfo.ProductConfig);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      [Note] Optional ProductConfig skipped: {ex.Message}");
                    }
                }

                var buildCfg = ParseKeyValueConfig(Encoding.UTF8.GetString(buildCfgBytes));
                var cdnCfg = ParseKeyValueConfig(Encoding.UTF8.GetString(cdnCfgBytes));

                if (buildCfg.TryGetValue("patch-config", out string patchCfgHash) && !string.IsNullOrWhiteSpace(patchCfgHash))
                {
                    try
                    {
                        await DownloadConfigFileAsync(configDir, _cdnPath + "/config", patchCfgHash);
                    }
                    catch { }
                }

                string[] archives = cdnCfg.TryGetValue("archives", out string archStr)
                    ? archStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(IsValidHash32).ToArray()
                    : Array.Empty<string>();
                string[] patchArchives = cdnCfg.TryGetValue("patch-archives", out string pArchStr)
                    ? pArchStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(IsValidHash32).ToArray()
                    : Array.Empty<string>();
                archives = await MergeWithSharedArchivesAsync(archives, versionInfo.CdnConfig, prodCode, configDir, log);
                cdnCfg.TryGetValue("archive-group", out string archiveGroupHash);
                cdnCfg.TryGetValue("patch-archive-group", out string patchArchiveGroupHash);
                cdnCfg.TryGetValue("file-index", out string fileIndexHash);
                cdnCfg.TryGetValue("patch-file-index", out string patchFileIndexHash);

                onProgress?.Invoke(10.0, $"Downloading {archives.Length} CDN archive indices...");
                // Step 3: Download CDN archive .index files & build archive-group.index
                Console.WriteLine($"[3/7] Downloading {archives.Length} CDN archive indices...");
                var archiveIndexMap = await DownloadAndParseIndicesAsync(
                    indicesDir,
                    archives,
                    patchArchives,
                    fileIndexHash,
                    patchFileIndexHash,
                    archiveGroupHash,
                    patchArchiveGroupHash,
                    maxConcurrency,
                    dryRunLimit,
                    onProgress);

                Console.WriteLine($"      Indexed {archiveIndexMap.Count:N0} unique BLTE chunks across {archives.Length} CDN archives.");

                onProgress?.Invoke(16.0, "Decoding Encoding, Install, and Download manifests...");
                // Step 4: Download & parse Encoding, Install, and Download manifests
                Console.WriteLine("[4/7] Downloading and decoding Encoding, Install, and Download manifests...");
                string[] encodingHashes = buildCfg["encoding"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string encodingEKey = encodingHashes.Length > 1 ? encodingHashes[1] : encodingHashes[0];
                byte[] rawEncoding = await DownloadDataBlobByEKeyAsync(encodingEKey, archiveIndexMap, archives);
                byte[] decEncoding = BlteDecoder.Decode(rawEncoding);
                var ckeyToEkey = ParseEncodingTableA(decEncoding);
                Console.WriteLine($"      Encoding manifest decoded ({ckeyToEkey.Count:N0} CKey->EKey mappings).");

                string[] installHashes = buildCfg["install"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string installCKey = installHashes[0];
                string installEKey = installHashes.Length > 1 ? installHashes[1] : ckeyToEkey[installCKey];
                byte[] rawInstall = await DownloadDataBlobByEKeyAsync(installEKey, archiveIndexMap, archives);
                byte[] decInstall = BlteDecoder.Decode(rawInstall);
                var installEntries = ParseInstallManifest(decInstall, locale);
                Console.WriteLine($"      Install manifest decoded ({installEntries.Count:N0} Windows+{locale} executable/runtime files).");

                string[] downloadHashes = buildCfg["download"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string downloadCKey = downloadHashes[0];
                string downloadEKey = downloadHashes.Length > 1 ? downloadHashes[1] : ckeyToEkey[downloadCKey];
                byte[] rawDownload = await DownloadDataBlobByEKeyAsync(downloadEKey, archiveIndexMap, archives);
                byte[] decDownload = BlteDecoder.Decode(rawDownload);
                var downloadEntries = ParseDownloadManifest(decDownload, locale);
                Console.WriteLine($"      Download manifest decoded ({downloadEntries.Count:N0} Windows+{locale} CASC data entries).");

                string rootCKey = buildCfg["root"].Trim();
                ckeyToEkey.TryGetValue(rootCKey, out string rootEKey);
                byte[] rawRoot = null;
                if (!string.IsNullOrEmpty(rootEKey))
                {
                    rawRoot = await DownloadDataBlobByEKeyAsync(rootEKey, archiveIndexMap, archives);
                }

                // Step 5: Write .build.info, Launcher.db, and .patch.result
                Console.WriteLine("[5/7] Generating .build.info, Launcher.db, and .patch.result...");
                WriteBuildMetadataFiles(targetDir, region, locale, versionInfo, cdnInfo, installCKey);
                onProgress?.Invoke(20.0, "Extracting game executables and runtime binaries...");

                // Step 6: Extract Install Manifest files (Diablo III64.exe, DLLs, CEF Scene, etc.)
                var installToProcess = dryRunLimit > 0
                    ? installEntries.Take(dryRunLimit).ToList()
                    : installEntries;

                Console.WriteLine($"[6/7] Extracting {installToProcess.Count:N0} game executables & DLLs into installation root...");

                if (binariesOnly)
                {
                    await ExtractInstallFilesAsync(
                        targetDir,
                        installToProcess,
                        ckeyToEkey,
                        archiveIndexMap,
                        archives,
                        maxConcurrency,
                        localArchiveSource,
                        selectedBuild,
                        onProgress: (pct, msg) =>
                        {
                            double mapped = 20.0 + (pct * 0.80);
                            onProgress?.Invoke(mapped, msg);
                        });

                    log("[7/7] Binaries-Only mode selected: skipping CASC data.NNN archive download.");
                    onProgress?.Invoke(100, $"Binaries extraction complete for {selectedBuild.Version}.{selectedBuild.BuildNumber}!");
                }
                else
                {
                    await ExtractInstallFilesAsync(
                        targetDir,
                        installToProcess,
                        ckeyToEkey,
                        archiveIndexMap,
                        archives,
                        maxConcurrency,
                        localArchiveSource,
                        selectedBuild,
                        onProgress: (pct, msg) =>
                        {
                            double mapped = 20.0 + (pct * 0.15);
                            onProgress?.Invoke(mapped, msg);
                        });

                    // Step 7: Build CASC Data\data (data.NNN, 16 .idx files, and shmem)
                    var cascToProcess = dryRunLimit > 0
                        ? downloadEntries.Take(dryRunLimit).ToList()
                        : downloadEntries;

                    log($"[7/7] Building CASC storage in Data\\data ({cascToProcess.Count:N0} entries)...");
                    using (var cascWriter = new CascStorageWriter(cascDataDir))
                    {
                        // Always store system manifests in CASC storage just like Battle.net Agent
                        cascWriter.WriteEntry(ParseHex16(encodingEKey), rawEncoding);
                        cascWriter.WriteEntry(ParseHex16(installEKey), rawInstall);
                        cascWriter.WriteEntry(ParseHex16(downloadEKey), rawDownload);
                        if (rawRoot != null && !string.IsNullOrEmpty(rootEKey))
                        {
                            cascWriter.WriteEntry(ParseHex16(rootEKey), rawRoot);
                        }

                        int missingChunks = await DownloadAndWriteCascEntriesAsync(
                            cascWriter,
                            cascToProcess,
                            archiveIndexMap,
                            archives,
                            maxConcurrency,
                            onProgress: (pct, msg) =>
                            {
                                double mapped = 35.0 + (pct * 0.65);
                                onProgress?.Invoke(mapped, msg);
                            });

                        if (missingChunks > 0)
                        {
                            log($"[WARNING] {missingChunks:N0} game data chunk(s) are no longer hosted by Blizzard and are missing from this build.");
                            log("          The client may hang or crash when it loads the affected assets (sounds/maps/textures).");
                            log("          Tip: install this build from an offline archive (Backup > Assemble from Archive) for a 100% complete copy.");
                        }

                        cascWriter.WriteIndicesAndShmem();
                    }
                }

                // The retail client resolves data through the CDN catalog, so the catalog must only reference
                // artefacts that are actually present locally (Blizzard retired several of them for old builds).
                try
                {
                    NgdpConsistency.Apply(targetDir, log);
                }
                catch (Exception ex)
                {
                    log($"[WARNING] CDN catalog repair failed: {ex.Message}");
                }

                if (configureShortcutFirewall)
                {
                    ConfigureShortcutAndFirewall(targetDir, locale);
                }

                log("========================================================================");
                log(" SUCCESS: Diablo III deployment completed with 0 errors!");
                log("========================================================================");
                onProgress?.Invoke(100, "Diablo III deployment completed with 0 errors!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"[FATAL ERROR] {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static async Task InstallSystemPrerequisitesAsync()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "D3Prereqs_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);
            try
            {
                Console.WriteLine("[Prereq 1/2] Installing Microsoft Visual C++ 2015-2022 Redistributables (x64 & x86)...");
                var vcList = new (string Arch, string Url)[]
                {
                    ("x64", "https://aka.ms/vs/17/release/vc_redist.x64.exe"),
                    ("x86", "https://aka.ms/vs/17/release/vc_redist.x86.exe")
                };

                foreach (var vc in vcList)
                {
                    try
                    {
                        string vcPath = Path.Combine(tempDir, $"vc_redist.{vc.Arch}.exe");
                        byte[] vcBytes = await _http.GetByteArrayAsync(vc.Url);
                        await File.WriteAllBytesAsync(vcPath, vcBytes);
                        using var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = vcPath,
                            Arguments = "/install /quiet /norestart",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        if (p != null) await p.WaitForExitAsync();
                        Console.WriteLine($"             VC++ {vc.Arch} installed (ExitCode: {p?.ExitCode ?? -1}).");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"             [Warn] VC++ {vc.Arch}: {ex.Message}");
                    }
                }

                Console.WriteLine("[Prereq 2/2] Installing DirectX End-User Runtimes...");
                try
                {
                    string dxExe = Path.Combine(tempDir, "directx_Jun2010_redist.exe");
                    string dxExtract = Path.Combine(tempDir, "dx_extract");
                    Directory.CreateDirectory(dxExtract);
                    byte[] dxBytes = await _http.GetByteArrayAsync("https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe");
                    await File.WriteAllBytesAsync(dxExe, dxBytes);

                    using (var pExt = Process.Start(new ProcessStartInfo
                    {
                        FileName = dxExe,
                        Arguments = $"/Q /T:\"{dxExtract}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }))
                    {
                        if (pExt != null) await pExt.WaitForExitAsync();
                    }

                    string dxSetup = Path.Combine(dxExtract, "DXSETUP.exe");
                    if (File.Exists(dxSetup))
                    {
                        using var pSetup = Process.Start(new ProcessStartInfo
                        {
                            FileName = dxSetup,
                            Arguments = "/silent",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        if (pSetup != null) await pSetup.WaitForExitAsync();
                        Console.WriteLine($"             DirectX Runtimes installed (ExitCode: {pSetup?.ExitCode ?? -1}).");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"             [Warn] DirectX Runtimes: {ex.Message}");
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void ConfigureShortcutAndFirewall(string targetDir, string locale = "enUS")
        {
            Console.WriteLine("[Post-Install] Configuring Public Desktop shortcut and Windows Defender Firewall...");
            string exe64 = Path.Combine(targetDir, "x64", "Diablo III64.exe");
            string exe32 = Path.Combine(targetDir, "Diablo III.exe");
            string shortcutTarget = File.Exists(exe64) ? exe64 : exe32;
            string launchLocaleArg = (locale == "allText" || locale == "allLocales") ? "enUS" : locale;

            // Write helper Launch-Game.bat
            try
            {
                string batPath = Path.Combine(targetDir, "Launch-Game.bat");
                string batContent = $"@echo off\r\ncd /d \"%~dp0\"\r\nif exist \"x64\\Diablo III64.exe\" (\r\n    start \"\" \"x64\\Diablo III64.exe\" -launch -locale {launchLocaleArg}\r\n) else (\r\n    start \"\" \"Diablo III.exe\" -launch -locale {launchLocaleArg}\r\n)\r\n";
                File.WriteAllText(batPath, batContent);
            }
            catch { }

            string psCmd = $@"
$ErrorActionPreference = 'SilentlyContinue'
$pub = [Environment]::GetFolderPath('CommonDesktopDirectory')
if (-not $pub) {{ $pub = 'C:\Users\Public\Desktop' }}
$lnk = Join-Path $pub 'Diablo III.lnk'
$wsh = New-Object -ComObject WScript.Shell
$s = $wsh.CreateShortcut($lnk)
$s.TargetPath = '{shortcutTarget.Replace("'", "''")}'
$s.Arguments = '-launch -locale {launchLocaleArg}'
$s.WorkingDirectory = '{targetDir.Replace("'", "''")}'
$s.IconLocation = '{shortcutTarget.Replace("'", "''")},0'
$s.Description = 'Launch Diablo III'
$s.Save()
if (Test-Path '{exe64.Replace("'", "''")}') {{
    Remove-NetFirewallRule -DisplayName 'Diablo III (64-bit)'
    New-NetFirewallRule -DisplayName 'Diablo III (64-bit)' -Direction Inbound -Program '{exe64.Replace("'", "''")}' -Action Allow -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName 'Diablo III (64-bit)' -Direction Outbound -Program '{exe64.Replace("'", "''")}' -Action Allow -Profile Any | Out-Null
}}
if (Test-Path '{exe32.Replace("'", "''")}') {{
    Remove-NetFirewallRule -DisplayName 'Diablo III (32-bit)'
    New-NetFirewallRule -DisplayName 'Diablo III (32-bit)' -Direction Inbound -Program '{exe32.Replace("'", "''")}' -Action Allow -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName 'Diablo III (32-bit)' -Direction Outbound -Program '{exe32.Replace("'", "''")}' -Action Allow -Profile Any | Out-Null
}}
";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psCmd));
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit();
            Console.WriteLine("               Public Desktop shortcut & Firewall rules configured.");
        }

        private static async Task<string> QueryNgdpEndpointAsync(string endpoint, string productCode = "d3")
        {
            Exception lastEx = null;
            foreach (string rawBaseUrl in DefaultPatchHosts)
            {
                string baseUrl = rawBaseUrl.EndsWith("/d3", StringComparison.OrdinalIgnoreCase)
                    ? rawBaseUrl.Substring(0, rawBaseUrl.Length - 3) + "/" + productCode
                    : rawBaseUrl;
                string url = $"{baseUrl}/{endpoint}";
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    string res = await _http.GetStringAsync(url, cts.Token);
                    if (!string.IsNullOrWhiteSpace(res) && res.Contains('|'))
                    {
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }
            throw new IOException($"Failed to query NGDP endpoint '{endpoint}' from all Blizzard patch servers.", lastEx);
        }

        private sealed class VersionRecord
        {
            public string Region;
            public string BuildConfig;
            public string CdnConfig;
            public string KeyRing;
            public string BuildId;
            public string VersionsName;
            public string ProductConfig;
        }

        private sealed class CdnRecord
        {
            public string Name;
            public string Path;
            public string[] Hosts;
            public string Servers;
            public string ConfigPath;
        }

        private static VersionRecord ParseVersions(string content, string targetRegion)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("#"))
                .ToArray();
            if (lines.Length < 2)
                throw new InvalidDataException("Invalid NGDP versions response.");

            string[] headers = lines[0].Split('|').Select(h => h.Split('!')[0]).ToArray();
            VersionRecord fallback = null;

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split('|');
                if (cols.Length < headers.Length) continue;
                var rec = new VersionRecord();
                for (int c = 0; c < headers.Length; c++)
                {
                    switch (headers[c])
                    {
                        case "Region": rec.Region = cols[c]; break;
                        case "BuildConfig": rec.BuildConfig = cols[c].ToLowerInvariant(); break;
                        case "CDNConfig": rec.CdnConfig = cols[c].ToLowerInvariant(); break;
                        case "KeyRing":
                        case "Keyring": rec.KeyRing = cols[c].ToLowerInvariant(); break;
                        case "BuildId": rec.BuildId = cols[c]; break;
                        case "VersionsName":
                        case "VersionName": rec.VersionsName = cols[c]; break;
                        case "ProductConfig": rec.ProductConfig = cols[c].ToLowerInvariant(); break;
                    }
                }
                fallback ??= rec;
                if (string.Equals(rec.Region, targetRegion, StringComparison.OrdinalIgnoreCase))
                    return rec;
            }
            return fallback ?? throw new InvalidDataException("No valid region found in NGDP versions.");
        }

        private static CdnRecord ParseCdns(string content, string targetRegion)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("#"))
                .ToArray();
            if (lines.Length < 2)
                throw new InvalidDataException("Invalid NGDP cdns response.");

            string[] headers = lines[0].Split('|').Select(h => h.Split('!')[0]).ToArray();
            CdnRecord fallback = null;

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split('|');
                if (cols.Length < headers.Length) continue;
                var rec = new CdnRecord();
                for (int c = 0; c < headers.Length; c++)
                {
                    switch (headers[c])
                    {
                        case "Name": rec.Name = cols[c]; break;
                        case "Path": rec.Path = cols[c]; break;
                        case "Hosts": rec.Hosts = cols[c].Split(' ', StringSplitOptions.RemoveEmptyEntries); break;
                        case "Servers": rec.Servers = cols[c]; break;
                        case "ConfigPath": rec.ConfigPath = cols[c]; break;
                    }
                }
                fallback ??= rec;
                if (string.Equals(rec.Name, targetRegion, StringComparison.OrdinalIgnoreCase))
                    return rec;
            }
            return fallback ?? throw new InvalidDataException("No valid region found in NGDP cdns.");
        }

        private static Dictionary<string, string> ParseKeyValueConfig(string text)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                dict[key] = val;
            }
            return dict;
        }

        private static async Task<byte[]> DownloadFromCdnAsync(string relativePath, long? rangeStart = null, long? rangeEnd = null, string expectedMd5Hex = null)
        {
            relativePath = relativePath.TrimStart('/').ToLowerInvariant();
            Exception lastEx = null;
            bool integrityFailure = false;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                bool hostHadTransientFailure = false;
                foreach (string host in _cdnHosts)
                {
                    if (!IsTrustedCdnHost(host))
                    {
                        continue;
                    }

                    string url = $"https://{host}/{relativePath}";
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        if (rangeStart.HasValue && rangeEnd.HasValue)
                        {
                            req.Headers.Range = new RangeHeaderValue(rangeStart.Value, rangeEnd.Value);
                        }
                        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        if (!resp.IsSuccessStatusCode)
                        {
                            lastEx = new HttpRequestException($"HTTP {(int)resp.StatusCode} on {url}");
                            if (!IsDefinitiveNotFound(resp.StatusCode))
                            {
                                hostHadTransientFailure = true;
                            }
                            continue;
                        }
                        byte[] data = await resp.Content.ReadAsByteArrayAsync(cts.Token);

                        if (rangeStart.HasValue && rangeEnd.HasValue)
                        {
                            long expectedRangeLength = rangeEnd.Value - rangeStart.Value + 1;
                            if (data.Length != expectedRangeLength)
                            {
                                lastEx = new InvalidDataException(
                                    $"Range request returned {data.Length} bytes instead of {expectedRangeLength} from {host}.");
                                continue;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(expectedMd5Hex) && !HashMatchesMd5(data, expectedMd5Hex))
                        {
                            integrityFailure = true;
                            lastEx = new InvalidDataException(
                                $"Integrity check failed for '{relativePath}' from {host} (expected MD5 {expectedMd5Hex}).");
                            continue;
                        }

                        return data;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        if (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
                        {
                            hostHadTransientFailure = true;
                        }
                    }
                }

                // Every host answered 404/403/410, so retrying cannot help; fail fast instead of waiting ~4x longer.
                if (!hostHadTransientFailure)
                {
                    break;
                }

                await Task.Delay(250 * (attempt + 1));
            }

            if (integrityFailure)
            {
                throw new InvalidDataException(
                    $"Downloaded data for '{relativePath}' failed integrity verification on every Blizzard CDN host.", lastEx);
            }

            throw new IOException($"Exhausted all Blizzard CDN hosts downloading '{relativePath}'.", lastEx);
        }

        private static bool IsDefinitiveNotFound(HttpStatusCode status) =>
            status == HttpStatusCode.NotFound
            || status == HttpStatusCode.Forbidden
            || status == HttpStatusCode.Gone
            || status == HttpStatusCode.Unauthorized;

        private static async Task<byte[]> DownloadConfigFileAsync(string configRootDir, string cdnPrefix, string hash)
        {
            hash = (hash ?? "").Trim().ToLowerInvariant();
            if (!IsValidHash32(hash))
            {
                throw new InvalidDataException($"Refusing invalid config hash '{hash}'.");
            }

            string subDir = Path.Combine(configRootDir, hash.Substring(0, 2), hash.Substring(2, 2));
            Directory.CreateDirectory(subDir);
            string localPath = Path.Combine(subDir, hash);
            if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                byte[] cached = await File.ReadAllBytesAsync(localPath);
                if (HashMatchesMd5(cached, hash))
                {
                    return cached;
                }

                // Drop a corrupted/stale cache entry and re-download it.
                try { File.Delete(localPath); } catch { }
            }
            string rel = $"{cdnPrefix}/{hash.Substring(0, 2)}/{hash.Substring(2, 2)}/{hash}";
            byte[] data = await DownloadFromCdnAsync(rel, expectedMd5Hex: hash);
            await File.WriteAllBytesAsync(localPath, data);
            return data;
        }

        public readonly struct ArchiveEntryLoc
        {
            public readonly short ArchiveIndex;
            public readonly uint Offset;
            public readonly uint Size;

            public ArchiveEntryLoc(short archiveIndex, uint offset, uint size)
            {
                ArchiveIndex = archiveIndex;
                Offset = offset;
                Size = size;
            }
        }

        private static async Task<Dictionary<string, ArchiveEntryLoc>> DownloadAndParseIndicesAsync(
            string indicesDir,
            string[] archives,
            string[] patchArchives,
            string fileIndexHash,
            string patchFileIndexHash,
            string archiveGroupHash,
            string patchArchiveGroupHash,
            int maxConcurrency,
            int dryRunLimit,
            Action<double, string> onProgress = null)
        {
            using var sem = new SemaphoreSlim(maxConcurrency);
            int completed = 0;
            int total = archives.Length;
            int missingIndices = 0;

            // Download data archive .index files in parallel
            var indexBytesArr = new byte[archives.Length][];
            var tasks = new Task[archives.Length];
            for (int i = 0; i < archives.Length; i++)
            {
                int idx = i;
                string hash = archives[idx].ToLowerInvariant();
                tasks[idx] = Task.Run(async () =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        string localPath = Path.Combine(indicesDir, hash + ".index");
                        byte[] bytes;
                        if (File.Exists(localPath) && new FileInfo(localPath).Length > 28)
                        {
                            bytes = await File.ReadAllBytesAsync(localPath);
                        }
                        else
                        {
                            string rel = $"{_cdnPath}/data/{hash.Substring(0, 2)}/{hash.Substring(2, 2)}/{hash}.index";
                            bytes = await DownloadFromCdnAsync(rel);
                            await File.WriteAllBytesAsync(localPath, bytes);
                        }
                        indexBytesArr[idx] = bytes;
                        int done = Interlocked.Increment(ref completed);
                        if (done % 5 == 0 || done == total)
                        {
                            Console.WriteLine($"      Archive indices: {done}/{total} ({(done * 100.0 / total):F0}%)");
                            onProgress?.Invoke(10.0 + (6.0 * done / total), $"Downloading CDN archive indices: {done}/{total} ({(done * 100.0 / total):F0}%)");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Blizzard has retired a few historical archive indices; skip them instead of aborting the build.
                        Interlocked.Increment(ref missingIndices);
                        indexBytesArr[idx] = null;
                        Console.WriteLine($"      [Notice] Archive index {hash.Substring(0, 12)}... is no longer hosted by Blizzard ({ex.Message}); skipping archive.");
                        int doneSkipped = Interlocked.Increment(ref completed);
                        onProgress?.Invoke(10.0 + (6.0 * doneSkipped / total), $"Downloading CDN archive indices: {doneSkipped}/{total} ({(doneSkipped * 100.0 / total):F0}%)");
                    }
                    finally
                    {
                        sem.Release();
                    }
                });
            }
            await Task.WhenAll(tasks);

            if (total > 0 && missingIndices == total)
            {
                throw new IOException("None of the CDN archive indices for this build are still hosted by Blizzard.");
            }
            if (missingIndices > 0)
            {
                Console.WriteLine($"      [Notice] {missingIndices}/{total} historical archive indices are retired; continuing with {total - missingIndices} live archives.");
            }

            // Download optional file-index, patch-file-index, and patch-archives indices when not in dry-run (or download all since they are tiny)
            if (!string.IsNullOrWhiteSpace(fileIndexHash))
            {
                try { await DownloadIndexFileIfMissingAsync(indicesDir, "data", fileIndexHash); }
                catch (Exception ex) { Console.WriteLine($"      [Notice] Optional data file-index unavailable ({ex.Message})."); }
            }
            if (!string.IsNullOrWhiteSpace(patchFileIndexHash))
            {
                try { await DownloadIndexFileIfMissingAsync(indicesDir, "patch", patchFileIndexHash); }
                catch (Exception ex) { Console.WriteLine($"      [Notice] Optional patch file-index unavailable ({ex.Message})."); }
            }

            var patchIndexBytesArr = new byte[patchArchives.Length][];
            for (int i = 0; i < patchArchives.Length; i++)
            {
                try
                {
                    patchIndexBytesArr[i] = await DownloadIndexFileIfMissingAsync(indicesDir, "patch", patchArchives[i]);
                }
                catch (Exception ex)
                {
                    patchIndexBytesArr[i] = null;
                    Console.WriteLine($"      [Notice] Patch archive index {patchArchives[i].Substring(0, 12)}... unavailable ({ex.Message}); skipping.");
                }
            }

            // Parse all data archive indices in order (0..archives.Length-1)
            var dict = new Dictionary<string, ArchiveEntryLoc>(900000, StringComparer.OrdinalIgnoreCase);
            for (short a = 0; a < archives.Length; a++)
            {
                ParseArchiveIndexBuffer(indexBytesArr[a], a, dict);
            }

            // The archive-group / patch-archive-group files are derived artefacts. They are generated later by
            // NgdpConsistency, which needs the final (possibly pruned) archive list and names the file after
            // the MD5 of its footer, exactly like the retail client does.

            return dict;
        }

        /// <summary>
        /// Historical CDNConfigs only list the archives that existed at release time; Blizzard has since retired
        /// some of them, while the live shared catalog still stores the very same chunks (e.g. the root manifest).
        /// Merging both catalogs keeps historical builds installable.
        /// </summary>
        private static async Task<string[]> MergeWithSharedArchivesAsync(
            string[] archives,
            string buildCdnConfig,
            string productCode,
            string configDir,
            Action<string> log)
        {
            try
            {
                string sharedCdnConfig = null;
                try
                {
                    string versionsText = await QueryNgdpEndpointAsync("versions", productCode);
                    sharedCdnConfig = ParseVersions(versionsText, "us")?.CdnConfig;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      [Notice] Live versions endpoint unavailable ({ex.Message}); using default shared catalog.");
                }

                if (!IsValidHash32(sharedCdnConfig)) sharedCdnConfig = DefaultSharedCdnConfigKey;
                if (string.Equals(sharedCdnConfig, buildCdnConfig, StringComparison.OrdinalIgnoreCase))
                {
                    return archives;
                }

                byte[] sharedCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", sharedCdnConfig);
                var sharedCfg = ParseKeyValueConfig(Encoding.UTF8.GetString(sharedCfgBytes));
                if (!sharedCfg.TryGetValue("archives", out string sharedList))
                {
                    return archives;
                }

                var merged = new List<string>(archives);
                var seen = new HashSet<string>(archives, StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (string h in sharedList.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(IsValidHash32))
                {
                    // ArchiveIndex is persisted as a single byte, so the combined catalog must stay below 256 entries.
                    if (merged.Count >= 255)
                    {
                        break;
                    }
                    if (seen.Add(h))
                    {
                        merged.Add(h);
                        added++;
                    }
                }

                if (added > 0)
                {
                    log($"[CASC Hybrid Engine] Federating {added} live shared archives ({sharedCdnConfig.Substring(0, 8)}...) to recover chunks retired from this build's catalog.");
                }
                return merged.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [Notice] Could not federate the live shared archive catalog ({ex.Message}); continuing with this build's archives only.");
                return archives;
            }
        }

        private static async Task<byte[]> DownloadIndexFileIfMissingAsync(string indicesDir, string folder, string hash)
        {
            hash = (hash ?? "").Trim().ToLowerInvariant();
            if (!IsValidHash32(hash))
            {
                throw new InvalidDataException($"Refusing invalid index hash '{hash}'.");
            }
            string localPath = Path.Combine(indicesDir, hash + ".index");
            if (File.Exists(localPath) && new FileInfo(localPath).Length > 28)
            {
                return await File.ReadAllBytesAsync(localPath);
            }
            string rel = $"{_cdnPath}/{folder}/{hash.Substring(0, 2)}/{hash.Substring(2, 2)}/{hash}.index";
            byte[] bytes = await DownloadFromCdnAsync(rel);
            await File.WriteAllBytesAsync(localPath, bytes);
            return bytes;
        }

        private static void ParseArchiveIndexBuffer(byte[] bytes, short archiveIdx, Dictionary<string, ArchiveEntryLoc> dict)
        {
            if (bytes == null || bytes.Length < 28) return;
            int footerPos = bytes.Length - 28;
            uint numElements = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(footerPos + 16, 4));
            if ((numElements & 0xFF000000u) != 0)
            {
                numElements = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(footerPos + 16, 4));
            }

            uint read = 0;
            for (int b = 0; read < numElements; b++)
            {
                int bStart = b * 4096;
                if (bStart + 4096 > footerPos) break;
                for (int e = 0; e < 170 && read < numElements; e++, read++)
                {
                    int pos = bStart + e * 24;
                    string ekey = Convert.ToHexString(bytes, pos, 16);
                    uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 16, 4));
                    uint offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 20, 4));
                    dict.TryAdd(ekey, new ArchiveEntryLoc(archiveIdx, offset, size));
                }
            }
        }

        /// <summary>
        /// Checks whether every chunk the client can ask for is present in the local storage.
        /// The download (DL) manifest lists the encoded chunks for the Windows + installed locale tags,
        /// which is exactly what the client resolves at runtime, so a missing entry is a real gap.
        /// </summary>
        private static async Task<int> VerifyCascAsync(string installRoot)
        {
            string buildInfoPath = Path.Combine(installRoot, ".build.info");
            string cascDataDir = Path.Combine(installRoot, "Data", "data");
            if (!File.Exists(buildInfoPath))
            {
                Console.Error.WriteLine($"[ERROR] {buildInfoPath} not found.");
                return 1;
            }
            if (!Directory.Exists(cascDataDir))
            {
                Console.Error.WriteLine($"[ERROR] {cascDataDir} not found.");
                return 1;
            }

            string[] rows = File.ReadAllLines(buildInfoPath);
            if (rows.Length < 2)
            {
                Console.Error.WriteLine("[ERROR] .build.info has no data row.");
                return 1;
            }
            string[] cols = rows[1].Split('|');
            string buildKey = cols[2].Trim().ToLowerInvariant();
            string cdnPath = cols.Length > 6 ? cols[6].Trim().Trim('/') : "tpr/diablo3";
            string hosts = cols.Length > 7 ? cols[7].Trim() : "";
            _cdnPath = string.IsNullOrWhiteSpace(cdnPath) ? "tpr/diablo3" : cdnPath;
            _cdnHosts = hosts.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(IsTrustedCdnHost)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_cdnHosts.Count == 0)
            {
                _cdnHosts.Add("us.cdn.blizzard.com");
                _cdnHosts.Add("blzddist1-a.akamaihd.net");
            }

            string cfgPath = Path.Combine(installRoot, "Data", "config", buildKey[..2], buildKey.Substring(2, 2), buildKey);
            if (!File.Exists(cfgPath))
            {
                Console.Error.WriteLine($"[ERROR] Build config {buildKey} is missing from Data/config.");
                return 1;
            }

            string locale = "enUS";
            string launcherDb = Path.Combine(installRoot, "Launcher.db");
            if (File.Exists(launcherDb))
            {
                string text = File.ReadAllText(launcherDb).Trim();
                if (text.Length > 0 && text.Length < 16) locale = text;
            }

            var buildCfg = ParseKeyValueConfig(File.ReadAllText(cfgPath));
            if (!buildCfg.TryGetValue("download", out string downloadField))
            {
                Console.Error.WriteLine("[ERROR] Build config has no download manifest reference.");
                return 1;
            }
            string[] downloadHashes = downloadField.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string ekey = downloadHashes.Length > 1 ? downloadHashes[1].ToLowerInvariant() : downloadHashes[0].ToLowerInvariant();

            // Manifests are plain CDN files: locate them through every archive index already on disk.
            // Group indexes use a wider record (footer byte 12 == 5) and must not be treated as archives.
            var archiveIndexMap = new Dictionary<string, ArchiveEntryLoc>(StringComparer.OrdinalIgnoreCase);
            var archiveHashes = new List<string>();
            foreach (string idxFile in Directory.GetFiles(Path.Combine(installRoot, "Data", "indices"), "*.index"))
            {
                byte[] idxBytes = File.ReadAllBytes(idxFile);
                if (idxBytes.Length < 40 || idxBytes[^16] != 4)
                {
                    continue;
                }
                archiveHashes.Add(Path.GetFileNameWithoutExtension(idxFile));
            }
            string[] archives = archiveHashes.ToArray();
            for (short a = 0; a < archives.Length; a++)
            {
                ParseArchiveIndexBuffer(
                    File.ReadAllBytes(Path.Combine(installRoot, "Data", "indices", archives[a] + ".index")), a, archiveIndexMap);
            }
            Console.WriteLine($"      Resolved {archiveIndexMap.Count:N0} chunk locations from {archives.Length} local archive indices.");

            Console.WriteLine($"      Fetching download manifest {ekey.Substring(0, 8)}...");
            byte[] rawDl = null;
            byte[] localRecord = ReadRecordFromStorage(cascDataDir, ekey);
            if (localRecord != null)
            {
                Console.WriteLine("      Read the download manifest from the local storage (no CDN needed).");
                rawDl = localRecord;
            }
            else
            {
                rawDl = await DownloadDataBlobByEKeyAsync(ekey, archiveIndexMap, archives);
            }
            byte[] decDl = BlteDecoder.Decode(rawDl);
            List<DownloadItem> items = ParseDownloadManifest(decDl, locale);

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string idxFile in Directory.GetFiles(cascDataDir, "*.idx"))
            {
                byte[] idx = File.ReadAllBytes(idxFile);
                if (idx.Length < 40) continue;
                uint entriesLen = BinaryPrimitives.ReadUInt32LittleEndian(idx.AsSpan(0x20, 4));
                int count = (int)Math.Min(entriesLen / 18u, (uint)((idx.Length - 40) / 18));
                for (int n = 0; n < count; n++)
                {
                    present.Add(Convert.ToHexString(idx, 40 + n * 18, 9));
                }
            }

            var missing = new List<string>();
            foreach (var item in items)
            {
                if (!present.Contains(item.EKeyHex[..18]))
                {
                    missing.Add(item.EKeyHex);
                }
            }

            Console.WriteLine($"      Storage records      : {present.Count:N0}");
            Console.WriteLine($"      Chunks required ({locale}): {items.Count:N0}");
            if (missing.Count == 0)
            {
                Console.WriteLine("[OK] CASC storage is COMPLETE: every chunk the client can request is present.");
                return 0;
            }

            long missingBytes = 0;
            foreach (var item in items)
            {
                if (missing.Contains(item.EKeyHex)) missingBytes += item.CompressedSize;
            }
            Console.WriteLine($"[WARNING] {missing.Count:N0} chunk(s) ({missingBytes / 1048576.0:F1} MB) are MISSING:");
            foreach (string key in missing.Take(10))
            {
                Console.WriteLine($"          {key}");
            }
            if (missing.Count > 10)
            {
                Console.WriteLine($"          ... and {missing.Count - 10:N0} more");
            }
            return 2;
        }

        /// <summary>
        /// Reads a BLTE chunk payload straight out of the local storage, without touching the CDN.
        /// </summary>
        private static byte[] ReadRecordFromStorage(string cascDataDir, string ekeyHex)
        {
            try
            {
                byte[] ekey = ParseHex16(ekeyHex);
                if (ekey == null || ekey.Length != 16) return null;
                byte bucket = CascHash.GetBucketIndex(ekey.AsSpan(0, 9));
                foreach (string idxFile in Directory.GetFiles(cascDataDir, $"{bucket:x2}*.idx"))
                {
                    byte[] idx = File.ReadAllBytes(idxFile);
                    if (idx.Length < 40) continue;
                    uint entriesLen = BinaryPrimitives.ReadUInt32LittleEndian(idx.AsSpan(0x20, 4));
                    int count = (int)Math.Min(entriesLen / 18u, (uint)((idx.Length - 40) / 18));
                    for (int n = 0; n < count; n++)
                    {
                        int p = 40 + n * 18;
                        if (!idx.AsSpan(p, 9).SequenceEqual(ekey.AsSpan(0, 9))) continue;

                        ulong field = 0;
                        for (int b = 0; b < 5; b++) field = (field << 8) | idx[p + 9 + b];
                        uint archive = (uint)(field >> 30);
                        uint offset = (uint)(field & 0x3FFFFFFFu);
                        uint size = BinaryPrimitives.ReadUInt32LittleEndian(idx.AsSpan(p + 14, 4));
                        if (size < 30) return null;

                        string dataPath = Path.Combine(cascDataDir, $"data.{archive:D3}");
                        if (!File.Exists(dataPath)) return null;
                        using var stream = File.OpenRead(dataPath);
                        stream.Position = offset;
                        byte[] buffer = new byte[size];
                        int read = 0;
                        while (read < buffer.Length)
                        {
                            int chunk = stream.Read(buffer, read, buffer.Length - read);
                            if (chunk <= 0) return null;
                            read += chunk;
                        }
                        return buffer.AsSpan(30).ToArray();
                    }
                }
            }
            catch
            {
                // Fall back to the CDN path when the local storage cannot satisfy the lookup.
            }
            return null;
        }

        private static int CompareBytes(byte[] a, byte[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                int cmp = a[i].CompareTo(b[i]);
                if (cmp != 0) return cmp;
            }
            return a.Length.CompareTo(b.Length);
        }

        private static async Task<byte[]> DownloadDataBlobByEKeyAsync(
            string ekey,
            Dictionary<string, ArchiveEntryLoc> archiveIndexMap,
            string[] archives)
        {
            ekey = ekey.ToUpperInvariant();
            if (archiveIndexMap.TryGetValue(ekey, out ArchiveEntryLoc loc))
            {
                string archHash = archives[loc.ArchiveIndex].ToLowerInvariant();
                string rel = $"{_cdnPath}/data/{archHash.Substring(0, 2)}/{archHash.Substring(2, 2)}/{archHash}";
                return await DownloadFromCdnAsync(rel, loc.Offset, loc.Offset + loc.Size - 1);
            }
            else
            {
                string lower = ekey.ToLowerInvariant();
                string rel = $"{_cdnPath}/data/{lower.Substring(0, 2)}/{lower.Substring(2, 2)}/{lower}";
                return await DownloadFromCdnAsync(rel);
            }
        }

        private static Dictionary<string, string> ParseEncodingTableA(byte[] decEncoding)
        {
            if (decEncoding.Length < 22 || decEncoding[0] != (byte)'E' || decEncoding[1] != (byte)'N')
                throw new InvalidDataException("Invalid Encoding (EN) manifest header.");

            byte hashSizeA = decEncoding[3];
            ushort pageSizeAKB = BinaryPrimitives.ReadUInt16BigEndian(decEncoding.AsSpan(5, 2));
            uint numPagesA = BinaryPrimitives.ReadUInt32BigEndian(decEncoding.AsSpan(9, 4));
            uint stringBlockSize = BinaryPrimitives.ReadUInt32BigEndian(decEncoding.AsSpan(18, 4));

            int pos = 22 + (int)stringBlockSize + (int)(numPagesA * 32);
            int pageSizeBytes = pageSizeAKB * 1024;
            var map = new Dictionary<string, string>((int)numPagesA * 100, StringComparer.OrdinalIgnoreCase);

            for (uint p = 0; p < numPagesA; p++)
            {
                int pageStart = pos + (int)(p * pageSizeBytes);
                int cur = pageStart;
                int pageEnd = pageStart + pageSizeBytes;

                while (cur + 6 + hashSizeA <= pageEnd)
                {
                    ushort keyCount = BinaryPrimitives.ReadUInt16LittleEndian(decEncoding.AsSpan(cur, 2));
                    if (keyCount == 0) break;
                    // uint32 BE file size at cur + 2
                    cur += 6;
                    string ckey = Convert.ToHexString(decEncoding, cur, hashSizeA);
                    cur += hashSizeA;
                    if (cur + keyCount * hashSizeA > pageEnd) break;
                    string firstEKey = Convert.ToHexString(decEncoding, cur, hashSizeA);
                    cur += keyCount * hashSizeA;
                    map.TryAdd(ckey, firstEKey);
                }
            }
            return map;
        }

        private sealed class InstallItem
        {
            public string RelativePath;
            public string CKey;
            public uint UncompressedSize;
        }

        private static List<InstallItem> ParseInstallManifest(byte[] decInstall, string targetLocale)
        {
            if (decInstall.Length < 10 || decInstall[0] != (byte)'I' || decInstall[1] != (byte)'N')
                throw new InvalidDataException("Invalid Install (IN) manifest header.");

            byte hashSize = decInstall[3];
            ushort numTags = BinaryPrimitives.ReadUInt16BigEndian(decInstall.AsSpan(4, 2));
            uint numEntries = BinaryPrimitives.ReadUInt32BigEndian(decInstall.AsSpan(6, 4));
            int bytesPerTag = ((int)numEntries + 7) / 8;

            int pos = 10;
            byte[] winMask = null;
            byte[] locMask = null;

            for (int t = 0; t < numTags; t++)
            {
                int s = pos;
                while (decInstall[pos] != 0) pos++;
                string tagName = Encoding.UTF8.GetString(decInstall, s, pos - s);
                pos++;
                ushort tagType = BinaryPrimitives.ReadUInt16BigEndian(decInstall.AsSpan(pos, 2));
                pos += 2;
                byte[] mask = new byte[bytesPerTag];
                Buffer.BlockCopy(decInstall, pos, mask, 0, bytesPerTag);
                pos += bytesPerTag;

                if (tagName.Equals("Windows", StringComparison.OrdinalIgnoreCase)) winMask = mask;
                if (tagName.Equals(targetLocale, StringComparison.OrdinalIgnoreCase)) locMask = mask;
            }

            var result = new List<InstallItem>();
            for (int i = 0; i < numEntries; i++)
            {
                int s = pos;
                while (decInstall[pos] != 0) pos++;
                string name = Encoding.UTF8.GetString(decInstall, s, pos - s);
                pos++;
                string ckey = Convert.ToHexString(decInstall, pos, hashSize);
                pos += hashSize;
                uint size = BinaryPrimitives.ReadUInt32BigEndian(decInstall.AsSpan(pos, 4));
                pos += 4;

                int byteIdx = i >> 3;
                int bitMask = 0x80 >> (i & 7);
                bool isWin = winMask == null || (winMask[byteIdx] & bitMask) != 0;
                bool isLoc = locMask == null || (locMask[byteIdx] & bitMask) != 0;

                if (isWin && isLoc)
                {
                    result.Add(new InstallItem
                    {
                        RelativePath = name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar),
                        CKey = ckey,
                        UncompressedSize = size
                    });
                }
            }
            return result;
        }

        public readonly struct DownloadItem
        {
            public readonly string EKeyHex;
            public readonly long CompressedSize;
            public readonly byte Priority;

            public DownloadItem(string ekeyHex, long compressedSize, byte priority)
            {
                EKeyHex = ekeyHex;
                CompressedSize = compressedSize;
                Priority = priority;
            }
        }

        private static List<DownloadItem> ParseDownloadManifest(byte[] decDl, string targetLocale)
        {
            if (decDl.Length < 11 || decDl[0] != (byte)'D' || decDl[1] != (byte)'L')
                throw new InvalidDataException("Invalid Download (DL) manifest header.");

            byte version = decDl[2];
            byte hashSize = decDl[3];
            byte hasChecksum = decDl[4];
            uint numEntries = BinaryPrimitives.ReadUInt32BigEndian(decDl.AsSpan(5, 4));
            ushort numTags = BinaryPrimitives.ReadUInt16BigEndian(decDl.AsSpan(9, 2));

            int headerLen = 11;
            byte flagSize = 0;
            if (version >= 2)
            {
                flagSize = decDl[11];
                headerLen = 12;
                if (version >= 3) headerLen = 16;
            }

            int entryStride = hashSize + 5 + 1 + (hasChecksum != 0 ? 4 : 0) + flagSize;
            int tagsStart = headerLen + (int)numEntries * entryStride;
            int bytesPerTag = ((int)numEntries + 7) / 8;

            int pos = tagsStart;
            byte[] winMask = null;
            byte[] locMask = null;

            for (int t = 0; t < numTags; t++)
            {
                int s = pos;
                while (decDl[pos] != 0) pos++;
                string tagName = Encoding.UTF8.GetString(decDl, s, pos - s);
                pos++;
                ushort tagType = BinaryPrimitives.ReadUInt16BigEndian(decDl.AsSpan(pos, 2));
                pos += 2;
                byte[] mask = new byte[bytesPerTag];
                Buffer.BlockCopy(decDl, pos, mask, 0, bytesPerTag);
                pos += bytesPerTag;

                if (tagName.Equals("Windows", StringComparison.OrdinalIgnoreCase)) winMask = mask;
                if (tagName.Equals(targetLocale, StringComparison.OrdinalIgnoreCase)) locMask = mask;
            }

            var list = new List<DownloadItem>((int)numEntries / 2);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < numEntries; i++)
            {
                int byteIdx = i >> 3;
                int bitMask = 0x80 >> (i & 7);
                bool isWin = winMask == null || (winMask[byteIdx] & bitMask) != 0;
                bool isLoc = locMask == null || (locMask[byteIdx] & bitMask) != 0;
                if (!isWin || !isLoc) continue;

                int ePos = headerLen + i * entryStride;
                string ekey = Convert.ToHexString(decDl, ePos, hashSize);
                if (!seen.Add(ekey)) continue;

                long sz = ((long)decDl[ePos + hashSize] << 32)
                        | ((long)decDl[ePos + hashSize + 1] << 24)
                        | ((long)decDl[ePos + hashSize + 2] << 16)
                        | ((long)decDl[ePos + hashSize + 3] << 8)
                        | decDl[ePos + hashSize + 4];
                byte prio = decDl[ePos + hashSize + 5];
                list.Add(new DownloadItem(ekey, sz, prio));
            }
            return list;
        }

        private static void WriteBuildMetadataFiles(
            string targetDir,
            string region,
            string locale,
            VersionRecord ver,
            CdnRecord cdn,
            string installCKey)
        {
            string buildInfoPath = Path.Combine(targetDir, ".build.info");
            string hostsJoined = string.Join(" ", (cdn.Hosts ?? Array.Empty<string>()).Where(IsTrustedCdnHost));
            string serversJoined = string.IsNullOrWhiteSpace(cdn.Servers)
                ? "https://level3.blizzard.com/?maxhosts=4 https://us.cdn.blizzard.com/?maxhosts=4 https://level3.ssl.blizzard.com/?maxhosts=4&fallback=1 https://us.cdn.blizzard.com/?maxhosts=4&fallback=1"
                : cdn.Servers;
            string tags = $"Windows {region.ToUpperInvariant()}? acct-USA? geoip-US? {locale} speech?:Windows {region.ToUpperInvariant()}? acct-USA? geoip-US? {locale} text?";

            var sb = new StringBuilder();
            sb.AppendLine("Branch!STRING:0|Active!DEC:1|Build Key!HEX:16|CDN Key!HEX:16|Install Key!HEX:16|IM Size!DEC:4|CDN Path!STRING:0|CDN Hosts!STRING:0|CDN Servers!STRING:0|Tags!STRING:0|Armadillo!STRING:0|Last Activated!STRING:0|Version!STRING:0|KeyRing!HEX:16|Product!STRING:0");
            // Blizzard leaves "Install Key" empty in locally deployed storages; the client validates it
            // during storage open and rejects the storage (E_STORAGE_INIT) when it is filled in.
            sb.AppendLine($"{region}|1|{ver.BuildConfig}|{ver.CdnConfig}|||{cdn.Path}|{hostsJoined}|{serversJoined}|{tags}|||{ver.VersionsName}|{ver.KeyRing}|");
            File.WriteAllText(buildInfoPath, sb.ToString(), Encoding.ASCII);

            string launcherDbPath = Path.Combine(targetDir, "Launcher.db");
            File.WriteAllText(launcherDbPath, locale, Encoding.ASCII);

            string patchResultPath = Path.Combine(targetDir, ".patch.result");
            File.WriteAllText(patchResultPath, "0", Encoding.ASCII);
        }

        private static async Task ExtractInstallFilesAsync(
            string targetDir,
            List<InstallItem> items,
            Dictionary<string, string> ckeyToEkey,
            Dictionary<string, ArchiveEntryLoc> archiveIndexMap,
            string[] archives,
            int maxConcurrency,
            string localArchiveSource = null,
            D3BuildCatalogItem selectedBuild = null,
            Action<double, string> onProgress = null)
        {
            using var sem = new SemaphoreSlim(maxConcurrency);
            int completed = 0;
            long bytesDone = 0;
            long totalBytes = items.Sum(x => (long)x.UncompressedSize);
            var sw = Stopwatch.StartNew();

            var tasks = items.Select(async item =>
            {
                await sem.WaitAsync();
                try
                {
                    string outPath = SafePathUnder(targetDir, item.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                    if (File.Exists(outPath) && new FileInfo(outPath).Length == item.UncompressedSize)
                    {
                        Interlocked.Add(ref bytesDone, item.UncompressedSize);
                        int curSkipped = Interlocked.Increment(ref completed);
                        double pct = (curSkipped * 100.0) / Math.Max(1, items.Count);
                        onProgress?.Invoke(pct, $"Verifying: {curSkipped}/{items.Count} files ({(curSkipped * 100.0 / items.Count):F0}%)");
                        return;
                    }

                    if (!ckeyToEkey.TryGetValue(item.CKey, out string ekey))
                    {
                        throw new KeyNotFoundException($"Install file '{item.RelativePath}' CKey {item.CKey} not found in Encoding.");
                    }

                    byte[] rawBlte = null;
                    try
                    {
                        rawBlte = await DownloadDataBlobByEKeyAsync(ekey, archiveIndexMap, archives);
                    }
                    catch (Exception ex)
                    {
                        rawBlte = await TryRecoverInstallFileBytesAsync(item, ekey, targetDir, localArchiveSource, selectedBuild);
                        if (rawBlte == null)
                        {
                            throw new FileNotFoundException($"Cannot recover required binary '{item.RelativePath}' (CKey: {item.CKey}): CDN returned error ({ex.Message}) and no local or delta fallback was found.");
                        }
                    }

                    byte[] decoded;
                    if (rawBlte.Length >= 4 && rawBlte[0] == (byte)'B' && rawBlte[1] == (byte)'L' && rawBlte[2] == (byte)'T' && rawBlte[3] == (byte)'E')
                    {
                        decoded = BlteDecoder.Decode(rawBlte);
                    }
                    else
                    {
                        decoded = rawBlte;
                    }

                    await File.WriteAllBytesAsync(outPath, decoded);

                    long curBytes = Interlocked.Add(ref bytesDone, decoded.Length);
                    int curFiles = Interlocked.Increment(ref completed);
                    double curPct = (curFiles * 100.0) / Math.Max(1, items.Count);
                    double mbps = (curBytes / 1048576.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                    string msg = $"Extracting: {curFiles}/{items.Count} files | {curBytes / 1048576.0:F1}/{totalBytes / 1048576.0:F1} MB ({curPct:F1}%) | {mbps:F1} MB/s";
                    if (curFiles % 5 == 0 || curFiles == items.Count)
                    {
                        Console.WriteLine($"      [Install] {curFiles}/{items.Count} files | {curBytes / 1048576.0:F1}/{totalBytes / 1048576.0:F1} MB ({curPct:F1}%) | {mbps:F1} MB/s");
                    }
                    onProgress?.Invoke(curPct, msg);
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private static async Task<byte[]> TryRecoverInstallFileBytesAsync(InstallItem item, string ekey, string targetDir, string localArchiveSource, D3BuildCatalogItem selectedBuild)
        {
            string normalizedRelPath = item.RelativePath.Replace('\\', '/');

            // 1. Try localArchiveSource if specified
            if (!string.IsNullOrWhiteSpace(localArchiveSource) && Directory.Exists(localArchiveSource))
            {
                string candidate = TrySafePathUnder(localArchiveSource, item.RelativePath);
                if (candidate != null && File.Exists(candidate))
                {
                    byte[] b = await File.ReadAllBytesAsync(candidate);
                    if (VerifyFileCKey(b, item.CKey)) return b;
                }

                // Check for Option B binary package zip in local archive source
                byte[] fromArchiveZip = TryExtractFileFromDirectoryZips(localArchiveSource, normalizedRelPath, item.CKey, selectedBuild);
                if (fromArchiveZip != null) return fromArchiveZip;
            }

            // 2. Check local application cache directory for Option B binary packages
            string appCacheDir = Path.Combine(AppContext.BaseDirectory, "cache");
            if (Directory.Exists(appCacheDir))
            {
                byte[] fromCacheZip = TryExtractFileFromDirectoryZips(appCacheDir, normalizedRelPath, item.CKey, selectedBuild);
                if (fromCacheZip != null) return fromCacheZip;
            }

            // 3. If selectedBuild has a BinaryDeltaUrl configured, download Option B zip package on demand
            if (selectedBuild != null
                && Uri.TryCreate(selectedBuild.BinaryDeltaUrl, UriKind.Absolute, out Uri deltaUri)
                && deltaUri.Scheme == Uri.UriSchemeHttps)
            {
                try
                {
                    Directory.CreateDirectory(appCacheDir);
                    string zipTarget = Path.Combine(appCacheDir, $"d3-{selectedBuild.BuildNumber}-binaries.zip");
                    if (!File.Exists(zipTarget))
                    {
                        Console.WriteLine($"      [Option B] Downloading verified clean binary package from {selectedBuild.BinaryDeltaUrl}...");
                        using var client = new HttpClient();
                        byte[] zipBytes = await client.GetByteArrayAsync(deltaUri);
                        await File.WriteAllBytesAsync(zipTarget, zipBytes);
                        Console.WriteLine($"      [Option B] Binary package cached ({zipBytes.Length / 1048576.0:F1} MB).");
                    }

                    byte[] fromDlZip = TryExtractFileFromZip(zipTarget, normalizedRelPath, item.CKey);
                    if (fromDlZip != null) return fromDlZip;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      [Option B] Binary delta download failed ({ex.Message}).");
                }
            }

            // 4. Try known installed Diablo III paths for static shared runtime libraries (e.g. icudt57.dll)
            string[] knownInstalledDirs = new[]
            {
                @"C:\Program Files (x86)\Diablo III",
                @"D:\Games\Diablo III",
                @"C:\Games\Diablo III",
                @"D:\Diablo III"
            };

            foreach (var dir in knownInstalledDirs)
            {
                if (Directory.Exists(dir))
                {
                    string candidate = TrySafePathUnder(dir, item.RelativePath);
                    if (candidate != null && File.Exists(candidate))
                    {
                        byte[] b = await File.ReadAllBytesAsync(candidate);
                        if (VerifyFileCKey(b, item.CKey))
                        {
                            Console.WriteLine($"      [Rescue] Reused verified runtime component from {candidate} (CKey match: {item.CKey})");
                            return b;
                        }
                    }
                }
            }

            return null;
        }

        private static byte[] TryExtractFileFromDirectoryZips(string dir, string normalizedRelPath, string expectedCKey, D3BuildCatalogItem selectedBuild)
        {
            var candidates = new List<string>();
            if (selectedBuild != null)
            {
                candidates.Add(Path.Combine(dir, $"d3-{selectedBuild.BuildNumber}-binaries.zip"));
                candidates.Add(Path.Combine(dir, $"d3-{selectedBuild.Version}.{selectedBuild.BuildNumber}-binaries.zip"));
                candidates.Add(Path.Combine(dir, $"d3-{selectedBuild.BuildNumber}.zip"));
            }
            candidates.Add(Path.Combine(dir, "binaries.zip"));

            foreach (var zipPath in candidates)
            {
                if (File.Exists(zipPath))
                {
                    byte[] data = TryExtractFileFromZip(zipPath, normalizedRelPath, expectedCKey);
                    if (data != null) return data;
                }
            }

            return null;
        }

        private static byte[] TryExtractFileFromZip(string zipPath, string normalizedRelPath, string expectedCKey)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    string entryNorm = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (entryNorm.Equals(normalizedRelPath, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(entryNorm).Equals(Path.GetFileName(normalizedRelPath), StringComparison.OrdinalIgnoreCase))
                    {
                        using var ms = new MemoryStream();
                        using var s = entry.Open();
                        s.CopyTo(ms);
                        byte[] b = ms.ToArray();
                        if (VerifyFileCKey(b, expectedCKey))
                        {
                            Console.WriteLine($"      [Option B] Extracted verified {normalizedRelPath} from {Path.GetFileName(zipPath)} (CKey match: {expectedCKey})");
                            return b;
                        }
                    }
                }
            }
            catch
            {
                // Ignore malformed zip
            }

            return null;
        }

        private static bool VerifyFileCKey(byte[] content, string expectedCKeyHex)
        {
            if (string.IsNullOrWhiteSpace(expectedCKeyHex)) return true;
            using var md5 = MD5.Create();
            string hash = Convert.ToHexString(md5.ComputeHash(content));
            return string.Equals(hash, expectedCKeyHex, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ArchiveSlice : IComparable<ArchiveSlice>
        {
            public string EKeyHex;
            public byte[] EKeyBytes;
            public uint Offset;
            public uint Size;

            public int CompareTo(ArchiveSlice other) => Offset.CompareTo(other.Offset);
        }

        private sealed class CoalescedRangeJob
        {
            public short ArchiveIndex;
            public uint RangeStart;
            public uint RangeEnd;
            public List<ArchiveSlice> Slices = new();
        }

        private static async Task<int> DownloadAndWriteCascEntriesAsync(
            CascStorageWriter cascWriter,
            List<DownloadItem> items,
            Dictionary<string, ArchiveEntryLoc> archiveIndexMap,
            string[] archives,
            int maxConcurrency,
            Action<double, string> onProgress = null)
        {
            int skippedChunks = 0;
            var perArchive = new List<ArchiveSlice>[archives.Length];
            for (int i = 0; i < archives.Length; i++) perArchive[i] = new List<ArchiveSlice>();
            var looseItems = new List<DownloadItem>();

            long totalExpectedBytes = 0;
            foreach (var item in items)
            {
                if (archiveIndexMap.TryGetValue(item.EKeyHex, out ArchiveEntryLoc loc))
                {
                    perArchive[loc.ArchiveIndex].Add(new ArchiveSlice
                    {
                        EKeyHex = item.EKeyHex,
                        EKeyBytes = ParseHex16(item.EKeyHex),
                        Offset = loc.Offset,
                        Size = loc.Size
                    });
                    totalExpectedBytes += loc.Size;
                }
                else
                {
                    looseItems.Add(item);
                    totalExpectedBytes += item.CompressedSize;
                }
            }

            // Build coalesced range jobs (gap <= 32 KB, max range <= 16 MB)
            var rangeJobs = new List<CoalescedRangeJob>();
            const uint maxGap = 32768;
            const uint maxRangeSize = 16 * 1024 * 1024;

            for (short a = 0; a < archives.Length; a++)
            {
                var list = perArchive[a];
                if (list.Count == 0) continue;
                list.Sort();

                var currentJob = new CoalescedRangeJob
                {
                    ArchiveIndex = a,
                    RangeStart = list[0].Offset,
                    RangeEnd = list[0].Offset + list[0].Size - 1
                };
                currentJob.Slices.Add(list[0]);

                for (int i = 1; i < list.Count; i++)
                {
                    var slice = list[i];
                    uint sliceEnd = slice.Offset + slice.Size - 1;
                    if (slice.Offset <= currentJob.RangeEnd + 1 + maxGap &&
                        (sliceEnd - currentJob.RangeStart + 1) <= maxRangeSize)
                    {
                        if (sliceEnd > currentJob.RangeEnd)
                            currentJob.RangeEnd = sliceEnd;
                        currentJob.Slices.Add(slice);
                    }
                    else
                    {
                        rangeJobs.Add(currentJob);
                        currentJob = new CoalescedRangeJob
                        {
                            ArchiveIndex = a,
                            RangeStart = slice.Offset,
                            RangeEnd = sliceEnd
                        };
                        currentJob.Slices.Add(slice);
                    }
                }
                rangeJobs.Add(currentJob);
            }

            Console.WriteLine($"      Coalesced {items.Count - looseItems.Count:N0} archived chunks into {rangeJobs.Count:N0} HTTP Range requests + {looseItems.Count:N0} loose files ({totalExpectedBytes / 1048576.0:F1} MB total).");

            using var sem = new SemaphoreSlim(maxConcurrency);
            long downloadedBytes = 0;
            int completedFiles = 0;
            int totalFiles = items.Count;
            var sw = Stopwatch.StartNew();
            long lastLogTick = 0;

            void ReportProgress(int filesAdded, long bytesAdded, bool force = false)
            {
                int f = Interlocked.Add(ref completedFiles, filesAdded);
                long b = Interlocked.Add(ref downloadedBytes, bytesAdded);
                long now = sw.ElapsedMilliseconds;
                long prev = Interlocked.Read(ref lastLogTick);
                if (force || f == totalFiles || (now - prev >= 300 && Interlocked.CompareExchange(ref lastLogTick, now, prev) == prev))
                {
                    double pct = totalExpectedBytes > 0 ? (b * 100.0 / totalExpectedBytes) : 100.0;
                    double mbps = (b / 1048576.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                    string msg = $"Downloading CASC data: {pct:F1}% | {b / 1048576.0:F1}/{totalExpectedBytes / 1048576.0:F1} MB | {mbps:F1} MB/s";
                    Console.WriteLine($"      [CASC] {pct,6:F2}% | {b / 1048576.0,8:F1} / {totalExpectedBytes / 1048576.0,8:F1} MB | {f,7:N0} / {totalFiles:N0} files | {mbps,6:F1} MB/s");
                    onProgress?.Invoke(pct, msg);
                }
            }

            // Execute coalesced archive range jobs
            var rangeTasks = rangeJobs.Select(async job =>
            {
                await sem.WaitAsync();
                try
                {
                    string archHash = archives[job.ArchiveIndex].ToLowerInvariant();
                    string rel = $"{_cdnPath}/data/{archHash.Substring(0, 2)}/{archHash.Substring(2, 2)}/{archHash}";

                    byte[] rangeBuf;
                    try
                    {
                        rangeBuf = await DownloadFromCdnAsync(rel, job.RangeStart, job.RangeEnd);
                    }
                    catch (Exception ex)
                    {
                        // A retired archive no longer serves its chunks; keep assembling the rest of the CASC storage.
                        Console.WriteLine($"      [Notice] Archive {archHash.Substring(0, 12)}... unavailable ({ex.Message}); skipped {job.Slices.Count} chunk(s).");
                        Interlocked.Add(ref skippedChunks, job.Slices.Count);
                        ReportProgress(job.Slices.Count, 0);
                        return;
                    }

                    long logicalBytes = 0;
                    foreach (var slice in job.Slices)
                    {
                        int relOffset = (int)(slice.Offset - job.RangeStart);
                        if (relOffset < 0 || relOffset + (int)slice.Size > rangeBuf.Length)
                        {
                            Console.WriteLine($"      [Notice] Incomplete archive response for chunk {slice.EKeyHex}; skipped.");
                            Interlocked.Increment(ref skippedChunks);
                            continue;
                        }
                        cascWriter.WriteEntry(slice.EKeyBytes, rangeBuf, relOffset, (int)slice.Size);
                        logicalBytes += slice.Size;
                    }
                    ReportProgress(job.Slices.Count, logicalBytes);
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(rangeTasks);

            // Execute loose file downloads
            var looseTasks = looseItems.Select(async item =>
            {
                await sem.WaitAsync();
                try
                {
                    string lower = item.EKeyHex.ToLowerInvariant();
                    string rel = $"{_cdnPath}/data/{lower.Substring(0, 2)}/{lower.Substring(2, 2)}/{lower}";
                    try
                    {
                        byte[] buf = await DownloadFromCdnAsync(rel);
                        cascWriter.WriteEntry(ParseHex16(item.EKeyHex), buf, 0, buf.Length);
                        ReportProgress(1, buf.Length);
                    }
                    catch (Exception ex)
                    {
                        // Some historical loose chunks may have been purged or relocated; log notice so archive assembly succeeds
                        Console.WriteLine($"      [Notice] Non-critical loose chunk {item.EKeyHex} skipped ({ex.Message}).");
                        Interlocked.Increment(ref skippedChunks);
                        ReportProgress(1, 0);
                    }
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(looseTasks);
            ReportProgress(0, 0, force: true);

            if (skippedChunks > 0)
            {
                Console.WriteLine($"      [WARNING] CASC storage is INCOMPLETE: {skippedChunks:N0} of {items.Count:N0} chunks could not be downloaded (Blizzard retired their archives). The game may hang or crash when it loads those specific assets.");
            }
            else
            {
                Console.WriteLine($"      [OK] CASC storage complete: all {items.Count:N0} chunks were written and hash-verified.");
            }
            return skippedChunks;
        }

        private static byte[] ParseHex16(string hex) => Convert.FromHexString(hex);
    }

    internal static class BlteDecoder
    {
        public static byte[] Decode(byte[] content)
        {
            if (content == null || content.Length < 8)
                throw new InvalidDataException("BLTE buffer too short.");

            if (BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(0, 4)) != 0x45544C42u)
                throw new InvalidDataException("Invalid BLTE magic.");

            uint headerSize = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(4, 4));
            if (headerSize == 0)
            {
                using var outMs = new MemoryStream();
                DecodeChunk(content, 8, content.Length - 8, outMs);
                return outMs.ToArray();
            }

            int chunkCount = (content[9] << 16) | (content[10] << 8) | content[11];
            if (chunkCount <= 0 || chunkCount > 1_000_000)
                throw new InvalidDataException("Invalid BLTE chunk count.");

            var compSizes = new int[chunkCount];
            var decompSizes = new int[chunkCount];
            var chunkHashes = new byte[chunkCount][];
            long totalDecomp = 0;

            int pos = 12;
            for (int i = 0; i < chunkCount; i++)
            {
                compSizes[i] = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(pos, 4));
                decompSizes[i] = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(pos + 4, 4));
                if (compSizes[i] < 0 || decompSizes[i] < 0)
                    throw new InvalidDataException("Invalid BLTE chunk sizes.");
                chunkHashes[i] = content.AsSpan(pos + 8, 16).ToArray();
                totalDecomp += decompSizes[i];
                pos += 24; // 4 comp + 4 decomp + 16 md5
            }

            if (totalDecomp > int.MaxValue)
                throw new InvalidDataException("BLTE payload is too large.");

            using var result = new MemoryStream((int)totalDecomp);
            for (int i = 0; i < chunkCount; i++)
            {
                if (pos + compSizes[i] > content.Length)
                    throw new InvalidDataException("BLTE chunk exceeds the buffer boundaries.");

                // Every chunk carries its own MD5 so truncated/corrupted downloads are rejected early.
                if (!IsZeroHash(chunkHashes[i]) && !MD5.HashData(content.AsSpan(pos, compSizes[i])).AsSpan().SequenceEqual(chunkHashes[i]))
                    throw new InvalidDataException("BLTE chunk checksum mismatch (corrupted or tampered download).");

                long before = result.Length;
                DecodeChunk(content, pos, compSizes[i], result);
                if (result.Length - before != decompSizes[i])
                    throw new InvalidDataException("BLTE chunk decompressed to an unexpected size.");
                pos += compSizes[i];
            }
            return result.ToArray();
        }

        private static bool IsZeroHash(byte[] hash)
        {
            foreach (byte b in hash)
            {
                if (b != 0) return false;
            }
            return true;
        }

        private static void DecodeChunk(byte[] buf, int offset, int length, MemoryStream output)
        {
            byte mode = buf[offset];
            switch (mode)
            {
                case 0x4E: // 'N' plain
                    output.Write(buf, offset + 1, length - 1);
                    break;
                case 0x5A: // 'Z' zlib (skip mode byte + 2-byte zlib header)
                    using (var zms = new MemoryStream(buf, offset + 3, length - 3))
                    using (var ds = new DeflateStream(zms, CompressionMode.Decompress))
                    {
                        ds.CopyTo(output);
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported BLTE encoding mode 0x{mode:X2}.");
            }
        }
    }

    internal static class CascHash
    {
        private static readonly uint[] Table16C57A8 =
        {
            0x049396b8, 0x72a82a9b, 0xee626cca, 0x9917754f,
            0x15de40b1, 0xf5a8a9b6, 0x421eac7e, 0xa9d55c9a,
            0x317fd40c, 0x04faf80d, 0x3d6be971, 0x52933cfd,
            0x27f64b7d, 0xc6f5c11b, 0xd5757e3a, 0x6c388745
        };

        private static uint Rot(uint x, int k) => (x << k) | (x >> (32 - k));

        public static void HashLittle2(ReadOnlySpan<byte> data, ref uint pc, ref uint pb)
        {
            uint a, b, c;
            a = b = c = 0xdeadbeefu + (uint)data.Length + pc;
            c += pb;

            int pos = 0;
            int rem = data.Length;
            while (rem > 12)
            {
                a += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos, 4));
                b += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4));
                c += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 8, 4));

                a -= c; a ^= Rot(c, 4); c += b;
                b -= a; b ^= Rot(a, 6); a += c;
                c -= b; c ^= Rot(b, 8); b += a;
                a -= c; a ^= Rot(c, 16); c += b;
                b -= a; b ^= Rot(a, 19); a += c;
                c -= b; c ^= Rot(b, 4); b += a;

                pos += 12;
                rem -= 12;
            }

            if (rem == 0)
            {
                pc = c;
                pb = b;
                return;
            }

            Span<byte> tail = stackalloc byte[12];
            data.Slice(pos, rem).CopyTo(tail);
            a += BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(0, 4));
            b += BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(4, 4));
            c += BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(8, 4));

            c ^= b; c -= Rot(b, 14);
            a ^= c; a -= Rot(c, 11);
            b ^= a; b -= Rot(a, 25);
            c ^= b; c -= Rot(b, 16);
            a ^= c; a -= Rot(c, 4);
            b ^= a; b -= Rot(a, 14);
            c ^= b; c -= Rot(b, 24);

            pc = c;
            pb = b;
        }

        public static uint HashLittle(ReadOnlySpan<byte> data, uint initval)
        {
            uint pc = initval;
            uint pb = 0;
            HashLittle2(data, ref pc, ref pb);
            return pc;
        }

        public static uint ComputeDataHeaderChecksumB(ReadOnlySpan<byte> header26, ushort archiveIndex, uint archiveOffset)
        {
            uint offset = (archiveOffset & 0x3FFFFFFFu) | (((uint)archiveIndex & 3u) << 30);
            uint encodedOffset = Table16C57A8[(offset + 0x1Eu) & 0xFu] ^ (offset + 0x1Eu);

            Span<byte> encodedBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(encodedBytes, encodedOffset);

            Span<byte> hashedHeader = stackalloc byte[4];
            for (uint i = 0; i < 0x1Au; i++)
            {
                hashedHeader[(int)((i + offset) & 3u)] ^= header26[(int)i];
            }

            Span<byte> checksumB = stackalloc byte[4];
            for (uint j = 0; j < 4u; j++)
            {
                uint i = j + 0x1Au + offset;
                checksumB[(int)j] = (byte)(hashedHeader[(int)(i & 3u)] ^ encodedBytes[(int)(i & 3u)]);
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(checksumB);
        }

        public static byte GetBucketIndex(ReadOnlySpan<byte> key9)
        {
            byte xor = 0;
            for (int i = 0; i < 9; i++) xor ^= key9[i];
            return (byte)((xor & 0x0F) ^ (xor >> 4));
        }
    }

    internal sealed class CascStorageWriter : IDisposable
    {
        private const uint MaxDataFileSize = 0x40000000u; // 1 GiB per data.NNN

        public readonly struct IdxRecord : IComparable<IdxRecord>
        {
            public readonly byte[] Key9;
            public readonly ushort ArchiveIndex;
            public readonly uint Offset;
            public readonly uint Size;

            public IdxRecord(byte[] key9, ushort archiveIndex, uint offset, uint size)
            {
                Key9 = key9;
                ArchiveIndex = archiveIndex;
                Offset = offset;
                Size = size;
            }

            public int CompareTo(IdxRecord other)
            {
                for (int i = 0; i < 9; i++)
                {
                    int cmp = Key9[i].CompareTo(other.Key9[i]);
                    if (cmp != 0) return cmp;
                }
                return 0;
            }
        }

        private readonly string _cascDataDir;
        private readonly object _writeLock = new();
        private readonly List<IdxRecord>[] _buckets = new List<IdxRecord>[16];
        private readonly HashSet<string> _writtenKeys9 = new(StringComparer.OrdinalIgnoreCase);
        private readonly byte[] _crossLinkSeed7 = new byte[7];

        private FileStream _currentStream;
        private ushort _currentArchiveIndex;
        private uint _currentOffset;

        public CascStorageWriter(string cascDataDir)
        {
            _cascDataDir = cascDataDir;
            for (int i = 0; i < 16; i++) _buckets[i] = new List<IdxRecord>(20000);

            // If existing data.* files exist and are populated, keep them rather than destroying user data
            bool hasExistingData = File.Exists(Path.Combine(_cascDataDir, "data.000")) &&
                                   new FileInfo(Path.Combine(_cascDataDir, "data.000")).Length > 1024 * 1024;
            if (!hasExistingData)
            {
                foreach (var f in Directory.GetFiles(_cascDataDir, "*.idx")) { try { File.Delete(f); } catch { } }
                foreach (var f in Directory.GetFiles(_cascDataDir, "data.*")) { try { File.Delete(f); } catch { } }
            }

            // Generate 7-byte cross-link seed from MD5(hostname + "data/data")
            using var md5 = MD5.Create();
            byte[] hostBytes = Encoding.ASCII.GetBytes(Environment.MachineName + "data/data");
            byte[] md5Bytes = md5.ComputeHash(hostBytes);
            Buffer.BlockCopy(md5Bytes, 2, _crossLinkSeed7, 0, 7);

            OpenNextDataArchive(0);
        }

        private void OpenNextDataArchive(ushort archiveIndex)
        {
            _currentStream?.Dispose();
            _currentArchiveIndex = archiveIndex;
            string dataPath = Path.Combine(_cascDataDir, $"data.{archiveIndex:D3}");
            _currentStream = new FileStream(dataPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024);
            _currentOffset = 0;

            // Write the 16 30-byte cross-linking headers at offsets 0..479
            byte tailXor = (byte)_currentArchiveIndex;
            for (int j = 0; j < 7; j++) tailXor ^= _crossLinkSeed7[j];
            byte tailBucket = (byte)((tailXor & 0x0F) ^ (tailXor >> 4));

            Span<byte> hdr = stackalloc byte[30];
            for (int bucket = 0; bucket < 16; bucket++)
            {
                hdr.Clear();
                byte targetHashBucket = (byte)((bucket + 15) & 0x0F);
                byte byte0 = (byte)(targetHashBucket ^ tailBucket);

                byte[] key16 = new byte[16];
                key16[0] = byte0;
                key16[1] = (byte)_currentArchiveIndex;
                Buffer.BlockCopy(_crossLinkSeed7, 0, key16, 2, 7);

                // Reversed 16-byte key in header[0..15]
                for (int k = 0; k < 16; k++) hdr[k] = key16[15 - k];
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(16, 4), 30u);
                hdr[20] = 1; // cross-link metadata flag
                hdr[21] = 0;

                uint chkA = CascHash.HashLittle(hdr.Slice(0, 22), 0x3D6BE971u);
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(22, 4), chkA);
                uint chkB = CascHash.ComputeDataHeaderChecksumB(hdr.Slice(0, 26), _currentArchiveIndex, _currentOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(26, 4), chkB);

                _currentStream.Write(hdr);

                byte[] key9 = new byte[9];
                Buffer.BlockCopy(key16, 0, key9, 0, 9);
                _buckets[bucket].Add(new IdxRecord(key9, _currentArchiveIndex, _currentOffset, 30u));
                _currentOffset += 30u;
            }
        }

        public void WriteEntry(byte[] ekey16, byte[] blteBuffer)
        {
            WriteEntry(ekey16, blteBuffer, 0, blteBuffer.Length);
        }

        public void WriteEntry(byte[] ekey16, byte[] blteBuffer, int offset, int count)
        {
            string key9Hex = Convert.ToHexString(ekey16, 0, 9);
            uint totalRecordSize = (uint)(30 + count);

            lock (_writeLock)
            {
                if (!_writtenKeys9.Add(key9Hex))
                    return;

                if (_currentOffset + totalRecordSize > MaxDataFileSize)
                {
                    OpenNextDataArchive((ushort)(_currentArchiveIndex + 1));
                }

                uint entryOffset = _currentOffset;
                ushort entryArchive = _currentArchiveIndex;

                Span<byte> hdr = stackalloc byte[30];
                for (int k = 0; k < 16; k++) hdr[k] = ekey16[15 - k];
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(16, 4), totalRecordSize);
                hdr[20] = 0;
                hdr[21] = 0;

                uint chkA = CascHash.HashLittle(hdr.Slice(0, 22), 0x3D6BE971u);
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(22, 4), chkA);
                uint chkB = CascHash.ComputeDataHeaderChecksumB(hdr.Slice(0, 26), entryArchive, entryOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(26, 4), chkB);

                _currentStream.Write(hdr);
                _currentStream.Write(blteBuffer, offset, count);
                _currentOffset += totalRecordSize;

                byte[] key9 = new byte[9];
                Buffer.BlockCopy(ekey16, 0, key9, 0, 9);
                byte bucket = CascHash.GetBucketIndex(key9);
                _buckets[bucket].Add(new IdxRecord(key9, entryArchive, entryOffset, totalRecordSize));
            }
        }

        public void WriteIndicesAndShmem()
        {
            _currentStream?.Flush();
            _currentStream?.Dispose();
            _currentStream = null;

            WriteIndexFiles(_cascDataDir, _buckets, Enumerable.Repeat(1u, 16).ToArray());
            WriteShmem(_cascDataDir, Enumerable.Repeat(1u, 16).ToArray());
        }

        /// <summary>
        /// Rebuilds the 16 .idx files and shmem from the data.NNN archives already on disk.
        /// This repairs a storage whose indexes were destroyed (for example by an interrupted
        /// client repair) without downloading the content again.
        /// </summary>
        internal static int RebuildFromExisting(string cascDataDir, Action<string> log, bool keepShmem = false)
        {
            if (!Directory.Exists(cascDataDir))
                throw new DirectoryNotFoundException($"CASC data folder not found: {cascDataDir}");

            // Reuse the index versions recorded in an existing shmem (the client bumps them when it resets a storage).
            uint[] idxVersions = null;
            string shmemPath = Path.Combine(cascDataDir, "shmem");
            if (File.Exists(shmemPath))
            {
                byte[] existing = File.ReadAllBytes(shmemPath);
                if (existing.Length >= 0x150)
                {
                    var versions = new uint[16];
                    bool valid = true;
                    for (int i = 0; i < 16; i++)
                    {
                        uint v = BinaryPrimitives.ReadUInt32LittleEndian(existing.AsSpan(0x110 + i * 4, 4));
                        if (v == 0 || v >= 0x1000000) { valid = false; break; }
                        versions[i] = v;
                    }
                    if (valid)
                    {
                        idxVersions = versions;
                        log($"      Reusing the 16 index versions from the existing shmem.");
                    }
                }
            }
            idxVersions ??= Enumerable.Repeat(1u, 16).ToArray();

            string[] dataFiles = Directory.GetFiles(cascDataDir, "data.*")
                .Where(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"^data\.\d{3}$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (dataFiles.Length == 0)
                throw new InvalidDataException($"No data.NNN archives found in {cascDataDir}");

            var buckets = new List<IdxRecord>[16];
            for (int i = 0; i < 16; i++) buckets[i] = new List<IdxRecord>(20000);
            var records = new List<(string Hex, byte[] Key9, ushort Archive, uint Offset, uint Size)>(350000);
            var indexOfKey = new Dictionary<string, int>(350000, StringComparer.OrdinalIgnoreCase);
            var header = new byte[30];
            long totalRecords = 0;

            foreach (string file in dataFiles)
            {
                ushort archiveIndex = ushort.Parse(Path.GetFileName(file).Substring(5));
                long length = new FileInfo(file).Length;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, FileOptions.SequentialScan);
                long offset = 0;
                int bad = 0;
                while (offset + 30 <= length)
                {
                    stream.Position = offset;
                    if (stream.Read(header, 0, 30) != 30) break;
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4));
                    if (size < 30 || offset + size > length)
                    {
                        if (bad++ < 3)
                            log($"      [warn] {Path.GetFileName(file)}: invalid record size {size} at offset {offset}; skipping remainder.");
                        break;
                    }

                    // The storage keeps the encoded key byte-reversed.
                    byte[] key9 = new byte[9];
                    for (int i = 0; i < 9; i++) key9[i] = header[15 - i];
                    string keyHex = Convert.ToHexString(key9);
                    // Duplicate keys keep the newest copy, matching what the client's own writer produces.
                    if (indexOfKey.TryGetValue(keyHex, out int existing))
                    {
                        records[existing] = (keyHex, key9, archiveIndex, (uint)offset, size);
                    }
                    else
                    {
                        indexOfKey[keyHex] = records.Count;
                        records.Add((keyHex, key9, archiveIndex, (uint)offset, size));
                        totalRecords++;
                    }
                    offset += size;
                }
                log($"      {Path.GetFileName(file)}: {length / 1048576.0:F0} MB scanned (next offset {offset}).");
            }

            foreach (var rec in records)
            {
                // Cross-link header records (the 16 at offsets 0..479 of each archive) live one bucket
                // above the bucket their key hashes to; every other record lives in its hash bucket.
                byte bucket = CascHash.GetBucketIndex(rec.Key9);
                if (rec.Offset < 480 && rec.Size == 30)
                {
                    bucket = (byte)((bucket + 1) & 0x0F);
                }
                buckets[bucket].Add(new IdxRecord(rec.Key9, rec.Archive, rec.Offset, rec.Size));
            }

            foreach (var stale in Directory.GetFiles(cascDataDir, "*.idx")) { try { File.Delete(stale); } catch { } }
            foreach (var stale in Directory.GetFiles(cascDataDir, "*.mrk")) { try { File.Delete(stale); } catch { } }

            WriteIndexFiles(cascDataDir, buckets, idxVersions);
            if (!keepShmem)
            {
                WriteShmem(cascDataDir, idxVersions);
            }
            else
            {
                log("      Keeping the existing shmem (client-generated layout).");
            }
            return (int)Math.Min(totalRecords, int.MaxValue);
        }

        internal static void WriteIndexFiles(string cascDataDir, List<IdxRecord>[] buckets, uint[] idxVersions)
        {
            foreach (string stale in Directory.GetFiles(cascDataDir, "*.idx"))
            {
                try { File.Delete(stale); } catch { }
            }
            for (byte b = 0; b < 16; b++)
            {
                var list = buckets[b];
                list.Sort();

                uint entriesBytesLen = (uint)(list.Count * 18);
                int rawLen = 40 + (int)entriesBytesLen;
                // The client expects the file to be the entry table rounded up to 4 KB plus a 32 KB
                // zero-filled reserve; a shorter file makes it reject the whole storage.
                int paddedLen = ((rawLen + 4095) & ~4095) + 8 * 4096;
                byte[] idxBuf = new byte[paddedLen];

                BinaryPrimitives.WriteUInt32LittleEndian(idxBuf.AsSpan(0, 4), 16u); // HeaderHashSize
                BinaryPrimitives.WriteUInt16LittleEndian(idxBuf.AsSpan(8, 2), 7);   // Version = 7
                idxBuf[10] = b;                                                     // BucketIndex
                idxBuf[11] = 0;                                                     // ExtraBytes
                idxBuf[12] = 4;                                                     // SpanSizeBytes
                idxBuf[13] = 5;                                                     // SpanOffsBytes
                idxBuf[14] = 9;                                                     // KeyBytes
                idxBuf[15] = 30;                                                    // SegmentBits
                BinaryPrimitives.WriteUInt64LittleEndian(idxBuf.AsSpan(16, 8), 0xFFC0000000UL); // MaxFileOffset (Blizzard constant)

                uint hdrPc = 0, hdrPb = 0;
                CascHash.HashLittle2(idxBuf.AsSpan(8, 16), ref hdrPc, ref hdrPb);
                BinaryPrimitives.WriteUInt32LittleEndian(idxBuf.AsSpan(4, 4), hdrPc);

                BinaryPrimitives.WriteUInt32LittleEndian(idxBuf.AsSpan(32, 4), entriesBytesLen);

                uint entPc = 0, entPb = 0;
                int pos = 40;
                foreach (var rec in list)
                {
                    Buffer.BlockCopy(rec.Key9, 0, idxBuf, pos, 9);
                    ulong combinedOffset = (((ulong)rec.ArchiveIndex) << 30) | (rec.Offset & 0x3FFFFFFFu);
                    idxBuf[pos + 9] = (byte)((combinedOffset >> 32) & 0xFF);
                    BinaryPrimitives.WriteUInt32BigEndian(idxBuf.AsSpan(pos + 10, 4), (uint)(combinedOffset & 0xFFFFFFFFu));
                    BinaryPrimitives.WriteUInt32LittleEndian(idxBuf.AsSpan(pos + 14, 4), rec.Size);

                    CascHash.HashLittle2(idxBuf.AsSpan(pos, 18), ref entPc, ref entPb);
                    pos += 18;
                }
                BinaryPrimitives.WriteUInt32LittleEndian(idxBuf.AsSpan(36, 4), entPc);

                string idxPath = Path.Combine(cascDataDir, $"{b:x2}{idxVersions[b]:x8}.idx");
                File.WriteAllBytes(idxPath, idxBuf);
            }
        }

        internal static void WriteShmem(string cascDataDir, uint[] idxVersions)
        {
            byte[] shmem = new byte[20480];
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0, 4), 5u);      // BlockType = 5
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(4, 4), 0x0154u); // NextBlock = 340

            string normalizedPath = Path.GetFullPath(cascDataDir).Replace('\\', '/');
            if (normalizedPath.Length >= 2 && normalizedPath[1] == ':')
            {
                normalizedPath = char.ToLowerInvariant(normalizedPath[0]) + normalizedPath.Substring(1);
            }
            byte[] pathBytes = Encoding.ASCII.GetBytes("Global\\" + normalizedPath);
            Buffer.BlockCopy(pathBytes, 0, shmem, 8, Math.Min(pathBytes.Length, 255));

            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0108, 4), 0x2AB8u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x010C, 4), 0x1000u);

            // 16 .idx versions
            for (int i = 0; i < 16; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0110 + i * 4, 4), idxVersions[i]);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0150, 4), 2u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0154, 4), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0158, 4), 0x0228u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x01D8, 4), 0x4000u);

            File.WriteAllBytes(Path.Combine(cascDataDir, "shmem"), shmem);
        }

        public void Dispose()
        {
            _currentStream?.Dispose();
        }
    }
}

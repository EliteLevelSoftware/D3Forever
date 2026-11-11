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
            "http://us.patch.battle.net:1119/d3",
            "https://us.version.battle.net/v2/products/d3",
            "http://eu.patch.battle.net:1119/d3",
            "https://eu.version.battle.net/v2/products/d3"
        };

        private static HttpClient _http;
        private static List<string> _cdnHosts = new();
        private static string _cdnPath = "tpr/diablo3";
        private static string _cdnConfigPath = "tpr/configs/data";

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

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

            if (listVersions)
            {
                Console.WriteLine("==========================================================================================");
                Console.WriteLine(" Available Official Blizzard CDN Diablo III Builds (--version <Version or BuildNumber>)");
                Console.WriteLine("==========================================================================================");
                foreach (var b in D3BuildCatalog.AllBuilds)
                {
                    Console.WriteLine($"  {b.Version,-8} | Build {b.BuildNumber,-6} | {(b.IsCasc ? "NGDP/CASC" : "MPQ/PTCH "),-9} | {b.ReleaseDate,-10} | {b.Summary}");
                }
                Console.WriteLine("==========================================================================================");
                return 0;
            }

            var resolvedBuild = D3BuildCatalog.Resolve(versionArg);
            if (resolvedBuild == null)
            {
                Console.Error.WriteLine($"[ERROR] Unknown version or build '{versionArg}'. Run with --list-versions to see all 19 supported builds.");
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
            CancellationToken ct = default)
        {
            log("========================================================================");
            log(" Diablo III Official Blizzard CDN Downloader & Multi-Version Builder");
            log("========================================================================");
            log($" Selected Build   : {selectedBuild.Version}.{selectedBuild.BuildNumber} ({(selectedBuild.IsCasc ? "NGDP/CASC" : "Legacy MPQ + PTCH")})");
            log($" Target Directory : {targetDir}");
            log($" Region / Locale  : {region} / {locale}");
            log($" Parallel Streams : {maxConcurrency}");
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
                        ct);

                    if (configureShortcutFirewall)
                    {
                        ConfigureShortcutAndFirewall(targetDir);
                    }

                    log("========================================================================");
                    log($" SUCCESS: Diablo III {selectedBuild.Version}.{selectedBuild.BuildNumber} completed with 0 errors!");
                    log("========================================================================");
                    return 0;
                }

                Directory.CreateDirectory(targetDir);
                string dataDir = Path.Combine(targetDir, "Data");
                string configDir = Path.Combine(dataDir, "config");
                string indicesDir = Path.Combine(dataDir, "indices");
                string cascDataDir = Path.Combine(dataDir, "data");
                Directory.CreateDirectory(configDir);
                Directory.CreateDirectory(indicesDir);
                Directory.CreateDirectory(cascDataDir);

                // Step 1: Query NGDP versions & cdns
                string prodCode = string.IsNullOrWhiteSpace(selectedBuild?.ProductCode) ? "d3" : selectedBuild.ProductCode;
                Console.WriteLine($"[1/7] Querying Blizzard NGDP versions and CDN endpoints ({prodCode})...");
                var versionsText = await QueryNgdpEndpointAsync("versions", prodCode);
                var cdnsText = await QueryNgdpEndpointAsync("cdns", prodCode);

                var versionInfo = ParseVersions(versionsText, region);
                var cdnInfo = ParseCdns(cdnsText, region);

                _cdnPath = cdnInfo.Path.Trim('/');
                _cdnConfigPath = string.IsNullOrWhiteSpace(cdnInfo.ConfigPath) ? "tpr/configs/data" : cdnInfo.ConfigPath.Trim('/');
                _cdnHosts = new[] { "us.cdn.blizzard.com", "cdn.blizzard.com" }
                    .Concat(cdnInfo.Hosts)
                    .Concat(new[] { "level3.blizzard.com", "eu.cdn.blizzard.com" })
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Console.WriteLine($"      Live Build   : {versionInfo.VersionsName} (Build ID {versionInfo.BuildId})");
                Console.WriteLine($"      BuildConfig  : {versionInfo.BuildConfig}");
                Console.WriteLine($"      CDNConfig    : {versionInfo.CdnConfig}");
                Console.WriteLine($"      CDN Hosts    : {string.Join(", ", _cdnHosts)}");

                // Step 2: Download BuildConfig, CDNConfig, ProductConfig
                Console.WriteLine("[2/7] Downloading BuildConfig and CDNConfig manifests...");
                byte[] buildCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.BuildConfig);
                byte[] cdnCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.CdnConfig);
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
                    ? archStr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>();
                string[] patchArchives = cdnCfg.TryGetValue("patch-archives", out string pArchStr)
                    ? pArchStr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>();
                cdnCfg.TryGetValue("archive-group", out string archiveGroupHash);
                cdnCfg.TryGetValue("patch-archive-group", out string patchArchiveGroupHash);
                cdnCfg.TryGetValue("file-index", out string fileIndexHash);
                cdnCfg.TryGetValue("patch-file-index", out string patchFileIndexHash);

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
                    dryRunLimit);

                Console.WriteLine($"      Indexed {archiveIndexMap.Count:N0} unique BLTE chunks across {archives.Length} CDN archives.");

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

                // Step 6: Extract Install Manifest files (Diablo III64.exe, DLLs, CEF Scene, etc.)
                var installToProcess = dryRunLimit > 0
                    ? installEntries.Take(dryRunLimit).ToList()
                    : installEntries;

                Console.WriteLine($"[6/7] Extracting {installToProcess.Count:N0} game executables & DLLs into installation root...");
                await ExtractInstallFilesAsync(targetDir, installToProcess, ckeyToEkey, archiveIndexMap, archives, maxConcurrency);

                if (binariesOnly)
                {
                    log("[7/7] Binaries-Only mode selected: skipping CASC data.NNN archive download.");
                    onProgress?.Invoke(100, "Binaries extraction complete!");
                }
                else
                {
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

                        await DownloadAndWriteCascEntriesAsync(
                            cascWriter,
                            cascToProcess,
                            archiveIndexMap,
                            archives,
                            maxConcurrency);

                        cascWriter.WriteIndicesAndShmem();
                    }
                }

                if (configureShortcutFirewall)
                {
                    ConfigureShortcutAndFirewall(targetDir);
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

        private static void ConfigureShortcutAndFirewall(string targetDir)
        {
            Console.WriteLine("[Post-Install] Configuring Public Desktop shortcut and Windows Defender Firewall...");
            string exe64 = Path.Combine(targetDir, "x64", "Diablo III64.exe");
            string exe32 = Path.Combine(targetDir, "Diablo III.exe");
            string shortcutTarget = File.Exists(exe64) ? exe64 : exe32;

            string psCmd = $@"
$ErrorActionPreference = 'SilentlyContinue'
$pub = [Environment]::GetFolderPath('CommonDesktopDirectory')
if (-not $pub) {{ $pub = 'C:\Users\Public\Desktop' }}
$lnk = Join-Path $pub 'Diablo III.lnk'
$wsh = New-Object -ComObject WScript.Shell
$s = $wsh.CreateShortcut($lnk)
$s.TargetPath = '{shortcutTarget.Replace("'", "''")}'
$s.Arguments = '-launch'
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
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
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

        private static async Task<byte[]> DownloadFromCdnAsync(string relativePath, long? rangeStart = null, long? rangeEnd = null)
        {
            relativePath = relativePath.TrimStart('/').ToLowerInvariant();
            Exception lastEx = null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                foreach (string host in _cdnHosts)
                {
                    string url = $"http://{host}/{relativePath}";
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
                            continue;
                        }
                        return await resp.Content.ReadAsByteArrayAsync(cts.Token);
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                    }
                }
                await Task.Delay(250 * (attempt + 1));
            }
            throw new IOException($"Exhausted all Blizzard CDN hosts downloading '{relativePath}'.", lastEx);
        }

        private static async Task<byte[]> DownloadConfigFileAsync(string configRootDir, string cdnPrefix, string hash)
        {
            hash = hash.ToLowerInvariant();
            string subDir = Path.Combine(configRootDir, hash.Substring(0, 2), hash.Substring(2, 2));
            Directory.CreateDirectory(subDir);
            string localPath = Path.Combine(subDir, hash);
            if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                return await File.ReadAllBytesAsync(localPath);
            }
            string rel = $"{cdnPrefix}/{hash.Substring(0, 2)}/{hash.Substring(2, 2)}/{hash}";
            byte[] data = await DownloadFromCdnAsync(rel);
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
            int dryRunLimit)
        {
            using var sem = new SemaphoreSlim(maxConcurrency);
            int completed = 0;
            int total = archives.Length;

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
                        if (done % 25 == 0 || done == total)
                        {
                            Console.WriteLine($"      Archive indices: {done}/{total} ({(done * 100.0 / total):F0}%)");
                        }
                    }
                    finally
                    {
                        sem.Release();
                    }
                });
            }
            await Task.WhenAll(tasks);

            // Download optional file-index, patch-file-index, and patch-archives indices when not in dry-run (or download all since they are tiny)
            if (!string.IsNullOrWhiteSpace(fileIndexHash))
            {
                await DownloadIndexFileIfMissingAsync(indicesDir, "data", fileIndexHash);
            }
            if (!string.IsNullOrWhiteSpace(patchFileIndexHash))
            {
                await DownloadIndexFileIfMissingAsync(indicesDir, "patch", patchFileIndexHash);
            }

            var patchIndexBytesArr = new byte[patchArchives.Length][];
            for (int i = 0; i < patchArchives.Length; i++)
            {
                patchIndexBytesArr[i] = await DownloadIndexFileIfMissingAsync(indicesDir, "patch", patchArchives[i]);
            }

            // Parse all data archive indices in order (0..archives.Length-1)
            var dict = new Dictionary<string, ArchiveEntryLoc>(900000, StringComparer.OrdinalIgnoreCase);
            for (short a = 0; a < archives.Length; a++)
            {
                ParseArchiveIndexBuffer(indexBytesArr[a], a, dict);
            }

            // Synthesize archive-group.index if missing
            if (!string.IsNullOrWhiteSpace(archiveGroupHash))
            {
                string agPath = Path.Combine(indicesDir, archiveGroupHash.ToLowerInvariant() + ".index");
                if (!File.Exists(agPath))
                {
                    BuildArchiveGroupIndexFile(agPath, dict);
                }
            }

            // Synthesize patch-archive-group.index if missing
            if (!string.IsNullOrWhiteSpace(patchArchiveGroupHash) && patchArchives.Length > 0)
            {
                string pagPath = Path.Combine(indicesDir, patchArchiveGroupHash.ToLowerInvariant() + ".index");
                if (!File.Exists(pagPath))
                {
                    var patchDict = new Dictionary<string, ArchiveEntryLoc>(StringComparer.OrdinalIgnoreCase);
                    for (short a = 0; a < patchArchives.Length; a++)
                    {
                        ParseArchiveIndexBuffer(patchIndexBytesArr[a], a, patchDict);
                    }
                    BuildArchiveGroupIndexFile(pagPath, patchDict);
                }
            }

            return dict;
        }

        private static async Task<byte[]> DownloadIndexFileIfMissingAsync(string indicesDir, string folder, string hash)
        {
            hash = hash.ToLowerInvariant();
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

        private static void BuildArchiveGroupIndexFile(string outputPath, Dictionary<string, ArchiveEntryLoc> dict)
        {
            var sorted = new List<(byte[] Key, ArchiveEntryLoc Loc)>(dict.Count);
            foreach (var kvp in dict)
            {
                sorted.Add((Convert.FromHexString(kvp.Key), kvp.Value));
            }
            sorted.Sort((a, b) => CompareBytes(a.Key, b.Key));

            const int recordsPerBlock = 163; // 4096 / 25 = 163
            int numElements = sorted.Count;
            int blockCount = (numElements + recordsPerBlock - 1) / recordsPerBlock;
            int tocStart = blockCount * 4096;
            int tocLen = blockCount * 24; // 16-byte key + 8-byte md5 per block
            byte[] outBuf = new byte[tocStart + tocLen + 28];

            using var md5 = MD5.Create();
            int idx = 0;
            for (int b = 0; b < blockCount; b++)
            {
                int bStart = b * 4096;
                byte[] lastKey = null;
                for (int e = 0; e < recordsPerBlock && idx < numElements; e++, idx++)
                {
                    int pos = bStart + e * 25;
                    var item = sorted[idx];
                    Buffer.BlockCopy(item.Key, 0, outBuf, pos, 16);
                    BinaryPrimitives.WriteUInt32BigEndian(outBuf.AsSpan(pos + 16, 4), item.Loc.Size);
                    outBuf[pos + 20] = (byte)item.Loc.ArchiveIndex;
                    BinaryPrimitives.WriteUInt32BigEndian(outBuf.AsSpan(pos + 21, 4), item.Loc.Offset);
                    lastKey = item.Key;
                }
                if (lastKey != null)
                {
                    Buffer.BlockCopy(lastKey, 0, outBuf, tocStart + b * 16, 16);
                }
                byte[] blockHash = md5.ComputeHash(outBuf, bStart, 4096);
                Buffer.BlockCopy(blockHash, 0, outBuf, tocStart + blockCount * 16 + b * 8, 8);
            }

            int footerPos = tocStart + tocLen;
            byte[] tocHash = md5.ComputeHash(outBuf, tocStart, tocLen);
            Buffer.BlockCopy(tocHash, 0, outBuf, footerPos, 8);
            outBuf[footerPos + 8] = 1;  // version
            outBuf[footerPos + 9] = 0;  // unk0
            outBuf[footerPos + 10] = 0; // unk1
            outBuf[footerPos + 11] = 4; // blockSizeKB
            outBuf[footerPos + 12] = 5; // offsetBytes (1 archive + 4 offset)
            outBuf[footerPos + 13] = 4; // sizeBytes
            outBuf[footerPos + 14] = 16; // keyBytes
            outBuf[footerPos + 15] = 8;  // checksumSize
            BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(footerPos + 16, 4), (uint)numElements);

            byte[] footerCheckInput = new byte[20];
            Buffer.BlockCopy(outBuf, footerPos + 8, footerCheckInput, 0, 12);
            byte[] footerCheck = md5.ComputeHash(footerCheckInput);
            Buffer.BlockCopy(footerCheck, 0, outBuf, footerPos + 20, 8);

            File.WriteAllBytes(outputPath, outBuf);
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
            string hostsJoined = string.Join(" ", cdn.Hosts);
            string serversJoined = string.IsNullOrWhiteSpace(cdn.Servers)
                ? "http://level3.blizzard.com/?maxhosts=4 http://us.cdn.blizzard.com/?maxhosts=4 https://level3.ssl.blizzard.com/?maxhosts=4&fallback=1 https://us.cdn.blizzard.com/?maxhosts=4&fallback=1"
                : cdn.Servers;
            string tags = $"Windows {region.ToUpperInvariant()}? acct-USA? geoip-US? {locale} speech?:Windows {region.ToUpperInvariant()}? acct-USA? geoip-US? {locale} text?";

            var sb = new StringBuilder();
            sb.AppendLine("Branch!STRING:0|Active!DEC:1|Build Key!HEX:16|CDN Key!HEX:16|Install Key!HEX:16|IM Size!DEC:4|CDN Path!STRING:0|CDN Hosts!STRING:0|CDN Servers!STRING:0|Tags!STRING:0|Armadillo!STRING:0|Last Activated!STRING:0|Version!STRING:0|KeyRing!HEX:16|Product!STRING:0");
            sb.AppendLine($"{region}|1|{ver.BuildConfig}|{ver.CdnConfig}|{installCKey.ToLowerInvariant()}||{cdn.Path}|{hostsJoined}|{serversJoined}|{tags}|||{ver.VersionsName}|{ver.KeyRing}|");
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
            int maxConcurrency)
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
                    string outPath = Path.Combine(targetDir, item.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                    if (File.Exists(outPath) && new FileInfo(outPath).Length == item.UncompressedSize)
                    {
                        Interlocked.Add(ref bytesDone, item.UncompressedSize);
                        Interlocked.Increment(ref completed);
                        return;
                    }

                    if (!ckeyToEkey.TryGetValue(item.CKey, out string ekey))
                    {
                        throw new KeyNotFoundException($"Install file '{item.RelativePath}' CKey {item.CKey} not found in Encoding.");
                    }

                    byte[] rawBlte = await DownloadDataBlobByEKeyAsync(ekey, archiveIndexMap, archives);
                    byte[] decoded = BlteDecoder.Decode(rawBlte);
                    await File.WriteAllBytesAsync(outPath, decoded);

                    long curBytes = Interlocked.Add(ref bytesDone, decoded.Length);
                    int curFiles = Interlocked.Increment(ref completed);
                    if (curFiles % 10 == 0 || curFiles == items.Count)
                    {
                        double mbps = (curBytes / 1048576.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                        Console.WriteLine($"      [Install] {curFiles}/{items.Count} files | {curBytes / 1048576.0:F1}/{totalBytes / 1048576.0:F1} MB ({(curFiles * 100.0 / items.Count):F1}%) | {mbps:F1} MB/s");
                    }
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
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

        private static async Task DownloadAndWriteCascEntriesAsync(
            CascStorageWriter cascWriter,
            List<DownloadItem> items,
            Dictionary<string, ArchiveEntryLoc> archiveIndexMap,
            string[] archives,
            int maxConcurrency)
        {
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
                if (force || f == totalFiles || (now - prev >= 1000 && Interlocked.CompareExchange(ref lastLogTick, now, prev) == prev))
                {
                    double pct = totalExpectedBytes > 0 ? (b * 100.0 / totalExpectedBytes) : 100.0;
                    double mbps = (b / 1048576.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                    Console.WriteLine($"      [CASC] {pct,6:F2}% | {b / 1048576.0,8:F1} / {totalExpectedBytes / 1048576.0,8:F1} MB | {f,7:N0} / {totalFiles:N0} files | {mbps,6:F1} MB/s");
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
                    byte[] rangeBuf = await DownloadFromCdnAsync(rel, job.RangeStart, job.RangeEnd);

                    long logicalBytes = 0;
                    foreach (var slice in job.Slices)
                    {
                        int relOffset = (int)(slice.Offset - job.RangeStart);
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
                    byte[] buf = await DownloadFromCdnAsync(rel);
                    cascWriter.WriteEntry(ParseHex16(item.EKeyHex), buf, 0, buf.Length);
                    ReportProgress(1, buf.Length);
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(looseTasks);
            ReportProgress(0, 0, force: true);
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
            var compSizes = new int[chunkCount];
            var decompSizes = new int[chunkCount];
            long totalDecomp = 0;

            int pos = 12;
            for (int i = 0; i < chunkCount; i++)
            {
                compSizes[i] = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(pos, 4));
                decompSizes[i] = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(pos + 4, 4));
                totalDecomp += decompSizes[i];
                pos += 24; // 4 comp + 4 decomp + 16 md5
            }

            using var result = new MemoryStream((int)Math.Min(totalDecomp, int.MaxValue));
            for (int i = 0; i < chunkCount; i++)
            {
                DecodeChunk(content, pos, compSizes[i], result);
                pos += compSizes[i];
            }
            return result.ToArray();
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

            // Clean any partial old data/idx files so build is deterministic
            foreach (var f in Directory.GetFiles(_cascDataDir, "*.idx")) File.Delete(f);
            foreach (var f in Directory.GetFiles(_cascDataDir, "data.*")) File.Delete(f);

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

            for (byte b = 0; b < 16; b++)
            {
                var list = _buckets[b];
                list.Sort();

                uint entriesBytesLen = (uint)(list.Count * 18);
                int rawLen = 40 + (int)entriesBytesLen;
                // Pad to 64 KB boundary (minimum 65536 bytes) just like Battle.net Agent
                int paddedLen = Math.Max(65536, (rawLen + 65535) & ~65535);
                byte[] idxBuf = new byte[paddedLen];

                BinaryPrimitives.WriteUInt32LittleEndian(idxBuf.AsSpan(0, 4), 16u); // HeaderHashSize
                BinaryPrimitives.WriteUInt16LittleEndian(idxBuf.AsSpan(8, 2), 7);   // Version = 7
                idxBuf[10] = b;                                                     // BucketIndex
                idxBuf[11] = 0;                                                     // ExtraBytes
                idxBuf[12] = 4;                                                     // SpanSizeBytes
                idxBuf[13] = 5;                                                     // SpanOffsBytes
                idxBuf[14] = 9;                                                     // KeyBytes
                idxBuf[15] = 30;                                                    // SegmentBits
                BinaryPrimitives.WriteUInt64LittleEndian(idxBuf.AsSpan(16, 8), 0x4000000000UL); // 256 GiB max

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

                string idxPath = Path.Combine(_cascDataDir, $"{b:x2}00000001.idx");
                File.WriteAllBytes(idxPath, idxBuf);
            }

            WriteShmemFile();
        }

        private void WriteShmemFile()
        {
            byte[] shmem = new byte[20480];
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0, 4), 5u);      // BlockType = 5
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(4, 4), 0x0154u); // NextBlock = 340

            string normalizedPath = Path.GetFullPath(_cascDataDir).Replace('\\', '/');
            if (normalizedPath.Length >= 2 && normalizedPath[1] == ':')
            {
                normalizedPath = char.ToLowerInvariant(normalizedPath[0]) + normalizedPath.Substring(1);
            }
            byte[] pathBytes = Encoding.ASCII.GetBytes("Global\\" + normalizedPath);
            Buffer.BlockCopy(pathBytes, 0, shmem, 8, Math.Min(pathBytes.Length, 255));

            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0108, 4), 0x2AB8u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x010C, 4), 0x1000u);

            // 16 .idx versions (all version 1)
            for (int i = 0; i < 16; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0110 + i * 4, 4), 1u);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0150, 4), 2u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0154, 4), 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x0158, 4), 0x0228u);
            BinaryPrimitives.WriteUInt32LittleEndian(shmem.AsSpan(0x01D8, 4), 0x4000u);

            File.WriteAllBytes(Path.Combine(_cascDataDir, "shmem"), shmem);
        }

        public void Dispose()
        {
            _currentStream?.Dispose();
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace D3Downloader
{
    public sealed class D3BuildCatalogItem
    {
        public string Version { get; init; }
        public int BuildNumber { get; init; }
        public bool IsCasc { get; init; }
        public bool IsRoS { get; init; }
        public string ProductCode { get; init; } = "d3";
        public string ReleaseDate { get; init; }
        public string Summary { get; init; }
        public string BaseDirectUrl { get; init; } = D3BuildCatalog.VanillaDirectBaseUrl;
        public string MfilFileName { get; init; } = D3BuildCatalog.VanillaMfilFileName;
        public int BaseBuildNumber { get; init; } = 8370;

        public string CheckUrl => IsCasc
            ? $"http://us.patch.battle.net:1119/{ProductCode}/versions"
            : (BuildNumber > BaseBuildNumber
                ? $"{BaseDirectUrl}/Updates/d3-0-{BuildNumber}-Win-final.MPQ"
                : $"{BaseDirectUrl}/Data_D3/PC/MPQs/base-Win.mpq");

        public string DisplayTitle => IsCasc
            ? $"{Version}.{BuildNumber} — [CASC / {ProductCode} 64-bit] ({Summary})"
            : (IsRoS
                ? $"{Version}.{BuildNumber} — [RoS MPQ 32-bit] ({ReleaseDate} - {Summary})"
                : $"{Version}.{BuildNumber} — [Vanilla MPQ 32-bit] ({ReleaseDate} - {Summary})");

        public override string ToString() => DisplayTitle;
    }

    public static class D3BuildCatalog
    {
        public const string VanillaDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod-retail/NA/8370.direct";
        public const string VanillaMfilFileName = "d3-16603-5A26B6C69DFDB16C25E1D05B0EEE83C2.mfil";
        public const string RosDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod-retail/NA/20813.direct";
        public const string RosMfilFileName = "d3-28709-85F13E62259AEC63DC654C8F847A7CAF.mfil";
        public const string RosBetaDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod/beta/00FC2DE9/NA/18962.direct";
        public const string VanillaBetaDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod/20FB5BE9/NA/7162.direct";

        public static readonly IReadOnlyList<D3BuildCatalogItem> AllBuilds = new List<D3BuildCatalogItem>
        {
            // =========================================================================
            // REAPER OF SOULS TILE (15 BUILDS)
            // =========================================================================
            new() { Version = "2.8.0",  BuildNumber = 99920,  IsCasc = true,  IsRoS = true,  ProductCode = "d3",   ReleaseDate = "Live Retail", Summary = "Global Live Retail Reaper of Souls Client (64-bit)" },
            new() { Version = "2.7.8",  BuildNumber = 96670,  IsCasc = true,  IsRoS = true,  ProductCode = "d3t",  ReleaseDate = "PTR CDN",     Summary = "Patch 2.7.8 Public Test Realm Client (64-bit)" },
            new() { Version = "2.8.1",  BuildNumber = 101167, IsCasc = true,  IsRoS = true,  ProductCode = "d3cn", ReleaseDate = "CN CDN",      Summary = "Patch 2.8.1 Reaper of Souls Client (64-bit)" },
            new() { Version = "2.1.2",  BuildNumber = 28709,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = RosMfilFileName, ReleaseDate = "2015-01-13", Summary = "Patch 2.1.2 (Ancient Items, Conduit Pylon Rework)" },
            new() { Version = "2.1.1",  BuildNumber = 27255,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-27255-35F65D5686569C9A188AC0264ED26583.mfil", ReleaseDate = "2014-09-23", Summary = "Patch 2.1.1 (Kadala Bloodshard Costs, Pet Toughness)" },
            new() { Version = "2.1.0",  BuildNumber = 26451,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-26451-6032812D57D4CE684194552583B4275F.mfil", ReleaseDate = "2014-08-26", Summary = "Major Update (Greater Rifts, Legendary Gems, The Vault)" },
            new() { Version = "2.0.6",  BuildNumber = 24641,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-24641-4D78C4B7698F7F79B3F5FD02049BF58C.mfil", ReleaseDate = "2014-06-10", Summary = "Patch 2.0.6 (Crafting Material Removal, Class Balancing)" },
            new() { Version = "2.0.5a", BuildNumber = 24017,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-24017-19A8EF9C0CDE1BABCA831DA6EF2481B7.mfil", ReleaseDate = "2014-05-15", Summary = "Patch 2.0.5a Stability & Bugfix Hotfix" },
            new() { Version = "2.0.5",  BuildNumber = 23920,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-23920-E2B432A14395F977F64B476003FF5BB8.mfil", ReleaseDate = "2014-05-13", Summary = "Patch 2.0.5 (Horadric Cache Torment Drop Rates, Gold Trade)" },
            new() { Version = "2.0.4",  BuildNumber = 23119,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-23119-137D06EF773BC6F7BB015C088AAC8568.mfil", ReleaseDate = "2014-04-08", Summary = "Patch 2.0.4 (Crusader Wrath Fixes, Class Tweaks)" },
            new() { Version = "2.0.3",  BuildNumber = 22427,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22427-426C3AE6756462BF912686D1FE56CD22.mfil", ReleaseDate = "2014-03-11", Summary = "Patch 2.0.3 Bugfix & Stability Update" },
            new() { Version = "2.0.2",  BuildNumber = 22274,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22274-B7A49A3F2CDE04523DC09380EF88DF52.mfil", ReleaseDate = "2014-03-04", Summary = "Patch 2.0.2 Pre-Expansion Optimization Patch" },
            new() { Version = "2.0.1",  BuildNumber = 22044,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22044-0DD436E701850277986966F412FDC4B7.mfil", ReleaseDate = "2014-02-25", Summary = "Reaper of Souls Pre-Patch (Loot 2.0, Paragon 2.0)" },
            new() { Version = "2.0.0",  BuildNumber = 20813,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22044-0DD436E701850277986966F412FDC4B7.mfil", ReleaseDate = "2014-01-20", Summary = "Reaper of Souls Unpatched Retail Base MPQ Build" },
            new() { Version = "Beta",   BuildNumber = 18962,  IsCasc = false, IsRoS = true,  BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "", ReleaseDate = "2013-11-20", Summary = "Original RoS Friends & Family Closed Beta Base Client" },

            // =========================================================================
            // VANILLA CLASSIC TILE (19 BUILDS)
            // =========================================================================
            new() { Version = "1.0.8a", BuildNumber = 16603,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-05-10", Summary = "Final Vanilla 1.0.8 Hotfix Build" },
            new() { Version = "1.0.8",  BuildNumber = 16416,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-05-07", Summary = "Monster Density & Co-op XP Bonus Patch" },
            new() { Version = "1.0.7a", BuildNumber = 15295,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-02-15", Summary = "Duelling & Demon Hunter Hotfix" },
            new() { Version = "1.0.7",  BuildNumber = 14633,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-02-12", Summary = "Brawling, Marquise Gems & BoA Crafting" },
            new() { Version = "1.0.6a", BuildNumber = 13644,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-12-06", Summary = "Quest & Stability Patch" },
            new() { Version = "1.0.6",  BuildNumber = 13300,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-11-27", Summary = "Network & Bugfix Patch" },
            new() { Version = "1.0.5a", BuildNumber = 12811,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-10-25", Summary = "Crash & Achievement Fix Patch" },
            new() { Version = "1.0.5",  BuildNumber = 12480,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-10-16", Summary = "Monster Power (MP1-10) & Infernal Machine" },
            new() { Version = "1.0.4",  BuildNumber = 11327,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-08-21", Summary = "Paragon 1.0 (Levels 1-100) & Legendary Overhaul" },
            new() { Version = "1.0.3b", BuildNumber = 10485,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-07-10", Summary = "Auction House & Stability Patch" },
            new() { Version = "1.0.3a", BuildNumber = 10235,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-26", Summary = "Zombie Bears & General Bugfix Patch" },
            new() { Version = "1.0.3",  BuildNumber = 10057,  IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-19", Summary = "Inferno Tuning, IAS Nerf & Repair Cost Patch" },
            new() { Version = "1.0.2c", BuildNumber = 9991,   IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-12", Summary = "Mac/PC Client Stability Patch" },
            new() { Version = "1.0.2b", BuildNumber = 9950,   IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-07", Summary = "Login & Auction House Responsiveness Patch" },
            new() { Version = "1.0.2a", BuildNumber = 9858,   IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-01", Summary = "Item & Skill Tooltip Fix Patch" },
            new() { Version = "1.0.2",  BuildNumber = 9749,   IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-05-29", Summary = "First Post-Launch Client Patch" },
            new() { Version = "1.0.1",  BuildNumber = 9558,   IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-05-15", Summary = "Original May 15, 2012 Launch Day Patch" },
            new() { Version = "1.0.0",  BuildNumber = 8370,   IsCasc = false, IsRoS = false, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-05-14", Summary = "Original Unpatched DVD/Retail Base MPQ Build" },
            new() { Version = "Beta",   BuildNumber = 7162,   IsCasc = false, IsRoS = false, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "", ReleaseDate = "2011-09-20", Summary = "Original Vanilla Friends & Family Beta Client" }
        };

        public static D3BuildCatalogItem Resolve(string versionOrBuild)
        {
            if (string.IsNullOrWhiteSpace(versionOrBuild) ||
                versionOrBuild.Equals("live", StringComparison.OrdinalIgnoreCase) ||
                versionOrBuild.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                return AllBuilds[0];
            }

            string clean = versionOrBuild.Trim();
            foreach (var item in AllBuilds)
            {
                if (item.BuildNumber.ToString() == clean ||
                    item.Version.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                    $"{item.Version}.{item.BuildNumber}".Equals(clean, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            return null;
        }
    }

    public sealed class MfilEntry
    {
        public string RelativePath { get; set; }
        public long Size { get; set; }
        public int FileVersion { get; set; }
        public int Flags { get; set; }
        public string Tag { get; set; }
    }

    public static class LegacyMpqInstaller
    {
        public static List<MfilEntry> ParseMfil(string mfilContent)
        {
            var list = new List<MfilEntry>();
            MfilEntry current = null;

            foreach (string rawLine in mfilContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (rawLine.StartsWith("file=", StringComparison.OrdinalIgnoreCase))
                {
                    if (current != null && !string.IsNullOrWhiteSpace(current.RelativePath))
                        list.Add(current);
                    current = new MfilEntry
                    {
                        RelativePath = rawLine.Substring(5).Trim()
                    };
                }
                else if (current != null && rawLine.StartsWith("\t"))
                {
                    string trimmed = rawLine.Trim();
                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = trimmed.Substring(0, eq);
                    string v = trimmed.Substring(eq + 1);
                    switch (k.ToLowerInvariant())
                    {
                        case "size":
                            if (long.TryParse(v, out long sz)) current.Size = sz;
                            break;
                        case "fileversion":
                            if (int.TryParse(v, out int fv)) current.FileVersion = fv;
                            break;
                        case "flags":
                            if (int.TryParse(v, out int fl)) current.Flags = fl;
                            break;
                        case "tag":
                            current.Tag = v;
                            break;
                    }
                }
            }

            if (current != null && !string.IsNullOrWhiteSpace(current.RelativePath))
                list.Add(current);

            return list;
        }

        public static async Task InstallLegacyMpqBuildAsync(
            HttpClient http,
            string targetDir,
            D3BuildCatalogItem build,
            string locale,
            bool binariesOnly,
            int maxConcurrency,
            Action<string> log,
            Action<double, string> onProgress = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(targetDir);
            string mpqRoot = Path.Combine(targetDir, "Data_D3", "PC", "MPQs");
            Directory.CreateDirectory(mpqRoot);

            List<MfilEntry> allMfilEntries = new();
            if (!string.IsNullOrWhiteSpace(build.MfilFileName))
            {
                string mfilUrl = $"{build.BaseDirectUrl}/{build.MfilFileName}";
                log($"[1/4] Fetching Blizzard .mfil manifest from {mfilUrl}...");
                onProgress?.Invoke(2, "Fetching Blizzard .mfil manifest...");
                try
                {
                    string mfilText = await http.GetStringAsync(mfilUrl, ct);
                    await File.WriteAllTextAsync(Path.Combine(targetDir, "Diablo III.mfil"), mfilText, Encoding.UTF8, ct);
                    await File.WriteAllTextAsync(Path.Combine(targetDir, build.MfilFileName), mfilText, Encoding.UTF8, ct);
                    allMfilEntries = ParseMfil(mfilText);
                    log($"      Parsed {allMfilEntries.Count} manifest entries across build archives.");
                }
                catch (Exception ex)
                {
                    log($"      Notice: Could not load .mfil directly ({ex.Message}), proceeding with direct binary extraction.");
                }
            }
            else
            {
                log($"[1/4] Historical base beta archive: manifest not required for client binaries.");
            }
            await File.WriteAllTextAsync(Path.Combine(targetDir, "Launcher.db"), locale, Encoding.ASCII, ct);

            // Step 2: Download base-Win.mpq (always needed to extract & patch binaries)
            string baseWinMpqPath = Path.Combine(mpqRoot, "base-Win.mpq");
            string baseWinUrl = $"{build.BaseDirectUrl}/Data_D3/PC/MPQs/base-Win.mpq";
            log($"[2/4] Downloading base-Win.mpq from Blizzard CDN ({build.BaseDirectUrl})...");
            onProgress?.Invoke(5, "Downloading base-Win.mpq...");
            await DownloadFileWithResumeAsync(http, baseWinUrl, baseWinMpqPath, -1L, ct);

            byte[] baseWinBytes = await File.ReadAllBytesAsync(baseWinMpqPath, ct);
            var baseMpq = new MpqArchiveReader(baseWinBytes);

            // Extract base binaries from base-Win.mpq
            log($"      Extracting base {build.BaseBuildNumber} binaries from base-Win.mpq...");
            string[] baseFiles = baseMpq.GetListfileEntries();
            int extractedBase = 0;
            foreach (string relFile in baseFiles)
            {
                if (relFile.Equals("base-Win-md5.lst", StringComparison.OrdinalIgnoreCase))
                    continue;
                byte[] data = baseMpq.ReadFile(relFile);
                if (data == null) continue;
                string destPath = Path.Combine(targetDir, relFile.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                await File.WriteAllBytesAsync(destPath, data, ct);
                extractedBase++;
            }
            log($"      Extracted {extractedBase} base binaries ({build.BaseBuildNumber}).");

            // Step 3: If TargetBuild > BaseBuildNumber, download Updates/d3-0-{BuildNumber}-Win-final.MPQ and apply diffs
            if (build.BuildNumber > build.BaseBuildNumber)
            {
                string patchMpqName = $"d3-0-{build.BuildNumber}-Win-final.MPQ";
                string updatesDir = Path.Combine(targetDir, "Updates");
                Directory.CreateDirectory(updatesDir);
                string patchMpqPath = Path.Combine(updatesDir, patchMpqName);
                string patchMpqUrl = $"{build.BaseDirectUrl}/Updates/{patchMpqName}";

                log($"[3/4] Downloading binary diff archive Updates/{patchMpqName} for {build.Version}.{build.BuildNumber}...");
                onProgress?.Invoke(12, $"Downloading {patchMpqName}...");
                await DownloadFileWithResumeAsync(http, patchMpqUrl, patchMpqPath, -1, ct);

                byte[] patchMpqBytes = await File.ReadAllBytesAsync(patchMpqPath, ct);
                var patchMpq = new MpqArchiveReader(patchMpqBytes);

                // Process delete.lst (removes obsoleted DLLs)
                byte[] deleteLstBytes = patchMpq.ReadFile("delete.lst");
                if (deleteLstBytes != null)
                {
                    string deleteText = Encoding.UTF8.GetString(deleteLstBytes);
                    foreach (string rawLine in deleteText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = rawLine.Trim();
                        if (line.Length == 0 || line.StartsWith("*") || line.StartsWith("Updates\\", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string delPath = Path.Combine(targetDir, line.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                        if (File.Exists(delPath))
                        {
                            try { File.Delete(delPath); } catch { }
                        }
                    }
                }

                using var md5 = MD5.Create();
                byte[] patchLstBytes = patchMpq.ReadFile("patch.lst");
                int patchedCount = 0;

                if (patchLstBytes != null)
                {
                    // Vanilla-style patch.lst mapping
                    string patchLstText = Encoding.UTF8.GetString(patchLstBytes);
                    foreach (string rawLine in patchLstText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = rawLine.Trim().Split(';');
                        if (parts.Length < 2) continue;
                        string targetRelPath = parts[0].Trim();
                        string patchArchiveEntry = parts[1].Trim();

                        byte[] ptchData = patchMpq.ReadFile(patchArchiveEntry)
                            ?? throw new FileNotFoundException($"Patch entry '{patchArchiveEntry}' not found in {patchMpqName}.");

                        byte[] oldData = baseMpq.ReadFile(targetRelPath);
                        byte[] newData = BlizzardPtchPatcher.ApplyPtch(oldData, ptchData, out string expectedMd5Hex);

                        string actualMd5Hex = Convert.ToHexString(md5.ComputeHash(newData));
                        if (!string.Equals(expectedMd5Hex, actualMd5Hex, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"MD5 mismatch after patching '{targetRelPath}': expected {expectedMd5Hex}, got {actualMd5Hex}.");
                        }

                        string outPath = Path.Combine(targetDir, targetRelPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                        await File.WriteAllBytesAsync(outPath, newData, ct);
                        patchedCount++;
                        log($"      [PTCH OK] {targetRelPath,-36} ({newData.Length / 1024.0,8:F1} KB | MD5: {actualMd5Hex.Substring(0, 12)}...)");
                    }
                }
                else
                {
                    // RoS-style direct MPQ_FILE_PATCH_FILE & replacement entries
                    string[] patchEntries = patchMpq.GetListfileEntries();
                    foreach (string entry in patchEntries)
                    {
                        if (entry.Equals("(listfile)", StringComparison.OrdinalIgnoreCase) ||
                            entry.Equals("(attributes)", StringComparison.OrdinalIgnoreCase) ||
                            entry.Equals("delete.lst", StringComparison.OrdinalIgnoreCase) ||
                            entry.StartsWith("Updates\\", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string outPath = Path.Combine(targetDir, entry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                        if (patchMpq.IsPatchFile(entry))
                        {
                            byte[] ptchData = patchMpq.ReadFile(entry);
                            byte[] oldData = baseMpq.ReadFile(entry);
                            if (oldData != null && ptchData != null)
                            {
                                byte[] newData = BlizzardPtchPatcher.ApplyPtch(oldData, ptchData, out string expectedMd5Hex);
                                string actualMd5Hex = Convert.ToHexString(md5.ComputeHash(newData));
                                await File.WriteAllBytesAsync(outPath, newData, ct);
                                patchedCount++;
                                log($"      [PTCH OK] {entry,-36} ({newData.Length / 1024.0,8:F1} KB | MD5: {actualMd5Hex.Substring(0, 12)}...)");
                            }
                        }
                        else
                        {
                            byte[] fileData = patchMpq.ReadFile(entry);
                            if (fileData != null)
                            {
                                await File.WriteAllBytesAsync(outPath, fileData, ct);
                                patchedCount++;
                                log($"      [FILE OK] {entry,-36} ({fileData.Length / 1024.0,8:F1} KB)");
                            }
                        }
                    }
                }

                // Extract locale InstallCD metadata if present in patch MPQ
                foreach (string entry in patchMpq.GetListfileEntries())
                {
                    if (entry.StartsWith(locale + "\\", StringComparison.OrdinalIgnoreCase) ||
                        entry.StartsWith("InstallCD\\", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] entryBytes = patchMpq.ReadFile(entry);
                        if (entryBytes != null)
                        {
                            string outPath = Path.Combine(targetDir, entry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                            await File.WriteAllBytesAsync(outPath, entryBytes, ct);
                        }
                    }
                }

                log($"      Successfully patched and verified {patchedCount} binaries for Diablo III {build.Version}.{build.BuildNumber}!");
            }
            else
            {
                log($"[3/4] Target is base build {build.BaseBuildNumber} — no binary diff patch required.");
            }

            if (binariesOnly || allMfilEntries.Count == 0)
            {
                onProgress?.Invoke(100, $"Binaries ready for {build.Version}.{build.BuildNumber}!");
                log($"[4/4] Binaries-Only mode selected: skipping multi-GB Data_D3 MPQ archives.");
                log("========================================================================");
                log($" SUCCESS: Diablo III {build.Version}.{build.BuildNumber} binaries ready in {targetDir}");
                log("========================================================================");
                return;
            }

            // Step 4: Full Production Client Mode: download multi-GB MPQs listed in .mfil
            var targetEntries = allMfilEntries
                .Where(e => (string.IsNullOrWhiteSpace(e.Tag) ||
                             e.Tag.Equals("base", StringComparison.OrdinalIgnoreCase) ||
                             e.Tag.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                             e.Tag.Equals(locale, StringComparison.OrdinalIgnoreCase)) &&
                            !e.RelativePath.EndsWith("base-Win.mpq", StringComparison.OrdinalIgnoreCase) &&
                            !e.RelativePath.StartsWith("Updates", StringComparison.OrdinalIgnoreCase))
                .ToList();

            long totalBytes = targetEntries.Sum(e => e.Size);
            log($"[4/4] Downloading {targetEntries.Count} core MPQ archives ({totalBytes / (1024.0 * 1024 * 1024):F2} GB total)...");

            long completedBytes = 0;
            var sem = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new List<Task>();

            foreach (var entry in targetEntries)
            {
                string fileDest = Path.Combine(targetDir, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                string fileUrl = $"{build.BaseDirectUrl}/{entry.RelativePath.Replace('\\', '/')}";

                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        await DownloadFileWithResumeAsync(http, fileUrl, fileDest, entry.Size, ct);
                        long cur = Interlocked.Add(ref completedBytes, entry.Size);
                        double pct = 15.0 + (cur / (double)totalBytes) * 85.0;
                        string fn = Path.GetFileName(fileDest);
                        onProgress?.Invoke(pct, $"[{pct:F1}%] Downloaded {fn} ({entry.Size / (1024.0 * 1024):F1} MB)");
                    }
                    finally
                    {
                        sem.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
            onProgress?.Invoke(100, $"All files downloaded for {build.Version}.{build.BuildNumber}!");
            log("========================================================================");
            log($" SUCCESS: Full Diablo III {build.Version}.{build.BuildNumber} client successfully installed!");
            log("========================================================================");
        }

        private static async Task DownloadFileWithResumeAsync(
            HttpClient http,
            string url,
            string destPath,
            long expectedSize,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));

            long existingLength = 0;
            if (File.Exists(destPath))
            {
                var fi = new FileInfo(destPath);
                existingLength = fi.Length;
                if (expectedSize > 0 && existingLength == expectedSize)
                    return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingLength > 0)
            {
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
            }

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                return;

            resp.EnsureSuccessStatusCode();

            FileMode mode = (existingLength > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent)
                ? FileMode.Append
                : FileMode.Create;

            await using var inStream = await resp.Content.ReadAsStreamAsync(ct);
            await using var outStream = new FileStream(destPath, mode, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
            await inStream.CopyToAsync(outStream, ct);
        }
    }

    internal sealed class MpqArchiveReader
    {
        private static readonly uint[] StormBuffer = new uint[0x500];

        static MpqArchiveReader()
        {
            uint seed = 0x00100001;
            for (uint index1 = 0; index1 < 0x100; index1++)
            {
                uint index2 = index1;
                for (int i = 0; i < 5; i++, index2 += 0x100)
                {
                    seed = (seed * 125 + 3) % 0x2AAAAB;
                    uint temp1 = (seed & 0xFFFF) << 0x10;
                    seed = (seed * 125 + 3) % 0x2AAAAB;
                    uint temp2 = (seed & 0xFFFF);
                    StormBuffer[index2] = temp1 | temp2;
                }
            }
        }

        private readonly byte[] _archive;
        private readonly int _hashPos;
        private readonly int _blockPos;
        private readonly int _hashCount;
        private readonly int _blockCount;
        private readonly int _sectorSize;

        public MpqArchiveReader(byte[] archiveBytes)
        {
            _archive = (byte[])archiveBytes.Clone();
            ushort blockShift = BinaryPrimitives.ReadUInt16LittleEndian(_archive.AsSpan(14, 2));
            _sectorSize = 512 << blockShift;
            _hashPos = (int)BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(16, 4));
            _blockPos = (int)BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(20, 4));
            _hashCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(24, 4));
            _blockCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(28, 4));

            DecryptTable(_archive, _hashPos, _hashCount * 4, "(hash table)");
            DecryptTable(_archive, _blockPos, _blockCount * 4, "(block table)");
        }

        public string[] GetListfileEntries()
        {
            byte[] lf = ReadFile("(listfile)");
            if (lf == null) return Array.Empty<string>();
            return Encoding.UTF8.GetString(lf)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public bool IsPatchFile(string fileName)
        {
            uint hA = HashString(fileName, 0x100);
            uint hB = HashString(fileName, 0x200);
            for (int i = 0; i < _hashCount; i++)
            {
                int hOff = _hashPos + i * 16;
                uint eA = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(hOff, 4));
                uint eB = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(hOff + 4, 4));
                uint blk = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(hOff + 12, 4));
                if (eA == hA && eB == hB && blk < (uint)_blockCount)
                {
                    int bOff = _blockPos + (int)blk * 16;
                    uint flags = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(bOff + 12, 4));
                    return (flags & 0x00100000u) != 0;
                }
            }
            return false;
        }

        public byte[] ReadFile(string fileName)
        {
            uint hA = HashString(fileName, 0x100);
            uint hB = HashString(fileName, 0x200);
            for (int i = 0; i < _hashCount; i++)
            {
                int hOff = _hashPos + i * 16;
                uint eA = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(hOff, 4));
                uint eB = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(hOff + 4, 4));
                uint blk = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(hOff + 12, 4));
                if (eA == hA && eB == hB && blk < (uint)_blockCount)
                {
                    return ReadBlock((int)blk);
                }
            }
            return null;
        }

        private byte[] ReadBlock(int blockIndex)
        {
            int bOff = _blockPos + blockIndex * 16;
            int fPos = (int)BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(bOff, 4));
            int cSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(bOff + 4, 4));
            int uSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(bOff + 8, 4));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(bOff + 12, 4));

            bool singleUnit = (flags & 0x01000000u) != 0;
            bool compressed = (flags & 0x00000200u) != 0;
            bool hasCrc = (flags & 0x04000000u) != 0;
            bool isPatch = (flags & 0x00100000u) != 0;

            if (isPatch)
            {
                uint patchHeaderLen = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(fPos, 4));
                uint patchDataSize = BinaryPrimitives.ReadUInt32LittleEndian(_archive.AsSpan(fPos + 8, 4));
                int dataPos = fPos + (int)patchHeaderLen;
                int dataCSize = cSize - (int)patchHeaderLen;
                return DecompressSector(_archive, dataPos, dataCSize, (int)patchDataSize);
            }

            if (!compressed)
            {
                byte[] raw = new byte[uSize];
                Buffer.BlockCopy(_archive, fPos, raw, 0, uSize);
                return raw;
            }

            if (singleUnit)
            {
                return DecompressSector(_archive, fPos, cSize, uSize);
            }

            int numSectors = (uSize + _sectorSize - 1) / _sectorSize;
            int tableEntries = numSectors + 1 + (hasCrc ? 1 : 0);
            int[] offsets = new int[tableEntries];
            for (int k = 0; k < tableEntries; k++)
            {
                offsets[k] = BinaryPrimitives.ReadInt32LittleEndian(_archive.AsSpan(fPos + k * 4, 4));
            }

            using var outMs = new MemoryStream(uSize);
            for (int s = 0; s < numSectors; s++)
            {
                int sStart = fPos + offsets[s];
                int sLen = offsets[s + 1] - offsets[s];
                int remU = Math.Min(_sectorSize, uSize - s * _sectorSize);
                byte[] dec = DecompressSector(_archive, sStart, sLen, remU);
                outMs.Write(dec, 0, dec.Length);
            }
            return outMs.ToArray();
        }

        private static byte[] DecompressSector(byte[] archive, int pos, int cSize, int uSize)
        {
            if (cSize == uSize)
            {
                byte[] raw = new byte[uSize];
                Buffer.BlockCopy(archive, pos, raw, 0, uSize);
                return raw;
            }

            byte compType = archive[pos];
            if (compType == 0x02) // Zlib
            {
                using var ms = new MemoryStream(archive, pos + 3, cSize - 3);
                using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream(uSize);
                ds.CopyTo(outMs);
                return outMs.ToArray();
            }
            if (compType == 0x12) // MPQ LZMA
            {
                return MpqLzmaDecoder.Decompress(archive, pos, cSize, uSize);
            }
            throw new NotSupportedException($"Unsupported MPQ compression type 0x{compType:X2}.");
        }

        private static uint HashString(string input, uint offset)
        {
            uint seed1 = 0x7FED7FED;
            uint seed2 = 0xEEEEEEEE;
            foreach (char rawChar in input.ToUpperInvariant())
            {
                char c = rawChar == '/' ? '\\' : rawChar;
                uint val = c;
                seed1 = StormBuffer[offset + val] ^ (seed1 + seed2);
                seed2 = val + seed1 + seed2 + (seed2 << 5) + 3;
            }
            return seed1;
        }

        private static void DecryptTable(byte[] data, int offset, int countUints, string keyStr)
        {
            uint seed1 = HashString(keyStr, 0x300);
            uint seed2 = 0xEEEEEEEE;
            for (int i = 0; i < countUints; i++)
            {
                seed2 += StormBuffer[0x400 + (seed1 & 0xFF)];
                uint val = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + i * 4, 4)) ^ (seed1 + seed2);
                seed1 = ((~seed1 << 0x15) + 0x11111111u) | (seed1 >> 0x0B);
                seed2 = val + seed2 + (seed2 << 5) + 3;
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + i * 4, 4), val);
            }
        }
    }

    internal static class BlizzardPtchPatcher
    {
        public static byte[] ApplyPtch(byte[] oldData, byte[] ptch, out string expectedMd5Hex)
        {
            int sizeAfter = BinaryPrimitives.ReadInt32LittleEndian(ptch.AsSpan(12, 4));
            expectedMd5Hex = Convert.ToHexString(ptch, 40, 16);
            string xfrmType = Encoding.ASCII.GetString(ptch, 64, 4);

            if (xfrmType == "COPY")
            {
                byte[] copy = new byte[sizeAfter];
                Buffer.BlockCopy(ptch, 68, copy, 0, sizeAfter);
                return copy;
            }

            if (xfrmType == "BSD0")
            {
                return ApplyBsd0(oldData, ptch.AsSpan(68).ToArray(), sizeAfter);
            }

            throw new NotSupportedException($"Unsupported PTCH transform '{xfrmType}'.");
        }

        private static byte[] ApplyBsd0(byte[] oldData, byte[] bsd0, int sizeAfter)
        {
            int controlLen = BinaryPrimitives.ReadInt32LittleEndian(bsd0.AsSpan(8, 4));
            int diffLen = BinaryPrimitives.ReadInt32LittleEndian(bsd0.AsSpan(16, 4));
            int newSize = BinaryPrimitives.ReadInt32LittleEndian(bsd0.AsSpan(24, 4));

            byte[] controlBlock = DecompressBsd0Stream(bsd0, 32, controlLen);
            byte[] diffBlock = DecompressBsd0Stream(bsd0, 32 + controlLen, diffLen);
            byte[] extraBlock = DecompressBsd0Stream(bsd0, 32 + controlLen + diffLen, bsd0.Length - 32 - controlLen - diffLen);

            byte[] newData = new byte[newSize];
            int oldPos = 0;
            int newPos = 0;
            int ctrlPos = 0;
            int diffPos = 0;
            int extraPos = 0;

            while (newPos < newSize && ctrlPos + 24 <= controlLen)
            {
                long addLen = BinaryPrimitives.ReadInt64LittleEndian(controlBlock.AsSpan(ctrlPos, 8));
                long copyLen = BinaryPrimitives.ReadInt64LittleEndian(controlBlock.AsSpan(ctrlPos + 8, 8));
                long seekDelta = BinaryPrimitives.ReadInt64LittleEndian(controlBlock.AsSpan(ctrlPos + 16, 8));
                ctrlPos += 24;

                for (int i = 0; i < addLen; i++)
                {
                    byte oldByte = (oldPos + i >= 0 && oldPos + i < oldData.Length) ? oldData[oldPos + i] : (byte)0;
                    byte diffByte = diffPos < diffBlock.Length ? diffBlock[diffPos++] : (byte)0;
                    newData[newPos + i] = (byte)(oldByte + diffByte);
                }
                newPos += (int)addLen;
                oldPos += (int)addLen;

                for (int i = 0; i < copyLen; i++)
                {
                    newData[newPos + i] = extraPos < extraBlock.Length ? extraBlock[extraPos++] : (byte)0;
                }
                newPos += (int)copyLen;
                oldPos += (int)seekDelta;
            }

            return newData;
        }

        private static byte[] DecompressBsd0Stream(byte[] raw, int offset, int length)
        {
            if (length <= 0) return Array.Empty<byte>();
            using var ms = new MemoryStream(raw, offset, length);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            ds.CopyTo(outMs);
            return outMs.ToArray();
        }
    }

    internal static class MpqLzmaDecoder
    {
        public static byte[] Decompress(byte[] archive, int pos, int cSize, int uSize)
        {
            if (archive[pos] == 0x12)
            {
                pos += 2;
                cSize -= 2;
            }

            byte prop = archive[pos];
            int lc = prop % 9;
            int rem = prop / 9;
            int pb = rem / 5;
            int lp = rem % 5;
            int posStateMask = (1 << pb) - 1;

            var rc = new RangeDecoder(archive, pos + 4);
            var litProbs = InitProbs(0x300 << (lc + lp));
            var isMatch = InitProbs(192);
            var isRep = InitProbs(12);
            var isRepG0 = InitProbs(12);
            var isRepG1 = InitProbs(12);
            var isRepG2 = InitProbs(12);
            var isRep0Long = InitProbs(192);
            var posSlotDecoder = new ushort[4][];
            for (int i = 0; i < 4; i++) posSlotDecoder[i] = InitProbs(64);
            var posDecoders = InitProbs(115);
            var alignDecoder = InitProbs(16);

            var lenDecoder = new LenDecoder(1 << pb);
            var repLenDecoder = new LenDecoder(1 << pb);

            byte[] outBuf = new byte[uSize];
            int outPos = 0;
            int state = 0;
            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;

            while (outPos < uSize)
            {
                int posState = outPos & posStateMask;
                int matchProbIdx = (state << 4) + posState;

                if (rc.DecodeBit(isMatch, matchProbIdx) == 0)
                {
                    byte prevByte = outPos > 0 ? outBuf[outPos - 1] : (byte)0;
                    int litState = ((outPos & ((1 << lp) - 1)) << lc) + (prevByte >> (8 - lc));
                    int litProbOffset = 0x300 * litState;

                    int b = 1;
                    if (state < 7)
                    {
                        while (b < 0x100)
                            b = (b << 1) | rc.DecodeBit(litProbs, litProbOffset + b);
                    }
                    else
                    {
                        byte matchByte = outBuf[outPos - (int)rep0 - 1];
                        while (b < 0x100)
                        {
                            int matchBit = (matchByte >> 7) & 1;
                            matchByte <<= 1;
                            int bit = rc.DecodeBit(litProbs, litProbOffset + 0x100 + (matchBit << 8) + b);
                            b = (b << 1) | bit;
                            if (matchBit != bit)
                            {
                                while (b < 0x100)
                                    b = (b << 1) | rc.DecodeBit(litProbs, litProbOffset + b);
                                break;
                            }
                        }
                    }

                    outBuf[outPos++] = (byte)b;
                    state = state < 4 ? 0 : (state < 10 ? state - 3 : state - 6);
                }
                else
                {
                    int len;
                    if (rc.DecodeBit(isRep, state) == 1)
                    {
                        if (rc.DecodeBit(isRepG0, state) == 0)
                        {
                            if (rc.DecodeBit(isRep0Long, matchProbIdx) == 0)
                            {
                                state = state < 7 ? 9 : 11;
                                outBuf[outPos] = outBuf[outPos - (int)rep0 - 1];
                                outPos++;
                                continue;
                            }
                        }
                        else
                        {
                            uint dist;
                            if (rc.DecodeBit(isRepG1, state) == 0)
                            {
                                dist = rep1;
                            }
                            else
                            {
                                if (rc.DecodeBit(isRepG2, state) == 0)
                                {
                                    dist = rep2;
                                }
                                else
                                {
                                    dist = rep3;
                                    rep3 = rep2;
                                }
                                rep2 = rep1;
                            }
                            rep1 = rep0;
                            rep0 = dist;
                        }
                        len = repLenDecoder.Decode(rc, posState) + 2;
                        state = state < 7 ? 8 : 11;
                    }
                    else
                    {
                        rep3 = rep2;
                        rep2 = rep1;
                        rep1 = rep0;
                        state = state < 7 ? 7 : 10;
                        len = lenDecoder.Decode(rc, posState) + 2;

                        int posSlot = rc.DecodeBitTree(posSlotDecoder[len < 6 ? len - 2 : 3], 0, 6);
                        if (posSlot >= 4)
                        {
                            int numDirectBits = (posSlot >> 1) - 1;
                            uint dist = (2 | ((uint)posSlot & 1)) << numDirectBits;
                            if (posSlot < 14)
                            {
                                dist += (uint)rc.ReverseDecode(posDecoders, (int)(dist - (uint)posSlot - 1), numDirectBits);
                            }
                            else
                            {
                                dist += (rc.DecodeDirectBits(numDirectBits - 4) << 4);
                                dist += (uint)rc.ReverseDecode(alignDecoder, 0, 4);
                            }
                            rep0 = dist;
                        }
                        else
                        {
                            rep0 = (uint)posSlot;
                        }
                    }

                    for (int i = 0; i < len && outPos < uSize; i++)
                    {
                        outBuf[outPos] = outBuf[outPos - (int)rep0 - 1];
                        outPos++;
                    }
                }
            }

            return outBuf;
        }

        private static ushort[] InitProbs(int count)
        {
            var arr = new ushort[count];
            Array.Fill(arr, (ushort)1024);
            return arr;
        }

        private sealed class RangeDecoder
        {
            private readonly byte[] _buf;
            private int _pos;
            private uint _range = 0xFFFFFFFF;
            private uint _code;

            public RangeDecoder(byte[] buf, int pos)
            {
                _buf = buf;
                _pos = pos + 1;
                for (int i = 0; i < 4; i++) _code = (_code << 8) | _buf[_pos++];
            }

            public int DecodeBit(ushort[] probs, int index)
            {
                uint prob = probs[index];
                uint bound = (_range >> 11) * prob;
                if (_code < bound)
                {
                    _range = bound;
                    probs[index] = (ushort)(prob + ((2048 - prob) >> 5));
                    if (_range < 0x01000000u) { _code = (_code << 8) | _buf[_pos++]; _range <<= 8; }
                    return 0;
                }
                else
                {
                    _range -= bound;
                    _code -= bound;
                    probs[index] = (ushort)(prob - (prob >> 5));
                    if (_range < 0x01000000u) { _code = (_code << 8) | _buf[_pos++]; _range <<= 8; }
                    return 1;
                }
            }

            public int DecodeBitTree(ushort[] probs, int offset, int numBits)
            {
                int m = 1;
                for (int i = 0; i < numBits; i++) m = (m << 1) | DecodeBit(probs, offset + m);
                return m - (1 << numBits);
            }

            public uint DecodeDirectBits(int numBits)
            {
                uint res = 0;
                for (int i = numBits; i > 0; i--)
                {
                    _range >>= 1;
                    uint t = (_code - _range) >> 31;
                    _code -= _range & (t - 1);
                    res = (res << 1) | (1 - t);
                    if (_range < 0x01000000u) { _code = (_code << 8) | _buf[_pos++]; _range <<= 8; }
                }
                return res;
            }

            public int ReverseDecode(ushort[] probs, int offset, int numBits)
            {
                int m = 1, symbol = 0;
                for (int i = 0; i < numBits; i++)
                {
                    int bit = DecodeBit(probs, offset + m);
                    m = (m << 1) | bit;
                    symbol |= bit << i;
                }
                return symbol;
            }
        }

        private sealed class LenDecoder
        {
            private readonly ushort[] _choice = InitProbs(2);
            private readonly ushort[] _low;
            private readonly ushort[] _mid;
            private readonly ushort[] _high = InitProbs(256);

            public LenDecoder(int numPosStates)
            {
                _low = InitProbs(numPosStates * 8);
                _mid = InitProbs(numPosStates * 8);
            }

            public int Decode(RangeDecoder rc, int posState)
            {
                if (rc.DecodeBit(_choice, 0) == 0) return rc.DecodeBitTree(_low, posState * 8, 3);
                if (rc.DecodeBit(_choice, 1) == 0) return 8 + rc.DecodeBitTree(_mid, posState * 8, 3);
                return 16 + rc.DecodeBitTree(_high, 0, 8);
            }
        }
    }
}

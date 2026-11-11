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
        public string ProductCode { get; init; } = "d3";
        public string ReleaseDate { get; init; }
        public string Summary { get; init; }

        public string DisplayTitle => IsCasc
            ? $"{Version}.{BuildNumber} — [CASC / {ProductCode} 64-bit] ({Summary})"
            : $"{Version}.{BuildNumber} — [MPQ / Vanilla 32-bit] ({ReleaseDate} - {Summary})";

        public override string ToString() => DisplayTitle;
    }

    public static class D3BuildCatalog
    {
        public const string LegacyDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod-retail/NA/8370.direct";
        public const string LegacyMfilFileName = "d3-16603-5A26B6C69DFDB16C25E1D05B0EEE83C2.mfil";
        public const string LegacyMfilUrl = LegacyDirectBaseUrl + "/" + LegacyMfilFileName;

        public static readonly IReadOnlyList<D3BuildCatalogItem> AllBuilds = new List<D3BuildCatalogItem>
        {
            new() { Version = "2.8.0",  BuildNumber = 99920,  IsCasc = true,  ProductCode = "d3",   ReleaseDate = "Live Retail", Summary = "Global Live Reaper of Souls Client (d3)" },
            new() { Version = "2.7.8",  BuildNumber = 96670,  IsCasc = true,  ProductCode = "d3t",  ReleaseDate = "PTR CDN",     Summary = "Patch 2.7.8 Public Test Realm Client (d3t)" },
            new() { Version = "2.8.1",  BuildNumber = 101167, IsCasc = true,  ProductCode = "d3cn", ReleaseDate = "CN CDN",      Summary = "Patch 2.8.1 Reaper of Souls Client (d3cn)" },
            new() { Version = "1.0.8a", BuildNumber = 16603,  IsCasc = false, ReleaseDate = "2013-05-10", Summary = "Final Vanilla 1.0.8 Hotfix Build" },
            new() { Version = "1.0.8",  BuildNumber = 16416, IsCasc = false, ReleaseDate = "2013-05-07", Summary = "Monster Density & Co-op XP Bonus Patch" },
            new() { Version = "1.0.7a", BuildNumber = 15295, IsCasc = false, ReleaseDate = "2013-02-15", Summary = "Duelling & Demon Hunter Hotfix" },
            new() { Version = "1.0.7",  BuildNumber = 14633, IsCasc = false, ReleaseDate = "2013-02-12", Summary = "Brawling, Marquise Gems & BoA Crafting" },
            new() { Version = "1.0.6a", BuildNumber = 13644, IsCasc = false, ReleaseDate = "2012-12-06", Summary = "Quest & Stability Patch" },
            new() { Version = "1.0.6",  BuildNumber = 13300, IsCasc = false, ReleaseDate = "2012-11-27", Summary = "Network & Bugfix Patch" },
            new() { Version = "1.0.5a", BuildNumber = 12811, IsCasc = false, ReleaseDate = "2012-10-25", Summary = "Crash & Achievement Fix Patch" },
            new() { Version = "1.0.5",  BuildNumber = 12480, IsCasc = false, ReleaseDate = "2012-10-16", Summary = "Monster Power (MP1-10) & Infernal Machine" },
            new() { Version = "1.0.4",  BuildNumber = 11327, IsCasc = false, ReleaseDate = "2012-08-21", Summary = "Paragon 1.0 (Levels 1-100) & Legendary Overhaul" },
            new() { Version = "1.0.3b", BuildNumber = 10485, IsCasc = false, ReleaseDate = "2012-07-10", Summary = "Auction House & Stability Patch" },
            new() { Version = "1.0.3a", BuildNumber = 10235, IsCasc = false, ReleaseDate = "2012-06-26", Summary = "Zombie Bears & General Bugfix Patch" },
            new() { Version = "1.0.3",  BuildNumber = 10057, IsCasc = false, ReleaseDate = "2012-06-19", Summary = "Inferno Tuning, IAS Nerf & Repair Cost Patch" },
            new() { Version = "1.0.2c", BuildNumber = 9991,  IsCasc = false, ReleaseDate = "2012-06-12", Summary = "Mac/PC Client Stability Patch" },
            new() { Version = "1.0.2b", BuildNumber = 9950,  IsCasc = false, ReleaseDate = "2012-06-07", Summary = "Login & Auction House Responsiveness Patch" },
            new() { Version = "1.0.2a", BuildNumber = 9858,  IsCasc = false, ReleaseDate = "2012-06-01", Summary = "Item & Skill Tooltip Fix Patch" },
            new() { Version = "1.0.2",  BuildNumber = 9749,  IsCasc = false, ReleaseDate = "2012-05-29", Summary = "First Post-Launch Client Patch" },
            new() { Version = "1.0.1",  BuildNumber = 9558,  IsCasc = false, ReleaseDate = "2012-05-15", Summary = "Original May 15, 2012 Launch Day Patch" },
            new() { Version = "1.0.0",  BuildNumber = 8370,  IsCasc = false, ReleaseDate = "2012-05-14", Summary = "Original Unpatched DVD/Retail Base MPQ Build" }
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

            log($"[1/4] Fetching Blizzard .mfil manifest from {D3BuildCatalog.LegacyMfilUrl}...");
            onProgress?.Invoke(2, "Fetching Blizzard .mfil manifest...");
            string mfilText = await http.GetStringAsync(D3BuildCatalog.LegacyMfilUrl, ct);
            await File.WriteAllTextAsync(Path.Combine(targetDir, "Diablo III.mfil"), mfilText, Encoding.UTF8, ct);
            await File.WriteAllTextAsync(Path.Combine(targetDir, D3BuildCatalog.LegacyMfilFileName), mfilText, Encoding.UTF8, ct);
            await File.WriteAllTextAsync(Path.Combine(targetDir, "Launcher.db"), locale, Encoding.ASCII, ct);

            var allMfilEntries = ParseMfil(mfilText);
            log($"      Parsed {allMfilEntries.Count} manifest entries across builds 8370..16603.");

            // Step 2: Download base-Win.mpq (always needed to extract & patch binaries)
            string baseWinMpqPath = Path.Combine(mpqRoot, "base-Win.mpq");
            string baseWinUrl = $"{D3BuildCatalog.LegacyDirectBaseUrl}/Data_D3/PC/MPQs/base-Win.mpq";
            log($"[2/4] Downloading base-Win.mpq (19.2 MB) from Blizzard CDN...");
            onProgress?.Invoke(5, "Downloading base-Win.mpq...");
            await DownloadFileWithResumeAsync(http, baseWinUrl, baseWinMpqPath, 19217193L, ct);

            byte[] baseWinBytes = await File.ReadAllBytesAsync(baseWinMpqPath, ct);
            var baseMpq = new MpqArchiveReader(baseWinBytes);

            // Extract base 8370 binaries from base-Win.mpq
            log($"      Extracting base 1.0.0.8370 binaries from base-Win.mpq...");
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
            log($"      Extracted {extractedBase} base binaries (1.0.0.8370).");

            // Step 3: If TargetBuild > 8370, download Updates/d3-0-{BuildNumber}-Win-final.MPQ and apply PTCH diffs
            if (build.BuildNumber > 8370)
            {
                string patchMpqName = $"d3-0-{build.BuildNumber}-Win-final.MPQ";
                string updatesDir = Path.Combine(targetDir, "Updates");
                Directory.CreateDirectory(updatesDir);
                string patchMpqPath = Path.Combine(updatesDir, patchMpqName);
                string patchMpqUrl = $"{D3BuildCatalog.LegacyDirectBaseUrl}/Updates/{patchMpqName}";

                log($"[3/4] Downloading binary diff archive Updates/{patchMpqName} for {build.Version}.{build.BuildNumber}...");
                onProgress?.Invoke(12, $"Downloading {patchMpqName}...");
                await DownloadFileWithResumeAsync(http, patchMpqUrl, patchMpqPath, -1, ct);

                byte[] patchMpqBytes = await File.ReadAllBytesAsync(patchMpqPath, ct);
                var patchMpq = new MpqArchiveReader(patchMpqBytes);

                // Process delete.lst (removes obsoleted DLLs like msvcr90.dll/icudt44.dll on 1.0.5+ builds)
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

                // Process patch.lst and apply PTCH (COPY / BSD0 BSDIFF40) to base-Win.mpq files
                byte[] patchLstBytes = patchMpq.ReadFile("patch.lst")
                    ?? throw new InvalidDataException($"patch.lst missing inside {patchMpqName}.");
                string patchLstText = Encoding.UTF8.GetString(patchLstBytes);
                using var md5 = MD5.Create();

                int patchedCount = 0;
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

                log($"      Successfully patched and MD5-verified {patchedCount} binaries for Diablo III {build.Version}.{build.BuildNumber}!");
            }
            else
            {
                log($"[3/4] Target is base build 1.0.0.8370 — no binary diff patch required.");
            }

            if (binariesOnly)
            {
                onProgress?.Invoke(100, $"Binaries ready for {build.Version}.{build.BuildNumber}!");
                log($"[4/4] Binaries-Only mode selected: skipping multi-GB Data_D3 MPQ archives.");
                return;
            }

            // Step 4: Filter & download Data_D3/PC/MPQs files up to build.BuildNumber
            var mpqsToDownload = allMfilEntries
                .Where(e => e.FileVersion <= build.BuildNumber)
                .Where(e => !e.RelativePath.StartsWith("Updates/", StringComparison.OrdinalIgnoreCase))
                .Where(e => !e.RelativePath.Equals("Data_D3/PC/MPQs/base-Win.mpq", StringComparison.OrdinalIgnoreCase))
                .Where(e =>
                    string.Equals(e.Tag, "base", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Tag, "Win", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Tag, locale, StringComparison.OrdinalIgnoreCase))
                .ToList();

            long totalMpqBytes = mpqsToDownload.Sum(x => x.Size);
            log($"[4/4] Downloading {mpqsToDownload.Count} Data_D3 MPQ archives for {build.Version}.{build.BuildNumber} ({totalMpqBytes / 1048576.0:F1} MB total)...");

            using var sem = new SemaphoreSlim(Math.Clamp(maxConcurrency, 1, 16));
            long completedBytes = 0;
            int completedFiles = 0;
            var sw = Stopwatch.StartNew();
            long lastLogTick = 0;

            var tasks = mpqsToDownload.Select(async item =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    string localPath = Path.Combine(targetDir, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                    string url = $"{D3BuildCatalog.LegacyDirectBaseUrl}/{item.RelativePath}";

                    await DownloadFileWithResumeAsync(http, url, localPath, item.Size, ct, bytesDelta =>
                    {
                        long b = Interlocked.Add(ref completedBytes, bytesDelta);
                        long now = sw.ElapsedMilliseconds;
                        long prev = Interlocked.Read(ref lastLogTick);
                        if (now - prev >= 1000 && Interlocked.CompareExchange(ref lastLogTick, now, prev) == prev)
                        {
                            double pct = totalMpqBytes > 0 ? (b * 100.0 / totalMpqBytes) : 100.0;
                            double mbps = (b / 1048576.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                            string msg = $"[MPQ] {pct,6:F2}% | {b / 1048576.0,8:F1} / {totalMpqBytes / 1048576.0,8:F1} MB | {completedFiles}/{mpqsToDownload.Count} files | {mbps,5:F1} MB/s";
                            log("      " + msg);
                            onProgress?.Invoke(15 + pct * 0.85, msg);
                        }
                    });

                    int f = Interlocked.Increment(ref completedFiles);
                    double finalPct = totalMpqBytes > 0 ? (Interlocked.Read(ref completedBytes) * 100.0 / totalMpqBytes) : 100.0;
                    onProgress?.Invoke(15 + finalPct * 0.85, $"Downloaded {Path.GetFileName(item.RelativePath)} ({f}/{mpqsToDownload.Count})");
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            onProgress?.Invoke(100, $"Diablo III {build.Version}.{build.BuildNumber} installation complete!");
        }

        private static async Task DownloadFileWithResumeAsync(
            HttpClient http,
            string url,
            string destinationPath,
            long expectedSize,
            CancellationToken ct,
            Action<long> onBytesProgress = null)
        {
            if (File.Exists(destinationPath))
            {
                long existingLen = new FileInfo(destinationPath).Length;
                if (expectedSize > 0 && existingLen == expectedSize)
                {
                    onBytesProgress?.Invoke(expectedSize);
                    return;
                }
            }

            string tempPath = destinationPath + ".part";
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();

                    using var netStream = await resp.Content.ReadAsStreamAsync(ct);
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 262144, true))
                    {
                        byte[] buf = new byte[262144];
                        int read;
                        while ((read = await netStream.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                        {
                            await fs.WriteAsync(buf, 0, read, ct);
                            onBytesProgress?.Invoke(read);
                        }
                    }

                    if (File.Exists(destinationPath)) File.Delete(destinationPath);
                    File.Move(tempPath, destinationPath);
                    return;
                }
                catch when (attempt < 3 && !ct.IsCancellationRequested)
                {
                    await Task.Delay(500 * (attempt + 1), ct);
                }
            }
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
        private readonly int _sectorSize;
        private readonly int _hashPos;
        private readonly int _blockPos;
        private readonly int _hashCount;
        private readonly int _blockCount;

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
                int rleUncompSize = BinaryPrimitives.ReadInt32LittleEndian(ptch.AsSpan(68, 4));
                byte[] bsd = new byte[rleUncompSize];
                int inPos = 72;
                int outPos = 0;
                while (outPos < rleUncompSize && inPos < ptch.Length)
                {
                    byte cmd = ptch[inPos++];
                    if ((cmd & 0x80) != 0)
                    {
                        int count = (cmd & 0x7F) + 1;
                        int copy = Math.Min(count, Math.Min(ptch.Length - inPos, rleUncompSize - outPos));
                        Buffer.BlockCopy(ptch, inPos, bsd, outPos, copy);
                        inPos += copy;
                        outPos += copy;
                    }
                    else
                    {
                        outPos += Math.Min(cmd + 1, rleUncompSize - outPos);
                    }
                }

                string bsdMagic = Encoding.ASCII.GetString(bsd, 0, 8);
                if (bsdMagic != "BSDIFF40")
                    throw new InvalidDataException($"Invalid BSDIFF40 magic '{bsdMagic}'.");

                int ctrlBlockLen = BinaryPrimitives.ReadInt32LittleEndian(bsd.AsSpan(8, 4));
                int diffBlockLen = BinaryPrimitives.ReadInt32LittleEndian(bsd.AsSpan(16, 4));
                int newSize = BinaryPrimitives.ReadInt32LittleEndian(bsd.AsSpan(24, 4));

                byte[] newData = new byte[newSize];
                int ctrlPos = 32;
                int ctrlEnd = 32 + ctrlBlockLen;
                int diffPos = ctrlEnd;
                int extraPos = ctrlEnd + diffBlockLen;
                int oldPos = 0;
                int newPos = 0;

                while (newPos < newSize && ctrlPos < ctrlEnd)
                {
                    int addLen = ReadBsdiffInt32(bsd, ctrlPos);
                    int copyLen = ReadBsdiffInt32(bsd, ctrlPos + 4);
                    int seekLen = ReadBsdiffInt32(bsd, ctrlPos + 8);
                    ctrlPos += 12;

                    if (newPos + addLen > newSize) addLen = newSize - newPos;
                    for (int i = 0; i < addLen; i++)
                    {
                        byte oldByte = (oldData != null && oldPos + i >= 0 && oldPos + i < oldData.Length)
                            ? oldData[oldPos + i]
                            : (byte)0;
                        newData[newPos + i] = (byte)(oldByte + bsd[diffPos + i]);
                    }
                    diffPos += addLen;
                    newPos += addLen;
                    oldPos += addLen;

                    if (newPos + copyLen > newSize) copyLen = newSize - newPos;
                    if (copyLen > 0)
                    {
                        int safeCopy = Math.Min(copyLen, bsd.Length - extraPos);
                        Buffer.BlockCopy(bsd, extraPos, newData, newPos, safeCopy);
                        extraPos += copyLen;
                        newPos += copyLen;
                    }
                    oldPos += seekLen;
                }
                return newData;
            }

            throw new NotSupportedException($"Unsupported Blizzard PTCH XFRM type '{xfrmType}'.");
        }

        private static int ReadBsdiffInt32(byte[] buf, int offset)
        {
            uint u = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(offset, 4));
            return (u & 0x80000000u) != 0 ? -(int)(u & 0x7FFFFFFFu) : (int)u;
        }
    }

    internal static class MpqLzmaDecoder
    {
        public static byte[] Decompress(byte[] input, int offset, int length, int uncompressedSize)
        {
            byte prop0 = input[offset + 2];
            int lc = prop0 % 9;
            int rem = prop0 / 9;
            int lp = rem % 5;
            int pb = rem / 5;
            long hdrSize = BinaryPrimitives.ReadInt64LittleEndian(input.AsSpan(offset + 7, 8));
            int streamPos = (hdrSize == uncompressedSize) ? (offset + 15) : (offset + 7);

            byte[] outBuf = new byte[uncompressedSize];
            int outPos = 0;
            int numPosStates = 1 << pb;
            int numLiteralStates = 1 << (lc + lp);
            int posStateMask = numPosStates - 1;
            int lpMask = (1 << lp) - 1;

            ushort[] isMatch = InitProbs(12 << pb);
            ushort[] isRep = InitProbs(12);
            ushort[] isRepG0 = InitProbs(12);
            ushort[] isRepG1 = InitProbs(12);
            ushort[] isRepG2 = InitProbs(12);
            ushort[] isRep0Long = InitProbs(12 << pb);
            ushort[] posSlotDecoder = InitProbs(4 * 64);
            ushort[] posDecoders = InitProbs(115);
            ushort[] posAlignDecoder = InitProbs(16);
            ushort[] literals = InitProbs(0x300 * numLiteralStates);

            var lenDecoder = new LenDecoder(numPosStates);
            var repLenDecoder = new LenDecoder(numPosStates);
            var rc = new RangeDecoder(input, streamPos);
            int state = 0;
            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
            byte prevByte = 0;

            while (outPos < uncompressedSize)
            {
                int posState = outPos & posStateMask;
                if (rc.DecodeBit(isMatch, (state << pb) + posState) == 0)
                {
                    int litState = ((outPos & lpMask) << lc) + (prevByte >> (8 - lc));
                    int litBase = 0x300 * litState;
                    int symbol = 1;
                    if (state >= 7)
                    {
                        byte matchByte = outBuf[outPos - (int)rep0 - 1];
                        do
                        {
                            int matchBit = (matchByte >> 7) & 1;
                            matchByte <<= 1;
                            int bit = rc.DecodeBit(literals, litBase + ((1 + matchBit) << 8) + symbol);
                            symbol = (symbol << 1) | bit;
                            if (matchBit != bit)
                            {
                                while (symbol < 0x100) symbol = (symbol << 1) | rc.DecodeBit(literals, litBase + symbol);
                                break;
                            }
                        } while (symbol < 0x100);
                    }
                    else
                    {
                        while (symbol < 0x100) symbol = (symbol << 1) | rc.DecodeBit(literals, litBase + symbol);
                    }
                    prevByte = (byte)symbol;
                    outBuf[outPos++] = prevByte;
                    state = (state < 4) ? 0 : ((state < 10) ? state - 3 : state - 6);
                }
                else
                {
                    int len;
                    if (rc.DecodeBit(isRep, state) == 1)
                    {
                        if (rc.DecodeBit(isRepG0, state) == 0)
                        {
                            if (rc.DecodeBit(isRep0Long, (state << pb) + posState) == 0)
                            {
                                state = state < 7 ? 9 : 11;
                                prevByte = outBuf[outPos - (int)rep0 - 1];
                                outBuf[outPos++] = prevByte;
                                continue;
                            }
                        }
                        else
                        {
                            uint dist;
                            if (rc.DecodeBit(isRepG1, state) == 0) dist = rep1;
                            else
                            {
                                if (rc.DecodeBit(isRepG2, state) == 0) dist = rep2;
                                else { dist = rep3; rep3 = rep2; }
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
                        rep3 = rep2; rep2 = rep1; rep1 = rep0;
                        len = lenDecoder.Decode(rc, posState) + 2;
                        state = state < 7 ? 7 : 10;
                        int lenToPosState = (len - 2 < 4) ? (len - 2) : 3;
                        int posSlot = rc.DecodeBitTree(posSlotDecoder, lenToPosState * 64, 6);
                        if (posSlot >= 4)
                        {
                            int numDirectBits = (posSlot >> 1) - 1;
                            rep0 = (uint)((2 | (posSlot & 1)) << numDirectBits);
                            if (posSlot < 14) rep0 += (uint)rc.ReverseDecode(posDecoders, (int)rep0 - posSlot, numDirectBits);
                            else
                            {
                                rep0 += rc.DecodeDirectBits(numDirectBits - 4) << 4;
                                rep0 += (uint)rc.ReverseDecode(posAlignDecoder, 0, 4);
                            }
                        }
                        else rep0 = (uint)posSlot;
                    }
                    for (int i = 0; i < len && outPos < uncompressedSize; i++)
                    {
                        prevByte = outBuf[outPos - (int)rep0 - 1];
                        outBuf[outPos++] = prevByte;
                    }
                }
            }
            return outBuf;
        }

        private static ushort[] InitProbs(int count)
        {
            ushort[] p = new ushort[count];
            for (int i = 0; i < count; i++) p[i] = 1024;
            return p;
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

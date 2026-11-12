using System.Security.Cryptography;
using System.Text;

namespace D3Downloader;

/// <summary>
/// Makes a locally built storage acceptable to the retail client's NGDP layer.
///
/// The client resolves data through the CDN catalog stored in <c>Data/config</c>: it needs a local
/// <c>.index</c> for every archive listed there plus the derived "index group" files. Blizzard retired
/// several of those artefacts for old builds, so a storage assembled from live CDN data ends up with a
/// catalog that points at files which no longer exist (<c>Data Retrieval Error</c>) and with group index
/// hashes that no longer match the archives present (<c>Data Error</c>).
///
/// This rewrites the CDN config so it only lists archives we actually have, regenerates the index group
/// files from those archives and re-keys the config file (NGDP names configs after their own MD5).
/// </summary>
internal static class NgdpConsistency
{
    private const int GroupRecordSize = 25;        // 16-byte key + size(4) + archive(1) + offset(4)
    private const int GroupRecordsPerBlock = 163;  // 163 * 25 = 4075, fits a 4096-byte page
    private const int PageSize = 4096;
    private const int FooterSize = 28;

    internal sealed class Result
    {
        public bool Changed;
        public int RemovedArchives;
        public int RemovedPatchArchives;
        public string ConfigKey;
    }

    internal static Result Apply(string installRoot, Action<string> log)
    {
        var result = new Result();
        string buildInfoPath = Path.Combine(installRoot, ".build.info");
        if (!File.Exists(buildInfoPath))
        {
            log("      [NGDP] No .build.info found; skipping catalog repair.");
            return result;
        }

        string indicesDir = Path.Combine(installRoot, "Data", "indices");
        string configDir = Path.Combine(installRoot, "Data", "config");
        if (!Directory.Exists(indicesDir) || !Directory.Exists(configDir))
        {
            log("      [NGDP] No Data/indices or Data/config directory; skipping catalog repair.");
            return result;
        }

        string cdnKey = ReadCdnKey(buildInfoPath);
        if (!IsHash32(cdnKey))
        {
            log("      [NGDP] .build.info has no usable CDN Key; skipping catalog repair.");
            return result;
        }

        string configPath = Path.Combine(configDir, cdnKey[..2], cdnKey.Substring(2, 2), cdnKey);
        if (!File.Exists(configPath))
        {
            log($"      [NGDP] CDN config {cdnKey[..8]}... is missing; skipping catalog repair.");
            return result;
        }
        result.ConfigKey = cdnKey;

        string text = Encoding.ASCII.GetString(File.ReadAllBytes(configPath));
        string[] lines = text.Split('\n');
        Dictionary<string, int> lineIndex = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Length; i++)
        {
            int sep = lines[i].IndexOf(" = ", StringComparison.Ordinal);
            if (sep > 0)
            {
                lineIndex[lines[i][..sep].Trim()] = i;
            }
        }

        // --- data archives -------------------------------------------------
        bool changed = false;
        changed |= PruneArchiveList(lines, lineIndex, "archives", "archives-index-size", indicesDir, log,
            out int removedData);
        result.RemovedArchives = removedData;

        // --- patch archives ------------------------------------------------
        changed |= PruneArchiveList(lines, lineIndex, "patch-archives", "patch-archives-index-size", indicesDir, log,
            out int removedPatch);
        result.RemovedPatchArchives = removedPatch;

        // --- index group files ---------------------------------------------
        changed |= RebuildGroup(lines, lineIndex, "archives", "archive-group", indicesDir, log);
        changed |= RebuildGroup(lines, lineIndex, "patch-archives", "patch-archive-group", indicesDir, log);

        foreach (string marker in Directory.GetFiles(Path.Combine(installRoot, "Data", "data"), "*.mrk"))
        {
            try
            {
                File.Delete(marker);
                log($"      [NGDP] Removed stale repair marker {Path.GetFileName(marker)}.");
                changed = true;
            }
            catch { }
        }

        if (!changed)
        {
            log("      [NGDP] CDN catalog already consistent (no retired archives, index groups valid).");
            return result;
        }

        byte[] newBytes = Encoding.ASCII.GetBytes(string.Join('\n', lines));
        string newKey = Convert.ToHexString(MD5.HashData(newBytes)).ToLowerInvariant();
        string newPath = Path.Combine(configDir, newKey[..2], newKey.Substring(2, 2), newKey);
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllBytes(newPath, newBytes);
        WriteCdnKey(buildInfoPath, cdnKey, newKey);

        result.Changed = true;
        result.ConfigKey = newKey;
        log($"      [NGDP] CDN catalog repaired: {removedData} retired archive(s) and {removedPatch} retired patch archive(s) dropped.");
        log($"      [NGDP] New CDN config {newKey[..8]}... registered in .build.info.");
        return result;
    }

    /// <summary>
    /// Removes catalog entries whose <c>.index</c> is not available locally (Blizzard purged them from the CDN).
    /// </summary>
    private static bool PruneArchiveList(
        string[] lines,
        Dictionary<string, int> lineIndex,
        string listKey,
        string sizeKey,
        string indicesDir,
        Action<string> log,
        out int removed)
    {
        removed = 0;
        if (!lineIndex.TryGetValue(listKey, out int listLine) || !lineIndex.TryGetValue(sizeKey, out int sizeLine))
        {
            return false;
        }

        string[] hashes = lines[listLine].Split('=', 2)[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] sizes = lines[sizeLine].Split('=', 2)[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (hashes.Length == 0 || hashes.Length != sizes.Length)
        {
            return false;
        }

        var keepHashes = new List<string>(hashes.Length);
        var keepSizes = new List<string>(hashes.Length);
        var dropped = new List<string>();
        for (int i = 0; i < hashes.Length; i++)
        {
            if (File.Exists(Path.Combine(indicesDir, hashes[i].ToLowerInvariant() + ".index")))
            {
                keepHashes.Add(hashes[i]);
                keepSizes.Add(sizes[i]);
            }
            else
            {
                dropped.Add(hashes[i]);
            }
        }

        if (dropped.Count == 0)
        {
            return false;
        }
        if (keepHashes.Count == 0)
        {
            // Never leave the client with an empty catalog: keep everything and let it fail loudly instead.
            log($"      [NGDP] Every {listKey} index is missing locally; leaving the catalog untouched.");
            return false;
        }

        removed = dropped.Count;
        foreach (string h in dropped)
        {
            log($"      [NGDP] {listKey}: dropping retired index {h[..12]}... (no longer hosted by Blizzard).");
        }

        lines[listLine] = $"{listKey} = {string.Join(' ', keepHashes)}";
        lines[sizeLine] = $"{sizeKey} = {string.Join(' ', keepSizes)}";
        return true;
    }

    /// <summary>
    /// Rebuilds an index group file from the archives still listed in the catalog and points the catalog at
    /// the regenerated file. The client names index groups after the MD5 of their footer, so the file is
    /// written under that hash and the catalog entry is updated accordingly.
    /// </summary>
    private static bool RebuildGroup(
        string[] lines,
        Dictionary<string, int> lineIndex,
        string listKey,
        string groupKey,
        string indicesDir,
        Action<string> log)
    {
        if (!lineIndex.TryGetValue(listKey, out int listLine) || !lineIndex.TryGetValue(groupKey, out int groupLine))
        {
            return false;
        }

        string[] hashes = lines[listLine].Split('=', 2)[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (hashes.Length == 0)
        {
            return false;
        }

        string configured = lines[groupLine].Split('=', 2)[1].Trim().ToLowerInvariant();
        string configuredPath = Path.Combine(indicesDir, configured + ".index");
        bool configuredValid = IsHash32(configured) && File.Exists(configuredPath) &&
                               FooterKey(File.ReadAllBytes(configuredPath)) == configured;
        if (configuredValid)
        {
            return false;
        }

        var records = new SortedDictionary<string, (byte[] Key, uint Size, byte Archive, uint Offset)>(StringComparer.Ordinal);
        for (byte archive = 0; archive < hashes.Length; archive++)
        {
            string indexPath = Path.Combine(indicesDir, hashes[archive].ToLowerInvariant() + ".index");
            if (!File.Exists(indexPath))
            {
                continue;
            }
            foreach (var entry in ReadArchiveIndex(File.ReadAllBytes(indexPath)))
            {
                // Later catalog entries win, matching the client's own merge behaviour.
                records[Convert.ToHexString(entry.Key)] = (entry.Key, entry.Size, archive, entry.Offset);
            }
        }

        byte[] groupBytes = BuildGroupIndex(records.Values.ToList());
        string key = FooterKey(groupBytes);
        File.WriteAllBytes(Path.Combine(indicesDir, key + ".index"), groupBytes);
        lines[groupLine] = $"{groupKey} = {key}";
        log($"      [NGDP] Rebuilt {groupKey} from {hashes.Length} archives ({records.Count:N0} entries) as {key[..12]}....");
        return true;
    }

    private static IEnumerable<(byte[] Key, uint Size, uint Offset)> ReadArchiveIndex(byte[] bytes)
    {
        if (bytes.Length < FooterSize)
        {
            yield break;
        }
        long footerPos = bytes.Length - FooterSize;
        uint count = BitConverter.ToUInt32(bytes, (int)footerPos + 16);
        if ((count & 0xFF000000u) != 0)
        {
            count = (uint)((bytes[footerPos + 16] << 24) | (bytes[footerPos + 17] << 16) |
                           (bytes[footerPos + 18] << 8) | bytes[footerPos + 19]);
        }

        uint read = 0;
        for (int block = 0; read < count; block++)
        {
            long blockStart = block * (long)PageSize;
            if (blockStart + PageSize > footerPos)
            {
                break;
            }
            for (int i = 0; i < 170 && read < count; i++, read++)
            {
                int pos = (int)blockStart + i * 24;
                byte[] key = bytes.AsSpan(pos, 16).ToArray();
                uint size = (uint)((bytes[pos + 16] << 24) | (bytes[pos + 17] << 16) | (bytes[pos + 18] << 8) | bytes[pos + 19]);
                uint offset = (uint)((bytes[pos + 20] << 24) | (bytes[pos + 21] << 16) | (bytes[pos + 22] << 8) | bytes[pos + 23]);
                yield return (key, size, offset);
            }
        }
    }

    private static byte[] BuildGroupIndex(IReadOnlyList<(byte[] Key, uint Size, byte Archive, uint Offset)> records)
    {
        int count = records.Count;
        int blockCount = (count + GroupRecordsPerBlock - 1) / GroupRecordsPerBlock;
        int tocStart = blockCount * PageSize;
        int tocLength = blockCount * 24;
        byte[] buffer = new byte[tocStart + tocLength + FooterSize];

        int index = 0;
        for (int block = 0; block < blockCount; block++)
        {
            int blockStart = block * PageSize;
            int inBlock = Math.Min(GroupRecordsPerBlock, count - block * GroupRecordsPerBlock);
            for (int i = 0; i < inBlock; i++, index++)
            {
                var record = records[index];
                int pos = blockStart + i * GroupRecordSize;
                Buffer.BlockCopy(record.Key, 0, buffer, pos, 16);
                buffer[pos + 16] = (byte)(record.Size >> 24);
                buffer[pos + 17] = (byte)(record.Size >> 16);
                buffer[pos + 18] = (byte)(record.Size >> 8);
                buffer[pos + 19] = (byte)record.Size;
                buffer[pos + 20] = record.Archive;
                buffer[pos + 21] = (byte)(record.Offset >> 24);
                buffer[pos + 22] = (byte)(record.Offset >> 16);
                buffer[pos + 23] = (byte)(record.Offset >> 8);
                buffer[pos + 24] = (byte)record.Offset;
            }

            Buffer.BlockCopy(records[index - 1].Key, 0, buffer, tocStart + block * 16, 16);
            byte[] pageHash = MD5.HashData(buffer.AsSpan(blockStart, PageSize));
            Buffer.BlockCopy(pageHash, 0, buffer, tocStart + blockCount * 16 + block * 8, 8);
        }

        int footer = tocStart + tocLength;
        byte[] tocHash = MD5.HashData(buffer.AsSpan(tocStart, tocLength));
        Buffer.BlockCopy(tocHash, 0, buffer, footer, 8);
        buffer[footer + 8] = 1;  // version
        buffer[footer + 9] = 0;
        buffer[footer + 10] = 0;
        buffer[footer + 11] = 4;  // page size in KB
        buffer[footer + 12] = 5;  // offset field width (1 archive byte + 4 offset bytes)
        buffer[footer + 13] = 4;  // size field width
        buffer[footer + 14] = 16; // key width
        buffer[footer + 15] = 8;  // hash width
        BitConverter.TryWriteBytes(buffer.AsSpan(footer + 16, 4), (uint)count);
        // The footer hash covers the 12 parameter bytes plus the (still zeroed) 8-byte hash field itself.
        byte[] footerHashInput = new byte[20];
        Buffer.BlockCopy(buffer, footer + 8, footerHashInput, 0, 12);
        byte[] footerHash = MD5.HashData(footerHashInput);
        Buffer.BlockCopy(footerHash, 0, buffer, footer + 20, 8);
        return buffer;
    }

    private static string FooterKey(byte[] file)
    {
        if (file.Length < FooterSize)
        {
            return "";
        }
        return Convert.ToHexString(MD5.HashData(file.AsSpan(file.Length - FooterSize, FooterSize))).ToLowerInvariant();
    }

    private static string ReadCdnKey(string buildInfoPath)
    {
        string[] lines = File.ReadAllLines(buildInfoPath);
        if (lines.Length < 2)
        {
            return "";
        }
        string[] columns = lines[1].Split('|');
        return columns.Length > 3 ? columns[3].Trim().ToLowerInvariant() : "";
    }

    private static void WriteCdnKey(string buildInfoPath, string oldKey, string newKey)
    {
        string[] lines = File.ReadAllLines(buildInfoPath);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('|');
            if (columns.Length > 3 && columns[3].Trim().Equals(oldKey, StringComparison.OrdinalIgnoreCase))
            {
                columns[3] = newKey;
                lines[i] = string.Join('|', columns);
            }
        }
        File.WriteAllLines(buildInfoPath, lines, Encoding.ASCII);
    }

    private static bool IsHash32(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 32 && value.All(Uri.IsHexDigit);
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace D3Downloader
{
    internal static partial class Program
    {
        /// <summary>
        /// Result of exporting one locale: the pack is a delta against the Windows
        /// enUS set, so it only carries the chunks another language adds on top.
        /// </summary>
        public sealed record LanguagePackResult(string Locale, string Directory, int Chunks, long Bytes, int Missing);

        private const long PackArchiveLimit = 1024L * 1024 * 1024 - 1;

        /// <summary>
        /// Normalizes a locale request shared by the CLI and the dialog.
        /// "all" (or an empty request) means every supported locale except enUS,
        /// which is the base client and therefore has no delta pack.
        /// </summary>
        internal static string[] ResolveLanguagePackLocales(IEnumerable<string> requested, out string error)
        {
            error = null;
            var values = (requested ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToArray();
            var pool = BackupEngine.AllSupportedLocales
                .Where(l => !l.Equals("enUS", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var wantsAll = values.Length == 0 ||
                           values.Any(v => v.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                           v.Equals("allLocales", StringComparison.OrdinalIgnoreCase));
            if (wantsAll)
            {
                foreach (var value in values)
                {
                    if (!value.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals("allLocales", StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Use either 'all' or an explicit language list, not '{value}' together with 'all'.";
                        return null;
                    }
                }
                return pool;
            }

            var selected = new List<string>();
            foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (value.Equals("enUS", StringComparison.OrdinalIgnoreCase))
                {
                    error = "enUS is the base client language and has no delta pack.";
                    return null;
                }
                var match = pool.FirstOrDefault(l => l.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    error = $"Unsupported language '{value}'. Use one of: {string.Join(", ", pool)}, all.";
                    return null;
                }
                selected.Add(match);
            }
            if (selected.Count == 0)
            {
                error = "Select at least one language.";
                return null;
            }
            return selected.ToArray();
        }

        /// <summary>
        /// Downloads the locale-specific chunks of a CASC build and writes one
        /// distributable folder per language: chunks/data.NNN plus pack.json.
        /// Nothing is written into a game installation.
        /// </summary>
        public static async Task<int> ExportLanguagePacksAsync(
            string outputDir,
            D3BuildCatalogItem build,
            string region,
            IEnumerable<string> locales,
            int maxConcurrency,
            Action<string> log,
            Action<double, string> onProgress = null,
            CancellationToken ct = default,
            bool planOnly = false,
            int limit = -1)
        {
            if (build == null)
            {
                log("[ERROR] No build selected.");
                return 1;
            }
            if (!build.IsCasc)
            {
                log($"[ERROR] {build.Version}.{build.BuildNumber} is a legacy MPQ build; language packs require a CASC build.");
                return 1;
            }
            if (string.IsNullOrWhiteSpace(outputDir) || !Path.IsPathFullyQualified(outputDir))
            {
                log("[ERROR] Language pack destination must be an absolute folder.");
                return 1;
            }
            string[] selected = ResolveLanguagePackLocales(locales, out string localeError);
            if (selected == null)
            {
                log("[ERROR] " + localeError);
                return 1;
            }

            string work = Path.Combine(Path.GetTempPath(), "D3Forever-LanguagePacks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var results = new List<LanguagePackResult>();
            try
            {
                _http = new HttpClient(new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = Math.Max(64, maxConcurrency * 2),
                    AutomaticDecompression = DecompressionMethods.None,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                    ConnectTimeout = TimeSpan.FromSeconds(20)
                })
                {
                    Timeout = TimeSpan.FromMinutes(5)
                };
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("NGDP-D3Downloader/2.0");

                log("================================================================================");
                log(" Language Pack Export (Blizzard CDN, delta against Windows enUS)");
                log("================================================================================");
                log($" Build     : {build.Version}.{build.BuildNumber}");
                log($" Region    : {region}");
                log($" Languages : {string.Join(", ", selected)}");
                log($" Output    : {outputDir}");
                log("================================================================================");

                string product = string.IsNullOrWhiteSpace(build.ProductCode) ? "d3" : build.ProductCode;
                onProgress?.Invoke(2.0, "Resolving Blizzard NGDP configuration...");

                var versionInfo = new VersionRecord
                {
                    Region = region,
                    BuildConfig = build.BuildConfigHash.ToLowerInvariant(),
                    CdnConfig = !string.IsNullOrWhiteSpace(build.CdnConfigHash)
                        ? build.CdnConfigHash.ToLowerInvariant()
                        : "6547fc64ca796c7c1b25864145814ace",
                    BuildId = build.BuildNumber.ToString(),
                    VersionsName = $"{build.Version}.{build.BuildNumber}",
                    ProductConfig = ""
                };

                CdnRecord cdnInfo;
                try
                {
                    cdnInfo = ParseCdns(await QueryNgdpEndpointAsync("cdns", product), region);
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

                _cdnPath = cdnInfo.Path.Trim('/');
                _cdnConfigPath = string.IsNullOrWhiteSpace(cdnInfo.ConfigPath) ? "tpr/configs/data" : cdnInfo.ConfigPath.Trim('/');
                _cdnHosts = new[] { "us.cdn.blizzard.com", "cdn.blizzard.com", "level3.blizzard.com", "blzddist1-a.akamaihd.net" }
                    .Concat(cdnInfo.Hosts ?? Array.Empty<string>())
                    .Select(h => h?.Trim())
                    .Where(h => IsTrustedCdnHost(h))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (_cdnHosts.Count == 0) _cdnHosts.Add("us.cdn.blizzard.com");

                string configDir = Path.Combine(work, "config");
                string indicesDir = Path.Combine(work, "indices");
                Directory.CreateDirectory(configDir);
                Directory.CreateDirectory(indicesDir);

                byte[] buildCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.BuildConfig);
                byte[] cdnCfgBytes;
                try
                {
                    cdnCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.CdnConfig);
                }
                catch (Exception ex)
                {
                    log($"      [Note] Historical CDNConfig unavailable ({ex.Message}), falling back to the live shared catalog.");
                    versionInfo.CdnConfig = "6547fc64ca796c7c1b25864145814ace";
                    cdnCfgBytes = await DownloadConfigFileAsync(configDir, _cdnPath + "/config", versionInfo.CdnConfig);
                }

                var buildCfg = ParseKeyValueConfig(Encoding.UTF8.GetString(buildCfgBytes));
                var cdnCfg = ParseKeyValueConfig(Encoding.UTF8.GetString(cdnCfgBytes));

                string[] archives = cdnCfg.TryGetValue("archives", out string archStr)
                    ? archStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(IsValidHash32).ToArray()
                    : Array.Empty<string>();
                string[] patchArchives = cdnCfg.TryGetValue("patch-archives", out string pArchStr)
                    ? pArchStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(IsValidHash32).ToArray()
                    : Array.Empty<string>();
                if (archives.Length == 0) throw new InvalidDataException("The CDN configuration lists no archives.");
                archives = await MergeWithSharedArchivesAsync(archives, versionInfo.CdnConfig, product, configDir, log);
                cdnCfg.TryGetValue("archive-group", out string archiveGroupHash);
                cdnCfg.TryGetValue("patch-archive-group", out string patchArchiveGroupHash);
                cdnCfg.TryGetValue("file-index", out string fileIndexHash);
                cdnCfg.TryGetValue("patch-file-index", out string patchFileIndexHash);

                onProgress?.Invoke(8.0, $"Downloading {archives.Length} CDN archive indices...");
                log($"[1/3] Downloading {archives.Length} CDN archive indices...");
                var archiveIndexMap = await DownloadAndParseIndicesAsync(
                    indicesDir, archives, patchArchives, fileIndexHash, patchFileIndexHash,
                    archiveGroupHash, patchArchiveGroupHash, maxConcurrency, -1, onProgress);
                log($"      Indexed {archiveIndexMap.Count:N0} BLTE chunks.");

                onProgress?.Invoke(14.0, "Decoding the Download manifest...");
                log("[2/3] Decoding the Download manifest...");
                string[] encodingHashes = buildCfg["encoding"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string encodingEKey = encodingHashes.Length > 1 ? encodingHashes[1] : encodingHashes[0];
                byte[] decEncoding = BlteDecoder.Decode(await DownloadDataBlobByEKeyAsync(encodingEKey, archiveIndexMap, archives));
                var ckeyToEkey = ParseEncodingTableA(decEncoding);

                string[] downloadHashes = buildCfg["download"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string downloadCKey = downloadHashes[0];
                string downloadEKey = downloadHashes.Length > 1
                    ? downloadHashes[1]
                    : (ckeyToEkey.TryGetValue(downloadCKey, out string mapped) ? mapped : downloadCKey);
                byte[] decDownload = BlteDecoder.Decode(await DownloadDataBlobByEKeyAsync(downloadEKey, archiveIndexMap, archives));

                var baseKeys = new HashSet<string>(
                    ParseDownloadManifest(decDownload, "enUS").Select(i => i.EKeyHex),
                    StringComparer.OrdinalIgnoreCase);
                log($"      Base Windows+enUS set: {baseKeys.Count:N0} chunks.");

                var plans = new List<(string Locale, List<DownloadItem> Delta)>();
                foreach (string locale in selected)
                {
                    var items = ParseDownloadManifest(decDownload, locale);
                    var delta = items
                        .Where(i => !baseKeys.Contains(i.EKeyHex))
                        .OrderBy(i => i.EKeyHex, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (limit > 0 && delta.Count > limit)
                    {
                        log($"      {locale}: DRY-RUN limited to the first {limit:N0} chunks for diagnostics.");
                        delta = delta.Take(limit).ToList();
                    }
                    plans.Add((locale, delta));
                    log($"      {locale}: {items.Count:N0} manifest chunks, {delta.Count:N0} language-specific, {delta.Sum(i => i.CompressedSize) / (1024.0 * 1024.0):N1} MB.");
                }

                long totalChunks = Math.Max(1, plans.Sum(p => (long)p.Delta.Count));
                if (planOnly)
                {
                    foreach (var (locale, delta) in plans)
                        log($"      {locale}: {delta.Count:N0} chunks, {delta.Sum(i => i.CompressedSize) / (1024.0 * 1024.0):N1} MB to download.");
                    log($" Plan only: {plans.Count} pack(s), {plans.Sum(p => (long)p.Delta.Count):N0} chunks, " +
                        $"{plans.Sum(p => p.Delta.Sum(i => i.CompressedSize)) / (1024.0 * 1024.0):N1} MB. Nothing downloaded.");
                    return 0;
                }
                long doneChunks = 0;
                string packRoot = Path.Combine(Path.GetFullPath(outputDir), $"{build.Version}.{build.BuildNumber}");
                foreach (var (locale, delta) in plans)
                {
                    ct.ThrowIfCancellationRequested();
                    onProgress?.Invoke(16.0 + 82.0 * doneChunks / totalChunks, $"Exporting {locale}...");
                    log($"[3/3] Exporting {locale} ({delta.Count:N0} chunks)...");
                    var result = await ExportLocalePackAsync(
                        packRoot, locale, build, region, versionInfo, delta, archiveIndexMap, archives,
                        maxConcurrency, log,
                        (written, total) => onProgress?.Invoke(
                            16.0 + 82.0 * (doneChunks + Math.Min(written, total)) / totalChunks,
                            $"Exporting {locale}: {written:N0}/{total:N0} chunks"),
                        ct);
                    results.Add(result);
                    doneChunks += delta.Count;
                    log(result.Missing == 0
                        ? $"      [OK] {locale}: {result.Chunks:N0} chunks, {result.Bytes / (1024.0 * 1024.0):N1} MB -> {result.Directory}"
                        : $"      [WARNING] {locale}: {result.Missing:N0} chunk(s) unavailable on the CDN.");
                }

                onProgress?.Invoke(100.0, "Language packs complete.");
                log("================================================================================");
                long bytes = results.Sum(r => r.Bytes);
                log($" SUCCESS: {results.Count} language pack(s), {results.Sum(r => r.Chunks):N0} chunks, {bytes / (1024.0 * 1024.0):N1} MB total.");
                log($" Output: {packRoot}");
                log("================================================================================");
                return results.Any(r => r.Missing > 0) ? 1 : 0;
            }
            catch (OperationCanceledException)
            {
                log("[CANCELLED] Language pack export cancelled.");
                return 1;
            }
            catch (Exception ex)
            {
                log($"[ERROR] Language pack export failed: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
            finally
            {
                _http?.Dispose();
                _http = null;
                try { Directory.Delete(work, true); } catch { }
            }
        }

        private static async Task<LanguagePackResult> ExportLocalePackAsync(
            string packRoot,
            string locale,
            D3BuildCatalogItem build,
            string region,
            VersionRecord versionInfo,
            List<DownloadItem> delta,
            Dictionary<string, ArchiveEntryLoc> archiveIndexMap,
            string[] archives,
            int maxConcurrency,
            Action<string> log,
            Action<long, long> progress,
            CancellationToken ct)
        {
            string directory = Path.Combine(packRoot, locale);
            string chunksDir = Path.Combine(directory, "chunks");
            string markerPath = Path.Combine(directory, "pack.partial.json");
            Directory.CreateDirectory(chunksDir);

            var entries = new List<(string Key, string FullKey, int Archive, long Offset, long Size, string Sha256)>();
            var missingKeys = new List<string>();
            int consumed = ResumePartialExport(markerPath, chunksDir, locale, build, versionInfo, delta, archiveIndexMap, log,
                entries, missingKeys, out int archiveIndex, out long archiveSize);

            FileStream stream = null;
            if (delta.Count > 0)
            {
                stream = OpenArchive(chunksDir, archiveIndex);
                stream.Seek(archiveSize, SeekOrigin.Begin);
            }
            // Chunks average ~150 KB, so a bounded window keeps the CDN busy
            // without buffering gigabytes in memory.
            int batch = Math.Clamp(maxConcurrency * 2, 4, 32);
            try
            {
                for (int start = consumed; start < delta.Count; start += batch)
                {
                    ct.ThrowIfCancellationRequested();
                    var slice = delta.Skip(start).Take(batch).ToArray();
                    var blobs = await Task.WhenAll(slice.Select(item => FetchChunkAsync(item, archiveIndexMap, archives)));
                    for (int n = 0; n < slice.Length; n++)
                    {
                        byte[] blob = blobs[n];
                        string ekey = slice[n].EKeyHex.ToUpperInvariant();
                        if (blob == null || blob.Length == 0)
                        {
                            missingKeys.Add(ekey);
                            log($"      [MISSING] {locale} chunk {ekey} is no longer hosted by the CDN.");
                            continue;
                        }
                        if (archiveSize + blob.Length > PackArchiveLimit)
                        {
                            stream?.Dispose();
                            archiveIndex++;
                            stream = OpenArchive(chunksDir, archiveIndex);
                            archiveSize = 0;
                        }
                        await stream.WriteAsync(blob, ct);
                        entries.Add((ekey.Substring(0, 18), ekey, archiveIndex, archiveSize, blob.Length,
                            Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant()));
                        archiveSize += blob.Length;
                    }
                    // Flush per batch so an interrupted run resumes from a chunk
                    // boundary instead of replaying the buffered tail.
                    if (stream != null) await stream.FlushAsync(ct);
                    WritePartialMarker(markerPath, locale, build, versionInfo, delta.Count, start + slice.Length, missingKeys);
                    progress(Math.Min(start + slice.Length, delta.Count), delta.Count);
                }
            }
            finally
            {
                stream?.Dispose();
                try { if (File.Exists(markerPath)) File.Delete(markerPath); } catch { }
            }
            int missing = missingKeys.Count;

            var packJsonPath = Path.Combine(directory, "pack.json");
            await using (var file = new FileStream(packJsonPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
            await using (var json = new Utf8JsonWriter(file))
            {
                json.WriteStartObject();
                json.WriteNumber("format", 1);
                json.WriteString("kind", "d3-language-pack");
                json.WriteString("createdUtc", DateTime.UtcNow.ToString("O"));
                json.WriteString("product", "d3");
                json.WriteString("version", $"{build.Version}.{build.BuildNumber}");
                json.WriteNumber("buildNumber", build.BuildNumber);
                json.WriteString("region", region);
                json.WriteString("locale", locale);
                json.WriteString("baseLocale", "enUS");
                json.WriteString("buildConfig", versionInfo.BuildConfig);
                json.WriteString("cdnConfig", versionInfo.CdnConfig);
                json.WriteStartObject("counts");
                json.WriteNumber("chunks", entries.Count);
                json.WriteNumber("bytes", entries.Sum(e => e.Size));
                json.WriteNumber("missing", missing);
                json.WriteEndObject();
                json.WriteStartArray("files");
                foreach (var entry in entries)
                {
                    json.WriteStartObject();
                    json.WriteString("k", entry.Key);
                    json.WriteString("K", entry.FullKey);
                    json.WriteNumber("a", entry.Archive);
                    json.WriteNumber("o", entry.Offset);
                    json.WriteNumber("s", entry.Size);
                    json.WriteString("sha256", entry.Sha256);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
                await json.FlushAsync(ct);
            }

            return new LanguagePackResult(locale, directory, entries.Count, entries.Sum(e => e.Size), missing);
        }

        private static long ExpectedChunkSize(DownloadItem item, Dictionary<string, ArchiveEntryLoc> archiveIndexMap)
        {
            if (archiveIndexMap.TryGetValue(item.EKeyHex, out ArchiveEntryLoc loc) && loc.Size > 0) return loc.Size;
            return item.CompressedSize > 0 ? item.CompressedSize : 0;
        }

        private static FileStream OpenArchive(string chunksDir, int index) =>
            new FileStream(Path.Combine(chunksDir, $"data.{index:000}"),
                FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1024 * 1024);

        private static void WritePartialMarker(string path, string locale, D3BuildCatalogItem build, VersionRecord versionInfo,
            int chunks, int consumed, List<string> missingKeys)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                using var json = new Utf8JsonWriter(file);
                json.WriteStartObject();
                json.WriteNumber("format", 1);
                json.WriteString("version", $"{build.Version}.{build.BuildNumber}");
                json.WriteString("locale", locale);
                json.WriteString("buildConfig", versionInfo.BuildConfig);
                json.WriteNumber("chunks", chunks);
                json.WriteNumber("consumed", consumed);
                json.WriteStartArray("missing");
                foreach (string key in missingKeys) json.WriteStringValue(key);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            catch
            {
                // A lost progress marker only costs a restart, never correctness.
            }
        }

        /// <summary>
        /// Rebuilds how far a previous run got. Chunks are appended in a fixed,
        /// deterministic order, so walking the planned sizes against the archive
        /// files proves exactly where the last run stopped; the marker supplies the
        /// chunk index and the chunks the CDN refused (they wrote no bytes).
        /// Returns the number of chunks already satisfied.
        /// </summary>
        private static int ResumePartialExport(
            string markerPath, string chunksDir, string locale, D3BuildCatalogItem build, VersionRecord versionInfo,
            List<DownloadItem> delta, Dictionary<string, ArchiveEntryLoc> archiveIndexMap, Action<string> log,
            List<(string Key, string FullKey, int Archive, long Offset, long Size, string Sha256)> entries,
            List<string> missingKeys, out int archiveIndex, out long archiveSize)
        {
            archiveIndex = 0;
            archiveSize = 0;
            var files = Directory.GetFiles(chunksDir, "data.*")
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
            if (files.Count == 0) return 0;

            int consumed = 0;
            bool hasMarker = File.Exists(markerPath);
            if (hasMarker)
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(markerPath));
                    var marker = document.RootElement;
                    bool matches = marker.GetProperty("version").GetString() == $"{build.Version}.{build.BuildNumber}"
                                && marker.GetProperty("locale").GetString() == locale
                                && marker.GetProperty("buildConfig").GetString() == versionInfo.BuildConfig
                                && marker.GetProperty("chunks").GetInt32() == delta.Count;
                    if (!matches)
                    {
                        DiscardPartialExport(files, markerPath);
                        log($"      {locale}: discarded an incomplete export from a different build.");
                        return 0;
                    }
                    consumed = marker.GetProperty("consumed").GetInt32();
                    foreach (var element in marker.GetProperty("missing").EnumerateArray())
                        missingKeys.Add(element.GetString());
                }
                catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    DiscardPartialExport(files, markerPath);
                    log($"      {locale}: previous partial data discarded (progress marker unreadable).");
                    return 0;
                }
            }
            else
            {
                // No marker (an older build wrote these archives). Adopt them anyway:
                // every resumed chunk is checked against its expected size and must
                // decode as valid BLTE, so a shifted layout stops at the first
                // mismatch instead of producing a wrong map.
                consumed = delta.Count;
            }

            if (consumed < 0 || consumed > delta.Count) consumed = 0;
            var missingSet = new HashSet<string>(missingKeys, StringComparer.OrdinalIgnoreCase);
            var sizes = files.Select(f => new FileInfo(f).Length).ToArray();
            int fileIndex = 0;
            long offset = 0;
            int resumed = 0;
            byte[] buffer = null;
            try
            {
                for (int i = 0; i < consumed; i++)
                {
                    string ekey = delta[i].EKeyHex.ToUpperInvariant();
                    while (fileIndex < sizes.Length && offset == sizes[fileIndex]) { fileIndex++; offset = 0; }
                    if (missingSet.Contains(ekey)) { resumed++; continue; }
                    if (fileIndex >= sizes.Length) break;

                    long size = ExpectedChunkSize(delta[i], archiveIndexMap);
                    if (size <= 0 || offset + size > sizes[fileIndex]) break;

                    if (buffer == null || buffer.Length < size) buffer = new byte[Math.Max(size, 1024 * 1024)];
                    using (var reader = new FileStream(files[fileIndex], FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        reader.Seek(offset, SeekOrigin.Begin);
                        int read = 0;
                        while (read < size)
                        {
                            int chunk = reader.Read(buffer, read, (int)size - read);
                            if (chunk <= 0) break;
                            read += chunk;
                        }
                        if (read != size) break;
                    }
                    if (size < 30 || buffer[0] != (byte)'B' || buffer[1] != (byte)'L' || buffer[2] != (byte)'T' || buffer[3] != (byte)'E')
                        break; // corrupt tail: stop here and rewrite from this chunk
                    try
                    {
                        // Structural proof that the bytes really are this chunk: a
                        // shifted layout lands mid-chunk and fails to decode.
                        BlteDecoder.Decode(buffer.AsSpan(0, (int)size).ToArray());
                    }
                    catch
                    {
                        break;
                    }

                    entries.Add((ekey.Substring(0, 18), ekey, fileIndex, offset, size,
                        Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, (int)size))).ToLowerInvariant()));
                    offset += size;
                    resumed++;
                }
            }
            finally
            {
                // Drop anything after the verified prefix so the next append lands
                // exactly on a chunk boundary.
                try
                {
                    if (fileIndex < files.Count)
                    {
                        using (var tail = new FileStream(files[fileIndex], FileMode.Open, FileAccess.Write, FileShare.None))
                            if (tail.Length != offset) tail.SetLength(offset);
                        for (int k = fileIndex + 1; k < files.Count; k++) File.Delete(files[k]);
                    }
                }
                catch { }
            }

            if (resumed == 0)
            {
                DiscardPartialExport(files, markerPath);
                return 0;
            }

            archiveIndex = fileIndex;
            archiveSize = offset;
            log($"      {locale}: resuming at chunk {resumed:N0}/{delta.Count:N0} " +
                $"({entries.Sum(e => e.Size) / (1024.0 * 1024.0):N1} MB already on disk).");
            return resumed;
        }

        private static void DiscardPartialExport(List<string> files, string markerPath)
        {
            foreach (string file in files) { try { File.Delete(file); } catch { } }
            try { if (File.Exists(markerPath)) File.Delete(markerPath); } catch { }
        }

        private static async Task<byte[]> FetchChunkAsync(
            DownloadItem item,
            Dictionary<string, ArchiveEntryLoc> archiveIndexMap,
            string[] archives)
        {
            try
            {
                byte[] blob = await DownloadDataBlobByEKeyAsync(item.EKeyHex, archiveIndexMap, archives);
                // A real CASC chunk always carries the 30-byte header; anything
                // shorter came from a broken or retired archive entry.
                return blob != null && blob.Length >= 30 ? blob : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

using System;
using System.IO;
using System.Text;

namespace D3Downloader
{
    /// <summary>
    /// Writes the Battle.net .product.db protobuf metadata file.
    /// Matches the exact protobuf structure used by Blizzard deployments.
    /// </summary>
    internal static class ProductDbWriter
    {
        public static void Write(
            string targetDir,
            string region,
            string locale,
            string version,
            string buildConfig,
            string cdnConfig,
            string installKey,
            Action<string> log = null)
        {
            try
            {
                byte[] pdbBytes = BuildProductDb(targetDir, region, locale, version, buildConfig, cdnConfig, installKey);
                string outPath = Path.Combine(targetDir, ".product.db");

                if (File.Exists(outPath))
                {
                    try { File.SetAttributes(outPath, FileAttributes.Normal); } catch { }
                }

                File.WriteAllBytes(outPath, pdbBytes);
                try
                {
                    File.SetAttributes(outPath, FileAttributes.Hidden);
                }
                catch { }

                log?.Invoke($"      Generated .product.db ({pdbBytes.Length} bytes, hidden).");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[WARNING] Failed to write .product.db: {ex.Message}");
            }
        }

        public static void EnsureProductDb(string targetDir, Action<string> log = null)
        {
            string outPath = Path.Combine(targetDir, ".product.db");
            if (File.Exists(outPath)) return;

            string buildInfoPath = Path.Combine(targetDir, ".build.info");
            string region = "us";
            string locale = "enUS";
            string version = "2.8.0.99920";
            string buildConfig = "e34fc3fb7831a77e0ad7def0bb399ac7";
            string cdnConfig = "e34fc3fb7831a77e0ad7def0bb399ac7";
            string installKey = "827b919b9e88016acb948efaaf76df08";

            if (File.Exists(buildInfoPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(buildInfoPath);
                    if (lines.Length >= 2)
                    {
                        string[] cols = lines[1].Split('|');
                        if (cols.Length > 0 && !string.IsNullOrWhiteSpace(cols[0])) region = cols[0].Trim();
                        if (cols.Length > 2 && !string.IsNullOrWhiteSpace(cols[2])) buildConfig = cols[2].Trim();
                        if (cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3])) cdnConfig = cols[3].Trim();
                        if (cols.Length > 4 && !string.IsNullOrWhiteSpace(cols[4])) installKey = cols[4].Trim();
                        if (cols.Length > 12 && !string.IsNullOrWhiteSpace(cols[12])) version = cols[12].Trim();
                    }
                }
                catch { }
            }

            string launcherDb = Path.Combine(targetDir, "Launcher.db");
            if (File.Exists(launcherDb))
            {
                try
                {
                    string l = File.ReadAllText(launcherDb).Trim();
                    if (!string.IsNullOrEmpty(l)) locale = l;
                }
                catch { }
            }

            Write(targetDir, region, locale, version, buildConfig, cdnConfig, installKey, log);
        }

        public static byte[] BuildProductDb(
            string targetDir,
            string region,
            string locale,
            string version,
            string buildConfig,
            string cdnConfig,
            string installKey)
        {
            region = string.IsNullOrWhiteSpace(region) ? "us" : region.Trim();
            locale = string.IsNullOrWhiteSpace(locale) ? "enUS" : locale.Trim();
            version = string.IsNullOrWhiteSpace(version) ? "2.8.0.99920" : version.Trim();
            buildConfig = string.IsNullOrWhiteSpace(buildConfig) ? "e34fc3fb7831a77e0ad7def0bb399ac7" : buildConfig.Trim();
            cdnConfig = string.IsNullOrWhiteSpace(cdnConfig) ? buildConfig : cdnConfig.Trim();
            installKey = string.IsNullOrWhiteSpace(installKey) ? "827b919b9e88016acb948efaaf76df08" : installKey.Trim();

            string normalizedPath = Path.GetFullPath(targetDir).Replace('\\', '/');
            string geoCountry = region.Equals("us", StringComparison.OrdinalIgnoreCase) ? "USA" : region.ToUpperInvariant();
            string country = region.Equals("us", StringComparison.OrdinalIgnoreCase) ? "US" : region.ToUpperInvariant();
            string tags = $"Windows {region.ToUpperInvariant()}? acct-USA? geoip-US? {locale} speech?:Windows {region.ToUpperInvariant()}? acct-USA? geoip-US? {locale} text?";

            // Field 3: InstallSettings
            var f3 = new MemoryStream();
            WriteString(f3, 1, normalizedPath);
            WriteString(f3, 2, region);
            WriteVarint(f3, 3, 0);
            WriteVarint(f3, 4, 2);
            WriteVarint(f3, 5, 3);
            WriteString(f3, 6, locale);
            WriteString(f3, 7, locale);

            var t8 = new MemoryStream();
            WriteString(t8, 1, locale);
            WriteVarint(t8, 2, 3);
            WriteBytesField(f3, 8, t8.ToArray());

            WriteString(f3, 9, "");
            WriteString(f3, 10, "");
            WriteString(f3, 11, geoCountry);
            WriteString(f3, 12, country);
            WriteString(f3, 13, "");

            // Field 4: Operations
            var f4_1 = new MemoryStream();
            WriteVarint(f4_1, 1, 1);
            WriteVarint(f4_1, 2, 1);
            WriteVarint(f4_1, 3, 1);
            WriteVarint(f4_1, 4, 0);
            WriteVarint(f4_1, 5, 0);
            WriteString(f4_1, 7, version);
            WriteString(f4_1, 10, "");
            WriteString(f4_1, 12, buildConfig);
            WriteString(f4_1, 14, cdnConfig);
            WriteString(f4_1, 16, installKey);
            WriteString(f4_1, 17, tags);
            WriteVarint(f4_1, 21, 0);

            // Tag 2 in Field 4: 15 bytes
            var t2 = new MemoryStream();
            WriteFixed64(t2, 1, 0UL);
            WriteVarint(t2, 2, 0);
            WriteVarint(t2, 3, 0);
            WriteVarint(t2, 5, 0);

            // Tag 3 in Field 4: 9 bytes
            var t3 = new MemoryStream();
            WriteFixed64(t3, 1, 0UL);

            // Tag 4 in Field 4:
            var t4_6 = new MemoryStream();
            WriteString(t4_6, 1, buildConfig);
            WriteVarint(t4_6, 6, 1580122212L);
            WriteVarint(t4_6, 6, 6996631542L);
            WriteVarint(t4_6, 6, 4568045299L);
            WriteVarint(t4_6, 7, 437324355L);
            WriteVarint(t4_6, 7, 1560999097L);
            WriteVarint(t4_6, 7, 0L);

            var t4 = new MemoryStream();
            WriteFixed64(t4, 2, 0x3FF0000000000000UL); // double 1.0 (00 00 00 00 00 00 F0 3F)
            WriteVarint(t4, 4, 1998323452L);
            WriteVarint(t4, 5, 0L);
            WriteBytesField(t4, 6, t4_6.ToArray());

            var f4 = new MemoryStream();
            WriteBytesField(f4, 1, f4_1.ToArray());
            WriteBytesField(f4, 2, t2.ToArray());
            WriteBytesField(f4, 3, t3.ToArray());
            WriteBytesField(f4, 4, t4.ToArray());

            // Root message
            var root = new MemoryStream();
            WriteString(root, 1, "diablo3");
            WriteString(root, 2, "d3");
            WriteBytesField(root, 3, f3.ToArray());
            WriteBytesField(root, 4, f4.ToArray());
            WriteString(root, 6, "d3");
            WriteVarint(root, 7, 0);
            WriteString(root, 8, "{\"user_install_package_settings\":[]}");

            return root.ToArray();
        }

        private static void WriteTag(Stream s, int fieldNumber, int wireType)
        {
            WriteRawVarint(s, (ulong)((fieldNumber << 3) | wireType));
        }

        private static void WriteVarint(Stream s, int fieldNumber, long value)
        {
            WriteTag(s, fieldNumber, 0);
            WriteRawVarint(s, (ulong)value);
        }

        private static void WriteString(Stream s, int fieldNumber, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            WriteTag(s, fieldNumber, 2);
            WriteRawVarint(s, (ulong)bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        private static void WriteBytesField(Stream s, int fieldNumber, byte[] bytes)
        {
            WriteTag(s, fieldNumber, 2);
            WriteRawVarint(s, (ulong)bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        private static void WriteFixed64(Stream s, int fieldNumber, ulong value)
        {
            WriteTag(s, fieldNumber, 1);
            byte[] b = BitConverter.GetBytes(value);
            s.Write(b, 0, 8);
        }

        private static void WriteRawVarint(Stream s, ulong value)
        {
            while (value >= 0x80)
            {
                s.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            s.WriteByte((byte)value);
        }
    }
}

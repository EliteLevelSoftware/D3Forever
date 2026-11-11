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
    public enum D3BuildCategory
    {
        Retail,
        Ptr,
        Beta
    }

    public sealed class D3BuildCatalogItem
    {
        public string Version { get; init; }
        public int BuildNumber { get; init; }
        public bool IsCasc { get; init; }
        public bool IsRoS { get; init; }
        public D3BuildCategory Category { get; init; } = D3BuildCategory.Retail;
        public string ProductCode { get; init; } = "d3";
        public string ReleaseDate { get; init; }
        public string Summary { get; init; }
        public string BaseDirectUrl { get; init; } = D3BuildCatalog.VanillaDirectBaseUrl;
        public string MfilFileName { get; init; } = D3BuildCatalog.VanillaMfilFileName;
        public int BaseBuildNumber { get; init; } = 8370;
        public string CdnConfigUrl { get; init; }
        public bool RequiresOfflineArchive { get; init; } = false;

        public string CheckUrl => RequiresOfflineArchive
            ? ""
            : (!string.IsNullOrEmpty(CdnConfigUrl)
                ? CdnConfigUrl
                : (IsCasc
                    ? $"http://us.patch.battle.net:1119/{ProductCode}/versions"
                    : (BuildNumber > BaseBuildNumber
                        ? $"{BaseDirectUrl}/Updates/d3-0-{BuildNumber}-Win-final.MPQ"
                        : $"{BaseDirectUrl}/Data_D3/PC/MPQs/base-Win.mpq")));

        public string DisplayTitle => IsCasc
            ? $"{Version}.{BuildNumber} — [CASC / {ProductCode} 64-bit] ({Summary})"
            : (IsRoS
                ? $"{Version}.{BuildNumber} — [{Category} RoS MPQ 32-bit] ({ReleaseDate} - {Summary})"
                : $"{Version}.{BuildNumber} — [{Category} Vanilla MPQ 32-bit] ({ReleaseDate} - {Summary})");

        public override string ToString() => DisplayTitle;
    }

    public static class D3BuildCatalog
    {
        public const string VanillaDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod-retail/NA/8370.direct";
        public const string VanillaMfilFileName = "d3-16603-5A26B6C69DFDB16C25E1D05B0EEE83C2.mfil";
        public const string VanillaPtrDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod/public-test/Global/10485.direct";
        public const string VanillaBetaDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod/20FB5BE9/NA/7162.direct";

        public const string RosDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod-retail/NA/20813.direct";
        public const string RosMfilFileName = "d3-28709-85F13E62259AEC63DC654C8F847A7CAF.mfil";
        public const string RosPtrDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod/public-test/NA/23119.direct";
        public const string RosBetaDirectBaseUrl = "http://dist.blizzard.com.edgesuite.net/d3-pod/beta/00FC2DE9/NA/18962.direct";

        public static readonly IReadOnlyList<D3BuildCatalogItem> AllBuilds = new List<D3BuildCatalogItem>
        {
            // =========================================================================
            // REAPER OF SOULS Ã¢â‚¬â€ RETAIL CATEGORY
            // =========================================================================
            new() { Version = "2.8.0", BuildNumber = 99920, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3", ReleaseDate = "Live Retail", Summary = "Global Live Retail Client (64-bit CASC)" },
            new() { Version = "2.8.1", BuildNumber = 101167, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3cn", ReleaseDate = "CN CDN", Summary = "Patch 2.8.1 Production Client (64-bit CASC)" },
            new() { Version = "2.7.8", BuildNumber = 97927, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3", ReleaseDate = "2024-09-10", Summary = "Patch 2.7.8 Post-Loc Production Build", RequiresOfflineArchive = true, CdnConfigUrl = "http://us.cdn.blizzard.com/tpr/diablo3/config/3c/08/3c08a11365511a36115399741311d6a1" },
            new() { Version = "2.7.7", BuildNumber = 93903, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3", ReleaseDate = "2024-02-06", Summary = "Patch 2.7.7a Production Release Candidate 2", RequiresOfflineArchive = true, CdnConfigUrl = "http://us.cdn.blizzard.com/tpr/diablo3/config/0e/49/0e49135ccc3ae6496e78c92e280da93f" },
            new() { Version = "2.7.7", BuildNumber = 92326, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3", ReleaseDate = "2024-01-09", Summary = "Patch 2.7.7 Production Release Candidate 1", RequiresOfflineArchive = true, CdnConfigUrl = "http://us.cdn.blizzard.com/tpr/diablo3/config/ef/5d/ef5defe7bbf218a323336273c86be31c" },
            new() { Version = "2.7.6", BuildNumber = 91007, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3", ReleaseDate = "2023-09-12", Summary = "Patch 2.7.6 Solo Self-Found Update (64-bit CASC)", RequiresOfflineArchive = true, CdnConfigUrl = "http://us.cdn.blizzard.com/tpr/diablo3/config/80/70/8070cfaf79f4ce7338a895fff28b135d" },
            new() { Version = "2.7.4", BuildNumber = 84161, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3", ReleaseDate = "2022-08-23", Summary = "Patch 2.7.4 Angelic Crucibles Update (64-bit CASC)", RequiresOfflineArchive = true, CdnConfigUrl = "http://us.cdn.blizzard.com/tpr/diablo3/config/51/94/5194054cfa6480884c9a451b6f1107a3" },
            new() { Version = "2.7.3", BuildNumber = 82785, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Retail, ProductCode = "d3cn", ReleaseDate = "2022-04-12", Summary = "Patch 2.7.3 Echoing Nightmare Client (64-bit CASC)", RequiresOfflineArchive = true, CdnConfigUrl = "http://us.cdn.blizzard.com/tpr/diablo3/config/ce/be/cebe6c6f58867665a442156284766fd2" },
            new() { Version = "2.1.2", BuildNumber = 28709, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = RosMfilFileName, ReleaseDate = "2015-01-13", Summary = "Patch 2.1.2 (Ancient Items, Conduit Pylon Rework)" },
            new() { Version = "2.1.1", BuildNumber = 27255, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-27255-35F65D5686569C9A188AC0264ED26583.mfil", ReleaseDate = "2014-09-23", Summary = "Patch 2.1.1 (Kadala Bloodshard Costs, Pet Toughness)" },
            new() { Version = "2.1.0", BuildNumber = 26451, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-26451-6032812D57D4CE684194552583B4275F.mfil", ReleaseDate = "2014-08-26", Summary = "Major Update (Greater Rifts, Legendary Gems, The Vault)" },
            new() { Version = "2.0.6", BuildNumber = 24641, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-24641-4D78C4B7698F7F79B3F5FD02049BF58C.mfil", ReleaseDate = "2014-06-10", Summary = "Patch 2.0.6 (Crafting Material Removal, Class Balancing)" },
            new() { Version = "2.0.5a", BuildNumber = 24017, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-24017-19A8EF9C0CDE1BABCA831DA6EF2481B7.mfil", ReleaseDate = "2014-05-15", Summary = "Patch 2.0.5a Stability & Bugfix Hotfix" },
            new() { Version = "2.0.5", BuildNumber = 23920, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-23920-E2B432A14395F977F64B476003FF5BB8.mfil", ReleaseDate = "2014-05-13", Summary = "Patch 2.0.5 (Horadric Cache Torment Drop Rates, Gold Trade)" },
            new() { Version = "2.0.4", BuildNumber = 23119, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-23119-137D06EF773BC6F7BB015C088AAC8568.mfil", ReleaseDate = "2014-04-08", Summary = "Patch 2.0.4 (Crusader Wrath Fixes, Class Tweaks)" },
            new() { Version = "2.0.3", BuildNumber = 22427, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22427-426C3AE6756462BF912686D1FE56CD22.mfil", ReleaseDate = "2014-03-11", Summary = "Patch 2.0.3 Bugfix & Stability Update" },
            new() { Version = "2.0.2", BuildNumber = 22274, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22274-B7A49A3F2CDE04523DC09380EF88DF52.mfil", ReleaseDate = "2014-03-04", Summary = "Patch 2.0.2 Pre-Expansion Optimization Patch" },
            new() { Version = "2.0.1", BuildNumber = 22044, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22044-0DD436E701850277986966F412FDC4B7.mfil", ReleaseDate = "2014-02-25", Summary = "Reaper of Souls Pre-Patch (Loot 2.0, Paragon 2.0)" },
            new() { Version = "2.0.0", BuildNumber = 20813, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Retail, BaseBuildNumber = 20813, BaseDirectUrl = RosDirectBaseUrl, MfilFileName = "d3-22044-0DD436E701850277986966F412FDC4B7.mfil", ReleaseDate = "2014-01-20", Summary = "Reaper of Souls Unpatched Retail Base MPQ Build" },
            // =========================================================================
            // REAPER OF SOULS Ã¢â‚¬â€ PUBLIC TEST REALM (PTR)
            // =========================================================================
            new() { Version = "2.7.8", BuildNumber = 96670, IsCasc = true, IsRoS = true, Category = D3BuildCategory.Ptr, ProductCode = "d3t", ReleaseDate = "2024-08-20", Summary = "Patch 2.7.8 Public Test Realm Client (64-bit CASC)", RequiresOfflineArchive = true },
            new() { Version = "2.1.2", BuildNumber = 28709, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-28709-D329E14E226965F76BDFDBC8BA9AD34C.mfil", ReleaseDate = "2015-01-08", Summary = "Patch 2.1.2 PTR Release Candidate Build" },
            new() { Version = "2.1.2", BuildNumber = 28341, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-28341-16554336E3E1757B9C0BB33438C527AB.mfil", ReleaseDate = "2014-11-20", Summary = "Patch 2.1.2 PTR Wave 1 (Ancient Items Debut)" },
            new() { Version = "2.1.0", BuildNumber = 26370, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-26370-C33663E1AE5D42CDDAFA24FFE2118D94.mfil", ReleaseDate = "2014-08-06", Summary = "Patch 2.1.0 PTR Release Candidate Build" },
            new() { Version = "2.1.0", BuildNumber = 26281, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-26281-4159E96D386B9C178AD4A7269ECA8E1F.mfil", ReleaseDate = "2014-07-30", Summary = "Patch 2.1.0 PTR Iteration 4 (Greater Rift Pacing)" },
            new() { Version = "2.1.0", BuildNumber = 26067, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-26067-EDD6F989981913DD9931F18B995DCA3F.mfil", ReleaseDate = "2014-07-23", Summary = "Patch 2.1.0 PTR Iteration 3 (Seasons Testing)" },
            new() { Version = "2.1.0", BuildNumber = 25658, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-25658-3DD1C595C4523AB7CA30D9AB5B2E75C3.mfil", ReleaseDate = "2014-07-16", Summary = "Patch 2.1.0 PTR Iteration 2 (Legendary Gems Balance)" },
            new() { Version = "2.1.0", BuildNumber = 25390, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-25390-7C7BFEC13676ACCB536F2C27A7E9C707.mfil", ReleaseDate = "2014-07-09", Summary = "Patch 2.1.0 PTR Iteration 1 (Cesspools & Greater Rifts)" },
            new() { Version = "2.1.0", BuildNumber = 25014, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Ptr, BaseBuildNumber = 23119, BaseDirectUrl = RosPtrDirectBaseUrl, MfilFileName = "d3t-25014-C93DA8CC000B60D9A69F90BD9635BC44.mfil", ReleaseDate = "2014-06-25", Summary = "Patch 2.1.0 Initial PTR Launch Build" },
            // =========================================================================
            // REAPER OF SOULS Ã¢â‚¬â€ CLOSED BETA CATEGORY
            // =========================================================================
            new() { Version = "Beta", BuildNumber = 21962, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-21962-733C32C3D5BB5B89EE6DAFB8A7418D17.mfil", ReleaseDate = "2014-02-20", Summary = "RoS Closed Beta Final Patch Before 2.0 Pre-Patch" },
            new() { Version = "Beta", BuildNumber = 21806, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-21806-4D9B877EA233CAE52D475B1B177F75C8.mfil", ReleaseDate = "2014-02-13", Summary = "RoS Closed Beta Iteration 9 (Crusader Final Polish)" },
            new() { Version = "Beta", BuildNumber = 21390, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-21390-BE3F50A1361453D3087C67445B34444E.mfil", ReleaseDate = "2014-02-06", Summary = "RoS Closed Beta Iteration 8 (Torment Scaling)" },
            new() { Version = "Beta", BuildNumber = 20874, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-20874-83B9CD4AD5271C047881602C465BA236.mfil", ReleaseDate = "2013-12-13", Summary = "RoS Closed Beta Patch 2.0.1 In-Game Notes Update" },
            new() { Version = "Beta", BuildNumber = 20493, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-20493-74305805A6878CE06D95546626D8F91F.mfil", ReleaseDate = "2014-01-22", Summary = "RoS Closed Beta Iteration 6 (Nephalem Rifts)" },
            new() { Version = "Beta", BuildNumber = 20134, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-20134-F09D7C0359A83B811BFF9172E7F69356.mfil", ReleaseDate = "2014-01-15", Summary = "RoS Closed Beta Iteration 5 (Blood Shards)" },
            new() { Version = "Beta", BuildNumber = 19992, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-19992-F7C9F26E91DE3358743D0050B1593013.mfil", ReleaseDate = "2014-01-08", Summary = "RoS Closed Beta Iteration 4 (Adventure Mode)" },
            new() { Version = "Beta", BuildNumber = 19862, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-19862-A845A173B2E62E656A380D30DDC57628.mfil", ReleaseDate = "2013-12-23", Summary = "RoS Closed Beta Iteration 3 (Kadala Gambling)" },
            new() { Version = "Beta", BuildNumber = 19796, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-19796-EC2230BA859671689C3A8067526199E4.mfil", ReleaseDate = "2013-12-19", Summary = "RoS Closed Beta Iteration 2 (Bounties System)" },
            new() { Version = "Beta", BuildNumber = 19561, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-19561-9EF4DEF9767D3A16A8354368C012C3B5.mfil", ReleaseDate = "2013-12-12", Summary = "RoS Closed Beta Iteration 1 (Act V Westmarch)" },
            new() { Version = "Beta", BuildNumber = 19177, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "d3b-19177-59FEF1D88EEE46964D175D7C2BE87DF9.mfil", ReleaseDate = "2013-12-03", Summary = "RoS Closed Beta F&F First Update Patch" },
            new() { Version = "Beta", BuildNumber = 18962, IsCasc = false, IsRoS = true, Category = D3BuildCategory.Beta, BaseBuildNumber = 18962, BaseDirectUrl = RosBetaDirectBaseUrl, MfilFileName = "", ReleaseDate = "2013-08-30", Summary = "Original RoS Friends & Family Closed Beta Base Client" },
            // =========================================================================
            // VANILLA CLASSIC Ã¢â‚¬â€ RETAIL CATEGORY
            // =========================================================================
            new() { Version = "1.0.8a", BuildNumber = 16603, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-05-10", Summary = "Final Vanilla 1.0.8 Hotfix Build" },
            new() { Version = "1.0.8",  BuildNumber = 16416, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-05-07", Summary = "Monster Density & Co-op XP Bonus Patch" },
            new() { Version = "1.0.7a", BuildNumber = 15295, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-02-15", Summary = "Duelling & Demon Hunter Hotfix" },
            new() { Version = "1.0.7",  BuildNumber = 14633, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2013-02-12", Summary = "Brawling, Marquise Gems & BoA Crafting" },
            new() { Version = "1.0.6a", BuildNumber = 13644, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-12-06", Summary = "Quest & Stability Patch" },
            new() { Version = "1.0.6",  BuildNumber = 13300, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-11-27", Summary = "Network & Bugfix Patch" },
            new() { Version = "1.0.5a", BuildNumber = 12811, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-10-25", Summary = "Crash & Achievement Fix Patch" },
            new() { Version = "1.0.5",  BuildNumber = 12480, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-10-16", Summary = "Monster Power (MP1-10) & Infernal Machine" },
            new() { Version = "1.0.4",  BuildNumber = 11327, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-08-21", Summary = "Paragon 1.0 (Levels 1-100) & Legendary Overhaul" },
            new() { Version = "1.0.3b", BuildNumber = 10485, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-07-10", Summary = "Auction House & Stability Patch" },
            new() { Version = "1.0.3a", BuildNumber = 10235, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-26", Summary = "Zombie Bears & General Bugfix Patch" },
            new() { Version = "1.0.3",  BuildNumber = 10057, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-19", Summary = "Inferno Tuning, IAS Nerf & Repair Cost Patch" },
            new() { Version = "1.0.2c", BuildNumber = 9991,  IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-12", Summary = "Mac/PC Client Stability Patch" },
            new() { Version = "1.0.2b", BuildNumber = 9950,  IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-07", Summary = "Login & Auction House Responsiveness Patch" },
            new() { Version = "1.0.2a", BuildNumber = 9858,  IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-06-01", Summary = "Item & Skill Tooltip Fix Patch" },
            new() { Version = "1.0.2",  BuildNumber = 9749,  IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-05-29", Summary = "First Post-Launch Client Patch" },
            new() { Version = "1.0.1",  BuildNumber = 9558,  IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-05-15", Summary = "Original May 15, 2012 Launch Day Patch" },
            new() { Version = "1.0.0",  BuildNumber = 8370,  IsCasc = false, IsRoS = false, Category = D3BuildCategory.Retail, BaseBuildNumber = 8370, BaseDirectUrl = VanillaDirectBaseUrl, MfilFileName = VanillaMfilFileName, ReleaseDate = "2012-05-14", Summary = "Original Unpatched Retail DVD Base MPQ Build" },
            // =========================================================================
            // VANILLA CLASSIC Ã¢â‚¬â€ PUBLIC TEST REALM (PTR)
            // =========================================================================
            new() { Version = "1.0.8", BuildNumber = 16256, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-16256-53616B71C058AED2B1A1D76B6315B158.mfil", ReleaseDate = "2013-04-30", Summary = "Patch 1.0.8 PTR Release Candidate 4" },
            new() { Version = "1.0.8", BuildNumber = 16107, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-16107-747CB4A9FF241AA308578179C4F07E8F.mfil", ReleaseDate = "2013-04-23", Summary = "Patch 1.0.8 PTR Iteration 3 (Matchmaking Tags)" },
            new() { Version = "1.0.8", BuildNumber = 16033, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-16033-B36721880A28BA1B030B300DA7323A29.mfil", ReleaseDate = "2013-04-16", Summary = "Patch 1.0.8 PTR Iteration 2 (Identify All & Density)" },
            new() { Version = "1.0.8", BuildNumber = 15926, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-15926-6DBCA3344FA8F980522B03661369D850.mfil", ReleaseDate = "2013-04-09", Summary = "Patch 1.0.8 Initial PTR Launch Build" },
            new() { Version = "1.0.7", BuildNumber = 14528, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-14528-27C385F1BC96E992210BD3DEDBECFA8F.mfil", ReleaseDate = "2013-02-05", Summary = "Patch 1.0.7 PTR Release Candidate 4" },
            new() { Version = "1.0.7", BuildNumber = 14456, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-14456-57EB0C55496919125E32926235DF86CA.mfil", ReleaseDate = "2013-01-29", Summary = "Patch 1.0.7 PTR Iteration 3 (Marquise Gems Balance)" },
            new() { Version = "1.0.7", BuildNumber = 14295, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-14295-404EE66D5F09A07CB8A988D203891CDB.mfil", ReleaseDate = "2013-01-22", Summary = "Patch 1.0.7 PTR Iteration 2 (Archon & Tempest Rush)" },
            new() { Version = "1.0.7", BuildNumber = 14161, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-14161-A5272C89921AA134282B3A1E2C582A6C.mfil", ReleaseDate = "2013-01-15", Summary = "Patch 1.0.7 Initial PTR Launch Build (Brawling)" },
            new() { Version = "1.0.5", BuildNumber = 12389, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-12389-2071DA320DE093A4E697154F6D9DB180.mfil", ReleaseDate = "2012-10-08", Summary = "Patch 1.0.5 PTR Release Candidate 4" },
            new() { Version = "1.0.5", BuildNumber = 12289, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-12289-C26F80F4E654CE96B5284C0543B857E7.mfil", ReleaseDate = "2012-10-02", Summary = "Patch 1.0.5 PTR Iteration 3 (Infernal Machine)" },
            new() { Version = "1.0.5", BuildNumber = 12166, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-12166-8947593DAC4C924A21CDE449A4D7BCA3.mfil", ReleaseDate = "2012-09-24", Summary = "Patch 1.0.5 PTR Iteration 2 (Monster Power)" },
            new() { Version = "1.0.5", BuildNumber = 12150, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "d3t-12150-9A72661DD9DE987F9D25641ECA1A3DE7.mfil", ReleaseDate = "2012-09-21", Summary = "Patch 1.0.5 Initial PTR Launch Build" },
            new() { Version = "1.0.3", BuildNumber = 10485, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Ptr, BaseBuildNumber = 10485, BaseDirectUrl = VanillaPtrDirectBaseUrl, MfilFileName = "", ReleaseDate = "2012-07-10", Summary = "Vanilla PTR Base Client Archive" },
            // =========================================================================
            // VANILLA CLASSIC Ã¢â‚¬â€ CLOSED BETA CATEGORY
            // =========================================================================
            new() { Version = "Beta", BuildNumber = 9359, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3b-9359-68ED600EDCC64664BF58EB40B2D7194A.mfil", ReleaseDate = "2012-04-19", Summary = "Vanilla Open Beta Weekend Client (April 20-23, 2012)" },
            new() { Version = "Beta", BuildNumber = 9327, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3b-9327-B14F344A3DCA481497ACE3B1C8014883.mfil", ReleaseDate = "2012-04-17", Summary = "Vanilla Beta Patch 17" },
            new() { Version = "Beta", BuildNumber = 9183, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-9183-51F7EC2D1A85DEC15996ADA75AF737B4.mfil", ReleaseDate = "2012-04-09", Summary = "Vanilla Beta Patch 16" },
            new() { Version = "Beta", BuildNumber = 8896, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8896-9A8717AF2CEC9EA05BCE2DFE632BCC3F.mfil", ReleaseDate = "2012-03-16", Summary = "Vanilla Beta Patch 15" },
            new() { Version = "Beta", BuildNumber = 8815, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8815-45C1BECF28BA4F6677F934AC8643348A.mfil", ReleaseDate = "2012-03-08", Summary = "Vanilla Beta Patch 14 (Hardcore Mode Added)" },
            new() { Version = "Beta", BuildNumber = 8610, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8610-C5D7D98C17EDB87C5DD07225CC40208B.mfil", ReleaseDate = "2012-02-18", Summary = "Vanilla Beta Patch 13 (Skill Rune System Overhaul)" },
            new() { Version = "Beta", BuildNumber = 8392, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8392-B5B8EBBA53C362322E1D90D965DAE796.mfil", ReleaseDate = "2012-02-03", Summary = "Vanilla Beta Patch 12" },
            new() { Version = "Beta", BuildNumber = 8350, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8350-CB476B021D6C830BC8C07D369A685190.mfil", ReleaseDate = "2012-01-30", Summary = "Vanilla Beta Patch 11" },
            new() { Version = "Beta", BuildNumber = 8296, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8296-941A7BD6BAF437A7A7773644DA9B795B.mfil", ReleaseDate = "2012-01-23", Summary = "Vanilla Beta Patch 10" },
            new() { Version = "Beta", BuildNumber = 8101, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8101-1A02921B149B3C3E410303C0037FAFFF.mfil", ReleaseDate = "2011-12-20", Summary = "Vanilla Beta Patch 9 (Attribute & Skill Overhaul)" },
            new() { Version = "Beta", BuildNumber = 8059, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-8059-C4FD424694EBBFB8D03094F26EE3C415.mfil", ReleaseDate = "2011-12-15", Summary = "Vanilla Beta Patch 8 (BattleTags Introduced)" },
            new() { Version = "Beta", BuildNumber = 7931, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-7931-7AE2328F2FF7EE9F27D746E4BEF83EB8.mfil", ReleaseDate = "2011-12-01", Summary = "Vanilla Beta Patch 7 (Real-Money Auction House)" },
            new() { Version = "Beta", BuildNumber = 7841, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-7841-D32C2A1C2D49B5109110980904077160.mfil", ReleaseDate = "2011-11-17", Summary = "Vanilla Beta Patch 6 (Profiles & Achievements)" },
            new() { Version = "Beta", BuildNumber = 7728, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-7728-812E3AE9810AA1AE273833DF0728C747.mfil", ReleaseDate = "2011-11-03", Summary = "Vanilla Beta Patch 5 (Demon Hunter Rework)" },
            new() { Version = "Beta", BuildNumber = 7447, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-7447-EC3B8521C7D7F248B915765294C7EB0D.mfil", ReleaseDate = "2011-09-26", Summary = "Vanilla Beta Patch 4 (Server Data Removed)" },
            new() { Version = "Beta", BuildNumber = 7338, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-7338-9B15715C360B12A3088699E62BCB8059.mfil", ReleaseDate = "2011-09-14", Summary = "Vanilla Beta Patch 3 (Auction House Added)" },
            new() { Version = "Beta", BuildNumber = 7318, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-7318-A0F73AD55B049A9A6B3D94D7B944F07E.mfil", ReleaseDate = "2011-09-07", Summary = "Vanilla Beta Patch 2" },
            new() { Version = "Beta", BuildNumber = 7200, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "d3-7200-31F92CE61E8FD73FE6D0B4C838973230.mfil", ReleaseDate = "2011-09-02", Summary = "Vanilla Beta Patch 1 (Friends & Family Launch)" },
            new() { Version = "Beta", BuildNumber = 7162, IsCasc = false, IsRoS = false, Category = D3BuildCategory.Beta, BaseBuildNumber = 7162, BaseDirectUrl = VanillaBetaDirectBaseUrl, MfilFileName = "", ReleaseDate = "2011-08-18", Summary = "Original Vanilla Friends & Family Beta Base Client" },
        };

        public static D3BuildCatalogItem Resolve(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            string q = query.Trim().ToLowerInvariant();
            if (q == "live" || q == "latest" || q == "newest") return AllBuilds[0];
            if (q == "ptr") return AllBuilds.FirstOrDefault(b => b.Category == D3BuildCategory.Ptr && b.IsCasc);
            if (q == "cn") return AllBuilds.FirstOrDefault(b => b.ProductCode == "d3cn");
            if (q == "vanilla" || q == "vanilla-latest") return AllBuilds.FirstOrDefault(b => !b.IsRoS && b.Category == D3BuildCategory.Retail);
            if (q == "beta") return AllBuilds.FirstOrDefault(b => b.Category == D3BuildCategory.Beta);
            if (int.TryParse(q, out int bNum))
            {
                var m = AllBuilds.FirstOrDefault(b => b.BuildNumber == bNum);
                if (m != null) return m;
            }
            return AllBuilds.FirstOrDefault(b => b.Version.Equals(q, StringComparison.OrdinalIgnoreCase)
                                              || $"{b.Version}.{b.BuildNumber}".Equals(q, StringComparison.OrdinalIgnoreCase)
                                              || b.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase));
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

        public static string TryResolveFromLocalArchive(string localArchiveRoot, string relativePath, D3BuildCatalogItem build)
        {
            if (string.IsNullOrWhiteSpace(localArchiveRoot) || !Directory.Exists(localArchiveRoot))
                return null;

            string normRel = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string baseRel = BackupEngine.GetRelativeFromBaseUrl(build.BaseDirectUrl).Replace('/', Path.DirectorySeparatorChar);

            // 1. Check RawBlizzardCDN with base URL subpath
            string p1 = Path.Combine(localArchiveRoot, "RawBlizzardCDN", baseRel, normRel);
            if (File.Exists(p1) && new FileInfo(p1).Length > 0) return p1;

            // 2. Check RawBlizzardCDN flat
            string p2 = Path.Combine(localArchiveRoot, "RawBlizzardCDN", normRel);
            if (File.Exists(p2) && new FileInfo(p2).Length > 0) return p2;

            // 3. Check _BaseArchiveCache
            string p3 = Path.Combine(localArchiveRoot, "_BaseArchiveCache", normRel);
            if (File.Exists(p3) && new FileInfo(p3).Length > 0) return p3;

            string p3b = Path.Combine(localArchiveRoot, "_BaseArchiveCache", Path.GetFileName(normRel));
            if (File.Exists(p3b) && new FileInfo(p3b).Length > 0) return p3b;

            // 4. Check directly under localArchiveRoot
            string p4 = Path.Combine(localArchiveRoot, normRel);
            if (File.Exists(p4) && new FileInfo(p4).Length > 0) return p4;

            // 5. Check in Fast Binaries folder for this build
            string editionDir = build.IsRoS ? "Reaper of Souls" : "Vanilla Classic";
            string categoryDir = build.Category switch
            {
                D3BuildCategory.Retail => "Retail",
                D3BuildCategory.Ptr => "Public Test Realm",
                D3BuildCategory.Beta => "Closed Beta",
                _ => "Other"
            };
            string p5 = Path.Combine(localArchiveRoot, editionDir, categoryDir, $"{build.Version}.{build.BuildNumber}", normRel);
            if (File.Exists(p5) && new FileInfo(p5).Length > 0) return p5;

            return null;
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
            CancellationToken ct = default,
            string localArchiveSource = null)
        {
            Directory.CreateDirectory(targetDir);
            string mpqRoot = Path.Combine(targetDir, "Data_D3", "PC", "MPQs");
            Directory.CreateDirectory(mpqRoot);

            List<MfilEntry> allMfilEntries = new();
            if (!string.IsNullOrWhiteSpace(build.MfilFileName))
            {
                string localMfil = TryResolveFromLocalArchive(localArchiveSource, build.MfilFileName, build);
                if (localMfil != null && File.Exists(localMfil))
                {
                    log($"[1/4] Resolving Blizzard .mfil manifest from local archive: {Path.GetFileName(localMfil)}");
                    string mfilText = await File.ReadAllTextAsync(localMfil, Encoding.UTF8, ct);
                    await File.WriteAllTextAsync(Path.Combine(targetDir, "Diablo III.mfil"), mfilText, Encoding.UTF8, ct);
                    await File.WriteAllTextAsync(Path.Combine(targetDir, build.MfilFileName), mfilText, Encoding.UTF8, ct);
                    allMfilEntries = ParseMfil(mfilText);
                    log($"      Parsed {allMfilEntries.Count} manifest entries from local archive.");
                }
                else
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
            }
            else
            {
                log($"[1/4] Historical base beta archive: manifest not required for client binaries.");
            }
            await File.WriteAllTextAsync(Path.Combine(targetDir, "Launcher.db"), locale, Encoding.ASCII, ct);

            // Step 2: Download or copy base-Win.mpq (always needed to extract & patch binaries)
            string baseWinMpqPath = Path.Combine(mpqRoot, "base-Win.mpq");
            string baseWinUrl = $"{build.BaseDirectUrl}/Data_D3/PC/MPQs/base-Win.mpq";
            string localBaseWin = TryResolveFromLocalArchive(localArchiveSource, "Data_D3/PC/MPQs/base-Win.mpq", build);

            if (localBaseWin != null && File.Exists(localBaseWin))
            {
                log($"[2/4] Resolving base-Win.mpq from local archive: {localBaseWin}");
                onProgress?.Invoke(5, "Copying base-Win.mpq from archive...");
                if (!baseWinMpqPath.Equals(localBaseWin, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(localBaseWin, baseWinMpqPath, overwrite: true);
                }
            }
            else
            {
                log($"[2/4] Downloading base-Win.mpq from Blizzard CDN ({build.BaseDirectUrl})...");
                onProgress?.Invoke(5, "Downloading base-Win.mpq...");
                await DownloadFileWithResumeAsync(http, baseWinUrl, baseWinMpqPath, -1L, ct);
            }

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

            // Step 3: If TargetBuild > BaseBuildNumber, download or copy Updates/d3-0-{BuildNumber}-Win-final.MPQ and apply diffs
            if (build.BuildNumber > build.BaseBuildNumber)
            {
                string patchMpqName = $"d3-0-{build.BuildNumber}-Win-final.MPQ";
                string updatesDir = Path.Combine(targetDir, "Updates");
                Directory.CreateDirectory(updatesDir);
                string patchMpqPath = Path.Combine(updatesDir, patchMpqName);
                string patchMpqUrl = $"{build.BaseDirectUrl}/Updates/{patchMpqName}";
                string localPatchMpq = TryResolveFromLocalArchive(localArchiveSource, $"Updates/{patchMpqName}", build);

                if (localPatchMpq != null && File.Exists(localPatchMpq))
                {
                    log($"[3/4] Resolving patch archive Updates/{patchMpqName} from local archive: {localPatchMpq}");
                    onProgress?.Invoke(12, $"Copying {patchMpqName} from archive...");
                    if (!patchMpqPath.Equals(localPatchMpq, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(localPatchMpq, patchMpqPath, overwrite: true);
                    }
                }
                else
                {
                    log($"[3/4] Downloading binary diff archive Updates/{patchMpqName} for {build.Version}.{build.BuildNumber}...");
                    onProgress?.Invoke(12, $"Downloading {patchMpqName}...");
                    await DownloadFileWithResumeAsync(http, patchMpqUrl, patchMpqPath, -1, ct);
                }

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
            bool isAllText = locale.Equals("allText", StringComparison.OrdinalIgnoreCase);
            bool isAllLocales = locale.Equals("allLocales", StringComparison.OrdinalIgnoreCase);

            var targetEntries = allMfilEntries
                .Where(e => {
                    if (e.RelativePath.EndsWith("base-Win.mpq", StringComparison.OrdinalIgnoreCase) ||
                        e.RelativePath.StartsWith("Updates", StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (string.IsNullOrWhiteSpace(e.Tag) ||
                        e.Tag.Equals("base", StringComparison.OrdinalIgnoreCase) ||
                        e.Tag.Equals("Win", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (isAllLocales) return true;

                    if (isAllText)
                    {
                        return e.RelativePath.EndsWith("_Text.mpq", StringComparison.OrdinalIgnoreCase) ||
                               e.Tag.Equals("enUS", StringComparison.OrdinalIgnoreCase);
                    }

                    return e.Tag.Equals(locale, StringComparison.OrdinalIgnoreCase);
                })
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
                string localCached = TryResolveFromLocalArchive(localArchiveSource, entry.RelativePath, build);

                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        if (localCached != null && File.Exists(localCached))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(fileDest)!);
                            if (!fileDest.Equals(localCached, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Copy(localCached, fileDest, overwrite: true);
                            }
                        }
                        else
                        {
                            await DownloadFileWithResumeAsync(http, fileUrl, fileDest, entry.Size, ct);
                        }
                        long cur = Interlocked.Add(ref completedBytes, entry.Size);
                        double pct = 15.0 + (cur / (double)totalBytes) * 85.0;
                        string fn = Path.GetFileName(fileDest);
                        string srcTag = (localCached != null && File.Exists(localCached)) ? "[Archive]" : "[CDN]";
                        onProgress?.Invoke(pct, $"[{pct:F1}%] {srcTag} {fn} ({entry.Size / (1024.0 * 1024):F1} MB)");
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
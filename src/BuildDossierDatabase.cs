using System;
using System.Collections.Generic;
using System.Linq;

namespace D3Downloader
{
    public sealed class BuildDossier
    {
        public string Version { get; init; } = string.Empty;
        public int BuildNumber { get; init; }
        public string Headline { get; init; } = string.Empty;
        public string EraContext { get; init; } = string.Empty;
        public List<string> MilestoneBadges { get; init; } = new();
        public List<string> KeyHighlights { get; init; } = new();
        public string FamousQuoteOrTrivia { get; init; } = string.Empty;
        public string OfficialUrl { get; init; } = string.Empty;
    }

    public static class BuildDossierDatabase
    {
        private static readonly Dictionary<int, BuildDossier> CuratedDossiers = new()
        {
            // -------------------------------------------------------------
            // VANILLA BETA ERA
            // -------------------------------------------------------------
            [7162] = new()
            {
                Version = "Beta.7162",
                BuildNumber = 7162,
                Headline = "The Genesis: Friends & Family Beta Launch",
                EraContext = "In August 2011, Blizzard opened the legendary Diablo III Friends & Family Beta. At this early stage, skills were modified by physical Rune items (Crimson, Indigo, Obsidian, Golden, Alabaster) dropped in the world rather than unlocked on level up. Players used the Cauldron of Jordan to sell items from anywhere and the Nephalem Cube to salvage gear in the field.",
                MilestoneBadges = new() { "🌱 First Public D3 Client", "💎 Physical Skill Runes", "🏺 Cauldron of Jordan", "👑 Skeleton King Cap" },
                KeyHighlights = new()
                {
                    "Level cap set to 13, ending at the defeat of the Skeleton King in Leoric's Tomb.",
                    "Contained the original Cauldron of Jordan and Nephalem Cube inventory utilities (later removed).",
                    "Early character UI with 5 skill slots instead of the eventual 6 elective slots.",
                    "Classic 2011 Friends & Family login screen and sound effects."
                },
                FamousQuoteOrTrivia = "“We want Diablo III to feel like the ultimate evolution of dark action RPGs, where every skill can be mutated with runes dropped from demons.” — Jay Wilson, 2011",
                OfficialUrl = "https://diablo.fandom.com/wiki/Diablo_III_Beta"
            },
            [9359] = new()
            {
                Version = "Beta.9359",
                BuildNumber = 9359,
                Headline = "Open Beta Weekend: The Stress Test of Sanctuary",
                EraContext = "Over the weekend of April 20–23, 2012, Blizzard opened Diablo III to the entire world for an unprecedented server stress test. Millions flooded the servers, giving gamers their very first taste of BattleTags, elective mode casting, and the redesigned Skill Rune interface that would launch weeks later.",
                MilestoneBadges = new() { "🌐 World Open Beta", "🏷️ BattleTags Debut", "🎯 Elective Mode Added", "🔥 2M+ Players Tested" },
                KeyHighlights = new()
                {
                    "Physical rune items converted into the modern level-up rune unlock interface.",
                    "Elective Mode added under Gameplay options allowing players to map any skill to any key.",
                    "Full introduction of BattleTags for social friending and party matchmaking.",
                    "Final stress test client before retail gold master pressing."
                },
                FamousQuoteOrTrivia = "“This weekend stress test is designed to break our hardware so May 15 doesn't break your heart.” — Bashiok, April 2012",
                OfficialUrl = "https://diablo.fandom.com/wiki/Diablo_III_Beta"
            },

            // -------------------------------------------------------------
            // VANILLA CLASSIC RETAIL ERA
            // -------------------------------------------------------------
            [8370] = new()
            {
                Version = "1.0.0.8370",
                BuildNumber = 8370,
                Headline = "May 15, 2012: The Midnight Release & Gold Master",
                EraContext = "The unpatched gold master printed on the retail collector's and standard edition DVD-ROM discs. This build represents Diablo III in its purest, rawest form on launch night—before day-one hotfixes and balance changes.",
                MilestoneBadges = new() { "💿 Retail Gold DVD", "⚔️ Day Zero Client", "⏳ Original Inferno Rules" },
                KeyHighlights = new()
                {
                    "The original factory build pressed to physical discs worldwide.",
                    "Original damage formulas and item level 63 drop distributions.",
                    "Requires Day One update 9558 to connect to Battle.net."
                },
                FamousQuoteOrTrivia = "“Sanctuary awaits after a 12-year wait.”",
                OfficialUrl = "https://us.diablo3.blizzard.com/"
            },
            [9558] = new()
            {
                Version = "1.0.1.9558",
                BuildNumber = 9558,
                Headline = "Launch Day: Error 37 & The Doubled Inferno",
                EraContext = "The legendary Day One patch released on May 15, 2012. Diablo III sold 3.5 million copies in its first 24 hours. Players who fought through Error 37 discovered an Inferno difficulty tuned to legendary brutality, where basic wasp projectiles in Act II Caldeum would one-shot fully geared Barbarians.",
                MilestoneBadges = new() { "🚀 Official Global Launch", "💀 “And Then We Doubled It”", "⚖️ Gold Auction House Live", "🩸 Enrage Timers" },
                KeyHighlights = new()
                {
                    "Global launch client enabling all 5 classes (Barbarian, Demon Hunter, Monk, Witch Doctor, Wizard) to level 60.",
                    "Inferno difficulty featuring Berserk/Enrage timers on champion and elite packs.",
                    "Gold Auction House enabled for peer-to-peer trading.",
                    "Original Smoke Screen and Force Armor mechanics before immediate hotfixing."
                },
                FamousQuoteOrTrivia = "“We had our internal strike teams beat Inferno, and then we doubled it.” — Jay Wilson",
                OfficialUrl = "https://www.diabloii.net/blog/comments/diablo-3-patch-1-0-1-notes"
            },
            [10057] = new()
            {
                Version = "1.0.3.10057",
                BuildNumber = 10057,
                Headline = "The Attack Speed Nerf & Inferno Retuning",
                EraContext = "Patch 1.0.3 was the most controversial balance patch of 2012. Players had spent hundreds of millions of gold stacking Attack Speed items. Overnight, Blizzard halved the Attack Speed values on all existing items by ~50%. In return, Inferno Act II, III, and IV monster damage was reduced and repair costs were increased significantly.",
                MilestoneBadges = new() { "⚡ 50% Attack Speed Nerf", "🛡️ Inferno Damage Reduced", "💸 4x Repair Costs", "📦 Act I Drops iLvl 63" },
                KeyHighlights = new()
                {
                    "Increased Attack Speed (IAS) on weapons and armor halved across the board.",
                    "Inferno Act II, III, and IV incoming monster damage reduced by 20% to 40%.",
                    "Item Level 63 rares allowed to drop in Act I (2% chance) and Act II (4% chance).",
                    "Repair costs increased dramatically (up to 400%) to discourage graveyard corpse-running."
                },
                FamousQuoteOrTrivia = "“Increased Attack Speed was simply too dominant. It bypassed too many weapon choice tradeoffs.” — Wyatt Cheng",
                OfficialUrl = "https://www.diablofans.com/news/1283-patch-1-0-3-patch-notes"
            },
            [11327] = new()
            {
                Version = "1.0.4.11327",
                BuildNumber = 11327,
                Headline = "The Turning Point: Paragon 100 & The Legendary Overhaul",
                EraContext = "Patch 1.0.4 saved Diablo III. Prior to this patch, Legendary items were underpowered vendor trash compared to generic blue/yellow rares. Blizzard completely reworked every Legendary weapon and armor with unique visual effects, guaranteed stats, and the iconic golden beam of light with mini-map ping. It also introduced Paragon Levels 1–100, giving players a permanent end-game progression system.",
                MilestoneBadges = new() { "⭐ Historic Major Update", "🏆 Paragon Levels (1-100)", "🌟 Legendary Beam & Ping", "🛡️ Invulnerable Minions Removed" },
                KeyHighlights = new()
                {
                    "Paragon System debuted: 100 bonus levels, each granting +3% Magic Find, +3% Gold Find, and core primary stats.",
                    "Legendary items completely redesigned with orange pillar of light beams, mini-map pings, and build-defining affixes.",
                    "Invulnerable Minions elite affix permanently removed from the game.",
                    "Enrage timers and elite pack full heals upon player death removed from normal Inferno play."
                },
                FamousQuoteOrTrivia = "“We heard loud and clear: you want Legendaries to actually feel legendary.” — Andrew Chambers",
                OfficialUrl = "https://www.diablofans.com/news/1344-patch-1-0-4-official-patch-notes"
            },
            [12480] = new()
            {
                Version = "1.0.5.12480",
                BuildNumber = 12480,
                Headline = "Monster Power & The Infernal Machine",
                EraContext = "Patch 1.0.5 addressed the request for customizable difficulty by introducing Monster Power (MP0 through MP10), giving players full control over enemy HP, damage, and bonus item drops. It also introduced the Infernal Machine event, sending players on Keywarden hunts to forge the legendary Hellfire Ring.",
                MilestoneBadges = new() { "🔥 Monster Power (MP1-10)", "🗝️ Keywardens & Portals", "💍 Hellfire Ring Quest", "✨ Defensive Skills Rework" },
                KeyHighlights = new()
                {
                    "Monster Power (MP0-10) system allowed players to scale difficulty for massive XP and item drop multipliers.",
                    "Infernal Machine event added: defeat Keywardens across Acts I–III to craft portals and battle Uber Boss pairs.",
                    "Hellfire Ring crafted via organs collected from Realm of Discord, Chaos, and Turmoil.",
                    "Defensive passive skills (War Cry: Impunity, One With Everything) normalized to increase build diversity."
                },
                FamousQuoteOrTrivia = "“Monster Power brings back the classic /players 8 feel from Diablo II.”",
                OfficialUrl = "https://www.diablofans.com/news/1399-patch-1-0-5-official-patch-notes"
            },
            [14633] = new()
            {
                Version = "1.0.7.14633",
                BuildNumber = 14633,
                Headline = "Brawling in the Scorched Chapel & Marquise Gems",
                EraContext = "Patch 1.0.7 finally added player-versus-player dueling via Nek the Brawler in New Tristram, transporting warriors to the Scorched Chapel arena. To combat hyperinflation on the Auction House, Blizzard added account-bound crafting recipes (Archon Armor and Amulets) and Marquise Gems that required Demonic Essences farmed in the world.",
                MilestoneBadges = new() { "⚔️ PvP Duelling / Brawling", "💎 Marquise Gem Tier", "📜 Account-Bound Crafting", "👹 Demonic Essences" },
                KeyHighlights = new()
                {
                    "PvP Brawling introduced in the Scorched Chapel for free-for-all party duels.",
                    "Marquise gem tier added (combining 3 Radiant Stars and 20 million gold).",
                    "Archon Armor and Amulet recipes added with high primary stat rolls (BoA to combat botting).",
                    "Death penalty timers removed in softcore multiplayer games."
                },
                FamousQuoteOrTrivia = "“Take it to the chapel if you think your build is better.”",
                OfficialUrl = "https://www.diablofans.com/news/1523-patch-1-0-7-official-patch-notes"
            },
            [16603] = new()
            {
                Version = "1.0.8a.16603",
                BuildNumber = 16603,
                Headline = "The Pinnacle of Vanilla: Mob Density & Co-op Farming",
                EraContext = "Patch 1.0.8 represents the definitive, absolute peak of Diablo III Vanilla Classic. Prior to 1.0.8, Act III (Alkaizer runs) was the only efficient farming path. Blizzard drastically increased monster density across Acts I, II, and IV, turning the Fields of Misery, Weeping Hollow, and Dahlgur Oasis into swarming battlegrounds. It also added the beloved 'Identify All' button and co-op XP buffs.",
                MilestoneBadges = new() { "👑 Final Vanilla Build", "🧟 Massive Mob Density", "🔍 Identify All Added", "🤝 +30% Co-op XP Buff" },
                KeyHighlights = new()
                {
                    "Monster density in Acts I, II, and IV scaled up to match Act III on Monster Power 1+.",
                    "Added Book of Cain 'Identify All' book in town, ending manual item-by-item identification.",
                    "Co-op multiplayer XP and Magic Find bonus buff (+10% per additional player in game, up to +30%).",
                    "Matchmaking party tags added (Questing, Brawling, Monster Power Farming, Keywarden)."
                },
                FamousQuoteOrTrivia = "“Fields of Misery on MP10 in 1.0.8 is the purest, most exhilarating density farming in Vanilla history.”",
                OfficialUrl = "https://www.diablofans.com/news/1739-patch-1-0-8-official-patch-notes"
            },

            // -------------------------------------------------------------
            // REAPER OF SOULS ERA
            // -------------------------------------------------------------
            [18962] = new()
            {
                Version = "Beta.18962",
                BuildNumber = 18962,
                Headline = "The Reaper Arrives: Friends & Family RoS Beta",
                EraContext = "In November 2013, Blizzard unveiled Reaper of Souls to testers. This build marked the first appearance of Act V Westmarch, the Crusader class, Adventure Mode, and the complete dismantling of the Auction House economy in favor of Smart Drops.",
                MilestoneBadges = new() { "💀 First RoS Client", "🛡️ Crusader Debuts", "🏰 Westmarch Act V", "🗺️ Adventure Mode Alpha" },
                KeyHighlights = new()
                {
                    "Crusader class playable from levels 1 to 70 with heavy flails and shields.",
                    "Act V Westmarch, Blood Marsh, and Pandemonium Fortress revealed.",
                    "Early Adventure Mode test featuring basic bounties and Nephalem Rifts.",
                    "Introduction of The Mystic (Myriam) for item enchanting and transmogrification."
                },
                FamousQuoteOrTrivia = "“No one can stop Death.” — Malthael",
                OfficialUrl = "https://diablo.fandom.com/wiki/Diablo_III:_Reaper_of_Souls"
            },
            [22044] = new()
            {
                Version = "2.0.1.22044",
                BuildNumber = 22044,
                Headline = "The Pre-Expansion Patch: Loot 2.0 & Uncapped Paragon",
                EraContext = "Released on February 25, 2014, Patch 2.0.1 fundamentally transformed Diablo III into the modern ARPG we know today. It replaced the four old difficulty tiers with Normal through Torment VI, made Paragon levels account-wide and uncapped with customizable stat points, launched Loot 2.0 (Smart Drops), and paved the way for the closure of the Auction House.",
                MilestoneBadges = new() { "🎁 Loot 2.0 (Smart Drops)", "♾️ Account-Wide Paragon", "🔥 Torment I–VI Difficulties", "🚫 Auction House Closed" },
                KeyHighlights = new()
                {
                    "Loot 2.0: Items drop smart-tailored to the class you are currently playing.",
                    "Account-Wide Paragon: All paragon levels merged across characters; stat points can be freely allocated.",
                    "Difficulty system rebuilt into Dynamic Scaling (Normal, Hard, Expert, Master, Torment I–VI).",
                    "Cursed Chest and Cursed Shrine world events introduced."
                },
                FamousQuoteOrTrivia = "“Diablo III is best when you are slaying monsters to get loot, not clicking auction house refreshes.” — Josh Mosqueira",
                OfficialUrl = "https://news.blizzard.com/en-us/article/13010156/patch-2-0-1-now-live"
            },
            [26451] = new()
            {
                Version = "2.1.0.26451",
                BuildNumber = 26451,
                Headline = "Greater Rifts, Legendary Gems & Season 1",
                EraContext = "Patch 2.1.0 is considered the foundation of modern Diablo III endgame. It introduced Greater Rifts with tiered leaderboards, socketable Legendary Gems upgraded by Urshi, Season 1 ladders, the Cesspools tileset, and the mythical Vault realm discovered by slaying Treasure Goblins.",
                MilestoneBadges = new() { "⏱️ Greater Rifts & Timer", "💎 Legendary Gems (Urshi)", "🏆 Season 1 Begins", "💰 Greed & The Vault" },
                KeyHighlights = new()
                {
                    "Greater Rifts introduced with 15-minute time limits and infinite difficulty scaling.",
                    "Legendary Gems added (Bane of the Trapped, Taeguk, Gem of Efficacious Toxin) upgraded after rift completion.",
                    "Season 1 officially launched with exclusive seasonal legendaries and leaderboards.",
                    "Treasure Goblins given a chance to leave behind golden portals leading to The Vault and Baroness Greed."
                },
                FamousQuoteOrTrivia = "“Greater Rifts give every build an infinite ceiling to test their absolute limits.” — Travis Day",
                OfficialUrl = "https://news.blizzard.com/en-us/article/15487814/patch-2-1-0-now-live"
            },
            [28709] = new()
            {
                Version = "2.1.2.28709",
                BuildNumber = 28709,
                Headline = "Ancient Legendaries & Conduit Pylon Balance",
                EraContext = "Patch 2.1.2 introduced Ancient Legendary items—an ultra-rare tier of items rolling with up to 30% higher base stats and damage. It also fundamentally reworked Conduit Pylons so Greater Rift leaderboards were decided by player skill and gear rather than fish-pylon luck, alongside revamping Marauder Demon Hunters.",
                MilestoneBadges = new() { "✨ Ancient Items (+30% Stats)", "⚡ Conduit Pylon Nerfed", "🏹 Marauder DH Rework", "🎒 Kadala Blood Shard Fix" },
                KeyHighlights = new()
                {
                    "Ancient Legendaries added (10% drop chance for any Legendary) rolling with significantly higher stat ceilings.",
                    "Conduit Pylon in Greater Rifts rebalanced from flat static damage to scale with weapon damage.",
                    "Embodiment of the Marauder set redesigned: Sentries now fire hatred spenders only when the Demon Hunter fires.",
                    "Density in Greater Rifts tuned for more consistent mob distributions across maps."
                },
                FamousQuoteOrTrivia = "“Ancient items provide that rush of finding an upgrade even when your character is fully geared in standard legendaries.”",
                OfficialUrl = "https://news.blizzard.com/en-us/article/17541521/patch-2-1-2-now-live"
            },
            [91007] = new()
            {
                Version = "2.7.6.91007",
                BuildNumber = 91007,
                Headline = "Solo Self-Found Mode & Paragon Cap Experiments",
                EraContext = "Released in September 2023 for Season 29 (Visions of Enmity), Patch 2.7.6 delivered the long-requested dedicated Solo Self-Found mode with standalone leaderboards, preventing group XP sharing. It also introduced Diabolical Fissures appearing in the world and experimental Paragon caps.",
                MilestoneBadges = new() { "👤 Solo Self-Found (SSF)", "🌀 Diabolical Fissures", "⚔️ Season 29: Visions", "🛡️ Area Damage Polish" },
                KeyHighlights = new()
                {
                    "Dedicated Solo Self-Found (SSF) game mode added with separate seasonal leaderboards.",
                    "Diabolical Fissure portals spawn from slain enemies, creating gauntlets of mini-rifts.",
                    "Experimental 800 Paragon point cap tested for the season.",
                    "Engine optimization: area damage calculation batching to reduce high-GR server lag."
                },
                FamousQuoteOrTrivia = "“Play entirely on your own terms. True solo leaderboards are finally here.”",
                OfficialUrl = "https://news.blizzard.com/en-us/article/23999068/season-29-visions-of-enmity-now-live"
            },
            [99920] = new()
            {
                Version = "2.8.0.99920",
                BuildNumber = 99920,
                Headline = "The Final Eternal Masterpiece: Altar of Rites & Automated Seasons",
                EraContext = "The definitive modern retail client of Diablo III. With Patch 2.8.0 / Season 30+, Blizzard permanently integrated the fan-favorite Altar of Rites progression tree and Soul Shards into the core game, enabled automated pet salvage for auto-pickup of crafting materials, and finalized Diablo III as an immortal, endlessly replayable ARPG classic.",
                MilestoneBadges = new() { "🏛️ Permanent Altar of Rites", "💎 Soul Shards / Ethereals", "🤖 Automated Pet Salvage", "🌟 Global Live Retail Client" },
                KeyHighlights = new()
                {
                    "The Rites of Sanctuary (Altar of Rites) permanently integrated into all future seasons.",
                    "Pets automatically pick up and salvage Normal, Magic, and Rare items in real time.",
                    "Automated seasonal themes rotative cadence established for eternal live operations.",
                    "Full 64-bit CASC high-performance client with DirectX 11 engine support."
                },
                FamousQuoteOrTrivia = "“Sanctuary will always be here for you. Diablo III Forever.”",
                OfficialUrl = "https://news.blizzard.com/en-us/article/24046220/season-30-the-lords-of-hell-now-live"
            }
        };

        public static BuildDossier GetDossier(D3BuildCatalogItem build)
        {
            if (CuratedDossiers.TryGetValue(build.BuildNumber, out var curated))
            {
                return curated;
            }

            // Generate clean automated dossier for other builds in the catalog
            string edition = build.IsRoS ? "Reaper of Souls" : "Vanilla Classic";
            string catName = build.Category switch
            {
                D3BuildCategory.Retail => "Official Retail Release",
                D3BuildCategory.Ptr => "Public Test Realm (PTR) Iteration",
                D3BuildCategory.Beta => "Pre-Release Beta Milestone",
                _ => "Historical Build"
            };

            var badges = new List<string>
            {
                build.IsRoS ? "⚔️ Reaper of Souls" : "🔥 Vanilla Classic",
                catName,
                build.IsCasc ? "⚡ 64-bit CASC" : "📦 32-bit MPQ",
                $"📅 {build.ReleaseDate}"
            };

            return new BuildDossier
            {
                Version = build.Version,
                BuildNumber = build.BuildNumber,
                Headline = $"{build.DisplayTitle} — {catName}",
                EraContext = $"Diablo III {build.Version} (Build {build.BuildNumber}) was released on {build.ReleaseDate} as part of the {edition} lifecycle. It preserves the exact game balance, executable binaries, and data structures deployed on official Blizzard servers during this era.",
                MilestoneBadges = badges,
                KeyHighlights = new()
                {
                    build.Summary,
                    $"Distribution format: {(build.IsCasc ? "Modern NGDP/CASC 64-bit content repository" : "Legacy 32-bit MPQ archive package with delta PTCH diffs")}.",
                    $"Base client build: {build.BaseBuildNumber} ({build.BaseDirectUrl}).",
                    $"Official Blizzard manifest: {(string.IsNullOrEmpty(build.MfilFileName) ? "CASC Build Config" : build.MfilFileName)}."
                },
                FamousQuoteOrTrivia = "Preserved directly from official Blizzard Entertainment Akamai and EdgeSuite CDN distribution nodes.",
                OfficialUrl = "https://diablo.fandom.com/wiki/Diablo_III"
            };
        }
    }
}

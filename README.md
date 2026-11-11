# Diablo 3 Forever

**Diablo 3 Forever** (v1.2.0) is a standalone, zero-dependency open-source archival downloader and multi-version installer that streams and reconstructs **91 official Diablo III builds** (spanning retail releases, Public Test Realm PTR iterations, and closed/open beta milestone clients from 2011 to 2025) **100% directly from Blizzard's official CDN servers**.

> ### **[Download `Diablo3Forever.exe` v1.2.0 (Standalone Windows x64 - Zero Dependencies)](https://github.com/EliteLevelSoftware/D3Forever/releases/download/v1.2.0/Diablo3Forever.exe)**
> - **Version:** `v1.2.0`
> - **SHA-256:** `750578b4a2eab1481419854634deb7b6a455f379c97a910ef894a348ba15f959` ([`release/SHA256SUMS.txt`](release/SHA256SUMS.txt))
> - **Build Catalog & Provenance Tracker:** [`docs/Diablo3_Versions_Tracker_Enriched.xlsx`](docs/Diablo3_Versions_Tracker_Enriched.xlsx)
> - Or browse **[GitHub Releases](https://github.com/EliteLevelSoftware/D3Forever/releases)**

![Diablo 3 Forever Screenshot](docs/diablo3forever-preview.png)

---

## Key Features

- **100% Direct from Official Blizzard CDNs:** Every byte is fetched directly from Blizzard's official distribution servers (`us.patch.battle.net`, `us.cdn.blizzard.com`, and `dist.blizzard.com.edgesuite.net`). Zero third-party mirrors, re-uploads, or modified game binaries are used.
- **Bulk Archival & Cold Storage Backup Engine:** Backup and preserve the entire 91-build catalog to an external hard drive, USB storage, or network share (NAS) via a single click or CLI command:
  - **⚡ Fast Binaries Archive (~3.2 GB Total):** Downloads and patches standalone ready-to-run executables (`Diablo III.exe`), DLLs, and configs for all 91 builds organized into subfolders (`\Vanilla\Retail\1.0.8a.16603`, `\RoS\PTR\2.1.2.28709`, etc.). Drops right into any existing Diablo III folder to immediately launch that version.
  - **📦 Raw Blizzard CDN Mirror (~48 GB Total):** Mirrors all 6 unpatched Base MPQ archives (`8370.direct`, `20813.direct`, etc.), all 85 historical patch MPQs (`Updates/d3-0-*.MPQ`), and CASC index manifests. Complete 100% offline self-hosted cold preservation.
  - **Integrity & Metadata:** Automatically generates `SHA256SUMS.txt` and `VERSION_INFO.txt` per build. Resumable at any time.
- **Segmented Drawer Tabs:** Both the **Reaper of Souls** and **Vanilla Classic** expansion drawers feature category tab selectors:
  - `⭐ Retail`: Official production retail patches and milestone base clients.
  - `🧪 Public Test Realm`: Historical PTR iterations, preview patches, and test candidates.
  - `🔒 Closed Beta`: Milestone Friends & Family, Closed Beta, and Open Beta builds (including Vanilla `7162`–`9359` and RoS `18962`–`21962`).
- **Real-Time Per-File CDN Availability Tracking:** Every single version chip displays an animated breathing status dot that probes Blizzard's CDN in real-time (`OK`, `CHECKING`, `OFFLINE`).
- **Fast "Binaries Only" Mode (~35 MB Fast Patch):** Need only the exact historical `Diablo III.exe` and DLLs for a specific patch without downloading 15 GB of assets? Both Reaper of Souls and Vanilla drawers feature a **Binaries Only (~35 MB Fast Patch)** checkbox to extract, patch, and verify clean binaries in seconds.
- **GUI + Headless CLI Support:** Run `Diablo3Forever.exe` with no arguments for the interactive GUI, or pass CLI flags for 100% silent, unattended deployment and batch backups.

---

## Headless CLI & Bulk Backup Usage

```powershell
# Interactive GUI
.\Diablo3Forever.exe

# List all 91 official Blizzard CDN builds with release dates and provenance
.\Diablo3Forever.exe --list-versions

# Install a specific historical build directly from Blizzard CDN
.\Diablo3Forever.exe --build 16603 "C:\Games\Diablo III 1.0.8a"
.\Diablo3Forever.exe --version "2.1.2" "C:\Games\Diablo III 2.1.2" --binaries-only

# Bulk Backup all 91 builds to external storage or NAS (Fast Binaries Mode ~3.2 GB)
.\Diablo3Forever.exe --backup-all "E:\Diablo3_Historical_Archive" --backup-mode binaries --backup-scope all

# Complete Raw Blizzard CDN Offline Mirror (~48 GB)
.\Diablo3Forever.exe --backup-all "E:\Diablo3_Cold_Storage" --backup-mode raw --backup-scope all
```

---

## Supported Official CDN Builds (91 Versions)

### Reaper of Souls (41 Builds Across Retail, PTR & Beta)
- **Retail (20 Builds):** `2.8.0.99920` (Global Live), `2.8.1.101167` (CN CDN), `2.7.8.97927`, `2.7.7.93903`, `2.7.7.92326`, `2.7.6.91007`, `2.7.4.84161`, `2.7.3.82785`, `2.1.2.28709`, `2.1.1.27255`, `2.1.0.26451`, `2.0.6.24641`, `2.0.5a.24017`, `2.0.5.23920`, `2.0.4.23119`, `2.0.3.22427`, `2.0.2.22274`, `2.0.1.22044`, `2.0.0.20813` (Base MPQ).
- **Public Test Realm (9 Builds):** `2.7.8.96670` (CASC `d3t`), `2.1.2.28709`, `2.1.2.28341`, `2.1.0.26370`, `2.1.0.26281`, `2.1.0.26067`, `2.1.0.25658`, `2.1.0.25390`, `2.1.0.25014`.
- **Closed Beta (12 Builds):** `Beta.21962`, `Beta.21806`, `Beta.21390`, `Beta.20874`, `Beta.20493`, `Beta.20134`, `Beta.19992`, `Beta.19614`, `Beta.19533`, `Beta.19445`, `Beta.19177`, `Beta.18962` (Base RoS Beta).

### Vanilla Classic (50 Builds Across Retail, PTR & Beta)
- **Retail (18 Builds):** `1.0.8a.16603`, `1.0.8.16416`, `1.0.7a.15295`, `1.0.7.14633`, `1.0.6a.13644`, `1.0.6.13300`, `1.0.5a.12811`, `1.0.5.12480`, `1.0.4.11327`, `1.0.3b.10485`, `1.0.3a.10235`, `1.0.3.10057`, `1.0.2c.9991`, `1.0.2b.9950`, `1.0.2a.9858`, `1.0.2.9749`, `1.0.1.9558`, `1.0.0.8370` (Original Retail Base).
- **Public Test Realm (13 Builds):** `1.0.8.16256`, `1.0.8.16107`, `1.0.8.16033`, `1.0.8.15926`, `1.0.7.14528`, `1.0.7.14456`, `1.0.7.14295`, `1.0.7.14161`, `1.0.5.12389`, `1.0.5.12289`, `1.0.5.12166`, `1.0.5.12150`, `1.0.3.10485` (Base PTR).
- **Closed & Open Beta (19 Builds):** `Beta.9359` (Open Beta Weekend), `Beta.9327`, `Beta.9183`, `Beta.8896`, `Beta.8815`, `Beta.8610`, `Beta.8392`, `Beta.8350`, `Beta.8296`, `Beta.8101`, `Beta.8059`, `Beta.7931`, `Beta.7841`, `Beta.7728`, `Beta.7447`, `Beta.7338`, `Beta.7318`, `Beta.7200`, `Beta.7162` (Friends & Family Base).
| **`2.0.5a.24017`**| `24017` | MPQ 32-bit (`20813.direct`) | 2014-05-15 | Patch 2.0.5a Stability & Bugfix Hotfix |
| **`2.0.5.23920`** | `23920` | MPQ 32-bit (`20813.direct`) | 2014-05-13 | Patch 2.0.5 (Horadric Cache Torment Drop Rates, Gold Trade) |
| **`2.0.4.23119`** | `23119` | MPQ 32-bit (`20813.direct`) | 2014-04-08 | Patch 2.0.4 (Crusader Wrath Fixes, Class Tweaks) |
| **`2.0.3.22427`** | `22427` | MPQ 32-bit (`20813.direct`) | 2014-03-11 | Patch 2.0.3 Bugfix & Stability Update |
| **`2.0.2.22274`** | `22274` | MPQ 32-bit (`20813.direct`) | 2014-03-04 | Patch 2.0.2 Pre-Expansion Optimization Patch |
| **`2.0.1.22044`** | `22044` | MPQ 32-bit (`20813.direct`) | 2014-02-25 | Reaper of Souls Pre-Patch (Loot 2.0, Paragon 2.0) |
| **`2.0.0.20813`** | `20813` | MPQ 32-bit (`20813.direct`) | 2014-01-20 | Reaper of Souls Unpatched Retail Base MPQ Build |
| **`Beta.18962`**  | `18962` | MPQ 32-bit (`18962.direct`) | 2013-11-20 | Original RoS Friends & Family Closed Beta Base Client |

### Diablo III Vanilla Classic (19 Builds — 2011 Beta & 2012-2013 MPQ Retail)
| Version | Build | Release Date | Highlights |
| :--- | :--- | :--- | :--- |
| **`1.0.8a.16603`** | `16603` | `2013-05-10` | **Default Newest Vanilla** - Final Vanilla 1.0.8 Hotfix Build |
| **`1.0.8.16416`** | `16416` | `2013-05-07` | Monster Density & Co-op XP Bonus Patch |
| **`1.0.7a.15295`** | `15295` | `2013-02-15` | Brawling & Demon Hunter Hotfix |
| **`1.0.7.14633`** | `14633` | `2013-02-12` | PvP Brawling, Marquise Gems & Account-Bound Crafting |
| **`1.0.6a.13644`** | `13644` | `2012-12-06` | Quest & Stability Patch |
| **`1.0.6.13300`** | `13300` | `2012-11-27` | Network & Bugfix Patch |
| **`1.0.5a.12811`** | `12811` | `2012-10-25` | Crash & Achievement Fix Patch |
| **`1.0.5.12480`** | `12480` | `2012-10-16` | Monster Power (MP1-10) & Infernal Machine |
| **`1.0.4.11327`** | `11327` | `2012-08-21` | Paragon 1.0 (Levels 1-100) & Legendary Overhaul |
| **`1.0.3b.10485`** | `10485` | `2012-07-10` | Auction House & Stability Patch |
| **`1.0.3a.10235`** | `10235` | `2012-06-26` | Zombie Bears & Wear-and-Tear Hotfix |
| **`1.0.3.10057`** | `10057` | `2012-06-19` | Inferno Tuning, IAS Nerf & Repair Cost Patch |
| **`1.0.2c.9991`** | `9991` | `2012-06-12` | Mac/PC Client Stability Patch |
| **`1.0.2b.9950`** | `9950` | `2012-06-07` | Login & Auction House Responsiveness Patch |
| **`1.0.2a.9858`** | `9858` | `2012-06-01` | Item & Skill Tooltip Fix Patch |
| **`1.0.2.9749`** | `9749` | `2012-05-29` | First Post-Launch Client Patch |
| **`1.0.1.9558`** | `9558` | `2012-05-15` | Original May 15, 2012 Launch Day Patch |
| **`1.0.0.8370`** | `8370` | `2012-05-14` | Original Unpatched Retail DVD Base MPQ Build |
| **`Beta.7162`**   | `7162` | `2011-09-20` | Original Vanilla Friends & Family Beta Client (`7162.direct`) |

---

## Command-Line (Headless) Usage

```powershell
# Launch the interactive GUI (default when no flags are passed)
Diablo3Forever.exe

# List all 34 official CDN builds and exit
Diablo3Forever.exe --list-versions

# Install the newest Live Reaper of Souls (2.8.0.99920) silently
Diablo3Forever.exe "C:\Games\Diablo III" --build 2.8.0 --locale enUS

# Install full playable Vanilla 1.0.8a (Build 16603)
Diablo3Forever.exe "C:\Games\Diablo III Vanilla" --build 16603 --locale enUS

# Fast-build ONLY the patched Vanilla binaries (~35 MB download) for 1.0.3 (Build 10057)
Diablo3Forever.exe "C:\Games\D3-1.0.3-Binaries" --build 10057 --binaries-only

# Fast-build ONLY the 2014 Reaper of Souls 2.0.1 binaries (~35 MB download)
Diablo3Forever.exe "C:\Games\D3-2.0.1-Binaries" --build 22044 --binaries-only
```

---

## Building from Source

Requires the [.NET 8.0+ / 10.0 SDK](https://dotnet.microsoft.com/download) on Windows:

```powershell
dotnet publish src/Diablo3Forever.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o release
```

---

## Legal & Non-Affiliation Disclaimer

- **Not Affiliated with Blizzard Entertainment:** This project is an independent, open-source community archival and deployment tool and is **not** affiliated with, endorsed by, or sponsored by Blizzard Entertainment, Inc. or Microsoft Corporation.
- **Trademarks:** *Diablo*, *Diablo III*, *Reaper of Souls*, and *Battle.net* are trademarks or registered trademarks of Blizzard Entertainment, Inc. in the U.S. and/or other countries.
- **Original Custom Artwork:** All UI banners, icons, and emblems bundled in [`src/Assets/`](src/Assets) (`ros_banner.jpg`, `vanilla_banner.jpg`, `d3forever_logo.png`, `app_icon.ico`) are **100% original, custom-generated dark-fantasy artwork** created specifically for this project and contain no copyrighted Blizzard promotional key art or official game logos.
- **No Copyrighted Game Code Redistributed & No DRM Bypass:** This repository contains **zero** Blizzard game binaries, MPQ archives, or proprietary code. All game files are downloaded directly by the end user from Blizzard's official public CDN servers. This tool does **not** bypass Battle.net authentication, modify game logic, or circumvent license checks - a valid Battle.net account and Diablo III license are required to play on official servers.

# Diablo 3 Forever

**Diablo 3 Forever** is a standalone, zero-dependency open-source archival downloader and multi-version installer that streams and reconstructs **21 official Diablo III builds** (from the May 15, 2012 retail launch `1.0.0.8370` through `1.0.8a.16603` and modern 64-bit `Reaper of Souls` `2.8.x`) **100% directly from Blizzard's official CDN servers**.

> ### **[⬇ Download `Diablo3Forever.exe` (Standalone Windows x64 — Zero Dependencies)](https://github.com/EliteLevelSoftware/D3Forever/raw/main/release/Diablo3Forever.exe)**
> - **SHA-256:** `dc1ad0389da9a33dfc0599bdd5606dab549ae557c8ca0c12c2cc8feb63e1e7c7` ([`release/SHA256SUMS.txt`](release/SHA256SUMS.txt))
> - Or browse **[GitHub Releases](https://github.com/EliteLevelSoftware/D3Forever/releases)**

![Diablo 3 Forever Screenshot](docs/screenshot.png)

---

## Key Features

- **100% Direct from Official Blizzard CDNs:** Every byte is fetched directly from Blizzard's official distribution servers (`us.patch.battle.net`, `us.cdn.blizzard.com`, and `dist.blizzard.com.edgesuite.net`). Zero third-party game mirrors or redistributed game binaries are used.
- **Dual-Pipeline Architecture (Modern NGDP/CASC + Legacy 2012–2013 MPQ/PTCH):**
  - **Reaper of Souls (`2.7.8` / `2.8.0` / `2.8.1`):** Queries Blizzard's NGDP/TACT endpoints, downloads `BuildConfig`, `CDNConfig`, `encoding`, `root`, and `install` manifests, decodes BLTE (`N`, `Z`, `E`) streams, and builds a complete local CASC `Data/` container (`data.000`+, `.idx` index files, `shmem`, and `.build.info`).
  - **Vanilla Classic (`1.0.0.8370` – `1.0.8a.16603`):** Parses the official `8370.direct` `.mfil` manifest, streams the cumulative base/locale MPQ archives (`Data_D3/PC/MPQs/`), extracts base binaries from `base-Win.mpq` (supporting both Zlib `0x02` and LZMA `0x12` MPQ sector compression), and applies Blizzard's `PTCH` (`BSD0`/`BSDIFF40` and `COPY`) binary diffs from `Updates/d3-0-{Build}-Win-final.MPQ` with full cryptographic MD5 verification.
- **Fast "Binaries Only" Mode for Vanilla (`~35 MB`):** Need only the exact historical `Diablo III.exe` and DLLs for a specific Vanilla patch (`1.0.0`–`1.0.8a`)? Check **Binaries Only (~35 MB Fast Patch)** inside the Vanilla `View More` drawer to extract and binary-patch the executables in seconds without downloading the 15 GB MPQ asset archives.
- **GUI + Headless CLI Support:** Run `Diablo3Forever.exe` with no arguments for the interactive GUI, or pass CLI flags for 100% silent, unattended deployment.

---

## Supported Official CDN Builds (21 Versions)

### Reaper of Souls (NGDP / TACT CASC — 64-Bit)
| Version | Build | Product Code | Description |
| :--- | :--- | :--- | :--- |
| **`2.8.0.99920`** | `99920` | `d3` | **Default Newest** — Global Live Reaper of Souls Client |
| **`2.7.8.96670`** | `96670` | `d3t` | Patch 2.7.8 Public Test Realm (PTR) Client |
| **`2.8.1.101167`** | `101167` | `d3cn` | Patch 2.8.1 Reaper of Souls Client |

### Diablo III Vanilla (2012–2013 `.mfil` + MPQ + `PTCH`/`BSD0` — 32-Bit)
| Version | Build | Release Date | Highlights |
| :--- | :--- | :--- | :--- |
| **`1.0.8a.16603`** | `16603` | `2013-05-10` | **Default Newest Vanilla** — Final Vanilla 1.0.8 Hotfix Build |
| **`1.0.8.16416`** | `16416` | `2013-05-07` | Monster Density & Co-op XP Bonus Patch |
| **`1.0.7a.15295`** | `15295` | `2013-02-15` | Brawling & Demon Hunter Hotfix |
| **`1.0.7.14633`** | `14633` | `2013-02-12` | PvP Brawling, Marquise Gems & Account-Bound Crafting |
| **`1.0.6a.13644`** | `13644` | `2012-12-06` | Quest & Stability Patch |
| **`1.0.6.13300`** | `13300` | `2012-11-27` | Network & Bugfix Patch |
| **`1.0.5a.12811`** | `12811` | `2012-10-25` | Crash & Achievement Fix Patch |
| **`1.0.5.12480`** | `12480` | `2012-10-16` | Monster Power (`MP1`–`MP10`) & Infernal Machine |
| **`1.0.4.11327`** | `11327` | `2012-08-21` | Paragon 1.0 (Levels 1–100) & Legendary Overhaul |
| **`1.0.3b.10485`** | `10485` | `2012-07-10` | Auction House & Stability Patch |
| **`1.0.3a.10235`** | `10235` | `2012-06-26` | Zombie Bears & General Bugfix Patch |
| **`1.0.3.10057`** | `10057` | `2012-06-19` | Original Inferno Tuning, IAS Nerf & Repair Cost Patch |
| **`1.0.2c.9991`** | `9991` | `2012-06-12` | Client Stability Patch |
| **`1.0.2b.9950`** | `9950` | `2012-06-07` | Login & Auction House Responsiveness Patch |
| **`1.0.2a.9858`** | `9858` | `2012-06-01` | Item & Skill Tooltip Fix Patch |
| **`1.0.2.9749`** | `9749` | `2012-05-29` | First Post-Launch Client Patch |
| **`1.0.1.9558`** | `9558` | `2012-05-15` | Original May 15, 2012 Launch Day Patch |
| **`1.0.0.8370`** | `8370` | `2012-05-14` | Unpatched Retail Collector's DVD Base Build |

---

## Command-Line Interface (Headless / RMM Mode)

Running `Diablo3Forever.exe` without arguments launches the GUI. Passing arguments runs headlessly in the console:

```powershell
# List all 21 available builds on Blizzard's CDN
Diablo3Forever.exe --list-builds

# Install the newest Live Reaper of Souls (2.8.0.99920) silently
Diablo3Forever.exe "C:\Games\Diablo III" --build 2.8.0 --locale enUS --all-in-one

# Install full playable Vanilla 1.0.8a (Build 16603)
Diablo3Forever.exe "C:\Games\Diablo III Vanilla" --build 16603 --locale enUS

# Fast-build ONLY the patched Vanilla binaries (~35 MB download) for 1.0.3 (Build 10057)
Diablo3Forever.exe "C:\Games\D3-1.0.3-Binaries" --build 10057 --binaries-only
```

---

## Building from Source

Requires the [.NET 8.0+ / 10.0 SDK](https://dotnet.microsoft.com/download) on Windows:

```powershell
dotnet publish src/Diablo3Forever.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

---

## Legal & Non-Affiliation Disclaimer

- **Not Affiliated with Blizzard Entertainment:** This project is an independent, open-source community archival and deployment tool and is **not** affiliated with, endorsed by, or sponsored by Blizzard Entertainment, Inc. or Microsoft Corporation.
- **Trademarks:** *Diablo*, *Diablo III*, *Reaper of Souls*, and *Battle.net* are trademarks or registered trademarks of Blizzard Entertainment, Inc. in the U.S. and/or other countries.
- **Original Custom Artwork:** All UI banners, icons, and emblems bundled in [`src/Assets/`](src/Assets) (`ros_banner.jpg`, `vanilla_banner.jpg`, `d3forever_logo.png`, `app_icon.ico`) are **100% original, custom-generated dark-fantasy artwork** created specifically for this project and contain no copyrighted Blizzard promotional key art or official game logos.
- **No Copyrighted Game Code Redistributed & No DRM Bypass:** This repository contains **zero** Blizzard game binaries, MPQ archives, or proprietary code. All game files are downloaded directly by the end user from Blizzard's official public CDN servers. This tool does **not** bypass Battle.net authentication, modify game logic, or circumvent license checks—a valid Battle.net account and Diablo III license are required to play on official servers.

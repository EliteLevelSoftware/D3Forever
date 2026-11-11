# Diablo 3 Forever v1.4.0 — Patch Notes

**Release Date:** November 11, 2026  
**Build:** `v1.4.0`  
**Binary:** `Diablo3Forever.exe` (Windows x64 Single-File Standalone — Zero Dependencies)

---

## 🚀 Major New Features & Enhancements

### 1. Live Destination Inventory Tracker (Smart Anti-Redownload Protection)
- **Automatic Destination Inspection:** Whenever you open the Bulk Archival tool or choose a destination folder, Diablo 3 Forever scans the directory in real time.
- **Visual Inventory Dashboard:**
  - Displays exactly how many builds are **already present & verified** on disk vs. how many are **still needed**.
  - Displays the exact count of mirrored Blizzard CDN patch archives present vs. missing.
  - Reports current total disk space consumed by your archive repository.
- **100% Incremental Resuming:** Guarantees that completed builds and verified raw archives are **never re-downloaded**. You can safely pause, restart, or add new versions anytime without wasting bandwidth or time.

### 2. Dual-Mode Archival (Download Fast Binaries AND Raw CDN Mirror Concurrently)
- **No Longer Mutually Exclusive:** Instead of choosing between Fast Binaries OR Raw CDN Mirror, you can now select **one or BOTH at the same time** via dual checkboxes.
- **All-in-One Archival:** Back up the ready-to-run patched binaries (`~3.2 GB`) AND the original unpatched Blizzard CDN cold-storage archives in a single unified backup run.

### 3. Universal Multi-Language Pack & Locale Space Optimization
- **The Problem:** Localized voiceover and cinematic cutscenes take `~2.5 GB` per language (`~32.5 GB` for all 13 languages).
- **The Optimization:** All translated strings, item stats, menus, skill tooltips, quest dialogs, and subtitles for **ALL 13 LANGUAGES** combine to **under 400 MB total**!
- **Universal Multi-Language Pack (Recommended):**
  - Downloads all 13 `{locale}_Text.mpq` files (`enUS`, `enGB`, `deDE`, `frFR`, `esES`, `esMX`, `ptBR`, `itIT`, `plPL`, `ruRU`, `koKR`, `zhTW`, `zhCN`).
  - Drops them directly into `Data_D3/PC/MPQs/`, allowing **ANY user to boot the game in ANY language worldwide** with full text translation while saving over `32 GB` of duplicate audio files!
- **Interactive Locale Launcher & Helper Scripts:** Every backed up build automatically includes:
  - `Launch-Game.bat`: An interactive command menu that prompts you to choose any of the 13 languages and launches the game immediately.
  - Dedicated shortcuts: `Launch-enUS.bat`, `Launch-deDE.bat`, `Launch-frFR.bat`, `Launch-koKR.bat`, `Launch-zhTW.bat`, etc.
  - `LOCALE_GUIDE.txt`: Quick reference for command-line `-locale <codecode>` arguments and `Launcher.db`.

### 4. Assemble Complete Playable Game Install From Offline Archive
- **Offline Client Builder:** Built-in secondary workspace in the Bulk Archival tool (**"🔨 ASSEMBLE COMPLETE GAME FROM ARCHIVE"**).
- **100% Offline Game Creation:** Select ANY of the 91 cataloged historical builds, choose an output folder (e.g. `C:\Games\Diablo III v1.0.8`), and the engine automatically extracts the base MPQ data, applies version diff patches, configures multi-locale language support, and builds a complete, fully playable Diablo III game client using your local/network backup archives.
- **Zero Internet Required:** Perfect for retro gaming, LAN parties, air-gapped systems, and preserving Diablo III forever.
- **Auto-Detection in Main Launcher:** When clicking "INSTALL / LAUNCH" on the main screen, Diablo 3 Forever automatically detects your offline archive and uses it as an instant local cache before falling back to Blizzard's CDN!

---

## ⚡ CLI Command Line Additions

```powershell
# Launch Graphical User Interface
.\Diablo3Forever.exe

# Bulk Backup with Dual-Mode (Fast Binaries + Raw CDN Mirror) & Universal Multi-Language Text
.\Diablo3Forever.exe --backup-all "S:\Diablo3_Archive" --backup-mode both --locale-mode all-text

# Full Cold Storage Backup with All Base Assets (~45 GB Standalone)
.\Diablo3Forever.exe --backup-all "S:\Diablo3_Archive" --backup-mode both --include-base-assets

# Assemble a complete game build from an offline archive with zero internet
.\Diablo3Forever.exe --build-from-archive "S:\Diablo3_Archive" --build 16603 --dest "C:\Games\Diablo III 1.0.8" --locale enUS
```

---

## 🛡️ File Integrity & Checksums

| File | SHA-256 Checksum |
| :--- | :--- |
| `Diablo3Forever.exe` | `7f792406d9156281b192b222ec4584b3bbcb8de016438e8ed2a88cc558b5edf3` |

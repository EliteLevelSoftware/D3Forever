# Diablo 3 Forever v1.3.0 — Patch Notes

**Release Date:** November 11, 2026  
**Build:** `v1.3.0`  
**Binary:** `Diablo3Forever.exe` (Windows x64 Single-File Standalone — Zero Dependencies)

---

## 🚀 Major New Features

### 1. In-App Update Checker & One-Click Self-Updater
- **Automatic Background Checks:** Diablo 3 Forever now queries GitHub Releases on startup to detect when a newer version is available.
- **Top Toolbar Notification:** When an update is detected, a glowing emerald green `[ UPDATE vX.X ]` button appears in the header.
- **In-App Updater Dialog:** View release highlights and patch notes directly inside the app, and click **"Update & Restart Now"** to automatically download, swap binaries, and restart seamlessly without ever opening a web browser.
- **CLI Support:** Run `.\Diablo3Forever.exe --check-update` in terminal for instant scriptable version inspection.

### 2. Bulk Archival & Cold Storage Backup Engine
- **Backup All Builds:** Easily preserve all historical Diablo III builds to an external hard drive, USB storage, or network share (NAS).
- **Two Archival Modes:**
  - ⚡ **Fast Binaries Archive (~3.2 GB Total):** Reconstructs and extracts standalone patched historical `Diablo III.exe`, DLLs, and configs for all 91 builds organized into subfolders (`\Vanilla Classic\Retail\1.0.8a.16603\`, `\Reaper of Souls\PTR\2.1.2.28709\`, etc.). Can be dropped into any existing Diablo III folder to immediately launch that version.
  - 📦 **Raw Blizzard CDN Mirror (~48 GB Total):** Mirrors all 6 unpatched Base MPQ archives (`8370.direct`, `20813.direct`, etc.), all 85 historical patch MPQs (`Updates/d3-0-*.MPQ`), and modern CASC index manifests for 100% offline cold preservation.
- **Target Scope Selection:** Choose between `All 91 Builds`, `Retail Only (38 Builds)`, `Reaper of Souls (41 Builds)`, or `Vanilla Classic (50 Builds)`.
- **Regional Locale Support:** Select your preferred regional language pack (`enUS`, `enGB`, `deDE`, `frFR`, `esES`, `zhTW`, `koKR`, `zhCN`, etc.) to generate localized configurations during backup.
- **Smart Resuming & Integrity:** Automatically skips verified files so backups can be paused and resumed anytime. Generates `VERSION_INFO.txt` per build and a root `SHA256SUMS.txt`.

---

## 🛠️ UI Fixes & Enhancements

- **Resolved Emoji Tofu Boxes (`▯`):** Replaced non-BMP emoji glyphs with custom GDI+ vector graphics:
  - Segmented drawer tabs now feature clean accent-colored glowing circle indicators (`● Blue` for Retail, `● Purple` for PTR, `● Amber` for Closed Beta) with crisp white/slate text.
  - The top toolbar backup button now features a crisp vector archive box icon.
- **Dynamic Drawer Version Counts:** Fixed hardcoded strings so "View More" dynamically calculates active builds:
  - Reaper of Souls: **"View More (Choose from 41 Versions)"**
  - Vanilla Classic: **"View More (Choose from 50 Versions)"**
- **Eliminated Double-Toggle Bug:** Fixed mouse event collision on the banner so clicking "View More" cleanly opens or closes the drawer without snapping shut.

---

## 📋 Catalog Summary (91 Official CDN Builds)

- **Reaper of Souls (41 Builds):**
  - **Retail (20 Builds):** `2.8.0.99920` (Global Live), `2.8.1.101167` (CN Production), `2.7.8.97927`, `2.7.7.93903`, `2.7.7.92326`, `2.7.6.91007`, `2.7.4.84161`, `2.7.3.82785`, `2.1.2.28709`, `2.1.1.27255`, `2.1.0.26451`, `2.0.6.24641`, `2.0.5a.24017`, `2.0.5.23920`, `2.0.4.23119`, `2.0.3.22427`, `2.0.2.22274`, `2.0.1.22044`, `2.0.0.20813` (Base MPQ).
  - **Public Test Realm (9 Builds):** `2.7.8.96670` (CASC `d3t`), `2.1.2.28709`, `2.1.2.28341`, `2.1.0.26370`, `2.1.0.26281`, `2.1.0.26067`, `2.1.0.25658`, `2.1.0.25390`, `2.1.0.25014`.
  - **Closed Beta (12 Builds):** `Beta.21962`, `Beta.21806`, `Beta.21390`, `Beta.20874`, `Beta.20493`, `Beta.20134`, `Beta.19992`, `Beta.19614`, `Beta.19533`, `Beta.19445`, `Beta.19177`, `Beta.18962` (Base RoS Beta).
- **Vanilla Classic (50 Builds):**
  - **Retail (18 Builds):** `1.0.8a.16603`, `1.0.8.16416`, `1.0.7a.15295`, `1.0.7.14633`, `1.0.6a.13644`, `1.0.6.13300`, `1.0.5a.12811`, `1.0.5.12480`, `1.0.4.11327`, `1.0.3b.10485`, `1.0.3a.10235`, `1.0.3.10057`, `1.0.2c.9991`, `1.0.2b.9950`, `1.0.2a.9858`, `1.0.2.9749`, `1.0.1.9558`, `1.0.0.8370` (Original Retail Base).
  - **Public Test Realm (13 Builds):** `1.0.8.16256`, `1.0.8.16107`, `1.0.8.16033`, `1.0.8.15926`, `1.0.7.14528`, `1.0.7.14456`, `1.0.7.14295`, `1.0.7.14161`, `1.0.5.12389`, `1.0.5.12289`, `1.0.5.12166`, `1.0.5.12150`, `1.0.3.10485` (Base PTR).
  - **Closed & Open Beta (19 Builds):** `Beta.9359` (Open Beta Weekend), `Beta.9327`, `Beta.9183`, `Beta.8896`, `Beta.8815`, `Beta.8610`, `Beta.8392`, `Beta.8350`, `Beta.8296`, `Beta.8101`, `Beta.8059`, `Beta.7931`, `Beta.7841`, `Beta.7728`, `Beta.7447`, `Beta.7338`, `Beta.7318`, `Beta.7200`, `Beta.7162` (Friends & Family Base).

---

## ⚡ Quick Start & CLI Usage

```powershell
# Interactive Graphical Installer
.\Diablo3Forever.exe

# Check for updates from command line
.\Diablo3Forever.exe --check-update

# Bulk Backup all 91 builds (Fast Binaries Mode ~3.2 GB)
.\Diablo3Forever.exe --backup-all "E:\Diablo3_Archive" --backup-mode binaries --backup-scope all

# Full Raw Blizzard CDN Offline Mirror (~48 GB)
.\Diablo3Forever.exe --backup-all "E:\Diablo3_Cold_Storage" --backup-mode raw --backup-scope all
```

---

## 🛡️ File Integrity & Checksums

| File | SHA-256 Checksum |
| :--- | :--- |
| `Diablo3Forever.exe` | `15f7975981a68739bb8ced7de7f320a780a09e08acc02daa6d36acfabaa126aa` |


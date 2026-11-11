using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace D3Downloader
{
    public sealed class BackupDialog : Form
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private int _activeTab = 0; // 0 = Backup/Archival, 1 = Assemble Game from Archive

        // Common Header & Tabs
        private readonly Panel _pnlHeader;
        private readonly Panel _pnlTabBackup;
        private readonly Panel _pnlTabAssemble;

        // Tab 1: Backup Controls
        private readonly TextBox _txtDest;
        private readonly Button _btnBrowse;
        private readonly CheckBox _chkFastBinaries;
        private readonly CheckBox _chkRawCdn;
        private readonly CheckBox _chkBaseAssets;
        private readonly ComboBox _cbScope;
        private readonly ComboBox _cbLocale;
        private readonly ComboBox _cbLocalePack;
        private readonly Label _lblInventoryTitle;
        private readonly Label _lblInventoryFast;
        private readonly Label _lblInventoryRaw;
        private readonly Label _lblInventoryStatus;
        private readonly Button _btnRescanInventory;

        // Tab 2: Assemble from Archive Controls
        private readonly TextBox _txtArchiveSrc;
        private readonly Button _btnBrowseArchiveSrc;
        private readonly ComboBox _cbAssembleBuild;
        private readonly TextBox _txtAssembleDest;
        private readonly Button _btnBrowseAssembleDest;
        private readonly ComboBox _cbAssembleLocale;
        private readonly CheckBox _chkAssembleUniversalText;
        private readonly CheckBox _chkAssembleShortcuts;
        private readonly CheckBox _chkAssembleFirewall;
        private readonly CheckBox _chkAssemblePrereqs;
        private readonly Button _btnAssembleStart;

        // Progress & Logging (Shared)
        private readonly ProgressBar _pbOverall;
        private readonly ProgressBar _pbCurrent;
        private readonly Label _lblOverall;
        private readonly Label _lblCurrent;
        private readonly RichTextBox _txtLog;
        private readonly Button _btnStart;
        private readonly Button _btnClose;

        private CancellationTokenSource _cts;
        private bool _isRunning;
        private ArchiveInventory _latestInventory;

        public BackupDialog(string defaultTargetDir)
        {
            Text = "Diablo 3 Forever — Bulk Backup & Archival Engine";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(780, 710);
            BackColor = Color.FromArgb(10, 13, 20);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            DoubleBuffered = true;

            string defDrive = "D:\\";
            if (!Directory.Exists(defDrive)) defDrive = "C:\\";
            string defDest = Path.Combine(defDrive, "Diablo3_Historical_Archive");

            // 1. Header Area
            _pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(780, 64),
                BackColor = Color.FromArgb(14, 18, 27)
            };
            _pnlHeader.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            _pnlHeader.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw vector archive box icon
                int bx = 22, by = 16;
                using var boxPen = new Pen(Color.FromArgb(245, 158, 11), 1.5f);
                e.Graphics.DrawRectangle(boxPen, bx, by + 4, 18, 14);
                e.Graphics.DrawLine(boxPen, bx - 2, by + 4, bx + 20, by + 4);
                e.Graphics.DrawLine(boxPen, bx + 6, by + 9, bx + 12, by + 9);

                using var titleFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                using var subFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                e.Graphics.DrawString("BULK ARCHIVAL & OFFLINE GAME BUILDER", titleFont, Brushes.White, bx + 28, 14);
                e.Graphics.DrawString("Preserve all 91 historical versions, mirror Blizzard CDN files, and assemble playable offline games.",
                    subFont, new SolidBrush(Color.FromArgb(148, 163, 184)), bx + 28, 38);

                using var pen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawLine(pen, 0, 63, 780, 63);
            };

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 32),
                Location = new Point(734, 8),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (_, _) => { if (!_isRunning) Close(); else CancelCurrentOperation(); };
            _pnlHeader.Controls.Add(btnX);
            Controls.Add(_pnlHeader);

            // 2. Navigation Tabs (Switch between Bulk Backup & Assemble Game)
            var pnlTabBar = new Panel
            {
                Location = new Point(0, 64),
                Size = new Size(780, 36),
                BackColor = Color.FromArgb(12, 15, 23)
            };
            pnlTabBar.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var btmPen = new Pen(Color.FromArgb(32, 40, 58), 1f);
                e.Graphics.DrawLine(btmPen, 0, 35, 780, 35);
            };

            var btnTab1 = new Button
            {
                Text = "📦 BULK ARCHIVAL & BACKUP",
                Location = new Point(20, 4),
                Size = new Size(240, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            var btnTab2 = new Button
            {
                Text = "🔨 ASSEMBLE COMPLETE GAME FROM ARCHIVE",
                Location = new Point(270, 4),
                Size = new Size(320, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            void UpdateTabs()
            {
                if (_activeTab == 0)
                {
                    btnTab1.BackColor = Color.FromArgb(30, 58, 138);
                    btnTab1.ForeColor = Color.White;
                    btnTab1.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
                    btnTab2.BackColor = Color.FromArgb(18, 24, 38);
                    btnTab2.ForeColor = Color.FromArgb(148, 163, 184);
                    btnTab2.FlatAppearance.BorderColor = Color.FromArgb(36, 45, 66);
                    _pnlTabBackup.Visible = true;
                    _pnlTabAssemble.Visible = false;
                    _btnStart.Visible = true;
                }
                else
                {
                    btnTab2.BackColor = Color.FromArgb(30, 58, 138);
                    btnTab2.ForeColor = Color.White;
                    btnTab2.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
                    btnTab1.BackColor = Color.FromArgb(18, 24, 38);
                    btnTab1.ForeColor = Color.FromArgb(148, 163, 184);
                    btnTab1.FlatAppearance.BorderColor = Color.FromArgb(36, 45, 66);
                    _pnlTabBackup.Visible = false;
                    _pnlTabAssemble.Visible = true;
                    _btnStart.Visible = false;
                }
            }

            btnTab1.Click += (_, _) => { _activeTab = 0; UpdateTabs(); };
            btnTab2.Click += (_, _) => { _activeTab = 1; UpdateTabs(); };
            pnlTabBar.Controls.Add(btnTab1);
            pnlTabBar.Controls.Add(btnTab2);
            Controls.Add(pnlTabBar);

            // ====================================================================
            // PANEL 1: BULK BACKUP ALL BUILDS
            // ====================================================================
            _pnlTabBackup = new Panel
            {
                Location = new Point(0, 100),
                Size = new Size(780, 280),
                BackColor = Color.Transparent
            };

            // Destination Folder
            var lblDest = new Label
            {
                Text = "BACKUP DESTINATION FOLDER:",
                Location = new Point(22, 6),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabBackup.Controls.Add(lblDest);

            _txtDest = new TextBox
            {
                Location = new Point(22, 26),
                Size = new Size(615, 25),
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.25f),
                BorderStyle = BorderStyle.FixedSingle,
                Text = defDest
            };
            _txtDest.TextChanged += (_, _) => TriggerInventoryScan();
            _pnlTabBackup.Controls.Add(_txtDest);

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(645, 25),
                Size = new Size(110, 26),
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            _btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
            _btnBrowse.Click += (_, _) =>
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Select Destination Folder for Diablo III Historical Backup",
                    UseDescriptionForTitle = true,
                    SelectedPath = Directory.Exists(_txtDest.Text) ? _txtDest.Text : defDrive
                };
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _txtDest.Text = fbd.SelectedPath;
                    _txtArchiveSrc.Text = fbd.SelectedPath;
                }
            };
            _pnlTabBackup.Controls.Add(_btnBrowse);

            // Live Inventory Tracker Card
            var pnlInventory = new Panel
            {
                Location = new Point(22, 58),
                Size = new Size(733, 76),
                BackColor = Color.FromArgb(13, 17, 26)
            };
            pnlInventory.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(36, 48, 72), 1f);
                e.Graphics.DrawRectangle(pen, 0, 0, pnlInventory.Width - 1, pnlInventory.Height - 1);
            };

            _lblInventoryTitle = new Label
            {
                Text = "LIVE DESTINATION INVENTORY TRACKER",
                Location = new Point(12, 8),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(59, 130, 246)
            };
            pnlInventory.Controls.Add(_lblInventoryTitle);

            _btnRescanInventory = new Button
            {
                Text = "🔄 Rescan",
                Location = new Point(645, 6),
                Size = new Size(76, 22),
                BackColor = Color.FromArgb(24, 32, 48),
                ForeColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRescanInventory.FlatAppearance.BorderColor = Color.FromArgb(45, 58, 85);
            _btnRescanInventory.Click += (_, _) => TriggerInventoryScan();
            pnlInventory.Controls.Add(_btnRescanInventory);

            _lblInventoryFast = new Label
            {
                Text = "⚡ Fast Binaries: Scanning destination folder...",
                Location = new Point(12, 28),
                Size = new Size(700, 16),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            pnlInventory.Controls.Add(_lblInventoryFast);

            _lblInventoryRaw = new Label
            {
                Text = "📦 Raw CDN Files: Scanning...",
                Location = new Point(12, 44),
                Size = new Size(700, 16),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(203, 213, 225)
            };
            pnlInventory.Controls.Add(_lblInventoryRaw);

            _lblInventoryStatus = new Label
            {
                Text = "Total Disk Footprint: Calculating...",
                Location = new Point(12, 59),
                Size = new Size(700, 15),
                Font = new Font("Segoe UI", 7.75f, FontStyle.Italic),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlInventory.Controls.Add(_lblInventoryStatus);
            _pnlTabBackup.Controls.Add(pnlInventory);

            // Archival Packages Checkboxes (Dual Mode)
            var lblPackages = new Label
            {
                Text = "BACKUP PACKAGES (Select One or BOTH):",
                Location = new Point(22, 140),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabBackup.Controls.Add(lblPackages);

            _chkFastBinaries = new CheckBox
            {
                Text = "Fast Binaries Package (~3.2 GB) — Patched standalone executables, DLLs & multi-locale launch scripts",
                Location = new Point(24, 158),
                Size = new Size(730, 22),
                Checked = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _pnlTabBackup.Controls.Add(_chkFastBinaries);

            _chkRawCdn = new CheckBox
            {
                Text = "Raw Blizzard CDN Mirror — Direct unpatched Blizzard CDN patch MPQs & manifests for offline cold storage",
                Location = new Point(24, 180),
                Size = new Size(730, 22),
                Checked = true,
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            _pnlTabBackup.Controls.Add(_chkRawCdn);

            _chkBaseAssets = new CheckBox
            {
                Text = "Include Full Base Game Assets (ClientData & CoreData ~45 GB) for 100% standalone offline play",
                Location = new Point(24, 202),
                Size = new Size(730, 22),
                Checked = false,
                ForeColor = Color.FromArgb(245, 158, 11),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _pnlTabBackup.Controls.Add(_chkBaseAssets);

            // Scope, Primary Locale, and Language Pack selection
            var lblScope = new Label
            {
                Text = "SCOPE:",
                Location = new Point(22, 230),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabBackup.Controls.Add(lblScope);

            _cbScope = new ComboBox
            {
                Location = new Point(74, 227),
                Size = new Size(220, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f)
            };
            _cbScope.Items.Add("All 91 Builds (Full Catalog)");
            _cbScope.Items.Add("Retail Releases Only (38 Builds)");
            _cbScope.Items.Add("Reaper of Souls Only (41 Builds)");
            _cbScope.Items.Add("Vanilla Classic Only (50 Builds)");
            _cbScope.SelectedIndex = 0;
            _cbScope.SelectedIndexChanged += (_, _) => TriggerInventoryScan();
            _pnlTabBackup.Controls.Add(_cbScope);

            var lblLoc = new Label
            {
                Text = "LOCALE:",
                Location = new Point(302, 230),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabBackup.Controls.Add(lblLoc);

            _cbLocale = new ComboBox
            {
                Location = new Point(356, 227),
                Size = new Size(135, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f)
            };
            _cbLocale.Items.AddRange(new object[]
            {
                "enUS - English", "enGB - British", "deDE - Deutsch", "frFR - Français",
                "esES - Spanish", "esMX - Latin Am", "ptBR - Português", "itIT - Italiano",
                "plPL - Polski", "ruRU - Russian", "koKR - Korean", "zhTW - Trad. Chinese", "zhCN - Simp. Chinese"
            });
            _cbLocale.SelectedIndex = 0;
            _pnlTabBackup.Controls.Add(_cbLocale);

            var lblPack = new Label
            {
                Text = "LANG PACK:",
                Location = new Point(498, 230),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabBackup.Controls.Add(lblPack);

            _cbLocalePack = new ComboBox
            {
                Location = new Point(572, 227),
                Size = new Size(183, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f)
            };
            _cbLocalePack.Items.Add("Universal Text (All 13 Langs, +400MB)");
            _cbLocalePack.Items.Add("Primary Selected Locale Only");
            _cbLocalePack.Items.Add("Complete Audio+Video+Text (All 13 Langs)");
            _cbLocalePack.SelectedIndex = 0;
            _pnlTabBackup.Controls.Add(_cbLocalePack);

            Controls.Add(_pnlTabBackup);

            // ====================================================================
            // PANEL 2: ASSEMBLE COMPLETE GAME FROM ARCHIVE
            // ====================================================================
            _pnlTabAssemble = new Panel
            {
                Location = new Point(0, 100),
                Size = new Size(780, 280),
                BackColor = Color.Transparent,
                Visible = false
            };

            var lblAsmExpl = new Label
            {
                Text = "Assemble any historical Diablo III build into a 100% complete, fully playable offline game installation using cached assets from your backup folder with ZERO internet download required.",
                Location = new Point(22, 6),
                Size = new Size(733, 32),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            _pnlTabAssemble.Controls.Add(lblAsmExpl);

            // Archive Source
            var lblSrc = new Label
            {
                Text = "OFFLINE ARCHIVE SOURCE FOLDER:",
                Location = new Point(22, 42),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabAssemble.Controls.Add(lblSrc);

            _txtArchiveSrc = new TextBox
            {
                Location = new Point(22, 62),
                Size = new Size(615, 25),
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.25f),
                BorderStyle = BorderStyle.FixedSingle,
                Text = defDest
            };
            _pnlTabAssemble.Controls.Add(_txtArchiveSrc);

            _btnBrowseArchiveSrc = new Button
            {
                Text = "Browse...",
                Location = new Point(645, 61),
                Size = new Size(110, 26),
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            _btnBrowseArchiveSrc.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
            _btnBrowseArchiveSrc.Click += (_, _) =>
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Select Existing Diablo III Offline Archive Folder",
                    UseDescriptionForTitle = true,
                    SelectedPath = Directory.Exists(_txtArchiveSrc.Text) ? _txtArchiveSrc.Text : defDrive
                };
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _txtArchiveSrc.Text = fbd.SelectedPath;
                }
            };
            _pnlTabAssemble.Controls.Add(_btnBrowseArchiveSrc);

            // Target Build Selector
            var lblBuild = new Label
            {
                Text = "SELECT TARGET GAME VERSION TO ASSEMBLE:",
                Location = new Point(22, 94),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabAssemble.Controls.Add(lblBuild);

            _cbAssembleBuild = new ComboBox
            {
                Location = new Point(22, 114),
                Size = new Size(470, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f)
            };
            foreach (var b in D3BuildCatalog.AllBuilds)
            {
                string ed = b.IsRoS ? "RoS" : "Vanilla";
                _cbAssembleBuild.Items.Add($"[{ed} {b.Category}] {b.Version}.{b.BuildNumber} — {b.Summary}");
            }
            _cbAssembleBuild.SelectedIndex = 0;
            _cbAssembleBuild.SelectedIndexChanged += (_, _) =>
            {
                if (_cbAssembleBuild.SelectedIndex >= 0 && _cbAssembleBuild.SelectedIndex < D3BuildCatalog.AllBuilds.Count)
                {
                    var b = D3BuildCatalog.AllBuilds[_cbAssembleBuild.SelectedIndex];
                    _txtAssembleDest.Text = Path.Combine(defDrive, "Games", $"Diablo III ({b.Version}.{b.BuildNumber})");
                }
            };
            _pnlTabAssemble.Controls.Add(_cbAssembleBuild);

            var lblAsmLoc = new Label
            {
                Text = "PRIMARY LANGUAGE:",
                Location = new Point(504, 94),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabAssemble.Controls.Add(lblAsmLoc);

            _cbAssembleLocale = new ComboBox
            {
                Location = new Point(504, 114),
                Size = new Size(251, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f)
            };
            _cbAssembleLocale.Items.AddRange(new object[]
            {
                "enUS - English (US)", "enGB - English (EU)", "deDE - Deutsch", "frFR - Français",
                "esES - Español (EU)", "esMX - Español (AL)", "ptBR - Português", "itIT - Italiano",
                "plPL - Polski", "ruRU - Русский", "koKR - 한국어", "zhTW - 繁體中文", "zhCN - 简体中文"
            });
            _cbAssembleLocale.SelectedIndex = 0;
            _pnlTabAssemble.Controls.Add(_cbAssembleLocale);

            // Install Destination
            var lblInstDest = new Label
            {
                Text = "DESTINATION GAME FOLDER:",
                Location = new Point(22, 146),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            _pnlTabAssemble.Controls.Add(lblInstDest);

            _txtAssembleDest = new TextBox
            {
                Location = new Point(22, 166),
                Size = new Size(615, 25),
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.25f),
                BorderStyle = BorderStyle.FixedSingle,
                Text = Path.Combine(defDrive, "Games", "Diablo III (v1.0.8)")
            };
            _pnlTabAssemble.Controls.Add(_txtAssembleDest);

            _btnBrowseAssembleDest = new Button
            {
                Text = "Browse...",
                Location = new Point(645, 165),
                Size = new Size(110, 26),
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            _btnBrowseAssembleDest.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
            _btnBrowseAssembleDest.Click += (_, _) =>
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Select Destination Folder for Assembled Game Install",
                    UseDescriptionForTitle = true,
                    SelectedPath = Directory.Exists(_txtAssembleDest.Text) ? _txtAssembleDest.Text : defDrive
                };
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _txtAssembleDest.Text = fbd.SelectedPath;
                }
            };
            _pnlTabAssemble.Controls.Add(_btnBrowseAssembleDest);

            // Assemble Checkboxes
            _chkAssembleUniversalText = new CheckBox
            {
                Text = "Universal Multi-Language Pack (Include text for all 13 languages so game can switch on the fly)",
                Location = new Point(24, 198),
                Size = new Size(730, 20),
                Checked = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _pnlTabAssemble.Controls.Add(_chkAssembleUniversalText);

            _chkAssembleShortcuts = new CheckBox
            {
                Text = "Create Desktop and Start Menu Shortcuts",
                Location = new Point(24, 222),
                Size = new Size(270, 20),
                Checked = true,
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 8.25f),
                Cursor = Cursors.Hand
            };
            _pnlTabAssemble.Controls.Add(_chkAssembleShortcuts);

            _chkAssembleFirewall = new CheckBox
            {
                Text = "Configure Windows Firewall Rule",
                Location = new Point(300, 222),
                Size = new Size(230, 20),
                Checked = true,
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 8.25f),
                Cursor = Cursors.Hand
            };
            _pnlTabAssemble.Controls.Add(_chkAssembleFirewall);

            _chkAssemblePrereqs = new CheckBox
            {
                Text = "Install DirectX & VC++ Runtimes",
                Location = new Point(540, 222),
                Size = new Size(215, 20),
                Checked = false,
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 8.25f),
                Cursor = Cursors.Hand
            };
            _pnlTabAssemble.Controls.Add(_chkAssemblePrereqs);

            _btnAssembleStart = new Button
            {
                Text = "⚡ ASSEMBLE COMPLETE GAME INSTALL",
                Location = new Point(22, 248),
                Size = new Size(733, 30),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnAssembleStart.FlatAppearance.BorderSize = 0;
            _btnAssembleStart.Click += async (_, _) =>
            {
                if (_isRunning) CancelCurrentOperation();
                else await StartAssembleAsync();
            };
            _pnlTabAssemble.Controls.Add(_btnAssembleStart);

            Controls.Add(_pnlTabAssemble);

            // ====================================================================
            // BOTTOM SECTION: Progress Bars, Log Output & Action Buttons
            // ====================================================================
            _lblOverall = new Label
            {
                Text = "Ready for archival or assembly operations.",
                Location = new Point(22, 385),
                Size = new Size(733, 18),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
            };
            Controls.Add(_lblOverall);

            _pbOverall = new ProgressBar
            {
                Location = new Point(22, 405),
                Size = new Size(733, 14),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Controls.Add(_pbOverall);

            _lblCurrent = new Label
            {
                Text = "",
                Location = new Point(22, 423),
                Size = new Size(733, 16),
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8f)
            };
            Controls.Add(_lblCurrent);

            _pbCurrent = new ProgressBar
            {
                Location = new Point(22, 441),
                Size = new Size(733, 8),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Controls.Add(_pbCurrent);

            _txtLog = new RichTextBox
            {
                Location = new Point(22, 456),
                Size = new Size(733, 192),
                BackColor = Color.FromArgb(6, 8, 13),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Consolas", 8.25f),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_txtLog);

            // Bottom Buttons
            _btnStart = new Button
            {
                Text = "🚀 START BULK BACKUP",
                Location = new Point(460, 658),
                Size = new Size(185, 38),
                BackColor = Color.FromArgb(0, 116, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnStart.FlatAppearance.BorderSize = 0;
            _btnStart.Click += async (_, _) =>
            {
                if (_isRunning) CancelCurrentOperation();
                else await StartBackupAsync();
            };
            Controls.Add(_btnStart);

            _btnClose = new Button
            {
                Text = "Close",
                Location = new Point(655, 658),
                Size = new Size(100, 38),
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderColor = Color.FromArgb(45, 55, 75);
            _btnClose.Click += (_, _) =>
            {
                if (_isRunning) CancelCurrentOperation();
                else Close();
            };
            Controls.Add(_btnClose);

            Paint += (_, e) =>
            {
                using var borderPen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            };

            UpdateTabs();
            Shown += (_, _) => TriggerInventoryScan();
        }

        private void TriggerInventoryScan()
        {
            string dest = _txtDest.Text.Trim();
            var scope = _cbScope.SelectedIndex switch
            {
                1 => BackupScope.RetailOnly,
                2 => BackupScope.ReaperOfSoulsOnly,
                3 => BackupScope.VanillaOnly,
                _ => BackupScope.All
            };

            Task.Run(() =>
            {
                var inv = BackupEngine.ScanInventory(dest, scope);
                _latestInventory = inv;

                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    int total = inv.TotalCatalogBuilds;
                    int fast = inv.FastBinariesCount;
                    int rawPresent = inv.RawCdnPresentCount;
                    int rawTotal = inv.RawCdnTotalExpected;

                    int fastPct = total > 0 ? (fast * 100 / total) : 0;
                    int rawPct = rawTotal > 0 ? (rawPresent * 100 / rawTotal) : 0;

                    _lblInventoryFast.Text = $"⚡ Fast Binaries : {fast} / {total} Present ({fastPct}%) | {inv.FastBinariesMissing} Still Needed";
                    _lblInventoryRaw.Text = $"📦 Raw CDN Files : {rawPresent} / {rawTotal} Archives Present ({rawPct}%) | {inv.RawCdnMissingCount} Still Needed";
                    _lblInventoryStatus.Text = $"Total Storage on Disk: {inv.FormattedDiskSize} | Destination: {dest}";

                    if (fast == total && rawPresent == rawTotal)
                    {
                        _lblInventoryTitle.Text = "LIVE DESTINATION INVENTORY TRACKER — [ 100% COMPLETE & VERIFIED ]";
                        _lblInventoryTitle.ForeColor = Color.FromArgb(16, 185, 129);
                    }
                    else
                    {
                        _lblInventoryTitle.Text = "LIVE DESTINATION INVENTORY TRACKER";
                        _lblInventoryTitle.ForeColor = Color.FromArgb(59, 130, 246);
                    }
                });
            });
        }

        private async Task StartBackupAsync()
        {
            string dest = _txtDest.Text.Trim();
            if (string.IsNullOrWhiteSpace(dest))
            {
                MessageBox.Show(this, "Please select a valid destination folder.", "Missing Destination",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_chkFastBinaries.Checked && !_chkRawCdn.Checked)
            {
                MessageBox.Show(this, "Please select at least one backup package (Fast Binaries or Raw CDN Mirror).", "No Packages Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            BackupMode mode = 0;
            if (_chkFastBinaries.Checked) mode |= BackupMode.FastBinaries;
            if (_chkRawCdn.Checked) mode |= BackupMode.RawCdnMirror;

            var scope = _cbScope.SelectedIndex switch
            {
                1 => BackupScope.RetailOnly,
                2 => BackupScope.ReaperOfSoulsOnly,
                3 => BackupScope.VanillaOnly,
                _ => BackupScope.All
            };

            string selectedLocale = "enUS";
            if (_cbLocale.SelectedItem != null)
            {
                string s = _cbLocale.SelectedItem.ToString();
                if (s.Length >= 4) selectedLocale = s.Substring(0, 4);
            }

            var localeMode = _cbLocalePack.SelectedIndex switch
            {
                1 => BackupLocaleMode.SingleLocale,
                2 => BackupLocaleMode.AllLocalesFull,
                _ => BackupLocaleMode.AllTextLocales
            };

            bool includeBase = _chkBaseAssets.Checked;

            SetRunningState(true, "Bulk Backup");
            _cts = new CancellationTokenSource();

            try
            {
                await BackupEngine.RunBackupAsync(
                    backupRoot: dest,
                    mode: mode,
                    scope: scope,
                    locale: selectedLocale,
                    localeMode: localeMode,
                    includeBaseAssets: includeBase,
                    onOverallProgress: (curr, total, status) =>
                    {
                        SafeInvoke(() =>
                        {
                            int pct = (int)Math.Clamp(curr * 100.0 / total, 0, 100);
                            _pbOverall.Value = pct;
                            _lblOverall.Text = $"{status} ({pct}%)";
                        });
                    },
                    onItemProgress: (pct, status) =>
                    {
                        SafeInvoke(() =>
                        {
                            _pbCurrent.Value = (int)Math.Clamp(pct, 0, 100);
                            _lblCurrent.Text = status;
                        });
                    },
                    log: msg =>
                    {
                        SafeInvoke(() =>
                        {
                            _txtLog.AppendText(msg + "\r\n");
                            _txtLog.SelectionStart = _txtLog.Text.Length;
                            _txtLog.ScrollToCaret();
                        });
                    },
                    ct: _cts.Token);

                TriggerInventoryScan();
                MessageBox.Show(this, "Bulk archival operation completed successfully!", "Backup Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                SafeInvoke(() => _txtLog.AppendText("\r\n[CANCELED] Archival operation canceled by user.\r\n"));
            }
            catch (Exception ex)
            {
                SafeInvoke(() => _txtLog.AppendText($"\r\n[ERROR] Archival operation failed: {ex.Message}\r\n"));
                MessageBox.Show(this, $"Backup failed: {ex.Message}", "Backup Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetRunningState(false, "Bulk Backup");
            }
        }

        private async Task StartAssembleAsync()
        {
            string archiveSrc = _txtArchiveSrc.Text.Trim();
            string gameDest = _txtAssembleDest.Text.Trim();

            if (string.IsNullOrWhiteSpace(archiveSrc) || !Directory.Exists(archiveSrc))
            {
                MessageBox.Show(this, "Please select a valid offline archive folder.", "Missing Archive Source",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(gameDest))
            {
                MessageBox.Show(this, "Please specify an output folder for the game.", "Missing Destination",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int selIdx = _cbAssembleBuild.SelectedIndex;
            if (selIdx < 0 || selIdx >= D3BuildCatalog.AllBuilds.Count)
                selIdx = 0;
            var targetBuild = D3BuildCatalog.AllBuilds[selIdx];

            string locale = "enUS";
            if (_cbAssembleLocale.SelectedItem != null)
            {
                string s = _cbAssembleLocale.SelectedItem.ToString();
                if (s.Length >= 4) locale = s.Substring(0, 4);
            }

            var localeMode = _chkAssembleUniversalText.Checked
                ? BackupLocaleMode.AllTextLocales
                : BackupLocaleMode.SingleLocale;

            bool shortcuts = _chkAssembleShortcuts.Checked;
            bool firewall = _chkAssembleFirewall.Checked;
            bool prereqs = _chkAssemblePrereqs.Checked;

            SetRunningState(true, "Assemble Game");
            _cts = new CancellationTokenSource();

            try
            {
                bool ok = await BackupEngine.BuildCompleteGameFromArchiveAsync(
                    archiveSource: archiveSrc,
                    destinationFolder: gameDest,
                    build: targetBuild,
                    locale: locale,
                    localeMode: localeMode,
                    createShortcuts: shortcuts,
                    configureFirewall: firewall,
                    installPrereqs: prereqs,
                    onProgress: (pct, status) =>
                    {
                        SafeInvoke(() =>
                        {
                            _pbOverall.Value = (int)Math.Clamp(pct, 0, 100);
                            _lblOverall.Text = $"{status} ({(int)pct}%)";
                        });
                    },
                    log: msg =>
                    {
                        SafeInvoke(() =>
                        {
                            _txtLog.AppendText(msg + "\r\n");
                            _txtLog.SelectionStart = _txtLog.Text.Length;
                            _txtLog.ScrollToCaret();
                        });
                    },
                    ct: _cts.Token);

                if (ok)
                {
                    MessageBox.Show(this,
                        $"Diablo III {targetBuild.Version}.{targetBuild.BuildNumber} was successfully assembled!\n\nLocation: {gameDest}\n\nReady to play offline.",
                        "Assembly Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                SafeInvoke(() => _txtLog.AppendText("\r\n[CANCELED] Game assembly canceled by user.\r\n"));
            }
            catch (Exception ex)
            {
                SafeInvoke(() => _txtLog.AppendText($"\r\n[ERROR] Game assembly failed: {ex.Message}\r\n"));
                MessageBox.Show(this, $"Assembly failed: {ex.Message}", "Assembly Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetRunningState(false, "Assemble Game");
            }
        }

        private void SetRunningState(bool running, string opName)
        {
            _isRunning = running;
            if (running)
            {
                _btnStart.Text = "⏹ CANCEL";
                _btnStart.BackColor = Color.FromArgb(185, 28, 28);
                _btnAssembleStart.Text = "⏹ CANCEL";
                _btnAssembleStart.BackColor = Color.FromArgb(185, 28, 28);
                _btnClose.Enabled = false;
                _txtDest.Enabled = false;
                _btnBrowse.Enabled = false;
                _chkFastBinaries.Enabled = false;
                _chkRawCdn.Enabled = false;
                _chkBaseAssets.Enabled = false;
                _cbScope.Enabled = false;
                _cbLocale.Enabled = false;
                _cbLocalePack.Enabled = false;
                _txtArchiveSrc.Enabled = false;
                _btnBrowseArchiveSrc.Enabled = false;
                _cbAssembleBuild.Enabled = false;
                _txtAssembleDest.Enabled = false;
                _btnBrowseAssembleDest.Enabled = false;
                _cbAssembleLocale.Enabled = false;
                _chkAssembleUniversalText.Enabled = false;
                _chkAssembleShortcuts.Enabled = false;
                _chkAssembleFirewall.Enabled = false;
                _chkAssemblePrereqs.Enabled = false;
            }
            else
            {
                _btnStart.Text = "🚀 START BULK BACKUP";
                _btnStart.BackColor = Color.FromArgb(0, 116, 224);
                _btnAssembleStart.Text = "⚡ ASSEMBLE COMPLETE GAME INSTALL";
                _btnAssembleStart.BackColor = Color.FromArgb(16, 185, 129);
                _btnClose.Enabled = true;
                _txtDest.Enabled = true;
                _btnBrowse.Enabled = true;
                _chkFastBinaries.Enabled = true;
                _chkRawCdn.Enabled = true;
                _chkBaseAssets.Enabled = true;
                _cbScope.Enabled = true;
                _cbLocale.Enabled = true;
                _cbLocalePack.Enabled = true;
                _txtArchiveSrc.Enabled = true;
                _btnBrowseArchiveSrc.Enabled = true;
                _cbAssembleBuild.Enabled = true;
                _txtAssembleDest.Enabled = true;
                _btnBrowseAssembleDest.Enabled = true;
                _cbAssembleLocale.Enabled = true;
                _chkAssembleUniversalText.Enabled = true;
                _chkAssembleShortcuts.Enabled = true;
                _chkAssembleFirewall.Enabled = true;
                _chkAssemblePrereqs.Enabled = true;
            }
        }

        private void CancelCurrentOperation()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _txtLog.AppendText("\r\n[CANCELING] Requesting cancellation...\r\n");
                _cts.Cancel();
            }
        }

        private void SafeInvoke(Action act)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(act);
            else act();
        }
    }
}

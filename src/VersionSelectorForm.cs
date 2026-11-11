using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace D3Downloader
{
    public enum CdnAvailability
    {
        Checking,
        Ok,
        Offline
    }

    public sealed class VersionSelectorForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private const int BANNER_HEIGHT = 196;

        // Embedded Artwork Assets
        private readonly Image _logoImg;
        private readonly Image _rosBannerImg;
        private readonly Image _vanillaBannerImg;

        // Selected builds (Default to Newest on both tiles)
        private D3BuildCatalogItem _selectedRosBuild;
        private D3BuildCatalogItem _selectedVanillaBuild;

        // State
        private string _selectedLocaleCode = "enUS";
        private string _selectedLocaleDisplay = "enUS (English US)";
        private bool _chkShortcutFirewall = true;
        private bool _chkPrereqs = false;
        private bool _chkVanillaBinariesOnly = false;
        private bool _chkRosBinariesOnly = false;

        private bool _rosExpanded;
        private bool _vanillaExpanded;
        private bool _logExpanded;
        private bool _userEditedTargetDir;
        private bool _isInstalling;

        private string _statusText = "Ready — Choose Target Location above and click Install on either edition.";
        private double _progressPercent = 100.0;

        // Controls
        private readonly Panel _topToolbar;
        private readonly Panel _locationBar;
        private readonly TextBox _txtTargetLocation;
        private readonly Panel _mainScrollPanel;
        private readonly Panel _rosCardWrapper;
        private readonly Panel _rosBannerPanel;
        private readonly Panel _rosDrawer;
        private readonly List<Panel> _rosVersionChips = new();

        private readonly Panel _vanillaCardWrapper;
        private readonly Panel _vanillaBannerPanel;
        private readonly Panel _vanillaDrawer;
        private readonly List<Panel> _vanillaVersionChips = new();

        // CDN Availability Tracking & Animation
        private readonly Dictionary<string, CdnAvailability> _cdnStatuses = new();
        private static string GetBuildKey(D3BuildCatalogItem b) => $"{b.ProductCode}_{b.Category}_{b.BuildNumber}_{b.IsCasc}";
        private D3BuildCategory _rosCategory = D3BuildCategory.Retail;
        private D3BuildCategory _vanillaCategory = D3BuildCategory.Retail;
        private BufferedPanel _rosChipsContainer;
        private BufferedPanel _vanillaChipsContainer;
        private UpdateInfo _availableUpdate;
        private bool _hoverUpdateBtn = false;
        private readonly System.Windows.Forms.Timer _pulseTimer;
        private float _pulsePhase = 0f;
        private static readonly System.Net.Http.HttpClient _probeClient = new()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        private readonly Panel _bottomPanel;
        private readonly TextBox _txtLog;
        private CancellationTokenSource _cts;

        private static readonly (string Code, string Display)[] Locales =
        {
            ("enUS", "enUS (English US)"),
            ("enGB", "enGB (English EU)"),
            ("deDE", "deDE (German)"),
            ("frFR", "frFR (French)"),
            ("esES", "esES (Spanish EU)"),
            ("esMX", "esMX (Spanish LatAm)"),
            ("itIT", "itIT (Italian)"),
            ("plPL", "plPL (Polish)"),
            ("ptBR", "ptBR (Portuguese)"),
            ("ruRU", "ruRU (Russian)"),
            ("koKR", "koKR (Korean)"),
            ("zhTW", "zhTW (Traditional Chinese)"),
            ("zhCN", "zhCN (Simplified Chinese)")
        };

        public VersionSelectorForm()
        {
            _selectedRosBuild = D3BuildCatalog.AllBuilds.First(b => b.IsRoS); // 2.8.0.99920
            _selectedVanillaBuild = D3BuildCatalog.AllBuilds.First(b => !b.IsRoS); // 1.0.8a.16603

            _logoImg = LoadEmbeddedImage("d3forever_logo.png");
            _rosBannerImg = LoadEmbeddedImage("ros_banner.jpg");
            _vanillaBannerImg = LoadEmbeddedImage("vanilla_banner.jpg");

            var appIcon = LoadEmbeddedIcon("app_icon.ico");
            if (appIcon != null)
            {
                Icon = appIcon;
            }

            Text = "Diablo 3 Forever — Official Blizzard CDN Installer";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            Size = new Size(980, 622);
            MinimumSize = new Size(980, 622);
            MaximumSize = new Size(980, 622);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(12, 15, 23);
            ForeColor = Color.FromArgb(237, 241, 247);
            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            DoubleBuffered = true;

            // Initialize all builds to Checking
            foreach (var b in D3BuildCatalog.AllBuilds)
            {
                _cdnStatuses[GetBuildKey(b)] = CdnAvailability.Checking;
            }

            // Pulse timer for breathing dot animation (40ms ~ 25 FPS)
            _pulseTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _pulseTimer.Tick += (_, _) =>
            {
                _pulsePhase = (_pulsePhase + 0.08f) % ((float)Math.PI * 2);
                if (_rosExpanded)
                {
                    foreach (var c in _rosVersionChips) c.Invalidate();
                }
                if (_vanillaExpanded)
                {
                    foreach (var c in _vanillaVersionChips) c.Invalidate();
                }
                _rosBannerPanel?.Invalidate();
                _vanillaBannerPanel?.Invalidate();
            };
            _pulseTimer.Start();

            // Asynchronously probe each build's availability in background
            Task.Run(ProbeAllBuildsAsync);
            Task.Run(CheckForAppUpdateAsync);

            // ================= 1. SEAMLESS TOP TOOLBAR (74px, Minimize + Close Only) =================
            _topToolbar = new BufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Color.FromArgb(14, 19, 30)
            };

            int hoverWinBtn = -1; // 0=min, 1=close
            bool hoverLocale = false;
            bool hoverBackup = false;

            var updateBtnRect = new Rectangle(320, 22, 148, 30);
            var backupBtnRect = new Rectangle(480, 22, 175, 30);
            var localeRect = new Rectangle(675, 22, 196, 30);
            var minRect = new Rectangle(890, 22, 32, 30);
            var closeRect = new Rectangle(926, 22, 32, 30);

            _topToolbar.Paint += (_, e) =>
            {
                ConfigureGraphics(e.Graphics);
                int w = _topToolbar.Width;
                int h = _topToolbar.Height;

                using (var bgBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, w), Math.Max(1, h)),
                    Color.FromArgb(22, 29, 44),
                    Color.FromArgb(13, 17, 26),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(bgBrush, 0, 0, w, h);
                }

                // Soft cyan glow behind D3 Forever crest
                using (var glowPath = new GraphicsPath())
                {
                    glowPath.AddEllipse(14, 6, 62, 62);
                    using var pgb = new PathGradientBrush(glowPath)
                    {
                        CenterColor = Color.FromArgb(75, 0, 180, 255),
                        SurroundColors = new[] { Color.FromArgb(0, 0, 180, 255) }
                    };
                    e.Graphics.FillPath(pgb, glowPath);
                }

                if (_logoImg != null)
                {
                    e.Graphics.DrawImage(_logoImg, new Rectangle(18, 10, 54, 54));
                }

                using (var titleFont = new Font("Georgia", 14.5f, FontStyle.Bold))
                using (var titleBrush = new SolidBrush(Color.FromArgb(245, 235, 210)))
                {
                    e.Graphics.DrawString("DIABLO 3 FOREVER", titleFont, titleBrush, 82, 15);
                }

                using (var subFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                using (var subBrush = new SolidBrush(Color.FromArgb(142, 153, 176)))
                {
                    e.Graphics.DrawString("Official Blizzard CDN Archive & Multi-Version Installer", subFont, subBrush, 83, 41);
                }

                // Update Available Pill (Toolbar)
                if (_availableUpdate?.HasUpdate == true)
                {
                    using (var uPath = CreateRoundRect(updateBtnRect, 5))
                    using (var uFill = new SolidBrush(_hoverUpdateBtn ? Color.FromArgb(20, 83, 45) : Color.FromArgb(6, 44, 28)))
                    using (var uBorder = new Pen(_hoverUpdateBtn ? Color.FromArgb(52, 211, 153) : Color.FromArgb(16, 185, 129), 1.4f))
                    {
                        e.Graphics.FillPath(uFill, uPath);
                        e.Graphics.DrawPath(uBorder, uPath);
                    }
                    using (var dotBrush = new SolidBrush(Color.FromArgb(52, 211, 153)))
                    {
                        e.Graphics.FillEllipse(dotBrush, updateBtnRect.X + 10, updateBtnRect.Y + 11, 8, 8);
                    }
                    using (var uFont = new Font("Segoe UI", 8.25f, FontStyle.Bold))
                    using (var uBrush = new SolidBrush(_hoverUpdateBtn ? Color.White : Color.FromArgb(209, 250, 229)))
                    {
                        var textRect = new Rectangle(updateBtnRect.X + 22, updateBtnRect.Y, updateBtnRect.Width - 26, updateBtnRect.Height);
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        e.Graphics.DrawString($"UPDATE v{_availableUpdate.LatestVersion}", uFont, uBrush, textRect, sf);
                    }
                }

                using (var locFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                using (var locBrush = new SolidBrush(Color.FromArgb(194, 203, 224)))
                {
                    e.Graphics.DrawString("LOCALE:", locFont, locBrush, localeRect.X - 56, 29);
                }

                using (var locPath = CreateRoundRect(localeRect, 5))
                using (var locFill = new SolidBrush(Color.FromArgb(12, 16, 24)))
                using (var locBorder = new Pen(hoverLocale ? Color.FromArgb(68, 88, 125) : Color.FromArgb(43, 53, 75), 1f))
                {
                    e.Graphics.FillPath(locFill, locPath);
                    e.Graphics.DrawPath(locBorder, locPath);
                }

                using (var selFont = new Font("Segoe UI", 8.75f, FontStyle.Bold))
                {
                    e.Graphics.DrawString(_selectedLocaleDisplay, selFont, Brushes.White, localeRect.X + 10, localeRect.Y + 6);
                }
                DrawChevron(e.Graphics, new Point(localeRect.Right - 14, localeRect.Y + 15), false, Color.FromArgb(148, 163, 184));

                // Backup All Versions Button (Toolbar)
                using (var bkPath = CreateRoundRect(backupBtnRect, 5))
                using (var bkFill = new SolidBrush(hoverBackup ? Color.FromArgb(32, 44, 66) : Color.FromArgb(18, 24, 38)))
                using (var bkBorder = new Pen(hoverBackup ? Color.FromArgb(245, 158, 11) : Color.FromArgb(60, 80, 115), hoverBackup ? 1.4f : 1f))
                {
                    e.Graphics.FillPath(bkFill, bkPath);
                    e.Graphics.DrawPath(bkBorder, bkPath);
                }

                // Crisp vector archive box icon
                int iconX = backupBtnRect.X + 14;
                int iconY = backupBtnRect.Y + 8;
                using (var boxPen = new Pen(hoverBackup ? Color.FromArgb(245, 158, 11) : Color.FromArgb(180, 195, 220), 1.4f))
                {
                    e.Graphics.DrawRectangle(boxPen, iconX, iconY + 3, 14, 9);
                    e.Graphics.DrawLine(boxPen, iconX - 1, iconY + 3, iconX + 15, iconY + 3);
                    e.Graphics.DrawLine(boxPen, iconX + 4, iconY + 7, iconX + 10, iconY + 7);
                }

                using (var bkFont = new Font("Segoe UI", 8.25f, FontStyle.Bold))
                using (var bkBrush = new SolidBrush(hoverBackup ? Color.FromArgb(254, 243, 199) : Color.FromArgb(226, 232, 240)))
                {
                    var textRect = new Rectangle(backupBtnRect.X + 30, backupBtnRect.Y, backupBtnRect.Width - 34, backupBtnRect.Height);
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString("BACKUP ALL BUILDS", bkFont, bkBrush, textRect, sf);
                }

                // Crisp Vector Window Buttons (Minimize & Close only)
                DrawMinimizeButton(e.Graphics, minRect, hoverWinBtn == 0);
                DrawCloseButton(e.Graphics, closeRect, hoverWinBtn == 1);

                using var framePen = new Pen(Color.FromArgb(38, 48, 68), 1);
                e.Graphics.DrawLine(framePen, 0, 0, w - 1, 0);
                e.Graphics.DrawLine(framePen, 0, 0, 0, h);
                e.Graphics.DrawLine(framePen, w - 1, 0, w - 1, h);
            };

            _topToolbar.MouseMove += (_, e) =>
            {
                int newHover = -1;
                if (minRect.Contains(e.Location)) newHover = 0;
                else if (closeRect.Contains(e.Location)) newHover = 1;

                bool newLoc = localeRect.Contains(e.Location);
                bool newBackup = backupBtnRect.Contains(e.Location);
                bool newUpdate = _availableUpdate?.HasUpdate == true && updateBtnRect.Contains(e.Location);
                _topToolbar.Cursor = (newHover >= 0 || newLoc || newBackup || newUpdate) ? Cursors.Hand : Cursors.Default;

                if (newHover != hoverWinBtn || newLoc != hoverLocale || newBackup != hoverBackup || newUpdate != _hoverUpdateBtn)
                {
                    hoverWinBtn = newHover;
                    hoverLocale = newLoc;
                    hoverBackup = newBackup;
                    _hoverUpdateBtn = newUpdate;
                    _topToolbar.Invalidate();
                }
            };
            _topToolbar.MouseLeave += (_, _) =>
            {
                hoverWinBtn = -1;
                hoverLocale = false;
                hoverBackup = false;
                _hoverUpdateBtn = false;
                _topToolbar.Invalidate();
            };
            _topToolbar.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (closeRect.Contains(e.Location)) { Close(); return; }
                if (minRect.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }
                if (_availableUpdate?.HasUpdate == true && updateBtnRect.Contains(e.Location))
                {
                    using var uDlg = new UpdateDialog(_availableUpdate);
                    uDlg.ShowDialog(this);
                    return;
                }
                if (backupBtnRect.Contains(e.Location))
                {
                    using var dlg = new BackupDialog(_txtTargetLocation?.Text ?? @"C:\Games\Diablo III");
                    dlg.ShowDialog(this);
                    return;
                }
                if (localeRect.Contains(e.Location))
                {
                    ShowLocaleMenu(_topToolbar, new Point(localeRect.X, localeRect.Bottom + 2));
                    return;
                }
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            };

            // ================= 2. SEAMLESS TARGET LOCATION BAR (56px) =================
            _locationBar = new BufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(13, 17, 26)
            };

            var outerBarRect = new Rectangle(22, 0, 936, 44);
            var inputBoxRect = new Rectangle(154, 7, 344, 30);
            var browseBtnRect = new Rectangle(508, 7, 122, 30);
            var chk1Rect = new Rectangle(648, 13, 172, 20);
            var chk2Rect = new Rectangle(828, 13, 118, 20);
            bool hoverBrowse = false;

            _txtTargetLocation = new TextBox
            {
                Location = new Point(inputBoxRect.X + 10, inputBoxRect.Y + 6),
                Width = inputBoxRect.Width - 20,
                BackColor = Color.FromArgb(7, 9, 15),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f, FontStyle.Regular),
                BorderStyle = BorderStyle.None,
                TabStop = false,
                Text = @"C:\Games\Diablo III"
            };
            Shown += (_, _) =>
            {
                _txtTargetLocation.SelectionLength = 0;
                ActiveControl = null;
            };
            _txtTargetLocation.TextChanged += (_, _) =>
            {
                if (_txtTargetLocation.Focused) _userEditedTargetDir = true;
            };
            _locationBar.Controls.Add(_txtTargetLocation);

            _locationBar.Paint += (_, e) =>
            {
                ConfigureGraphics(e.Graphics);

                using (var barPath = CreateRoundRect(outerBarRect, 6))
                using (var barFill = new SolidBrush(Color.FromArgb(10, 13, 20)))
                using (var barPen = new Pen(Color.FromArgb(37, 47, 68), 1f))
                {
                    e.Graphics.FillPath(barFill, barPath);
                    e.Graphics.DrawPath(barPen, barPath);
                }

                using (var capFont = new Font("Segoe UI", 8.25f, FontStyle.Bold))
                using (var capBrush = new SolidBrush(Color.FromArgb(154, 166, 191)))
                {
                    e.Graphics.DrawString("TARGET LOCATION:", capFont, capBrush, 36, 15);
                }

                using (var inPath = CreateRoundRect(inputBoxRect, 4))
                using (var inFill = new SolidBrush(Color.FromArgb(7, 9, 15)))
                using (var inPen = new Pen(_txtTargetLocation.Focused ? Color.FromArgb(56, 139, 253) : Color.FromArgb(40, 50, 72), 1f))
                {
                    e.Graphics.FillPath(inFill, inPath);
                    e.Graphics.DrawPath(inPen, inPath);
                }

                using (var brPath = CreateRoundRect(browseBtnRect, 4))
                using (var brFill = new SolidBrush(hoverBrowse ? Color.FromArgb(43, 55, 82) : Color.FromArgb(32, 41, 61)))
                using (var brPen = new Pen(Color.FromArgb(51, 65, 94), 1f))
                {
                    e.Graphics.FillPath(brFill, brPath);
                    e.Graphics.DrawPath(brPen, brPath);
                }
                using (var brFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                using (var brTextBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString("Browse Folder...", brFont, brTextBrush, browseBtnRect, sf);
                }

                using (var divPen = new Pen(Color.FromArgb(37, 47, 68), 1f))
                {
                    e.Graphics.DrawLine(divPen, 639, 9, 639, 35);
                }

                DrawModernCheckBox(e.Graphics, chk1Rect.Location, _chkShortcutFirewall, "Desktop Shortcut & Firewall");
                DrawModernCheckBox(e.Graphics, chk2Rect.Location, _chkPrereqs, "VC++ / DirectX");

                using var framePen = new Pen(Color.FromArgb(38, 48, 68), 1);
                e.Graphics.DrawLine(framePen, 0, 0, 0, _locationBar.Height);
                e.Graphics.DrawLine(framePen, _locationBar.Width - 1, 0, _locationBar.Width - 1, _locationBar.Height);
            };

            _txtTargetLocation.GotFocus += (_, _) => _locationBar.Invalidate();
            _txtTargetLocation.LostFocus += (_, _) => _locationBar.Invalidate();

            _locationBar.MouseMove += (_, e) =>
            {
                bool hb = browseBtnRect.Contains(e.Location);
                bool hc1 = chk1Rect.Contains(e.Location);
                bool hc2 = chk2Rect.Contains(e.Location);
                _locationBar.Cursor = (hb || hc1 || hc2) ? Cursors.Hand : Cursors.Default;
                if (hb != hoverBrowse)
                {
                    hoverBrowse = hb;
                    _locationBar.Invalidate();
                }
            };
            _locationBar.MouseLeave += (_, _) =>
            {
                hoverBrowse = false;
                _locationBar.Invalidate();
            };
            _locationBar.MouseClick += (_, e) =>
            {
                if (browseBtnRect.Contains(e.Location))
                {
                    using var fbd = new FolderBrowserDialog
                    {
                        SelectedPath = _txtTargetLocation.Text.Trim(),
                        Description = "Select Target Installation Location for Diablo 3 Forever"
                    };
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        _userEditedTargetDir = true;
                        _txtTargetLocation.Text = fbd.SelectedPath;
                    }
                }
                else if (chk1Rect.Contains(e.Location))
                {
                    _chkShortcutFirewall = !_chkShortcutFirewall;
                    _locationBar.Invalidate();
                }
                else if (chk2Rect.Contains(e.Location))
                {
                    _chkPrereqs = !_chkPrereqs;
                    _locationBar.Invalidate();
                }
            };

            // ================= 3. BOTTOM STATUS & GRADIENT PROGRESS BAR (60px) =================
            _bottomPanel = new BufferedPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.FromArgb(9, 12, 18)
            };

            var openFolderRect = new Rectangle(836, 13, 122, 34);
            var viewLogRect = new Rectangle(744, 13, 84, 34);
            var cancelRect = new Rectangle(662, 13, 74, 34);

            bool hoverOpenFolder = false;
            bool hoverViewLog = false;
            bool hoverCancel = false;

            _txtLog = new TextBox
            {
                Location = new Point(22, 58),
                Size = new Size(936, 128),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(7, 9, 15),
                ForeColor = Color.FromArgb(145, 225, 175),
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                Text = "[Diablo 3 Forever] Ready. Click 'Install' on either tile (defaults to newest version) or expand 'View More' to choose a build.\r\n"
            };
            _bottomPanel.Controls.Add(_txtLog);

            _bottomPanel.Paint += (_, e) =>
            {
                ConfigureGraphics(e.Graphics);

                using (var topLinePen = new Pen(Color.FromArgb(32, 40, 58), 1f))
                {
                    e.Graphics.DrawLine(topLinePen, 0, 0, _bottomPanel.Width, 0);
                }

                int barRight = _isInstalling ? 646 : 728;
                int barWidth = barRight - 22;

                using (var stFont = new Font("Segoe UI", 8.75f, FontStyle.Bold))
                using (var stBrush = new SolidBrush(Color.FromArgb(213, 222, 239)))
                using (var pctBrush = new SolidBrush(Color.FromArgb(88, 166, 255)))
                {
                    e.Graphics.DrawString(_statusText, stFont, stBrush, new RectangleF(22, 12, barWidth - 55, 18));
                    var sfRight = new StringFormat { Alignment = StringAlignment.Far };
                    e.Graphics.DrawString($"{_progressPercent:F0}%", stFont, pctBrush, new RectangleF(barRight - 55, 12, 55, 18), sfRight);
                }

                var trackRect = new Rectangle(22, 34, barWidth, 7);
                using (var trackPath = CreateRoundRect(trackRect, 3))
                using (var trackBrush = new SolidBrush(Color.FromArgb(23, 29, 43)))
                {
                    e.Graphics.FillPath(trackBrush, trackPath);
                }

                int fillW = (int)Math.Round(barWidth * (Math.Clamp(_progressPercent, 0.0, 100.0) / 100.0));
                if (fillW > 4)
                {
                    var fillRect = new Rectangle(22, 34, fillW, 7);
                    using var fillPath = CreateRoundRect(fillRect, 3);
                    using var gradBrush = new LinearGradientBrush(
                        fillRect,
                        Color.FromArgb(0, 116, 224),
                        Color.FromArgb(0, 198, 255),
                        LinearGradientMode.Horizontal);
                    e.Graphics.FillPath(gradBrush, fillPath);
                }

                var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var btnFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);

                if (_isInstalling)
                {
                    using var cPath = CreateRoundRect(cancelRect, 5);
                    using var cFill = new SolidBrush(hoverCancel ? Color.FromArgb(145, 38, 46) : Color.FromArgb(110, 28, 35));
                    using var cPen = new Pen(Color.FromArgb(175, 65, 75), 1f);
                    e.Graphics.FillPath(cFill, cPath);
                    e.Graphics.DrawPath(cPen, cPath);
                    e.Graphics.DrawString("Cancel", btnFont, Brushes.White, cancelRect, sfCenter);
                }

                using (var lPath = CreateRoundRect(viewLogRect, 5))
                using (var lFill = new SolidBrush(hoverViewLog ? Color.FromArgb(34, 44, 66) : Color.FromArgb(22, 29, 44)))
                using (var lPen = new Pen(Color.FromArgb(42, 54, 78), 1f))
                using (var lText = new SolidBrush(Color.FromArgb(205, 215, 232)))
                {
                    e.Graphics.FillPath(lFill, lPath);
                    e.Graphics.DrawPath(lPen, lPath);
                    e.Graphics.DrawString(_logExpanded ? "Hide Log" : "View Log", btnFont, lText,
                        new Rectangle(viewLogRect.X, viewLogRect.Y, viewLogRect.Width - 14, viewLogRect.Height), sfCenter);
                    DrawChevron(e.Graphics, new Point(viewLogRect.Right - 14, viewLogRect.Y + 17), !_logExpanded, Color.FromArgb(205, 215, 232));
                }

                using (var oPath = CreateRoundRect(openFolderRect, 5))
                using (var oFill = new SolidBrush(hoverOpenFolder ? Color.FromArgb(38, 49, 73) : Color.FromArgb(28, 36, 54)))
                using (var oPen = new Pen(Color.FromArgb(46, 58, 84), 1f))
                using (var oText = new SolidBrush(Color.FromArgb(220, 228, 242)))
                {
                    e.Graphics.FillPath(oFill, oPath);
                    e.Graphics.DrawPath(oPen, oPath);
                    e.Graphics.DrawString("Open Folder →", btnFont, oText, openFolderRect, sfCenter);
                }

                using var framePen = new Pen(Color.FromArgb(38, 48, 68), 1);
                e.Graphics.DrawLine(framePen, 0, 0, 0, _bottomPanel.Height - 1);
                e.Graphics.DrawLine(framePen, _bottomPanel.Width - 1, 0, _bottomPanel.Width - 1, _bottomPanel.Height - 1);
                e.Graphics.DrawLine(framePen, 0, _bottomPanel.Height - 1, _bottomPanel.Width - 1, _bottomPanel.Height - 1);
            };

            _bottomPanel.MouseMove += (_, e) =>
            {
                bool ho = openFolderRect.Contains(e.Location);
                bool hl = viewLogRect.Contains(e.Location);
                bool hc = _isInstalling && cancelRect.Contains(e.Location);
                _bottomPanel.Cursor = (ho || hl || hc) ? Cursors.Hand : Cursors.Default;
                if (ho != hoverOpenFolder || hl != hoverViewLog || hc != hoverCancel)
                {
                    hoverOpenFolder = ho;
                    hoverViewLog = hl;
                    hoverCancel = hc;
                    _bottomPanel.Invalidate();
                }
            };
            _bottomPanel.MouseLeave += (_, _) =>
            {
                hoverOpenFolder = hoverViewLog = hoverCancel = false;
                _bottomPanel.Invalidate();
            };
            _bottomPanel.MouseClick += (_, e) =>
            {
                if (openFolderRect.Contains(e.Location))
                {
                    string dir = _txtTargetLocation.Text.Trim();
                    if (Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                    }
                    else
                    {
                        MessageBox.Show(this, $"Folder does not exist yet:\n{dir}", "Diablo 3 Forever",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else if (viewLogRect.Contains(e.Location))
                {
                    ToggleLogDrawer();
                }
                else if (_isInstalling && cancelRect.Contains(e.Location))
                {
                    _cts?.Cancel();
                }
            };

            // ================= 4. MAIN 2-TILE CONTAINER =================
            _mainScrollPanel = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(12, 15, 23)
            };
            _mainScrollPanel.Paint += (_, e) =>
            {
                using var framePen = new Pen(Color.FromArgb(38, 48, 68), 1);
                e.Graphics.DrawLine(framePen, 0, 0, 0, _mainScrollPanel.Height);
                e.Graphics.DrawLine(framePen, _mainScrollPanel.Width - 1, 0, _mainScrollPanel.Width - 1, _mainScrollPanel.Height);
            };

            // TILE 1: DIABLO III: REAPER OF SOULS
            _rosCardWrapper = new BufferedPanel
            {
                Location = new Point(22, 4),
                Size = new Size(936, BANNER_HEIGHT),
                BackColor = Color.FromArgb(12, 15, 23)
            };
            _rosBannerPanel = CreateHeroBannerPanel(isRos: true);
            _rosDrawer = BuildRosDrawer();
            _rosCardWrapper.Controls.Add(_rosBannerPanel);
            _rosCardWrapper.Controls.Add(_rosDrawer);
            _mainScrollPanel.Controls.Add(_rosCardWrapper);

            // TILE 2: DIABLO III: VANILLA
            _vanillaCardWrapper = new BufferedPanel
            {
                Location = new Point(22, 4 + BANNER_HEIGHT + 16),
                Size = new Size(936, BANNER_HEIGHT),
                BackColor = Color.FromArgb(12, 15, 23)
            };
            _vanillaBannerPanel = CreateHeroBannerPanel(isRos: false);
            _vanillaDrawer = BuildVanillaDrawer();
            _vanillaCardWrapper.Controls.Add(_vanillaBannerPanel);
            _vanillaCardWrapper.Controls.Add(_vanillaDrawer);
            _mainScrollPanel.Controls.Add(_vanillaCardWrapper);

            Controls.Add(_mainScrollPanel);
            Controls.Add(_locationBar);
            Controls.Add(_topToolbar);
            Controls.Add(_bottomPanel);
        }

        private Panel CreateHeroBannerPanel(bool isRos)
        {
            var banner = new BufferedPanel
            {
                Location = new Point(0, 0),
                Size = new Size(936, BANNER_HEIGHT),
                BackColor = Color.FromArgb(12, 15, 23)
            };

            var viewMoreRect = new Rectangle(16, BANNER_HEIGHT - 40, 238, 28);
            var verBoxRect = new Rectangle(646, 26, 264, 48);
            var installBtnRect = new Rectangle(646, 84, 264, 42);

            bool hoverViewMore = false;
            bool hoverVerBox = false;
            bool hoverInstall = false;

            banner.Paint += (_, e) =>
            {
                ConfigureGraphics(e.Graphics);

                var cardRect = new Rectangle(0, 0, banner.Width - 1, banner.Height - 1);
                using var cardPath = CreateRoundRect(cardRect, 8);

                var oldClip = e.Graphics.Clip;
                e.Graphics.SetClip(cardPath);

                Color rightBg = isRos ? Color.FromArgb(7, 11, 18) : Color.FromArgb(14, 8, 9);
                using (var baseBrush = new SolidBrush(rightBg))
                {
                    e.Graphics.FillRectangle(baseBrush, 0, 0, banner.Width, banner.Height);
                }

                Image art = isRos ? _rosBannerImg : _vanillaBannerImg;
                if (art != null)
                {
                    int artAreaWidth = 615;
                    float scale = Math.Max((float)artAreaWidth / art.Width, (float)banner.Height / art.Height);
                    int drawW = (int)Math.Round(art.Width * scale);
                    int drawH = (int)Math.Round(art.Height * scale);
                    int drawX = (artAreaWidth - drawW) / 2;
                    int drawY = (banner.Height - drawH) / 2;

                    e.Graphics.DrawImage(art, new Rectangle(drawX, drawY, drawW, drawH));

                    var fadeRect = new Rectangle(450, 0, 170, banner.Height);
                    using var fadeBrush = new LinearGradientBrush(
                        fadeRect,
                        Color.FromArgb(0, rightBg),
                        rightBg,
                        LinearGradientMode.Horizontal);
                    fadeBrush.WrapMode = WrapMode.TileFlipXY;
                    e.Graphics.FillRectangle(fadeBrush, fadeRect);
                }

                string badgeText = isRos ? "EXPANSION • 41 BUILDS" : "CLASSIC • 50 BUILDS";
                Color badgeColor = isRos ? Color.FromArgb(91, 215, 43) : Color.FromArgb(245, 158, 11);
                var badgeRect = new Rectangle(16, 12, isRos ? 152 : 138, 20);
                using (var bPath = CreateRoundRect(badgeRect, 3))
                using (var bFill = new SolidBrush(badgeColor))
                using (var bFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                {
                    e.Graphics.FillPath(bFill, bPath);
                    var sfBadge = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString(badgeText, bFont, Brushes.Black, badgeRect, sfBadge);
                }

                bool isExp = isRos ? _rosExpanded : _vanillaExpanded;
                int totalBuildCount = isRos
                    ? D3BuildCatalog.AllBuilds.Count(b => b.IsRoS)
                    : D3BuildCatalog.AllBuilds.Count(b => !b.IsRoS);
                string vmText = isExp
                    ? (isRos ? $"Hide {totalBuildCount} RoS Versions" : $"Hide {totalBuildCount} Vanilla Versions")
                    : $"View More (Choose from {totalBuildCount} Versions)";

                using (var vmPath = CreateRoundRect(viewMoreRect, 4))
                using (var vmFill = new SolidBrush(hoverViewMore ? Color.FromArgb(230, 24, 34, 52) : Color.FromArgb(215, 12, 16, 26)))
                using (var vmPen = new Pen(hoverViewMore ? Color.FromArgb(88, 166, 255) : Color.FromArgb(65, 255, 255, 255), 1f))
                using (var vmFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                using (var vmTextBrush = new SolidBrush(hoverViewMore ? Color.White : Color.FromArgb(220, 228, 242)))
                {
                    e.Graphics.FillPath(vmFill, vmPath);
                    e.Graphics.DrawPath(vmPen, vmPath);
                    DrawChevron(e.Graphics, new Point(viewMoreRect.X + 16, viewMoreRect.Y + 14), isExp, hoverViewMore ? Color.White : Color.FromArgb(220, 228, 242));
                    var sfVm = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString(vmText, vmFont, vmTextBrush, new Rectangle(viewMoreRect.X + 14, viewMoreRect.Y, viewMoreRect.Width - 18, viewMoreRect.Height), sfVm);
                }

                using (var vbPath = CreateRoundRect(verBoxRect, 5))
                using (var vbFill = new SolidBrush(Color.FromArgb(12, 16, 25)))
                using (var vbPen = new Pen(hoverVerBox ? Color.FromArgb(79, 96, 130) : Color.FromArgb(46, 56, 77), 1f))
                {
                    e.Graphics.FillPath(vbFill, vbPath);
                    e.Graphics.DrawPath(vbPen, vbPath);
                }

                using (var capFont = new Font("Segoe UI", 7.25f, FontStyle.Bold))
                using (var capBrush = new SolidBrush(Color.FromArgb(139, 149, 171)))
                {
                    e.Graphics.DrawString("VERSION (DEFAULTS TO NEWEST)", capFont, capBrush, verBoxRect.X + 11, verBoxRect.Y + 7);
                }

                D3BuildCatalogItem curBuild = isRos ? _selectedRosBuild : _selectedVanillaBuild;
                string verLabel = isRos
                    ? $"{curBuild.Version}.{curBuild.BuildNumber} ({(curBuild.BuildNumber == 99920 ? "Newest Live" : curBuild.ProductCode.ToUpperInvariant())})"
                    : $"{curBuild.Version}.{curBuild.BuildNumber}{(curBuild.BuildNumber == 16603 ? " (Newest Vanilla)" : "")}";

                using (var valFont = new Font("Segoe UI", 9.25f, FontStyle.Bold))
                {
                    e.Graphics.DrawString(verLabel, valFont, Brushes.White, verBoxRect.X + 11, verBoxRect.Y + 23);
                }

                // Draw breathing status indicator in the version box
                CdnAvailability curStatus = _cdnStatuses.TryGetValue(GetBuildKey(curBuild), out var st) ? st : CdnAvailability.Checking;
                DrawStatusPill(e.Graphics, new Point(verBoxRect.Right - 84, verBoxRect.Y + 16), curStatus);

                DrawChevron(e.Graphics, new Point(verBoxRect.Right - 16, verBoxRect.Y + 24), isExp, Color.FromArgb(148, 163, 184));

                var glowRect = new Rectangle(installBtnRect.X, installBtnRect.Y + 3, installBtnRect.Width, installBtnRect.Height);
                using (var gPath = CreateRoundRect(glowRect, 6))
                using (var gFill = new SolidBrush(Color.FromArgb(65, 0, 116, 224)))
                {
                    e.Graphics.FillPath(gFill, gPath);
                }

                Color btnColor = _isInstalling
                    ? Color.FromArgb(35, 65, 105)
                    : (hoverInstall ? Color.FromArgb(26, 136, 240) : Color.FromArgb(0, 116, 224));

                using (var iPath = CreateRoundRect(installBtnRect, 5))
                using (var iFill = new SolidBrush(btnColor))
                using (var iPen = new Pen(Color.FromArgb(60, 160, 255), 1f))
                using (var iFont = new Font("Segoe UI", 10.75f, FontStyle.Bold))
                {
                    e.Graphics.FillPath(iFill, iPath);
                    e.Graphics.DrawPath(iPen, iPath);
                    var sfInst = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString("Install", iFont, Brushes.White, installBtnRect, sfInst);
                }

                string subNote = isRos
                    ? "Installs directly into Target Location above"
                    : "Defaults to 1.0.8a unless changed in View More";
                using (var noteFont = new Font("Segoe UI", 8f, FontStyle.Regular))
                using (var noteBrush = new SolidBrush(Color.FromArgb(147, 159, 184)))
                {
                    var sfNote = new StringFormat { Alignment = StringAlignment.Center };
                    e.Graphics.DrawString(subNote, noteFont, noteBrush, new RectangleF(installBtnRect.X, installBtnRect.Bottom + 8, installBtnRect.Width, 18), sfNote);
                }

                e.Graphics.Clip = oldClip;

                using var borderPen = new Pen(Color.FromArgb(40, 52, 76), 1f);
                e.Graphics.DrawPath(borderPen, cardPath);
            };

            banner.MouseMove += (_, e) =>
            {
                bool hvm = viewMoreRect.Contains(e.Location);
                bool hvb = verBoxRect.Contains(e.Location);
                bool hin = !_isInstalling && installBtnRect.Contains(e.Location);
                banner.Cursor = (hvm || hvb || hin) ? Cursors.Hand : Cursors.Default;
                if (hvm != hoverViewMore || hvb != hoverVerBox || hin != hoverInstall)
                {
                    hoverViewMore = hvm;
                    hoverVerBox = hvb;
                    hoverInstall = hin;
                    banner.Invalidate();
                }
            };
            banner.MouseLeave += (_, _) =>
            {
                hoverViewMore = hoverVerBox = hoverInstall = false;
                banner.Invalidate();
            };
            banner.MouseClick += async (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;

                if (viewMoreRect.Contains(e.Location) || verBoxRect.Contains(e.Location))
                {
                    if (isRos) ToggleRosDrawer();
                    else ToggleVanillaDrawer();
                }
                else if (!_isInstalling && installBtnRect.Contains(e.Location))
                {
                    await StartTileInstallAsync(isRos);
                }
            };

            return banner;
        }

        private Panel BuildRosDrawer()
        {
            var drawer = new BufferedPanel
            {
                Location = new Point(0, BANNER_HEIGHT),
                Size = new Size(936, 450),
                BackColor = Color.FromArgb(11, 14, 22),
                Visible = false
            };

            var binToggleRect = new Rectangle(648, 10, 268, 22);
            var tabRetailRect = new Rectangle(20, 36, 150, 32);
            var tabPtrRect = new Rectangle(176, 36, 186, 32);
            var tabBetaRect = new Rectangle(368, 36, 156, 32);

            _rosChipsContainer = new BufferedPanel
            {
                Location = new Point(0, 76),
                Size = new Size(936, 360),
                BackColor = Color.Transparent
            };
            drawer.Controls.Add(_rosChipsContainer);

            void PopulateRosChips()
            {
                _rosChipsContainer.Controls.Clear();
                _rosVersionChips.Clear();

                var filtered = D3BuildCatalog.AllBuilds.Where(b => b.IsRoS && b.Category == _rosCategory).ToList();
                int rows = Math.Max(1, (int)Math.Ceiling(filtered.Count / 3.0));
                int containerHeight = rows * 52 + 8;
                _rosChipsContainer.Height = containerHeight;
                int drawerHeight = 84 + containerHeight;
                drawer.Height = drawerHeight;
                if (_rosExpanded)
                {
                    _rosCardWrapper.Height = BANNER_HEIGHT + drawerHeight;
                    ReflowTiles();
                }

                int colWidth = 292;
                int rowHeight = 46;

                for (int i = 0; i < filtered.Count; i++)
                {
                    var item = filtered[i];
                    int col = i % 3;
                    int row = i / 3;
                    string badge = (i == 0 && _rosCategory == D3BuildCategory.Retail)
                        ? "LIVE"
                        : (item.IsCasc ? item.ProductCode.ToUpperInvariant()
                        : (item.BuildNumber == 20813 ? "BASE"
                        : (item.Category == D3BuildCategory.Beta ? "BETA"
                        : (item.Category == D3BuildCategory.Ptr ? "PTR" : "MPQ"))));

                    var chip = CreateVersionChip(
                        item,
                        badge,
                        new Point(20 + col * (colWidth + 9), row * (rowHeight + 6)),
                        new Size(colWidth, rowHeight),
                        isSelected: () => ReferenceEquals(item, _selectedRosBuild),
                        onClick: () =>
                        {
                            _selectedRosBuild = item;
                            if (!_userEditedTargetDir)
                            {
                                _txtTargetLocation.Text = item.BuildNumber == 99920
                                    ? @"C:\Games\Diablo III"
                                    : $@"C:\Games\Diablo III ({item.Version}.{item.BuildNumber})";
                            }
                            foreach (var c in _rosVersionChips) c.Invalidate();
                            _rosBannerPanel.Invalidate();
                        });

                    _rosVersionChips.Add(chip);
                    _rosChipsContainer.Controls.Add(chip);
                }
            }

            int countRetail = D3BuildCatalog.AllBuilds.Count(b => b.IsRoS && b.Category == D3BuildCategory.Retail);
            int countPtr = D3BuildCatalog.AllBuilds.Count(b => b.IsRoS && b.Category == D3BuildCategory.Ptr);
            int countBeta = D3BuildCatalog.AllBuilds.Count(b => b.IsRoS && b.Category == D3BuildCategory.Beta);

            drawer.Paint += (_, e) =>
            {
                ConfigureGraphics(e.Graphics);
                using var pen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(pen, 0, 0, drawer.Width - 1, drawer.Height - 1);

                using var hdrFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
                using var hdrBrush = new SolidBrush(Color.FromArgb(154, 166, 191));
                e.Graphics.DrawString("CHOOSE REAPER OF SOULS BUILD (41 BUILDS ARCHIVED):", hdrFont, hdrBrush, 20, 13);

                DrawModernCheckBox(e.Graphics, binToggleRect.Location, _chkRosBinariesOnly,
                    "Binaries Only (~35 MB Fast Patch)", Color.FromArgb(126, 231, 135));

                // Draw Segmented Tabs
                DrawDrawerTab(e.Graphics, tabRetailRect, $"Retail ({countRetail})", _rosCategory == D3BuildCategory.Retail, Color.FromArgb(59, 130, 246));
                DrawDrawerTab(e.Graphics, tabPtrRect, $"Public Test Realm ({countPtr})", _rosCategory == D3BuildCategory.Ptr, Color.FromArgb(168, 85, 247));
                DrawDrawerTab(e.Graphics, tabBetaRect, $"Closed Beta ({countBeta})", _rosCategory == D3BuildCategory.Beta, Color.FromArgb(234, 179, 8));
            };

            drawer.MouseMove += (_, e) =>
            {
                bool hover = binToggleRect.Contains(e.Location) || tabRetailRect.Contains(e.Location) || tabPtrRect.Contains(e.Location) || tabBetaRect.Contains(e.Location);
                drawer.Cursor = hover ? Cursors.Hand : Cursors.Default;
            };

            drawer.MouseClick += (_, e) =>
            {
                if (binToggleRect.Contains(e.Location))
                {
                    _chkRosBinariesOnly = !_chkRosBinariesOnly;
                    drawer.Invalidate();
                }
                else if (tabRetailRect.Contains(e.Location) && _rosCategory != D3BuildCategory.Retail)
                {
                    _rosCategory = D3BuildCategory.Retail;
                    PopulateRosChips();
                    drawer.Invalidate();
                }
                else if (tabPtrRect.Contains(e.Location) && _rosCategory != D3BuildCategory.Ptr)
                {
                    _rosCategory = D3BuildCategory.Ptr;
                    PopulateRosChips();
                    drawer.Invalidate();
                }
                else if (tabBetaRect.Contains(e.Location) && _rosCategory != D3BuildCategory.Beta)
                {
                    _rosCategory = D3BuildCategory.Beta;
                    PopulateRosChips();
                    drawer.Invalidate();
                }
            };

            PopulateRosChips();
            return drawer;
        }

        private Panel BuildVanillaDrawer()
        {
            var drawer = new BufferedPanel
            {
                Location = new Point(0, BANNER_HEIGHT),
                Size = new Size(936, 450),
                BackColor = Color.FromArgb(11, 14, 22),
                Visible = false
            };

            var binToggleRect = new Rectangle(648, 10, 268, 22);
            var tabRetailRect = new Rectangle(20, 36, 150, 32);
            var tabPtrRect = new Rectangle(176, 36, 186, 32);
            var tabBetaRect = new Rectangle(368, 36, 156, 32);

            _vanillaChipsContainer = new BufferedPanel
            {
                Location = new Point(0, 76),
                Size = new Size(936, 360),
                BackColor = Color.Transparent
            };
            drawer.Controls.Add(_vanillaChipsContainer);

            void PopulateVanillaChips()
            {
                _vanillaChipsContainer.Controls.Clear();
                _vanillaVersionChips.Clear();

                var filtered = D3BuildCatalog.AllBuilds.Where(b => !b.IsRoS && b.Category == _vanillaCategory).ToList();
                int rows = Math.Max(1, (int)Math.Ceiling(filtered.Count / 3.0));
                int containerHeight = rows * 52 + 8;
                _vanillaChipsContainer.Height = containerHeight;
                int drawerHeight = 84 + containerHeight;
                drawer.Height = drawerHeight;
                if (_vanillaExpanded)
                {
                    _vanillaCardWrapper.Height = BANNER_HEIGHT + drawerHeight;
                    ReflowTiles();
                }

                int colWidth = 292;
                int rowHeight = 46;

                for (int i = 0; i < filtered.Count; i++)
                {
                    var item = filtered[i];
                    int col = i % 3;
                    int row = i / 3;
                    string badge = (i == 0 && _vanillaCategory == D3BuildCategory.Retail)
                        ? "NEWEST"
                        : (item.BuildNumber == 8370 ? "BASE"
                        : (item.Category == D3BuildCategory.Beta ? "BETA"
                        : (item.Category == D3BuildCategory.Ptr ? "PTR" : "MPQ")));

                    var chip = CreateVersionChip(
                        item,
                        badge,
                        new Point(20 + col * (colWidth + 9), row * (rowHeight + 6)),
                        new Size(colWidth, rowHeight),
                        isSelected: () => ReferenceEquals(item, _selectedVanillaBuild),
                        onClick: () =>
                        {
                            _selectedVanillaBuild = item;
                            if (!_userEditedTargetDir)
                            {
                                _txtTargetLocation.Text = $@"C:\Games\Diablo III ({item.Version}.{item.BuildNumber})";
                            }
                            foreach (var c in _vanillaVersionChips) c.Invalidate();
                            _vanillaBannerPanel.Invalidate();
                        });

                    _vanillaVersionChips.Add(chip);
                    _vanillaChipsContainer.Controls.Add(chip);
                }
            }

            int countRetail = D3BuildCatalog.AllBuilds.Count(b => !b.IsRoS && b.Category == D3BuildCategory.Retail);
            int countPtr = D3BuildCatalog.AllBuilds.Count(b => !b.IsRoS && b.Category == D3BuildCategory.Ptr);
            int countBeta = D3BuildCatalog.AllBuilds.Count(b => !b.IsRoS && b.Category == D3BuildCategory.Beta);

            drawer.Paint += (_, e) =>
            {
                ConfigureGraphics(e.Graphics);
                using var pen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(pen, 0, 0, drawer.Width - 1, drawer.Height - 1);

                using var hdrFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
                using var hdrBrush = new SolidBrush(Color.FromArgb(154, 166, 191));
                e.Graphics.DrawString("CHOOSE VANILLA CLASSIC BUILD (50 BUILDS ARCHIVED):", hdrFont, hdrBrush, 20, 13);

                DrawModernCheckBox(e.Graphics, binToggleRect.Location, _chkVanillaBinariesOnly,
                    "Binaries Only (~35 MB Fast Patch)", Color.FromArgb(126, 231, 135));

                // Draw Segmented Tabs
                DrawDrawerTab(e.Graphics, tabRetailRect, $"Retail ({countRetail})", _vanillaCategory == D3BuildCategory.Retail, Color.FromArgb(59, 130, 246));
                DrawDrawerTab(e.Graphics, tabPtrRect, $"Public Test Realm ({countPtr})", _vanillaCategory == D3BuildCategory.Ptr, Color.FromArgb(168, 85, 247));
                DrawDrawerTab(e.Graphics, tabBetaRect, $"Closed Beta ({countBeta})", _vanillaCategory == D3BuildCategory.Beta, Color.FromArgb(234, 179, 8));
            };

            drawer.MouseMove += (_, e) =>
            {
                bool hover = binToggleRect.Contains(e.Location) || tabRetailRect.Contains(e.Location) || tabPtrRect.Contains(e.Location) || tabBetaRect.Contains(e.Location);
                drawer.Cursor = hover ? Cursors.Hand : Cursors.Default;
            };

            drawer.MouseClick += (_, e) =>
            {
                if (binToggleRect.Contains(e.Location))
                {
                    _chkVanillaBinariesOnly = !_chkVanillaBinariesOnly;
                    drawer.Invalidate();
                }
                else if (tabRetailRect.Contains(e.Location) && _vanillaCategory != D3BuildCategory.Retail)
                {
                    _vanillaCategory = D3BuildCategory.Retail;
                    PopulateVanillaChips();
                    drawer.Invalidate();
                }
                else if (tabPtrRect.Contains(e.Location) && _vanillaCategory != D3BuildCategory.Ptr)
                {
                    _vanillaCategory = D3BuildCategory.Ptr;
                    PopulateVanillaChips();
                    drawer.Invalidate();
                }
                else if (tabBetaRect.Contains(e.Location) && _vanillaCategory != D3BuildCategory.Beta)
                {
                    _vanillaCategory = D3BuildCategory.Beta;
                    PopulateVanillaChips();
                    drawer.Invalidate();
                }
            };

            PopulateVanillaChips();
            return drawer;
        }

        private static void DrawDrawerTab(Graphics g, Rectangle rect, string text, bool isSelected, Color activeAccent)
        {
            using var path = CreateRoundRect(rect, 4);
            using var fill = new SolidBrush(isSelected ? Color.FromArgb(28, 38, 56) : Color.FromArgb(14, 18, 26));
            using var pen = new Pen(isSelected ? activeAccent : Color.FromArgb(36, 45, 66), isSelected ? 1.5f : 1f);

            g.FillPath(fill, path);
            g.DrawPath(pen, path);

            // Draw crisp colored vector dot indicator
            using (var dotBrush = new SolidBrush(isSelected ? activeAccent : Color.FromArgb(100, 116, 139)))
            {
                g.FillEllipse(dotBrush, rect.X + 12, rect.Y + (rect.Height - 6) / 2, 6, 6);
            }

            using var font = new Font("Segoe UI", 8.25f, isSelected ? FontStyle.Bold : FontStyle.Regular);
            using var brush = new SolidBrush(isSelected ? Color.White : Color.FromArgb(148, 163, 184));
            var textRect = new Rectangle(rect.X + 22, rect.Y, rect.Width - 26, rect.Height);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, textRect, sf);
        }

        private Panel CreateVersionChip(
            D3BuildCatalogItem item,
            string badgeText,
            Point location,
            Size size,
            Func<bool> isSelected,
            Action onClick)
        {
            var chip = new BufferedPanel
            {
                Location = location,
                Size = size,
                BackColor = Color.FromArgb(11, 14, 22),
                Cursor = Cursors.Hand
            };
            bool hover = false;

            chip.Paint += (_, e) =>
            {
                ConfigureGraphics(e.Graphics);
                bool sel = isSelected();
                var r = new Rectangle(0, 0, chip.Width - 1, chip.Height - 1);
                using var path = CreateRoundRect(r, 5);
                using var fill = new SolidBrush(sel
                    ? Color.FromArgb(18, 42, 74)
                    : (hover ? Color.FromArgb(23, 30, 46) : Color.FromArgb(19, 24, 37)));
                using var pen = new Pen(sel ? Color.FromArgb(0, 132, 255) : (hover ? Color.FromArgb(65, 81, 115) : Color.FromArgb(37, 46, 66)), 1f);

                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);

                using var topFont = new Font("Segoe UI", 8.75f, FontStyle.Bold);
                e.Graphics.DrawString($"{item.Version}.{item.BuildNumber}", topFont, Brushes.White, 10, 6);

                // Draw Per-File Live Status Pill
                CdnAvailability status = _cdnStatuses.TryGetValue(GetBuildKey(item), out var s) ? s : CdnAvailability.Checking;
                DrawStatusPill(e.Graphics, new Point(chip.Width - 118, 7), status);

                var tagRect = new Rectangle(chip.Width - 52, 7, 44, 16);
                using var tagPath = CreateRoundRect(tagRect, 3);
                using var tagFill = new SolidBrush(sel ? Color.FromArgb(0, 116, 224) : Color.FromArgb(35, 44, 63));
                using var tagFont = new Font("Segoe UI", 7f, FontStyle.Bold);
                using var tagTextBrush = new SolidBrush(sel ? Color.White : Color.FromArgb(154, 176, 212));
                e.Graphics.FillPath(tagFill, tagPath);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(badgeText, tagFont, tagTextBrush, tagRect, sf);

                using var subFont = new Font("Segoe UI", 7.75f, FontStyle.Regular);
                using var subBrush = new SolidBrush(Color.FromArgb(135, 147, 171));
                e.Graphics.DrawString(item.Summary, subFont, subBrush, new RectangleF(10, 24, chip.Width - 18, 18));
            };

            chip.MouseEnter += (_, _) => { hover = true; chip.Invalidate(); };
            chip.MouseLeave += (_, _) => { hover = false; chip.Invalidate(); };
            chip.MouseClick += (_, _) => onClick();

            return chip;
        }

        private void DrawStatusPill(Graphics g, Point location, CdnAvailability status)
        {
            var pillRect = new Rectangle(location.X, location.Y, 60, 16);
            using var pillPath = CreateRoundRect(pillRect, 3);

            Color bg, border, text, dot;
            string label;

            switch (status)
            {
                case CdnAvailability.Ok:
                    bg = Color.FromArgb(40, 46, 160, 67);
                    border = Color.FromArgb(120, 63, 185, 80);
                    text = Color.FromArgb(86, 211, 100);
                    dot = Color.FromArgb(63, 185, 80);
                    label = "OK";
                    break;
                case CdnAvailability.Offline:
                    bg = Color.FromArgb(45, 248, 81, 73);
                    border = Color.FromArgb(120, 248, 81, 73);
                    text = Color.FromArgb(255, 123, 114);
                    dot = Color.FromArgb(248, 81, 73);
                    label = "OFFLINE";
                    break;
                default: // Checking
                    bg = Color.FromArgb(40, 210, 153, 34);
                    border = Color.FromArgb(120, 210, 153, 34);
                    text = Color.FromArgb(227, 179, 65);
                    dot = Color.FromArgb(210, 153, 34);
                    label = "CHECKING";
                    break;
            }

            using (var bFill = new SolidBrush(bg))
            using (var bPen = new Pen(border, 1f))
            {
                g.FillPath(bFill, pillPath);
                g.DrawPath(bPen, pillPath);
            }

            // Breathing / pulsing dot calculation
            float breathe = 0.5f + 0.5f * (float)Math.Sin(_pulsePhase);
            int dotAlpha = (int)(110 + 145 * breathe);
            using (var dBrush = new SolidBrush(Color.FromArgb(dotAlpha, dot)))
            {
                g.FillEllipse(dBrush, location.X + 6, location.Y + 5, 6, 6);
            }

            // Glow around dot if OK
            if (status == CdnAvailability.Ok && breathe > 0.3f)
            {
                int glowAlpha = (int)(70 * breathe);
                using var gBrush = new SolidBrush(Color.FromArgb(glowAlpha, dot));
                g.FillEllipse(gBrush, location.X + 4, location.Y + 3, 10, 10);
            }

            using (var fFont = new Font("Segoe UI", 6.75f, FontStyle.Bold))
            using (var fBrush = new SolidBrush(text))
            {
                var textRect = new Rectangle(location.X + 13, location.Y, pillRect.Width - 14, pillRect.Height);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, fFont, fBrush, textRect, sf);
            }
        }

        private async Task ProbeAllBuildsAsync()
        {
            foreach (var build in D3BuildCatalog.AllBuilds)
            {
                _ = Task.Run(async () =>
                {
                    bool isOk = false;
                    try
                    {
                        string url = build.CheckUrl;
                        var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                        var res = await _probeClient.SendAsync(req);
                        isOk = res.IsSuccessStatusCode;
                    }
                    catch
                    {
                        isOk = false;
                    }

                    _cdnStatuses[GetBuildKey(build)] = isOk ? CdnAvailability.Ok : CdnAvailability.Offline;
                });
            }
            await Task.CompletedTask;
        }

        private void ShowLocaleMenu(Control parent, Point location)
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(16, 21, 32),
                ForeColor = Color.White,
                ShowImageMargin = false
            };
            foreach (var loc in Locales)
            {
                var item = new ToolStripMenuItem(loc.Display)
                {
                    BackColor = Color.FromArgb(16, 21, 32),
                    ForeColor = loc.Code == _selectedLocaleCode ? Color.FromArgb(88, 166, 255) : Color.White
                };
                item.Click += (_, _) =>
                {
                    _selectedLocaleCode = loc.Code;
                    _selectedLocaleDisplay = loc.Display;
                    parent.Invalidate();
                };
                menu.Items.Add(item);
            }
            menu.Show(parent, location);
        }

        private void ToggleRosDrawer()
        {
            _rosExpanded = !_rosExpanded;
            _rosDrawer.Visible = _rosExpanded;
            _rosCardWrapper.Height = _rosExpanded ? (BANNER_HEIGHT + _rosDrawer.Height) : BANNER_HEIGHT;
            _rosBannerPanel.Invalidate();
            ReflowTiles();
        }

        private void ToggleVanillaDrawer()
        {
            _vanillaExpanded = !_vanillaExpanded;
            _vanillaDrawer.Visible = _vanillaExpanded;
            _vanillaCardWrapper.Height = _vanillaExpanded ? (BANNER_HEIGHT + _vanillaDrawer.Height) : BANNER_HEIGHT;
            _vanillaBannerPanel.Invalidate();
            ReflowTiles();
        }

        private void ReflowTiles()
        {
            _vanillaCardWrapper.Top = _rosCardWrapper.Bottom + 16;
            _mainScrollPanel.AutoScrollMinSize = new Size(0, _vanillaCardWrapper.Bottom + 20);
        }

        private void ToggleLogDrawer()
        {
            _logExpanded = !_logExpanded;
            _txtLog.Visible = _logExpanded;
            _bottomPanel.Height = _logExpanded ? 196 : 60;
            _bottomPanel.Invalidate();
        }

        public void CaptureSnapshot(string outPath, bool expandRos, bool expandVanilla)
        {
            if (expandRos && !_rosExpanded) ToggleRosDrawer();
            if (expandVanilla && !_vanillaExpanded) ToggleVanillaDrawer();
            ReflowTiles();
            for (int i = 0; i < 20; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(100);
            }

            using var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        private async Task StartTileInstallAsync(bool isRos)
        {
            D3BuildCatalogItem selectedBuild = isRos ? _selectedRosBuild : _selectedVanillaBuild;

            if (!_userEditedTargetDir)
            {
                _txtTargetLocation.Text = isRos && selectedBuild.BuildNumber == 99920
                    ? @"C:\Games\Diablo III"
                    : $@"C:\Games\Diablo III ({selectedBuild.Version}.{selectedBuild.BuildNumber})";
            }

            string targetDir = Path.GetFullPath(_txtTargetLocation.Text.Trim());
            bool binariesOnly = isRos ? _chkRosBinariesOnly : _chkVanillaBinariesOnly;

            _isInstalling = true;
            _rosBannerPanel.Invalidate();
            _vanillaBannerPanel.Invalidate();
            _bottomPanel.Invalidate();
            _txtLog.Clear();

            AppendLog($"=== Diablo 3 Forever: Installing {selectedBuild.Version}.{selectedBuild.BuildNumber} ({selectedBuild.ProductCode}) ===");
            AppendLog($"Target Location: {targetDir}");
            UpdateProgress(0, $"Installing {selectedBuild.Version}.{selectedBuild.BuildNumber} -> {targetDir}");

            _cts = new CancellationTokenSource();
            try
            {
                int rc = await Task.Run(() => Program.ExecuteBuildPipelineAsync(
                    targetDir: targetDir,
                    selectedBuild: selectedBuild,
                    region: "us",
                    locale: _selectedLocaleCode,
                    maxConcurrency: 16,
                    dryRunLimit: -1,
                    binariesOnly: binariesOnly,
                    installPrereqs: _chkPrereqs,
                    configureShortcutFirewall: _chkShortcutFirewall,
                    log: AppendLog,
                    onProgress: UpdateProgress,
                    ct: _cts.Token));

                if (rc == 0)
                {
                    UpdateProgress(100, $"Completed: Diablo III {selectedBuild.Version}.{selectedBuild.BuildNumber} ready in {targetDir}");
                    MessageBox.Show(
                        this,
                        $"Diablo III {selectedBuild.Version}.{selectedBuild.BuildNumber} is installed and ready in:\n\n{targetDir}",
                        "Diablo 3 Forever — Installation Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    UpdateProgress(0, "Failed — click 'View Log' for details.");
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("Canceled by user.");
                UpdateProgress(0, "Installation canceled.");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] {ex.Message}");
                UpdateProgress(0, $"Error: {ex.Message}");
            }
            finally
            {
                _isInstalling = false;
                _rosBannerPanel.Invalidate();
                _vanillaBannerPanel.Invalidate();
                _bottomPanel.Invalidate();
            }
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        private void UpdateProgress(double percent, string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<double, string>(UpdateProgress), percent, status);
                return;
            }
            _progressPercent = Math.Clamp(percent, 0.0, 100.0);
            if (!string.IsNullOrWhiteSpace(status))
            {
                _statusText = status;
            }
            _bottomPanel.Invalidate();
        }

        private static void ConfigureGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        private static void DrawModernCheckBox(Graphics g, Point pt, bool isChecked, string label, Color? customTextColor = null)
        {
            var boxRect = new Rectangle(pt.X, pt.Y + 2, 15, 15);
            using (var path = CreateRoundRect(boxRect, 3))
            using (var fill = new SolidBrush(isChecked ? Color.FromArgb(0, 116, 224) : Color.FromArgb(20, 26, 38)))
            using (var pen = new Pen(isChecked ? Color.FromArgb(56, 155, 255) : Color.FromArgb(62, 76, 104), 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            if (isChecked)
            {
                using var checkPen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(checkPen, new[]
                {
                    new Point(boxRect.X + 3, boxRect.Y + 8),
                    new Point(boxRect.X + 6, boxRect.Y + 11),
                    new Point(boxRect.X + 12, boxRect.Y + 4)
                });
            }

            using var font = new Font("Segoe UI", 8.5f, customTextColor.HasValue ? FontStyle.Bold : FontStyle.Regular);
            using var brush = new SolidBrush(customTextColor ?? Color.FromArgb(184, 194, 216));
            g.DrawString(label, font, brush, pt.X + 20, pt.Y + 1);
        }

        private static void DrawMinimizeButton(Graphics g, Rectangle rect, bool hover)
        {
            if (hover)
            {
                using var path = CreateRoundRect(rect, 4);
                using var fill = new SolidBrush(Color.FromArgb(35, 44, 62));
                g.FillPath(fill, path);
            }
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            using var pen = new Pen(hover ? Color.White : Color.FromArgb(160, 172, 194), 1.6f);
            g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
        }

        private static void DrawCloseButton(Graphics g, Rectangle rect, bool hover)
        {
            if (hover)
            {
                using var path = CreateRoundRect(rect, 4);
                using var fill = new SolidBrush(Color.FromArgb(232, 17, 35));
                g.FillPath(fill, path);
            }
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            using var pen = new Pen(hover ? Color.White : Color.FromArgb(160, 172, 194), 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }

        private static void DrawChevron(Graphics g, Point center, bool pointUp, Color color)
        {
            using var pen = new Pen(color, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            if (pointUp)
            {
                g.DrawLines(pen, new[]
                {
                    new Point(center.X - 4, center.Y + 2),
                    new Point(center.X, center.Y - 2),
                    new Point(center.X + 4, center.Y + 2)
                });
            }
            else
            {
                g.DrawLines(pen, new[]
                {
                    new Point(center.X - 4, center.Y - 2),
                    new Point(center.X, center.Y + 2),
                    new Point(center.X + 4, center.Y - 2)
                });
            }
        }

        private static GraphicsPath CreateRoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || r.Width <= 0 || r.Height <= 0)
            {
                if (r.Width > 0 && r.Height > 0) path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Image LoadEmbeddedImage(string logicalName)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName);
                if (stream == null) return null;
                using var temp = Image.FromStream(stream);
                return new Bitmap(temp);
            }
            catch
            {
                return null;
            }
        }

        private static Icon LoadEmbeddedIcon(string logicalName)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName);
                return stream != null ? new Icon(stream) : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task CheckForAppUpdateAsync()
        {
            try
            {
                var info = await UpdateChecker.CheckForUpdatesAsync();
                if (info.HasUpdate)
                {
                    _availableUpdate = info;
                    if (InvokeRequired) BeginInvoke((Action)(() => _topToolbar.Invalidate()));
                    else _topToolbar.Invalidate();
                }
            }
            catch { }
        }

        private sealed class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }
    }
}

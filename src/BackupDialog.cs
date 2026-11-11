using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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

        private readonly TextBox _txtDest;
        private readonly RadioButton _rbFastBinaries;
        private readonly RadioButton _rbRawCdn;
        private readonly ComboBox _cbScope;
        private readonly ComboBox _cbLocale;
        private readonly ProgressBar _pbOverall;
        private readonly ProgressBar _pbCurrent;
        private readonly Label _lblOverall;
        private readonly Label _lblCurrent;
        private readonly RichTextBox _txtLog;
        private readonly Button _btnStart;
        private readonly Button _btnClose;
        private readonly Button _btnBrowse;

        private CancellationTokenSource _cts;
        private bool _isRunning;

        public BackupDialog(string defaultTargetDir)
        {
            Text = "Diablo 3 Forever — Bulk Backup & Archival";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(740, 580);
            BackColor = Color.FromArgb(10, 13, 20);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            DoubleBuffered = true;

            // Determine sensible default destination
            string defDrive = "D:\\";
            if (!Directory.Exists(defDrive)) defDrive = "C:\\";
            string defDest = Path.Combine(defDrive, "Diablo3_Historical_Archive");

            // Header Area
            var pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(740, 70),
                BackColor = Color.FromArgb(14, 18, 27)
            };
            pnlHeader.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            pnlHeader.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
                using var subFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                e.Graphics.DrawString("📦 BULK ARCHIVAL & BACKUP ENGINE", titleFont, Brushes.White, 20, 15);
                e.Graphics.DrawString("Preserve all 91 historical Diablo III versions to external drives, USB, or network storage.",
                    subFont, new SolidBrush(Color.FromArgb(148, 163, 184)), 22, 40);

                using var pen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawLine(pen, 0, 69, 740, 69);
            };

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 32),
                Location = new Point(694, 8),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (_, _) => { if (!_isRunning) Close(); else CancelBackup(); };
            pnlHeader.Controls.Add(btnX);
            Controls.Add(pnlHeader);

            // Destination Folder
            var lblDest = new Label
            {
                Text = "BACKUP DESTINATION FOLDER:",
                Location = new Point(24, 82),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            Controls.Add(lblDest);

            _txtDest = new TextBox
            {
                Location = new Point(24, 102),
                Size = new Size(575, 26),
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.FixedSingle,
                Text = defDest
            };
            Controls.Add(_txtDest);

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(608, 101),
                Size = new Size(106, 27),
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
                }
            };
            Controls.Add(_btnBrowse);

            // Group 1: Archival Mode
            var lblMode = new Label
            {
                Text = "ARCHIVAL MODE:",
                Location = new Point(24, 138),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            Controls.Add(lblMode);

            _rbFastBinaries = new RadioButton
            {
                Text = "⚡ Fast Binaries Archive (~3.2 GB Total) — Patched standalone executables & DLLs for all builds. Ready to run.",
                Location = new Point(26, 158),
                Size = new Size(690, 24),
                Checked = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            Controls.Add(_rbFastBinaries);

            _rbRawCdn = new RadioButton
            {
                Text = "📦 Raw Blizzard CDN Mirror (~48 GB Total) — Complete unpatched Base MPQs, patch archives & manifests for offline cold storage.",
                Location = new Point(26, 184),
                Size = new Size(690, 24),
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            Controls.Add(_rbRawCdn);

            // Group 2: Scope & Regional Locale
            var lblScope = new Label
            {
                Text = "SCOPE:",
                Location = new Point(24, 216),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            Controls.Add(lblScope);

            _cbScope = new ComboBox
            {
                Location = new Point(78, 213),
                Size = new Size(302, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f)
            };
            _cbScope.Items.Add("All 91 Builds (Full Catalog — Retail, PTR, Beta)");
            _cbScope.Items.Add("Retail Releases Only (38 Milestone Builds)");
            _cbScope.Items.Add("Reaper of Souls Only (41 Expansion Builds)");
            _cbScope.Items.Add("Vanilla Classic Only (50 Classic Builds)");
            _cbScope.SelectedIndex = 0;
            Controls.Add(_cbScope);

            var lblLocale = new Label
            {
                Text = "LOCALE:",
                Location = new Point(394, 216),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            Controls.Add(lblLocale);

            _cbLocale = new ComboBox
            {
                Location = new Point(456, 213),
                Size = new Size(258, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(17, 23, 34),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f)
            };
            _cbLocale.Items.AddRange(new object[]
            {
                "enUS - English (US)",
                "enGB - English (EU)",
                "deDE - Deutsch (German)",
                "frFR - Français (French)",
                "esES - Español (Spanish EU)",
                "esMX - Español (Spanish AL)",
                "ptBR - Português (Portuguese)",
                "itIT - Italiano (Italian)",
                "plPL - Polski (Polish)",
                "ruRU - Русский (Russian)",
                "koKR - 한국어 (Korean)",
                "zhTW - 繁體中文 (Traditional Chinese)",
                "zhCN - 简体中文 (Simplified Chinese)"
            });
            _cbLocale.SelectedIndex = 0;
            Controls.Add(_cbLocale);

            // Progress Section
            _lblOverall = new Label
            {
                Text = "Ready to start backup.",
                Location = new Point(24, 248),
                Size = new Size(690, 18),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
            };
            Controls.Add(_lblOverall);

            _pbOverall = new ProgressBar
            {
                Location = new Point(24, 268),
                Size = new Size(690, 14),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Controls.Add(_pbOverall);

            _lblCurrent = new Label
            {
                Text = "",
                Location = new Point(24, 286),
                Size = new Size(690, 16),
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8f)
            };
            Controls.Add(_lblCurrent);

            _pbCurrent = new ProgressBar
            {
                Location = new Point(24, 304),
                Size = new Size(690, 8),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Controls.Add(_pbCurrent);

            // Status Log Output
            _txtLog = new RichTextBox
            {
                Location = new Point(24, 320),
                Size = new Size(690, 190),
                BackColor = Color.FromArgb(6, 8, 13),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Consolas", 8.25f),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_txtLog);

            // Action Buttons
            _btnStart = new Button
            {
                Text = "🚀 START BACKUP",
                Location = new Point(444, 524),
                Size = new Size(160, 36),
                BackColor = Color.FromArgb(0, 116, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnStart.FlatAppearance.BorderSize = 0;
            _btnStart.Click += async (_, _) =>
            {
                if (_isRunning)
                {
                    CancelBackup();
                }
                else
                {
                    await StartBackupAsync();
                }
            };
            Controls.Add(_btnStart);

            _btnClose = new Button
            {
                Text = "Close",
                Location = new Point(614, 524),
                Size = new Size(100, 36),
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderColor = Color.FromArgb(45, 55, 75);
            _btnClose.Click += (_, _) =>
            {
                if (_isRunning) CancelBackup();
                else Close();
            };
            Controls.Add(_btnClose);

            Paint += (_, e) =>
            {
                using var borderPen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            };
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

            _isRunning = true;
            _btnStart.Text = "⏹ CANCEL";
            _btnStart.BackColor = Color.FromArgb(185, 28, 28);
            _btnClose.Enabled = false;
            _txtDest.Enabled = false;
            _btnBrowse.Enabled = false;
            _rbFastBinaries.Enabled = false;
            _rbRawCdn.Enabled = false;
            _cbScope.Enabled = false;
            _cbLocale.Enabled = false;

            _cts = new CancellationTokenSource();

            var mode = _rbFastBinaries.Checked ? BackupMode.FastBinaries : BackupMode.RawCdnMirror;
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

            try
            {
                await BackupEngine.RunBackupAsync(
                    backupRoot: dest,
                    mode: mode,
                    scope: scope,
                    locale: selectedLocale,
                    onOverallProgress: (curr, total, status) =>
                    {
                        if (InvokeRequired)
                        {
                            BeginInvoke(() =>
                            {
                                int pct = (int)Math.Clamp(curr * 100.0 / total, 0, 100);
                                _pbOverall.Value = pct;
                                _lblOverall.Text = $"{status} ({pct}%)";
                            });
                        }
                        else
                        {
                            int pct = (int)Math.Clamp(curr * 100.0 / total, 0, 100);
                            _pbOverall.Value = pct;
                            _lblOverall.Text = $"{status} ({pct}%)";
                        }
                    },
                    onItemProgress: (pct, status) =>
                    {
                        if (InvokeRequired)
                        {
                            BeginInvoke(() =>
                            {
                                _pbCurrent.Value = (int)Math.Clamp(pct, 0, 100);
                                _lblCurrent.Text = status;
                            });
                        }
                        else
                        {
                            _pbCurrent.Value = (int)Math.Clamp(pct, 0, 100);
                            _lblCurrent.Text = status;
                        }
                    },
                    log: msg =>
                    {
                        if (InvokeRequired)
                        {
                            BeginInvoke(() =>
                            {
                                _txtLog.AppendText(msg + "\r\n");
                                _txtLog.SelectionStart = _txtLog.Text.Length;
                                _txtLog.ScrollToCaret();
                            });
                        }
                        else
                        {
                            _txtLog.AppendText(msg + "\r\n");
                            _txtLog.SelectionStart = _txtLog.Text.Length;
                            _txtLog.ScrollToCaret();
                        }
                    },
                    ct: _cts.Token);

                _lblOverall.Text = "🎉 Backup completed successfully!";
                _pbOverall.Value = 100;
                _pbCurrent.Value = 100;
                MessageBox.Show(this, $"Backup completed successfully to:\n{dest}", "Backup Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                _lblOverall.Text = "⚠️ Backup was cancelled by user.";
                _txtLog.AppendText("\r\n[CANCELED] Backup operation stopped.\r\n");
            }
            catch (Exception ex)
            {
                _lblOverall.Text = "❌ Backup error occurred.";
                _txtLog.AppendText($"\r\n[ERROR] {ex.Message}\r\n");
                MessageBox.Show(this, $"Backup encountered an error:\n{ex.Message}", "Backup Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRunning = false;
                _btnStart.Text = "🚀 START BACKUP";
                _btnStart.BackColor = Color.FromArgb(0, 116, 224);
                _btnClose.Enabled = true;
                _txtDest.Enabled = true;
                _btnBrowse.Enabled = true;
                _rbFastBinaries.Enabled = true;
                _rbRawCdn.Enabled = true;
                _cbScope.Enabled = true;
                _cbLocale.Enabled = true;
            }
        }

        private void CancelBackup()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _lblOverall.Text = "Stopping backup...";
            }
        }
    }
}

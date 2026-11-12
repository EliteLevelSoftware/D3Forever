using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace D3Downloader
{
    public sealed class UpdateDialog : Form
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly UpdateInfo _info;
        private readonly ProgressBar _pb;
        private readonly Label _lblStatus;
        private readonly Button _btnUpdate;
        private readonly Button _btnLater;
        private CancellationTokenSource _cts;
        private bool _isUpdating;

        public UpdateDialog(UpdateInfo info)
        {
            _info = info;
            Text = "Diablo 3 Forever — Update Available";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(640, 440);
            BackColor = Color.FromArgb(11, 14, 22);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;

            // Header panel
            var pnlHdr = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(640, 72),
                BackColor = Color.FromArgb(16, 22, 34)
            };
            pnlHdr.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            pnlHdr.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var tFont = new Font("Segoe UI", 12f, FontStyle.Bold);
                using var sFont = new Font("Segoe UI", 8.5f);
                e.Graphics.DrawString($"✨ NEW VERSION AVAILABLE: v{_info.LatestVersion}", tFont, new SolidBrush(Color.FromArgb(52, 211, 153)), 20, 14);
                e.Graphics.DrawString($"You are currently running v{_info.CurrentVersion}. Update now to get the latest features and fixes.",
                    sFont, new SolidBrush(Color.FromArgb(148, 163, 184)), 22, 40);

                using var pen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawLine(pen, 0, 71, 640, 71);
            };

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 32),
                Location = new Point(594, 8),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (_, _) => { if (!_isUpdating) Close(); };
            pnlHdr.Controls.Add(btnX);
            Controls.Add(pnlHdr);

            // Release Notes Title
            var lblChangelog = new Label
            {
                Text = "RELEASE HIGHLIGHTS & CHANGELOG:",
                Location = new Point(20, 84),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191)
            };
            Controls.Add(lblChangelog);

            var txtNotes = new RichTextBox
            {
                Location = new Point(20, 106),
                Size = new Size(600, 210),
                BackColor = Color.FromArgb(6, 8, 13),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 8.5f),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                Text = string.IsNullOrWhiteSpace(_info.ReleaseNotes)
                    ? "See GitHub Releases page for full release details."
                    : _info.ReleaseNotes.Replace("\r\n", "\n").Replace("\n", "\r\n")
            };
            Controls.Add(txtNotes);

            // Progress Bar
            _lblStatus = new Label
            {
                Text = $"Ready to download v{_info.LatestVersion} ({(_info.AssetSize > 0 ? $"{_info.AssetSize / 1048576.0:F1} MB" : "Official Binary")}).",
                Location = new Point(20, 326),
                Size = new Size(600, 18),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold)
            };
            Controls.Add(_lblStatus);

            _pb = new ProgressBar
            {
                Location = new Point(20, 346),
                Size = new Size(600, 14),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Controls.Add(_pb);

            // Buttons
            _btnUpdate = new Button
            {
                Text = "🚀 UPDATE & RESTART NOW",
                Location = new Point(310, 380),
                Size = new Size(200, 38),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnUpdate.FlatAppearance.BorderSize = 0;
            _btnUpdate.Click += async (_, _) => await StartUpdateAsync();
            Controls.Add(_btnUpdate);

            _btnLater = new Button
            {
                Text = "Later",
                Location = new Point(520, 380),
                Size = new Size(100, 38),
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            _btnLater.FlatAppearance.BorderColor = Color.FromArgb(45, 55, 75);
            _btnLater.Click += (_, _) => Close();
            Controls.Add(_btnLater);

            var btnGh = new Button
            {
                Text = "View on GitHub →",
                Location = new Point(20, 382),
                Size = new Size(130, 34),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(88, 166, 255),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f),
                Cursor = Cursors.Hand
            };
            btnGh.FlatAppearance.BorderSize = 0;
            btnGh.Click += (_, _) =>
            {
                string url = !string.IsNullOrEmpty(_info.HtmlUrl)
                    ? _info.HtmlUrl
                    : "https://github.com/EliteLevelSoftware/D3Forever/releases/latest";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            };
            Controls.Add(btnGh);

            Paint += (_, e) =>
            {
                using var borderPen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            };
        }

        private async Task StartUpdateAsync()
        {
            if (_isUpdating) return;
            _isUpdating = true;
            _btnUpdate.Enabled = false;
            _btnLater.Enabled = false;
            _btnUpdate.Text = "Downloading...";
            _lblStatus.Text = "Downloading new Diablo 3 Forever binary from GitHub...";
            _cts = new CancellationTokenSource();

            try
            {
                await UpdateChecker.ApplyUpdateAsync(
                    _info.DownloadUrl,
                    _info.ChecksumUrl,
                    (pct, status) =>
                    {
                        if (InvokeRequired)
                        {
                            BeginInvoke(() =>
                            {
                                _pb.Value = (int)Math.Clamp(pct, 0, 100);
                                _lblStatus.Text = $"Downloading: {status} ({pct:F0}%)";
                            });
                        }
                        else
                        {
                            _pb.Value = (int)Math.Clamp(pct, 0, 100);
                            _lblStatus.Text = $"Downloading: {status} ({pct:F0}%)";
                        }
                    },
                    _cts.Token);
            }
            catch (Exception ex)
            {
                _isUpdating = false;
                _btnUpdate.Enabled = true;
                _btnLater.Enabled = true;
                _btnUpdate.Text = "🚀 UPDATE & RESTART NOW";
                _lblStatus.Text = $"Update failed: {ex.Message}";
                MessageBox.Show(this, $"Failed to download or apply update:\n{ex.Message}", "Update Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace D3Downloader
{
    public sealed class BuildDossierDialog : Form
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        public bool SelectBuildRequested { get; private set; }
        public D3BuildCatalogItem SelectedBuild { get; }

        public BuildDossierDialog(D3BuildCatalogItem build)
        {
            SelectedBuild = build;
            var dossier = BuildDossierDatabase.GetDossier(build);

            Text = $"Diablo 3 Forever — Historical Dossier: {build.DisplayTitle}";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 580);
            BackColor = Color.FromArgb(10, 13, 20);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            DoubleBuffered = true;

            // 1. Header Area
            var pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(760, 60),
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

                // Draw vector dossier parchment icon
                int sx = 20, sy = 16;
                using var scrollPen = new Pen(Color.FromArgb(245, 158, 11), 1.5f);
                e.Graphics.DrawRectangle(scrollPen, sx, sy + 2, 14, 18);
                e.Graphics.DrawLine(scrollPen, sx + 3, sy + 6, sx + 11, sy + 6);
                e.Graphics.DrawLine(scrollPen, sx + 3, sy + 10, sx + 11, sy + 10);
                e.Graphics.DrawLine(scrollPen, sx + 3, sy + 14, sx + 8, sy + 14);

                using var titleFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                using var subFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                e.Graphics.DrawString($"HISTORICAL TIME-CAPSULE DOSSIER — {build.Version}.{build.BuildNumber}",
                    titleFont, Brushes.White, sx + 24, 13);
                e.Graphics.DrawString($"{dossier.Headline} • Released {dossier.Version}",
                    subFont, new SolidBrush(Color.FromArgb(148, 163, 184)), sx + 24, 36);

                using var pen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawLine(pen, 0, 59, 760, 59);
            };

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 32),
                Location = new Point(714, 8),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (_, _) => Close();
            pnlHeader.Controls.Add(btnX);
            Controls.Add(pnlHeader);

            // 2. Milestone Badges Flow Panel
            var pnlBadges = new FlowLayoutPanel
            {
                Location = new Point(20, 68),
                Size = new Size(720, 36),
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            foreach (var badge in dossier.MilestoneBadges)
            {
                var lblBadge = new Label
                {
                    Text = badge,
                    AutoSize = true,
                    BackColor = Color.FromArgb(24, 34, 52),
                    ForeColor = Color.FromArgb(254, 240, 138),
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    Padding = new Padding(8, 4, 8, 4),
                    Margin = new Padding(0, 4, 8, 0)
                };
                lblBadge.Paint += (_, e) =>
                {
                    using var p = new Pen(Color.FromArgb(245, 158, 11), 1f);
                    e.Graphics.DrawRectangle(p, 0, 0, lblBadge.Width - 1, lblBadge.Height - 1);
                };
                pnlBadges.Controls.Add(lblBadge);
            }
            Controls.Add(pnlBadges);

            // 3. Rich Text Dossier Content
            var rtbContent = new RichTextBox
            {
                Location = new Point(20, 108),
                Size = new Size(720, 400),
                BackColor = Color.FromArgb(8, 10, 16),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 9.25f),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            PopulateDossierText(rtbContent, dossier, build);
            Controls.Add(rtbContent);

            // 4. Bottom Action Bar
            var btnSelectAndInstall = new Button
            {
                Text = $"🚀 SELECT & INSTALL {build.Version}.{build.BuildNumber}",
                Location = new Point(20, 522),
                Size = new Size(270, 38),
                BackColor = Color.FromArgb(0, 116, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSelectAndInstall.FlatAppearance.BorderSize = 0;
            btnSelectAndInstall.Click += (_, _) =>
            {
                SelectBuildRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnSelectAndInstall);

            if (!string.IsNullOrWhiteSpace(dossier.OfficialUrl))
            {
                var btnWeb = new Button
                {
                    Text = "🌐 View Blizzard / Wiki Archive",
                    Location = new Point(300, 522),
                    Size = new Size(230, 38),
                    BackColor = Color.FromArgb(28, 38, 56),
                    ForeColor = Color.FromArgb(226, 232, 240),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btnWeb.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
                btnWeb.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(dossier.OfficialUrl) { UseShellExecute = true });
                    }
                    catch { }
                };
                Controls.Add(btnWeb);
            }

            var btnClose = new Button
            {
                Text = "Close",
                Location = new Point(640, 522),
                Size = new Size(100, 38),
                BackColor = Color.FromArgb(24, 30, 42),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(45, 55, 75);
            btnClose.Click += (_, _) => Close();
            Controls.Add(btnClose);

            Paint += (_, e) =>
            {
                using var borderPen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            };
        }

        private static void PopulateDossierText(RichTextBox rtb, BuildDossier dossier, D3BuildCatalogItem build)
        {
            rtb.Clear();

            void AppendHeader(string title, Color color)
            {
                rtb.SelectionFont = new Font("Segoe UI", 11f, FontStyle.Bold);
                rtb.SelectionColor = color;
                rtb.AppendText(title + "\r\n");
            }

            void AppendBody(string text, Color color)
            {
                rtb.SelectionFont = new Font("Segoe UI", 9.25f, FontStyle.Regular);
                rtb.SelectionColor = color;
                rtb.AppendText(text + "\r\n\r\n");
            }

            void AppendBullet(string text)
            {
                rtb.SelectionFont = new Font("Segoe UI", 9.25f, FontStyle.Regular);
                rtb.SelectionColor = Color.FromArgb(226, 232, 240);
                rtb.AppendText("  •  " + text + "\r\n");
            }

            // Section 1: Historical Context
            AppendHeader("📜 HISTORICAL CONTEXT & ERA NARRATIVE", Color.FromArgb(59, 130, 246));
            AppendBody(dossier.EraContext, Color.FromArgb(203, 213, 225));

            // Section 2: Key Patch Highlights
            AppendHeader("⚔️ MAJOR PATCH HIGHLIGHTS & SYSTEM CHANGES", Color.FromArgb(245, 158, 11));
            foreach (var h in dossier.KeyHighlights)
            {
                AppendBullet(h);
            }
            rtb.AppendText("\r\n");

            // Section 3: Developer Quote or Trivia
            if (!string.IsNullOrWhiteSpace(dossier.FamousQuoteOrTrivia))
            {
                AppendHeader("💬 ERA TRIVIA & DEVELOPER PERSPECTIVE", Color.FromArgb(16, 185, 129));
                AppendBody(dossier.FamousQuoteOrTrivia, Color.FromArgb(148, 163, 184));
            }

            // Section 4: Technical & CDN Provenance
            AppendHeader("🛡️ TECHNICAL ARCHIVE & CDN PROVENANCE", Color.FromArgb(168, 85, 247));
            AppendBullet($"Build Version: {build.Version} (Build Number: {build.BuildNumber})");
            AppendBullet($"Edition: {(build.IsRoS ? "Reaper of Souls (Expansion)" : "Vanilla Classic")}");
            AppendBullet($"Category: {build.Category}");
            AppendBullet($"Architecture: {(build.IsCasc ? "Modern 64-bit CASC Content Pipeline" : "Legacy 32-bit MPQ Archive with PTCH Deltas")}");
            if (build.IsCasc && !string.IsNullOrEmpty(build.BuildConfigHash))
            {
                AppendBullet($"Blizzard BuildConfig: {build.BuildConfigHash}");
                if (!string.IsNullOrEmpty(build.CdnConfigHash))
                {
                    AppendBullet($"Blizzard CDNConfig: {build.CdnConfigHash}");
                }
            }
            AppendBullet($"Blizzard Base Node: {build.BaseDirectUrl}");
            AppendBullet($"Blizzard Manifest: {(string.IsNullOrEmpty(build.MfilFileName) ? "CASC Build.info" : build.MfilFileName)}");
            AppendBullet($"Official Release Date: {build.ReleaseDate}");
            rtb.AppendText("\r\n");

            rtb.SelectionStart = 0;
            rtb.ScrollToCaret();
        }
    }
}

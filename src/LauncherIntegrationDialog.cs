using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace D3Downloader
{
    public sealed class LauncherIntegrationDialog : Form
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly ComboBox _cbLanguage;
        private readonly ComboBox _cbBuild;
        private readonly ComboBox _cbMode;
        private readonly ComboBox _cbLocale;
        private readonly TextBox _txtDefaultPath;
        private readonly TextBox _txtServerHost;
        private readonly TextBox _txtServerPort;

        private readonly CheckBox _chkAutoDownload;
        private readonly CheckBox _chkSilent;
        private readonly CheckBox _chkProgress;
        private readonly CheckBox _chkAutoLaunch;

        private readonly RichTextBox _rtbCode;
        private readonly Button _btnCopy;
        private readonly Button _btnExport;
        private readonly Label _lblCodeHeader;

        public LauncherIntegrationDialog(D3BuildCatalogItem initialBuild = null)
        {
            Text = "Diablo 3 Forever — Server Launcher Integration Studio";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 720);
            BackColor = Color.FromArgb(10, 13, 20);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            DoubleBuffered = true;

            // ================= 1. HEADER (64px) =================
            var pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(880, 64),
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

                int bx = 22, by = 16;
                using var iconPen = new Pen(Color.FromArgb(59, 130, 246), 1.5f);
                e.Graphics.DrawRectangle(iconPen, bx, by + 4, 18, 14);
                e.Graphics.DrawLine(iconPen, bx + 5, by + 11, bx + 9, by + 15);
                e.Graphics.DrawLine(iconPen, bx + 9, by + 15, bx + 15, by + 8);

                using var titleFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                using var subFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
                e.Graphics.DrawString("SERVER LAUNCHER INTEGRATION STUDIO", titleFont, Brushes.White, bx + 28, 14);
                e.Graphics.DrawString("Generate automated headless installation code for your private server launcher with GitHub download attribution.",
                    subFont, new SolidBrush(Color.FromArgb(148, 163, 184)), bx + 28, 38);

                using var pen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawLine(pen, 0, 63, 880, 63);
            };

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(154, 166, 191),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 32),
                Location = new Point(834, 8),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (_, _) => Close();
            pnlHeader.Controls.Add(btnX);
            Controls.Add(pnlHeader);

            // ================= 2. LEFT SETTINGS COLUMN (370px) =================
            var pnlSettings = new Panel
            {
                Location = new Point(20, 74),
                Size = new Size(360, 626),
                BackColor = Color.FromArgb(13, 17, 26)
            };
            pnlSettings.Paint += (_, e) =>
            {
                using var borderPen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, pnlSettings.Width - 1, pnlSettings.Height - 1);
            };

            int curY = 14;

            // Target Framework
            AddSectionLabel(pnlSettings, "1. LAUNCHER LANGUAGE / FRAMEWORK:", ref curY);
            _cbLanguage = new ComboBox
            {
                Location = new Point(16, curY),
                Size = new Size(328, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold)
            };
            _cbLanguage.Items.AddRange(new object[]
            {
                "C# / .NET (WPF / WinForms / DiIiS Launcher)",
                "Electron / Node.js / TypeScript",
                "Python 3 (Zero Dependencies)",
                "PowerShell Script (.ps1)",
                "Windows Batch Script (.bat)",
                "C++ / Win32 API"
            });
            _cbLanguage.SelectedIndex = 0;
            _cbLanguage.SelectedIndexChanged += (_, _) => RegenerateCode();
            pnlSettings.Controls.Add(_cbLanguage);
            curY += 36;

            // Target Build
            AddSectionLabel(pnlSettings, "2. DIABLO III BUILD FOR YOUR SERVER:", ref curY);
            _cbBuild = new ComboBox
            {
                Location = new Point(16, curY),
                Size = new Size(328, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f)
            };
            foreach (var b in D3BuildCatalog.AllBuilds)
            {
                _cbBuild.Items.Add(b);
            }
            _cbBuild.DisplayMember = "DisplayTitle";

            // Default to 2.7.4.84161 (Server Standard) or provided build
            var defaultBuild = initialBuild ?? D3BuildCatalog.AllBuilds.FirstOrDefault(b => b.BuildNumber == 84161) ?? D3BuildCatalog.AllBuilds.First();
            _cbBuild.SelectedItem = defaultBuild;
            _cbBuild.SelectedIndexChanged += (_, _) => RegenerateCode();
            pnlSettings.Controls.Add(_cbBuild);
            curY += 36;

            // Mode & Locale
            AddSectionLabel(pnlSettings, "3. INSTALLATION MODE & LOCALE:", ref curY);
            _cbMode = new ComboBox
            {
                Location = new Point(16, curY),
                Size = new Size(190, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f)
            };
            _cbMode.Items.Add("Full Playable Client");
            _cbMode.Items.Add("Fast Binaries Only");
            _cbMode.SelectedIndex = 0;
            _cbMode.SelectedIndexChanged += (_, _) => RegenerateCode();
            pnlSettings.Controls.Add(_cbMode);

            _cbLocale = new ComboBox
            {
                Location = new Point(212, curY),
                Size = new Size(132, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f)
            };
            _cbLocale.Items.AddRange(new object[] { "enUS", "enGB", "deDE", "frFR", "esES", "esMX", "ptBR", "itIT", "plPL", "ruRU", "koKR", "zhTW", "zhCN" });
            _cbLocale.SelectedIndex = 0;
            _cbLocale.SelectedIndexChanged += (_, _) => RegenerateCode();
            pnlSettings.Controls.Add(_cbLocale);
            curY += 36;

            // Default Path
            AddSectionLabel(pnlSettings, "4. DEFAULT INSTALLATION PATH:", ref curY);
            _txtDefaultPath = new TextBox
            {
                Location = new Point(16, curY),
                Size = new Size(328, 24),
                BackColor = Color.FromArgb(7, 9, 15),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 8.5f),
                Text = @"C:\Games\Diablo III"
            };
            _txtDefaultPath.TextChanged += (_, _) => RegenerateCode();
            pnlSettings.Controls.Add(_txtDefaultPath);
            curY += 34;

            // Server Host & Port
            AddSectionLabel(pnlSettings, "5. SERVER IP / AUTH HOST & PORT:", ref curY);
            _txtServerHost = new TextBox
            {
                Location = new Point(16, curY),
                Size = new Size(240, 24),
                BackColor = Color.FromArgb(7, 9, 15),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 8.5f),
                Text = "logon.myserver.com"
            };
            _txtServerHost.TextChanged += (_, _) => RegenerateCode();
            pnlSettings.Controls.Add(_txtServerHost);

            _txtServerPort = new TextBox
            {
                Location = new Point(262, curY),
                Size = new Size(82, 24),
                BackColor = Color.FromArgb(7, 9, 15),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 8.5f),
                Text = "1119"
            };
            _txtServerPort.TextChanged += (_, _) => RegenerateCode();
            pnlSettings.Controls.Add(_txtServerPort);
            curY += 36;

            // Checkbox Options
            AddSectionLabel(pnlSettings, "6. AUTOMATION FEATURES:", ref curY);

            _chkAutoDownload = CreateCheck("Auto-fetch Diablo3Forever from GitHub Release", true, ref curY, pnlSettings,
                "Ensures players always have the latest engine and increments your project's GitHub download counter for every player.");
            _chkSilent = CreateCheck("Silent / Hidden Background Execution", true, ref curY, pnlSettings,
                "Runs 100% headless with no foreign windows appearing to the player.");
            _chkProgress = CreateCheck("Stream Real-Time Progress to UI", true, ref curY, pnlSettings,
                "Captures standard output to update your launcher's custom progress bar.");
            _chkAutoLaunch = CreateCheck("Auto-Launch Game with Server IP on Complete", true, ref curY, pnlSettings,
                "Boots Diablo III64.exe passing -launch -authserver <host> upon exit code 0.");

            Controls.Add(pnlSettings);

            // ================= 3. RIGHT CODE COLUMN (470px) =================
            var pnlCode = new Panel
            {
                Location = new Point(390, 74),
                Size = new Size(470, 626),
                BackColor = Color.FromArgb(13, 17, 26)
            };
            pnlCode.Paint += (_, e) =>
            {
                using var borderPen = new Pen(Color.FromArgb(36, 45, 66), 1f);
                e.Graphics.DrawRectangle(borderPen, 0, 0, pnlCode.Width - 1, pnlCode.Height - 1);
            };

            _lblCodeHeader = new Label
            {
                Text = "GENERATED LAUNCHER CODE:",
                Location = new Point(14, 12),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(59, 130, 246)
            };
            pnlCode.Controls.Add(_lblCodeHeader);

            _rtbCode = new RichTextBox
            {
                Location = new Point(14, 36),
                Size = new Size(442, 528),
                BackColor = Color.FromArgb(7, 9, 15),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 8.75f),
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                ReadOnly = true
            };
            pnlCode.Controls.Add(_rtbCode);

            // Action Buttons
            _btnCopy = new Button
            {
                Text = "📋 COPY CODE",
                Location = new Point(14, 574),
                Size = new Size(140, 38),
                BackColor = Color.FromArgb(30, 58, 138),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCopy.FlatAppearance.BorderColor = Color.FromArgb(59, 130, 246);
            _btnCopy.Click += (_, _) =>
            {
                Clipboard.SetText(_rtbCode.Text);
                _btnCopy.Text = "✅ COPIED!";
                var t = new System.Windows.Forms.Timer { Interval = 2000 };
                t.Tick += (_, _) => { _btnCopy.Text = "📋 COPY CODE"; t.Stop(); t.Dispose(); };
                t.Start();
            };
            pnlCode.Controls.Add(_btnCopy);

            _btnExport = new Button
            {
                Text = "💾 EXPORT FILE...",
                Location = new Point(160, 574),
                Size = new Size(144, 38),
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.FromArgb(226, 232, 240),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnExport.FlatAppearance.BorderColor = Color.FromArgb(60, 75, 105);
            _btnExport.Click += (_, _) => ExportCodeFile();
            pnlCode.Controls.Add(_btnExport);

            var btnGuide = new Button
            {
                Text = "📖 INTEGRATION GUIDE",
                Location = new Point(310, 574),
                Size = new Size(146, 38),
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.FromArgb(148, 163, 184),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGuide.FlatAppearance.BorderColor = Color.FromArgb(45, 55, 75);
            btnGuide.Click += (_, _) =>
            {
                string guidePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "SERVER_INTEGRATION_GUIDE.md");
                if (!File.Exists(guidePath))
                {
                    guidePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "docs", "SERVER_INTEGRATION_GUIDE.md");
                }
                if (File.Exists(guidePath))
                {
                    Process.Start(new ProcessStartInfo { FileName = guidePath, UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/EliteLevelSoftware/D3Forever#readme",
                        UseShellExecute = true
                    });
                }
            };
            pnlCode.Controls.Add(btnGuide);

            Controls.Add(pnlCode);

            RegenerateCode();
        }

        private static void AddSectionLabel(Panel p, string text, ref int curY)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(16, curY),
                AutoSize = true,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            p.Controls.Add(lbl);
            curY += 20;
        }

        private CheckBox CreateCheck(string text, bool def, ref int curY, Panel p, string tipText)
        {
            var chk = new CheckBox
            {
                Text = text,
                Checked = def,
                Location = new Point(16, curY),
                Size = new Size(328, 20),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 8.25f),
                Cursor = Cursors.Hand
            };
            chk.CheckedChanged += (_, _) => RegenerateCode();
            var tip = new ToolTip();
            tip.SetToolTip(chk, tipText);
            p.Controls.Add(chk);
            curY += 24;
            return chk;
        }

        private void RegenerateCode()
        {
            var build = _cbBuild.SelectedItem as D3BuildCatalogItem;
            int buildNum = build?.BuildNumber ?? 84161;
            string buildVer = build?.Version ?? "2.7.4";
            string lang = _cbLanguage.SelectedItem?.ToString() ?? "C#";
            string locale = _cbLocale.SelectedItem?.ToString() ?? "enUS";
            string targetPath = _txtDefaultPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetPath)) targetPath = @"C:\Games\Diablo III";
            string host = _txtServerHost.Text.Trim();
            if (string.IsNullOrWhiteSpace(host)) host = "logon.myserver.com";
            string port = _txtServerPort.Text.Trim();
            if (string.IsNullOrWhiteSpace(port)) port = "1119";
            bool isFastBinaries = _cbMode.SelectedIndex == 1;

            bool autoDl = _chkAutoDownload.Checked;
            bool silent = _chkSilent.Checked;
            bool progress = _chkProgress.Checked;
            bool autoLaunch = _chkAutoLaunch.Checked;

            string code;
            if (lang.StartsWith("C#"))
            {
                code = GenerateCSharp(buildVer, buildNum, locale, targetPath, host, port, isFastBinaries, autoDl, silent, progress, autoLaunch);
                _lblCodeHeader.Text = "GENERATED C# / .NET WRAPPER (D3GameInstaller.cs):";
            }
            else if (lang.StartsWith("Electron"))
            {
                code = GenerateElectron(buildVer, buildNum, locale, targetPath, host, port, isFastBinaries, autoDl, silent, progress, autoLaunch);
                _lblCodeHeader.Text = "GENERATED ELECTRON / NODE.JS (d3Installer.js):";
            }
            else if (lang.StartsWith("Python"))
            {
                code = GeneratePython(buildVer, buildNum, locale, targetPath, host, port, isFastBinaries, autoDl, silent, progress, autoLaunch);
                _lblCodeHeader.Text = "GENERATED PYTHON 3 WRAPPER (d3_installer.py):";
            }
            else if (lang.StartsWith("PowerShell"))
            {
                code = GeneratePowerShell(buildVer, buildNum, locale, targetPath, host, port, isFastBinaries, autoDl, silent, progress, autoLaunch);
                _lblCodeHeader.Text = "GENERATED POWERSHELL SCRIPT (Install-Diablo3.ps1):";
            }
            else if (lang.StartsWith("Windows Batch"))
            {
                code = GenerateBatch(buildVer, buildNum, locale, targetPath, host, port, isFastBinaries, autoDl, silent, progress, autoLaunch);
                _lblCodeHeader.Text = "GENERATED BATCH SCRIPT (Install-Diablo3.bat):";
            }
            else
            {
                code = GenerateCpp(buildVer, buildNum, locale, targetPath, host, port, isFastBinaries, autoDl, silent, progress, autoLaunch);
                _lblCodeHeader.Text = "GENERATED C++ / WIN32 (D3Installer.cpp):";
            }

            _rtbCode.Text = code;
        }

        private void ExportCodeFile()
        {
            string lang = _cbLanguage.SelectedItem?.ToString() ?? "C#";
            string defName = lang.StartsWith("C#") ? "D3GameInstaller.cs" :
                             lang.StartsWith("Electron") ? "d3Installer.js" :
                             lang.StartsWith("Python") ? "d3_installer.py" :
                             lang.StartsWith("PowerShell") ? "Install-Diablo3.ps1" :
                             lang.StartsWith("Windows Batch") ? "Install-Diablo3.bat" : "D3Installer.cpp";

            using var sfd = new SaveFileDialog
            {
                FileName = defName,
                Filter = "Source File (*.*)|*.*",
                Title = "Save Generated Launcher Integration File"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(sfd.FileName, _rtbCode.Text, Encoding.UTF8);
                MessageBox.Show(this, $"Integration code saved to:\n{sfd.FileName}", "Diablo 3 Forever — Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ================= TEMPLATE GENERATORS =================

        private static string GenerateCSharp(string ver, int build, string locale, string path, string host, string port, bool fastBinaries, bool autoDl, bool silent, bool progress, bool launch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// =============================================================================");
            sb.AppendLine("// Diablo 3 Forever — Automated Server Launcher Integration Wrapper");
            sb.AppendLine($"// Configured for: Diablo III {ver} (Build {build}) | Locale: {locale}");
            sb.AppendLine("// Powered by Diablo 3 Forever (https://github.com/EliteLevelSoftware/D3Forever)");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("using System.IO;");
            sb.AppendLine("using System.Net.Http;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine("public static class D3GameInstaller");
            sb.AppendLine("{");
            sb.AppendLine("    // Downloading from GitHub Release increments the official project download metric!");
            sb.AppendLine("    private const string GitHubReleaseUrl = \"https://github.com/EliteLevelSoftware/D3Forever/releases/latest/download/Diablo3Forever.exe\";");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Installs or verifies the exact game build required for this server.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public static async Task InstallOrUpdateAsync(string targetDir = @\"{path}\", Action<string> onStatus = null)");
            sb.AppendLine("    {");
            sb.AppendLine("        string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, \"tools\");");
            sb.AppendLine("        Directory.CreateDirectory(toolsDir);");
            sb.AppendLine("        string exePath = Path.Combine(toolsDir, \"Diablo3Forever.exe\");");
            sb.AppendLine();

            if (autoDl)
            {
                sb.AppendLine("        // Step 1: Ensure Diablo3Forever is downloaded from GitHub release");
                sb.AppendLine("        if (!File.Exists(exePath))");
                sb.AppendLine("        {");
                sb.AppendLine("            onStatus?.Invoke(\"Downloading installation engine from GitHub Releases...\");");
                sb.AppendLine("            using var http = new HttpClient();");
                sb.AppendLine("            byte[] binaryBytes = await http.GetByteArrayAsync(GitHubReleaseUrl);");
                sb.AppendLine("            await File.WriteAllBytesAsync(exePath, binaryBytes);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            string flags = $"\"{path}\" --build {build} --locale {locale}";
            if (fastBinaries) flags += " --binaries-only";

            sb.AppendLine("        // Step 2: Run headless installation directly from official Blizzard CDN");
            sb.AppendLine($"        onStatus?.Invoke(\"Streaming and assembling Diablo III {ver} from Blizzard CDN...\");");
            sb.AppendLine("        var psi = new ProcessStartInfo");
            sb.AppendLine("        {");
            sb.AppendLine("            FileName = exePath,");
            sb.AppendLine($"            Arguments = $\"{flags}\",");
            sb.AppendLine("            UseShellExecute = false,");
            sb.AppendLine($"            CreateNoWindow = {(silent ? "true" : "false")},");
            sb.AppendLine($"            RedirectStandardOutput = {(progress ? "true" : "false")}");
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        using var proc = Process.Start(psi);");
            if (progress)
            {
                sb.AppendLine("        while (!proc.StandardOutput.EndOfStream)");
                sb.AppendLine("        {");
                sb.AppendLine("            string line = await proc.StandardOutput.ReadLineAsync();");
                sb.AppendLine("            if (!string.IsNullOrWhiteSpace(line)) onStatus?.Invoke(line);");
                sb.AppendLine("        }");
            }
            sb.AppendLine("        await proc.WaitForExitAsync();");
            sb.AppendLine();
            sb.AppendLine("        if (proc.ExitCode != 0)");
            sb.AppendLine("            throw new InvalidOperationException($\"Installation failed with exit code {proc.ExitCode}\");");
            sb.AppendLine();

            if (launch)
            {
                sb.AppendLine("        // Step 3: Launch Diablo III connected to your server");
                sb.AppendLine("        LaunchGame(targetDir);");
            }
            sb.AppendLine("    }");

            if (launch)
            {
                sb.AppendLine();
                sb.AppendLine("    public static void LaunchGame(string targetDir)");
                sb.AppendLine("    {");
                sb.AppendLine("        string exe64 = Path.Combine(targetDir, \"x64\", \"Diablo III64.exe\");");
                sb.AppendLine("        string exe32 = Path.Combine(targetDir, \"Diablo III.exe\");");
                sb.AppendLine("        string exeToLaunch = File.Exists(exe64) ? exe64 : exe32;");
                sb.AppendLine();
                sb.AppendLine($"        string args = \"-launch -authserver {host} -authport {port}\";");
                sb.AppendLine("        Process.Start(new ProcessStartInfo");
                sb.AppendLine("        {");
                sb.AppendLine("            FileName = exeToLaunch,");
                sb.AppendLine("            Arguments = args,");
                sb.AppendLine("            WorkingDirectory = targetDir");
                sb.AppendLine("        });");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string GenerateElectron(string ver, int build, string locale, string path, string host, string port, bool fastBinaries, bool autoDl, bool silent, bool progress, bool launch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// =============================================================================");
            sb.AppendLine("// Diablo 3 Forever — Electron / Node.js Launcher Integration");
            sb.AppendLine($"// Configured for: Diablo III {ver} (Build {build}) | Locale: {locale}");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine("const fs = require('fs');");
            sb.AppendLine("const path = require('path');");
            sb.AppendLine("const https = require('https');");
            sb.AppendLine("const { spawn } = require('child_process');");
            sb.AppendLine();
            sb.AppendLine("const GITHUB_RELEASE_URL = 'https://github.com/EliteLevelSoftware/D3Forever/releases/latest/download/Diablo3Forever.exe';");
            sb.AppendLine();
            sb.AppendLine("async function installGame(targetDir, onProgress) {");
            sb.AppendLine("  const toolsDir = path.join(__dirname, 'tools');");
            sb.AppendLine("  if (!fs.existsSync(toolsDir)) fs.mkdirSync(toolsDir, { recursive: true });");
            sb.AppendLine("  const exePath = path.join(toolsDir, 'Diablo3Forever.exe');");
            sb.AppendLine();

            if (autoDl)
            {
                sb.AppendLine("  // Step 1: Download Diablo3Forever from GitHub (increments project download count)");
                sb.AppendLine("  if (!fs.existsSync(exePath)) {");
                sb.AppendLine("    if (onProgress) onProgress('Downloading installer engine from GitHub...');");
                sb.AppendLine("    await downloadFromGitHub(GITHUB_RELEASE_URL, exePath);");
                sb.AppendLine("  }");
                sb.AppendLine();
            }

            string flags = $"['{path}', '--build', '{build}', '--locale', '{locale}']";
            if (fastBinaries) flags = $"['{path}', '--build', '{build}', '--locale', '{locale}', '--binaries-only']";

            sb.AppendLine("  // Step 2: Spawn headless process");
            sb.AppendLine("  if (onProgress) onProgress('Streaming game data from Blizzard CDN...');");
            sb.AppendLine($"  const args = {flags};");
            sb.AppendLine($"  const proc = spawn(exePath, args, {{ windowsHide: {(silent ? "true" : "false")} }});");
            sb.AppendLine();

            if (progress)
            {
                sb.AppendLine("  proc.stdout.on('data', (data) => {");
                sb.AppendLine("    const msg = data.toString().trim();");
                sb.AppendLine("    if (onProgress && msg) onProgress(msg);");
                sb.AppendLine("  });");
                sb.AppendLine();
            }

            sb.AppendLine("  return new Promise((resolve, reject) => {");
            sb.AppendLine("    proc.on('close', (code) => {");
            sb.AppendLine("      if (code === 0) {");
            if (launch)
            {
                sb.AppendLine("        launchGame(targetDir);");
            }
            sb.AppendLine("        resolve();");
            sb.AppendLine("      } else {");
            sb.AppendLine("        reject(new Error(`Installer exited with code ${code}`));");
            sb.AppendLine("      }");
            sb.AppendLine("    });");
            sb.AppendLine("  });");
            sb.AppendLine("}");

            if (launch)
            {
                sb.AppendLine();
                sb.AppendLine("function launchGame(targetDir) {");
                sb.AppendLine("  const exe64 = path.join(targetDir, 'x64', 'Diablo III64.exe');");
                sb.AppendLine("  const exe32 = path.join(targetDir, 'Diablo III.exe');");
                sb.AppendLine("  const exe = fs.existsSync(exe64) ? exe64 : exe32;");
                sb.AppendLine($"  spawn(exe, ['-launch', '-authserver', '{host}', '-authport', '{port}'], {{");
                sb.AppendLine("    cwd: targetDir,");
                sb.AppendLine("    detached: true,");
                sb.AppendLine("    stdio: 'ignore'");
                sb.AppendLine("  }).unref();");
                sb.AppendLine("}");
            }

            sb.AppendLine();
            sb.AppendLine("function downloadFromGitHub(url, dest) {");
            sb.AppendLine("  return new Promise((resolve, reject) => {");
            sb.AppendLine("    const file = fs.createWriteStream(dest);");
            sb.AppendLine("    https.get(url, (res) => {");
            sb.AppendLine("      if (res.statusCode === 302 || res.statusCode === 301) {");
            sb.AppendLine("        https.get(res.headers.location, (redRes) => {");
            sb.AppendLine("          redRes.pipe(file);");
            sb.AppendLine("          file.on('finish', () => file.close(resolve));");
            sb.AppendLine("        }).on('error', reject);");
            sb.AppendLine("      } else {");
            sb.AppendLine("        res.pipe(file);");
            sb.AppendLine("        file.on('finish', () => file.close(resolve));");
            sb.AppendLine("      }");
            sb.AppendLine("    }).on('error', reject);");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("module.exports = { installGame };");
            return sb.ToString();
        }

        private static string GeneratePython(string ver, int build, string locale, string path, string host, string port, bool fastBinaries, bool autoDl, bool silent, bool progress, bool launch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# =============================================================================");
            sb.AppendLine("# Diablo 3 Forever — Python 3 Launcher Automation (Zero Dependencies)");
            sb.AppendLine($"# Configured for: Diablo III {ver} (Build {build}) | Locale: {locale}");
            sb.AppendLine("# =============================================================================");
            sb.AppendLine("import os");
            sb.AppendLine("import sys");
            sb.AppendLine("import subprocess");
            sb.AppendLine("import urllib.request");
            sb.AppendLine();
            sb.AppendLine("GITHUB_URL = 'https://github.com/EliteLevelSoftware/D3Forever/releases/latest/download/Diablo3Forever.exe'");
            sb.AppendLine();
            sb.AppendLine("def install_game(target_dir=r'" + path + "', on_progress=print):");
            sb.AppendLine("    tools_dir = os.path.join(os.path.dirname(__file__), 'tools')");
            sb.AppendLine("    os.makedirs(tools_dir, exist_ok=True)");
            sb.AppendLine("    exe_path = os.path.join(tools_dir, 'Diablo3Forever.exe')");
            sb.AppendLine();

            if (autoDl)
            {
                sb.AppendLine("    # Step 1: Fetch binary from GitHub (counts as official release download)");
                sb.AppendLine("    if not os.path.exists(exe_path):");
                sb.AppendLine("        on_progress('Downloading Diablo3Forever engine from GitHub...')");
                sb.AppendLine("        urllib.request.urlretrieve(GITHUB_URL, exe_path)");
                sb.AppendLine();
            }

            string flags = $"[exe_path, target_dir, '--build', '{build}', '--locale', '{locale}']";
            if (fastBinaries) flags = $"[exe_path, target_dir, '--build', '{build}', '--locale', '{locale}', '--binaries-only']";

            sb.AppendLine("    # Step 2: Run headless installer");
            sb.AppendLine("    on_progress('Streaming game data from Blizzard CDN...')");
            sb.AppendLine($"    cmd = {flags}");
            sb.AppendLine();
            sb.AppendLine("    si = subprocess.STARTUPINFO()");
            if (silent) sb.AppendLine("    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW");
            sb.AppendLine();
            sb.AppendLine("    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, startupinfo=si)");
            if (progress)
            {
                sb.AppendLine("    for line in proc.stdout:");
                sb.AppendLine("        clean = line.strip()");
                sb.AppendLine("        if clean: on_progress(clean)");
            }
            sb.AppendLine("    proc.wait()");
            sb.AppendLine("    if proc.returncode != 0:");
            sb.AppendLine("        raise RuntimeError(f'Installation failed with return code {proc.returncode}')");
            sb.AppendLine();

            if (launch)
            {
                sb.AppendLine("    # Step 3: Launch Diablo III");
                sb.AppendLine("    exe64 = os.path.join(target_dir, 'x64', 'Diablo III64.exe')");
                sb.AppendLine("    exe = exe64 if os.path.exists(exe64) else os.path.join(target_dir, 'Diablo III.exe')");
                sb.AppendLine($"    launch_cmd = [exe, '-launch', '-authserver', '{host}', '-authport', '{port}']");
                sb.AppendLine("    subprocess.Popen(launch_cmd, cwd=target_dir)");
            }

            sb.AppendLine();
            sb.AppendLine("if __name__ == '__main__':");
            sb.AppendLine("    install_game()");
            return sb.ToString();
        }

        private static string GeneratePowerShell(string ver, int build, string locale, string path, string host, string port, bool fastBinaries, bool autoDl, bool silent, bool progress, bool launch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# =============================================================================");
            sb.AppendLine("# Diablo 3 Forever — One-Click Automated Server Setup Script");
            sb.AppendLine($"# Configured for: Diablo III {ver} (Build {build}) | Locale: {locale}");
            sb.AppendLine("# =============================================================================");
            sb.AppendLine($"$TargetDir = \"{path}\"");
            sb.AppendLine("$ToolsDir  = \"$PSScriptRoot\\tools\"");
            sb.AppendLine("New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null");
            sb.AppendLine("$ToolExe   = \"$ToolsDir\\Diablo3Forever.exe\"");
            sb.AppendLine("$GitHubUrl = \"https://github.com/EliteLevelSoftware/D3Forever/releases/latest/download/Diablo3Forever.exe\"");
            sb.AppendLine();

            if (autoDl)
            {
                sb.AppendLine("# Step 1: Download installer engine from GitHub Release");
                sb.AppendLine("if (-not (Test-Path $ToolExe)) {");
                sb.AppendLine("    Write-Host \"[1/3] Downloading installer from GitHub Releases...\" -ForegroundColor Cyan");
                sb.AppendLine("    Invoke-WebRequest -Uri $GitHubUrl -OutFile $ToolExe");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            string flags = $"\"`\"$TargetDir`\" --build {build} --locale {locale}";
            if (fastBinaries) flags += " --binaries-only";
            flags += "\"";

            sb.AppendLine("# Step 2: Stream game files directly from official Blizzard CDN");
            sb.AppendLine($"Write-Host \"[2/3] Installing Diablo III {ver} (Build {build})...\" -ForegroundColor Green");
            sb.AppendLine($"$args = {flags}");
            sb.AppendLine($"Start-Process -FilePath $ToolExe -ArgumentList $args -Wait {(silent ? "-NoNewWindow" : "")}");
            sb.AppendLine();

            if (launch)
            {
                sb.AppendLine("# Step 3: Launch connected to private server");
                sb.AppendLine("Write-Host \"[3/3] Launching Diablo III...\" -ForegroundColor Green");
                sb.AppendLine("$Exe64 = \"$TargetDir\\x64\\Diablo III64.exe\"");
                sb.AppendLine("$Exe32 = \"$TargetDir\\Diablo III.exe\"");
                sb.AppendLine("$Exe   = if (Test-Path $Exe64) { $Exe64 } else { $Exe32 }");
                sb.AppendLine($"Start-Process -FilePath $Exe -ArgumentList \"-launch -authserver {host} -authport {port}\" -WorkingDirectory $TargetDir");
            }

            return sb.ToString();
        }

        private static string GenerateBatch(string ver, int build, string locale, string path, string host, string port, bool fastBinaries, bool autoDl, bool silent, bool progress, bool launch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine(":: =============================================================================");
            sb.AppendLine(":: Diablo 3 Forever — Windows Batch Game Installer");
            sb.AppendLine($":: Configured for: Diablo III {ver} (Build {build})");
            sb.AppendLine(":: =============================================================================");
            sb.AppendLine($"set \"TARGET={path}\"");
            sb.AppendLine("set \"TOOL=tools\\Diablo3Forever.exe\"");
            sb.AppendLine("if not exist \"tools\" mkdir \"tools\"");
            sb.AppendLine();

            if (autoDl)
            {
                sb.AppendLine("if not exist \"%TOOL%\" (");
                sb.AppendLine("    echo [1/3] Downloading Diablo3Forever engine from GitHub Releases...");
                sb.AppendLine("    curl -sL \"https://github.com/EliteLevelSoftware/D3Forever/releases/latest/download/Diablo3Forever.exe\" -o \"%TOOL%\"");
                sb.AppendLine(")");
                sb.AppendLine();
            }

            string flags = $"\"%TARGET%\" --build {build} --locale {locale}";
            if (fastBinaries) flags += " --binaries-only";

            sb.AppendLine($"echo [2/3] Installing Diablo III {ver} from official Blizzard CDN...");
            sb.AppendLine($"\"%TOOL%\" {flags}");
            sb.AppendLine("if %errorlevel% neq 0 ( echo [ERROR] Installation failed. & pause & exit /b 1 )");
            sb.AppendLine();

            if (launch)
            {
                sb.AppendLine("echo [3/3] Launching Diablo III...");
                sb.AppendLine("cd /d \"%TARGET%\"");
                sb.AppendLine("if exist \"x64\\Diablo III64.exe\" (");
                sb.AppendLine($"    start \"\" \"x64\\Diablo III64.exe\" -launch -authserver {host} -authport {port}");
                sb.AppendLine(") else (");
                sb.AppendLine($"    start \"\" \"Diablo III.exe\" -launch -authserver {host} -authport {port}");
                sb.AppendLine(")");
            }

            return sb.ToString();
        }

        private static string GenerateCpp(string ver, int build, string locale, string path, string host, string port, bool fastBinaries, bool autoDl, bool silent, bool progress, bool launch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// =============================================================================");
            sb.AppendLine("// Diablo 3 Forever — Native C++ Win32 Launcher Integration");
            sb.AppendLine($"// Configured for: Diablo III {ver} (Build {build})");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine("#include <windows.h>");
            sb.AppendLine("#include <urlmon.h>");
            sb.AppendLine("#include <iostream>");
            sb.AppendLine("#pragma comment(lib, \"urlmon.lib\")");
            sb.AppendLine();
            sb.AppendLine("int main() {");
            sb.AppendLine("    CreateDirectoryW(L\"tools\", NULL);");
            sb.AppendLine("    LPCWSTR exePath = L\"tools\\\\Diablo3Forever.exe\";");
            sb.AppendLine();

            if (autoDl)
            {
                sb.AppendLine("    // Step 1: Download from GitHub Release (increments download count)");
                sb.AppendLine("    if (GetFileAttributesW(exePath) == INVALID_FILE_ATTRIBUTES) {");
                sb.AppendLine("        std::wcout << L\"Downloading Diablo3Forever from GitHub...\\n\";");
                sb.AppendLine("        URLDownloadToFileW(NULL, L\"https://github.com/EliteLevelSoftware/D3Forever/releases/latest/download/Diablo3Forever.exe\", exePath, 0, NULL);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            string flags = $"\\\"{path}\\\" --build {build} --locale {locale}";
            if (fastBinaries) flags += " --binaries-only";

            sb.AppendLine("    // Step 2: Spawn headless process");
            sb.AppendLine($"    std::wstring cmd = L\"tools\\\\Diablo3Forever.exe {flags}\";");
            sb.AppendLine("    STARTUPINFOW si = { sizeof(si) };");
            if (silent) sb.AppendLine("    si.dwFlags = STARTF_USESHOWWINDOW; si.wShowWindow = SW_HIDE;");
            sb.AppendLine("    PROCESS_INFORMATION pi = { 0 };");
            sb.AppendLine();
            sb.AppendLine("    if (CreateProcessW(NULL, &cmd[0], NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)) {");
            sb.AppendLine("        WaitForSingleObject(pi.hProcess, INFINITE);");
            sb.AppendLine("        CloseHandle(pi.hProcess);");
            sb.AppendLine("        CloseHandle(pi.hThread);");
            sb.AppendLine("    }");
            sb.AppendLine();

            if (launch)
            {
                sb.AppendLine("    // Step 3: Launch Diablo III");
                sb.AppendLine($"    std::wstring gameExe = L\"{path}\\\\x64\\\\Diablo III64.exe\";");
                sb.AppendLine($"    std::wstring gameArgs = L\"-launch -authserver {host} -authport {port}\";");
                sb.AppendLine($"    ShellExecuteW(NULL, L\"open\", gameExe.c_str(), gameArgs.c_str(), L\"{path}\", SW_SHOW);");
            }

            sb.AppendLine("    return 0;");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
    public partial class Form1 : Form
    {
        private static readonly Color ColorBackground = Color.FromArgb(22, 22, 29);
        private static readonly Color ColorAccent = Color.FromArgb(94, 106, 246);
        private static readonly Color ColorAccentHover = Color.FromArgb(112, 123, 250);
        private static readonly Color ColorTextPrimary = Color.FromArgb(240, 240, 244);
        private static readonly Color ColorTextSecondary = Color.FromArgb(150, 150, 164);
        private static readonly Color ColorSurface = Color.FromArgb(38, 38, 48);
        private static readonly Color ColorSurfaceHover = Color.FromArgb(48, 48, 60);
        private static readonly Color ColorBorder = Color.FromArgb(58, 58, 72);
        private static readonly Color ColorDanger = Color.FromArgb(220, 96, 96);

        private Label labelTitle;
        private Label labelInstructions;
        private ModernCheckBox chkDisable;
        private ModernCheckBox chkStartWithWindows;
        private ModernButton btnOk;
        private ModernButton btnCancel;
        private ModernButton btnCheckUpdate;
        private Label labelUpdateStatus;
        private LinkLabel linkUninstall;
        private LinkLabel linkExit;
        private Label labelStatus;
        private ModernButton btnOpenLog;
        private System.Windows.Forms.Timer _statusTimer;
        private readonly Func<string> _getStatus;

        private readonly Action<bool> _applySteelSeriesSetting;
        private readonly Action _exitApp;
        private readonly Action _uninstallApp;
        private readonly Action _ensureWatcherStarted;
        private readonly string[] _updateRestartArgs;

        public Form1(Action<bool> applySteelSeriesSetting, Action exitApp, Action uninstallApp, Action ensureWatcherStarted, string[] updateRestartArgs, Func<string> getStatus = null)
        {
            _getStatus = getStatus;
            _applySteelSeriesSetting = applySteelSeriesSetting;
            _exitApp = exitApp;
            _uninstallApp = uninstallApp;
            _ensureWatcherStarted = ensureWatcherStarted;
            _updateRestartArgs = updateRestartArgs ?? Array.Empty<string>();
            InitializeComponent();
            InitializeCustomComponents();
            LoadCurrentSettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryEnableDarkTitleBar();
            TryEnableRoundedCorners();
        }

        private void TryEnableRoundedCorners()
        {
            try
            {
                int preference = 2;
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch { }
        }

        private void TryEnableDarkTitleBar()
        {
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(480, 548);
            Name = "Form1";
            Text = "SteelSeries Discord Stream Fix — Settings";
            ResumeLayout(false);
        }

        private void InitializeCustomComponents()
        {
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = ColorBackground;
            Font = new Font("Segoe UI", 9F);
            ShowInTaskbar = true;

            var accentBar = new RoundedPanel
            {
                Location = new Point(32, 28),
                Size = new Size(4, 32),
                FillColor = ColorAccent,
                CornerRadius = 2
            };

            labelTitle = new Label
            {
                AutoSize = false,
                Location = new Point(48, 24),
                Size = new Size(400, 32),
                Text = "SteelSeries Discord Stream Fix",
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            labelInstructions = new Label
            {
                AutoSize = false,
                Location = new Point(32, 70),
                Size = new Size(416, 60),
                Text = "Running in the background: Discord is auto-muted the instant it opens a " +
                       $"session on \"{MuteWatcherService.MicEndpointNameFragment}\", and re-muted if it is un-muted in the first seconds " +
                       "(a manual un-mute later is respected). There's no tray icon — reopen this window any time by " +
                       "running the app again.",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 9.5F),
                BackColor = Color.Transparent
            };

            var labelStatusCaption = new Label
            {
                AutoSize = false,
                Location = new Point(32, 140),
                Size = new Size(300, 16),
                Text = "STATUS",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            var panelStatus = new RoundedPanel
            {
                Location = new Point(32, 160),
                Size = new Size(416, 60),
                FillColor = ColorSurface,
                CornerRadius = 14
            };

            labelStatus = new Label
            {
                AutoSize = false,
                Location = new Point(16, 8),
                Size = new Size(290, 44),
                Text = "",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnOpenLog = new ModernButton
            {
                Text = "Open log",
                Size = new Size(88, 32),
                Location = new Point(312, 14),
                BaseColor = ColorSurfaceHover,
                HoverColor = ColorBorder,
                TextColor = ColorTextPrimary,
                BorderColor = ColorBorder
            };
            btnOpenLog.Click += (s, e) => Logger.OpenLogFile();

            panelStatus.Controls.Add(labelStatus);
            panelStatus.Controls.Add(btnOpenLog);

            _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _statusTimer.Tick += (s, e) => RefreshStatus();

            var dividerTop = new Panel
            {
                Location = new Point(32, 228),
                Size = new Size(416, 1),
                BackColor = ColorBorder
            };

            var labelGeneralCaption = new Label
            {
                AutoSize = false,
                Location = new Point(32, 240),
                Size = new Size(300, 16),
                Text = "PREFERENCES",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            var panelGeneral = new RoundedPanel
            {
                Location = new Point(32, 260),
                Size = new Size(416, 88),
                FillColor = ColorSurface,
                CornerRadius = 14
            };

            chkDisable = new ModernCheckBox
            {
                Location = new Point(16, 14),
                Size = new Size(384, 24),
                Text = "Also hide Sonar Chat && Gaming (optional)",
                AccentColor = ColorAccent,
                BorderColor = ColorBorder,
                TextColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9.5F)
            };

            chkStartWithWindows = new ModernCheckBox
            {
                Location = new Point(16, 48),
                Size = new Size(384, 24),
                Text = "Start automatically when I log in",
                AccentColor = ColorAccent,
                BorderColor = ColorBorder,
                TextColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9.5F)
            };
            new ToolTip().SetToolTip(chkDisable,
                "Optional. SteelSeries recommends keeping Discord's output on Sonar - Chat; hiding it " +
                "can cut Discord's audio. Turn this off if Discord goes silent.");
            panelGeneral.Controls.Add(chkDisable);
            panelGeneral.Controls.Add(chkStartWithWindows);

            var labelUpdateCaption = new Label
            {
                AutoSize = false,
                Location = new Point(32, 360),
                Size = new Size(300, 16),
                Text = "UPDATES",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            var panelUpdate = new RoundedPanel
            {
                Location = new Point(32, 380),
                Size = new Size(416, 64),
                FillColor = ColorSurface,
                CornerRadius = 14
            };

            btnCheckUpdate = new ModernButton
            {
                Text = "Check for Updates",
                Size = new Size(160, 36),
                Location = new Point(16, 14),
                BaseColor = ColorSurfaceHover,
                HoverColor = ColorBorder,
                TextColor = ColorTextPrimary,
                BorderColor = ColorBorder
            };
            btnCheckUpdate.Click += BtnCheckUpdate_Click;

            labelUpdateStatus = new Label
            {
                AutoSize = false,
                Location = new Point(188, 14),
                Size = new Size(212, 36),
                Text = "",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panelUpdate.Controls.Add(btnCheckUpdate);
            panelUpdate.Controls.Add(labelUpdateStatus);

            var dividerBottom = new Panel
            {
                Location = new Point(32, 460),
                Size = new Size(416, 1),
                BackColor = ColorBorder
            };

            linkUninstall = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(32, 476),
                Text = "Uninstall...",
                LinkColor = ColorDanger,
                ActiveLinkColor = ColorDanger,
                VisitedLinkColor = ColorDanger,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F)
            };
            linkUninstall.LinkClicked += (s, e) => _uninstallApp();

            linkExit = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(130, 476),
                Text = "Exit app",
                LinkColor = ColorTextSecondary,
                ActiveLinkColor = ColorTextPrimary,
                VisitedLinkColor = ColorTextSecondary,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F)
            };
            // Really quits: un-mutes Discord and brings back the Sonar devices we hid.
            linkExit.LinkClicked += (s, e) => _exitApp();

            btnCancel = new ModernButton
            {
                Text = "Cancel",
                Size = new Size(90, 36),
                Location = new Point(238, 496),
                BaseColor = ColorSurface,
                HoverColor = ColorSurfaceHover,
                TextColor = ColorTextPrimary,
                BorderColor = ColorBorder
            };
            btnCancel.Click += (s, e) => CloseOrHide();

            btnOk = new ModernButton
            {
                Text = "Save",
                Size = new Size(108, 36),
                Location = new Point(340, 496),
                BaseColor = ColorAccent,
                HoverColor = ColorAccentHover,
                TextColor = Color.White
            };
            btnOk.Click += BtnOk_Click;

            Controls.Add(accentBar);
            Controls.Add(labelTitle);
            Controls.Add(labelInstructions);
            Controls.Add(labelStatusCaption);
            Controls.Add(panelStatus);
            Controls.Add(dividerTop);
            Controls.Add(labelGeneralCaption);
            Controls.Add(panelGeneral);
            Controls.Add(labelUpdateCaption);
            Controls.Add(panelUpdate);
            Controls.Add(dividerBottom);
            Controls.Add(linkUninstall);
            Controls.Add(linkExit);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = null;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) CloseOrHide(); };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_statusTimer == null) return;

            if (Visible)
            {
                LoadCurrentSettings(); // drop any unsaved changes from a previous Cancel/X/Esc
                RefreshStatus();
                _statusTimer.Start();
            }
            else
            {
                _statusTimer.Stop();
            }
        }

        private void RefreshStatus()
        {
            string status = _getStatus?.Invoke() ?? string.Empty;
            labelStatus.Text = status;

            if (status.StartsWith("Connected to", StringComparison.Ordinal))
                labelStatus.ForeColor = Color.FromArgb(110, 200, 140);
            else if (status.StartsWith("Not started", StringComparison.Ordinal))
                labelStatus.ForeColor = ColorTextSecondary;
            else
                labelStatus.ForeColor = ColorDanger;

            if (AppUpdater.PendingVersion != null && btnCheckUpdate.Enabled && string.IsNullOrEmpty(labelUpdateStatus.Text))
            {
                labelUpdateStatus.Text = $"Version {AppUpdater.PendingVersion} is ready - it installs when you close the app.";
            }
        }

        private void CloseOrHide()
        {
            if (AppSettings.HasConfirmedSettings)
            {
                Hide();
            }
            else
            {
                _exitApp();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                CloseOrHide();
                return;
            }
            base.OnFormClosing(e);
        }

        private async void BtnCheckUpdate_Click(object sender, EventArgs e)
        {
            btnCheckUpdate.Enabled = false;
            labelUpdateStatus.ForeColor = ColorTextSecondary;
            labelUpdateStatus.Text = "Checking for updates...";

            UpdateCheckResult result;
            try
            {
                result = await AppUpdater.CheckForUpdatesInteractiveAsync(status =>
                {
                    if (labelUpdateStatus.InvokeRequired)
                        labelUpdateStatus.Invoke(new Action(() => labelUpdateStatus.Text = status));
                    else
                        labelUpdateStatus.Text = status;
                }, _updateRestartArgs);
            }
            catch (Exception ex)
            {
                Logger.Log("BtnCheckUpdate_Click failed: " + ex);
                result = UpdateCheckResult.Error;
            }

            switch (result)
            {
                case UpdateCheckResult.UpToDate:
                    labelUpdateStatus.Text = "You're up to date.";
                    break;
                case UpdateCheckResult.NotInstalled:
                    labelUpdateStatus.ForeColor = ColorDanger;
                    labelUpdateStatus.Text = "Not available in dev builds.";
                    break;
                case UpdateCheckResult.Error:
                    labelUpdateStatus.ForeColor = ColorDanger;
                    labelUpdateStatus.Text = "Check failed — see log file.";
                    break;
                case UpdateCheckResult.Updating:
                    break;
            }

            btnCheckUpdate.Enabled = true;
        }

        private void LoadCurrentSettings()
        {
            chkDisable.Checked = AppSettings.SavedDisableSteelSeries;
            chkStartWithWindows.Checked = AppSettings.StartWithWindows;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            try
            {
                AppSettings.SaveConfig(chkDisable.Checked);
                AppSettings.StartWithWindows = chkStartWithWindows.Checked;

                AppSettings.MarkSettingsConfirmed();

                _applySteelSeriesSetting(chkDisable.Checked);

                _ensureWatcherStarted?.Invoke();

                MessageBox.Show(
                    "Settings saved. Discord auto-mute keeps running in the background.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Log("BtnOk_Click failed: " + ex);
                MessageBox.Show("Settings did not save successfully. See the log file for details.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Hide();
        }
    }
}
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
    internal partial class Form1 : ThemedForm
    {
        private WrapLabel labelInstructions;
        private ModernCheckBox chkDisable;
        private ModernCheckBox chkStartWithWindows;
        private ModernButton btnOk;
        private ModernButton btnCancel;
        private ModernButton btnCheckUpdate;
        private WrapLabel labelUpdateStatus;
        private ThemedLink linkSkipUpdate;
        private string _shownUpdateVersion;
        private ThemedLink linkUninstall;
        private ThemedLink linkExit;
        private WrapLabel labelStatus;
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
            InitializeCustomComponents();
            LoadCurrentSettings();
        }

        private static string GetVersionText()
        {
            try
            {
                var v = typeof(Form1).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "?"; }
        }

        private void InitializeCustomComponents()
        {
            Text = "SteelSeries Discord Stream Fix \u2014 Settings (v" + GetVersionText() + ")";
            DesignWidth = 480;
            DesignMinWidth = 420;

            // ---- Body: header, intro, three titled cards. Nothing here has a fixed position or size. ----
            Body.Add(new PageHeader { Text = "SteelSeries Discord Stream Fix" });

            labelInstructions = Ui.Text(
                "Running in the background: Discord is auto-muted the instant it opens a " +
                $"session on \"{MuteWatcherService.MicEndpointNameFragment}\", and re-muted if it is un-muted in the first seconds " +
                "(a manual un-mute later is respected). There's no tray icon \u2014 reopen this window any time by " +
                "running the app again.");
            Body.Add(labelInstructions, 14);

            // Status
            Body.Add(Ui.Caption("STATUS"), 22);
            labelStatus = Ui.Small(string.Empty);
            btnOpenLog = Ui.Button("Open log", ButtonKind.Secondary);
            btnOpenLog.MinWidthDesign = 88;
            btnOpenLog.MinHeightDesign = 32;
            btnOpenLog.Click += (s, e) => Logger.OpenLogFile();
            var statusRow = new ActionRow { Gap = 14 };
            statusRow.SetFill(labelStatus);
            statusRow.AddRight(btnOpenLog);
            var statusCard = new Card();
            statusCard.Add(statusRow);
            Body.Add(statusCard, 6);

            _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _statusTimer.Tick += (s, e) => RefreshStatus();

            // Preferences
            Body.Add(Ui.Caption("PREFERENCES"), 20);
            chkDisable = Ui.Check("Also hide Sonar Chat && Gaming (optional)");
            chkStartWithWindows = Ui.Check("Start automatically when I log in");
            Tips.SetToolTip(chkDisable,
                "Optional. SteelSeries recommends keeping Discord's output on Sonar - Chat; hiding it " +
                "can cut Discord's audio. Turn this off if Discord goes silent.");
            var prefsCard = new Card();
            prefsCard.Add(chkDisable);
            prefsCard.Add(chkStartWithWindows, 10);
            Body.Add(prefsCard, 6);

            // Updates
            Body.Add(Ui.Caption("UPDATES"), 20);
            btnCheckUpdate = Ui.Button("Check for Updates", ButtonKind.Secondary);
            btnCheckUpdate.MinWidthDesign = 150;
            btnCheckUpdate.Click += BtnCheckUpdate_Click;

            labelUpdateStatus = Ui.Small(string.Empty);
            linkSkipUpdate = Ui.Link("Skip this version", Theme.TextSecondary, Theme.TextPrimary, Theme.Small);
            linkSkipUpdate.Collapsed = true;
            linkSkipUpdate.LinkClicked += (s, e) =>
            {
                AppUpdater.SkipAvailable();
                RefreshUpdateUi();
            };
            var updateText = new VStack();
            updateText.Add(labelUpdateStatus);
            updateText.Add(linkSkipUpdate, 4, false);

            var updateRow = new ActionRow { Gap = 14 };
            updateRow.AddLeft(btnCheckUpdate);
            updateRow.SetFill(updateText);
            var updateCard = new Card();
            updateCard.Add(updateRow);
            Body.Add(updateCard, 6);

            // ---- Footer: pinned to the bottom of the window. ----
            Footer.Gap = 14;
            linkUninstall = Ui.Link("Uninstall...", Theme.Danger, Theme.Danger);
            linkUninstall.LinkClicked += (s, e) => _uninstallApp();

            linkExit = Ui.Link("Exit app", Theme.TextSecondary, Theme.TextPrimary);
            // Really quits: un-mutes Discord and brings back the Sonar devices we hid.
            linkExit.LinkClicked += (s, e) => _exitApp();

            btnCancel = Ui.Button("Cancel", ButtonKind.Secondary);
            btnCancel.Click += (s, e) => CloseOrHide();

            btnOk = Ui.Button("Save", ButtonKind.Primary);
            btnOk.Click += BtnOk_Click;

            Footer.AddLeft(linkUninstall);
            Footer.AddLeft(linkExit);
            Footer.AddRight(btnCancel);
            Footer.AddRight(btnOk);

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
                labelStatus.ForeColor = Theme.Success;
            else if (status.StartsWith("Not started", StringComparison.Ordinal) ||
                     status.StartsWith("Starting", StringComparison.Ordinal))
                labelStatus.ForeColor = Theme.TextSecondary;
            else
                labelStatus.ForeColor = Theme.Danger;

            // An update found by the background check shows up here without the user pressing anything.
            if (btnCheckUpdate.Enabled && AppUpdater.AvailableVersion != _shownUpdateVersion)
            {
                RefreshUpdateUi();
            }
        }

        /// <summary>Shows "Update to X" (+ Skip) while a newer version is available, otherwise the normal check button.</summary>
        private void RefreshUpdateUi()
        {
            string v = AppUpdater.AvailableVersion;
            _shownUpdateVersion = v;

            if (v == null)
            {
                btnCheckUpdate.Text = "Check for Updates";
                linkSkipUpdate.Collapsed = true;
                return;
            }

            bool skipped = AppUpdater.IsSkipped(v);
            btnCheckUpdate.Text = "Update to " + v;
            labelUpdateStatus.ForeColor = Theme.TextSecondary;
            labelUpdateStatus.Text = skipped ? v + " available (skipped)." : "Version " + v + " is available.";
            linkSkipUpdate.Collapsed = skipped;
        }

        private void SetUpdateStatus(string text)
        {
            try
            {
                if (IsDisposed || labelUpdateStatus.IsDisposed) return;

                if (labelUpdateStatus.InvokeRequired)
                {
                    // BeginInvoke: never block the calling (worker) thread on a window that may be closing.
                    if (!labelUpdateStatus.IsHandleCreated) return;
                    labelUpdateStatus.BeginInvoke(new Action(() =>
                    {
                        if (!labelUpdateStatus.IsDisposed) labelUpdateStatus.Text = text;
                    }));
                }
                else
                {
                    labelUpdateStatus.Text = text;
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void CloseOrHide()
        {
            if (AppSettings.HasConfirmedSettings)
            {
                Hide();
            }
            else
            {
                // First run, nothing saved yet: closing quits the app, so the user must know it will NOT protect Discord.
                var answer = ThemedMessageBox.Show(this,
                    "You haven't pressed Save yet, so Discord is not being protected.\n\n" +
                    "Close anyway? The app will quit. Run it again and press Save to turn protection on.",
                    "SteelSeries Discord Stream Fix",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Yes) _exitApp();
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
            linkSkipUpdate.Collapsed = true;
            labelUpdateStatus.ForeColor = Theme.TextSecondary;

            // A newer version is already known: this click means "install it".
            if (AppUpdater.AvailableVersion != null)
            {
                bool ok = await AppUpdater.DownloadAndApplyAsync(SetUpdateStatus, _updateRestartArgs);
                if (ok) return; // the app is closing and restarts as the new version

                labelUpdateStatus.ForeColor = Theme.Danger;
                labelUpdateStatus.Text = "Update failed — see log file.";
                btnCheckUpdate.Enabled = true;
                return;
            }

            labelUpdateStatus.Text = "Checking for updates...";

            UpdateCheckResult result;
            try
            {
                result = await AppUpdater.CheckForUpdateAsync(false, SetUpdateStatus);
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
                case UpdateCheckResult.UpdateAvailable:
                    RefreshUpdateUi();
                    break;
                case UpdateCheckResult.NotInstalled:
                    labelUpdateStatus.ForeColor = Theme.Danger;
                    labelUpdateStatus.Text = "Not available in dev builds.";
                    break;
                case UpdateCheckResult.Error:
                    labelUpdateStatus.ForeColor = Theme.Danger;
                    labelUpdateStatus.Text = "Check failed — see log file.";
                    break;
            }

            btnCheckUpdate.Enabled = true;
        }

        private void LoadCurrentSettings()
        {
            chkDisable.Checked = AppSettings.SavedDisableSteelSeries;
            // Before the user has pressed Save once, the box is pre-ticked as the suggested default
            // ("install once and forget"). Nothing is registered with Windows until they confirm with Save.
            chkStartWithWindows.Checked = AppSettings.StartWithWindows || !AppSettings.HasConfirmedSettings;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            try
            {
                AppSettings.SaveConfig(chkDisable.Checked);
                AppSettings.StartWithWindows = chkStartWithWindows.Checked;

                AppSettings.MarkSettingsConfirmed();

                // Hiding/showing devices talks to the audio engine and can be slow: never on the UI thread.
                // If it fails, the app shows a warning notification by itself.
                bool hideDevices = chkDisable.Checked;
                Task.Run(() =>
                {
                    try { _applySteelSeriesSetting(hideDevices); }
                    catch (Exception ex) { Logger.Log("Apply after Save failed: " + ex); }
                });

                _ensureWatcherStarted?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Log("BtnOk_Click failed: " + ex);
                ThemedMessageBox.Show(this, "Settings did not save successfully. See the log file for details.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Hide();
                return;
            }

            // No extra "OK" dialog: the Save button itself says "Saved" for a moment, then the window hides.
            string originalText = btnOk.Text;
            btnOk.Text = "Saved";
            btnOk.Enabled = false;
            btnCancel.Enabled = false;

            var hideTimer = new System.Windows.Forms.Timer { Interval = 1100 };
            hideTimer.Tick += (s, ev) =>
            {
                hideTimer.Stop();
                hideTimer.Dispose();
                if (IsDisposed) return;
                btnOk.Text = originalText;
                btnOk.Enabled = true;
                btnCancel.Enabled = true;
                Hide();
            };
            hideTimer.Start();
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
    /// <summary>
    /// Opened when the user clicks the "update available" Windows notification.
    /// Three choices: Update now, Skip this version (no more notifications for it), Later (close, nothing changes).
    /// </summary>
    internal sealed class UpdatePromptForm : ThemedForm
    {
        private readonly string[] _restartArgs;
        private readonly WrapLabel _message;
        private readonly ModernButton _btnUpdate;
        private readonly ModernButton _btnSkip;
        private readonly ModernButton _btnLater;
        private bool _busy;

        public UpdatePromptForm(string[] restartArgs)
        {
            _restartArgs = restartArgs ?? Array.Empty<string>();
            string version = AppUpdater.AvailableVersion ?? "?";

            Text = "SteelSeries Discord Stream Fix \u2014 Update available";
            TopMost = true;
            DesignWidth = 480;
            DesignMinWidth = 380;
            DesignMinBodyHeight = 100;

            Body.Add(new PageHeader { Text = "Version " + version + " is available" });

            _message = Ui.Text(
                "You are running " + CurrentVersionText() + ". Updating takes a few seconds: the app closes, " +
                "installs the new version and starts again. Nothing is installed unless you choose Update now.");
            Body.Add(_message, 14);

            _btnLater = Ui.Button("Later", ButtonKind.Secondary);
            _btnLater.Click += (s, e) => { if (!_busy) Close(); };

            _btnSkip = Ui.Button("Skip this version", ButtonKind.Secondary);
            _btnSkip.Click += (s, e) =>
            {
                if (_busy) return;
                AppUpdater.SkipAvailable();
                Close();
            };

            _btnUpdate = Ui.Button("Update now", ButtonKind.Primary);
            _btnUpdate.Click += BtnUpdate_Click;

            Footer.AddRight(_btnLater);
            Footer.AddRight(_btnSkip);
            Footer.AddRight(_btnUpdate);

            AcceptButton = _btnUpdate;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !_busy) Close(); };
        }

        private static string CurrentVersionText()
        {
            try
            {
                var v = typeof(UpdatePromptForm).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "an older version"; }
        }

        private async void BtnUpdate_Click(object sender, EventArgs e)
        {
            _busy = true;
            _btnUpdate.Enabled = false;
            _btnSkip.Enabled = false;
            _btnLater.Enabled = false;
            _message.ForeColor = Theme.TextSecondary;

            bool ok = await AppUpdater.DownloadAndApplyAsync(SetMessage, _restartArgs);
            if (ok) return; // the app is closing and restarts as the new version

            _busy = false;
            _message.ForeColor = Theme.Danger;
            _message.Text = "The update could not be installed. The app keeps running as before. See the log file for details.";
            _btnUpdate.Enabled = true;
            _btnSkip.Enabled = true;
            _btnLater.Enabled = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Do not let the dialog disappear while the download/install is running.
            if (_busy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        private void SetMessage(string text)
        {
            try
            {
                if (IsDisposed || _message.IsDisposed) return;

                if (_message.InvokeRequired)
                {
                    if (!_message.IsHandleCreated) return;
                    _message.BeginInvoke(new Action(() =>
                    {
                        if (!_message.IsDisposed) _message.Text = text;
                    }));
                }
                else
                {
                    _message.Text = text;
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}

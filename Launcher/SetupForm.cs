using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix.Launcher
{
    /// <summary>
    /// The options window shown first when the app is not installed yet. Nothing is installed until Save.
    /// </summary>
    internal sealed class SetupForm : ThemedForm
    {
        private readonly string _versionText;
        private readonly ModernCheckBox _chkHide;
        private readonly ModernCheckBox _chkStart;
        private readonly ModernButton _btnSave;
        private readonly ModernButton _btnCancel;
        private readonly WrapLabel _status;
        private bool _busy;

        /// <summary>True when the install finished successfully.</summary>
        public bool Installed { get; private set; }

        public SetupForm(string versionText)
        {
            _versionText = versionText;

            Text = "SteelSeries Discord Stream Fix \u2014 Setup (v" + versionText + ")";
            DesignWidth = 480;
            DesignMinWidth = 400;

            Body.Add(new PageHeader { Text = "SteelSeries Discord Stream Fix" });

            Body.Add(Ui.Text(
                "Keeps Discord out of Sonar's virtual microphone. Choose your options and press Save: " +
                "the app is installed for your Windows account only (no admin rights) and starts " +
                "protecting Discord right away."), 14);

            Body.Add(Ui.Caption("PREFERENCES"), 22);

            _chkHide = Ui.Check("Also hide Sonar Chat && Gaming (optional)", false);
            _chkStart = Ui.Check("Start automatically when I log in", true); // same suggested default as the app's own first run
            Tips.SetToolTip(_chkHide,
                "Optional. SteelSeries recommends keeping Discord's output on Sonar - Chat; hiding it " +
                "can cut Discord's audio. Turn this off if Discord goes silent.");
            var card = new Card();
            card.Add(_chkHide);
            card.Add(_chkStart, 10);
            Body.Add(card, 6);

            _status = Ui.Small("Version " + versionText + " \u00B7 nothing is installed until you press Save");
            Body.Add(_status, 14);

            _btnCancel = Ui.Button("Cancel", ButtonKind.Secondary);
            _btnCancel.Click += (s, e) => { if (!_busy) Close(); };

            _btnSave = Ui.Button("Save", ButtonKind.Primary);
            _btnSave.Click += BtnSave_Click;

            Footer.AddRight(_btnCancel);
            Footer.AddRight(_btnSave);

            AcceptButton = _btnSave;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !_busy) Close(); };
        }

        private async void BtnSave_Click(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            _btnSave.Enabled = false;
            _btnCancel.Enabled = false;
            _chkHide.Enabled = false;
            _chkStart.Enabled = false;
            _status.ForeColor = Theme.TextSecondary;
            _status.Text = "Installing version " + _versionText + "...";

            var settings = new LauncherSettings
            {
                HideSonarChatAndGaming = _chkHide.Checked,
                StartWithWindows = _chkStart.Checked
            };

            Action<string> progress = msg =>
            {
                try { BeginInvoke(new Action(() => _status.Text = msg)); } catch { }
            };
            string error = await Task.Run(() => Installer.Install(settings, progress));

            _busy = false;
            if (error == null)
            {
                Installed = true;
                Close();
                return;
            }

            _status.ForeColor = Theme.Danger;
            _status.Text = error;
            _btnSave.Enabled = true;
            _btnCancel.Enabled = true;
            _chkHide.Enabled = true;
            _chkStart.Enabled = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; // installing: do not abandon halfway
            base.OnFormClosing(e);
        }
    }
}

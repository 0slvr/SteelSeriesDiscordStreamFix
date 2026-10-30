using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix.Launcher
{
    /// <summary>
    /// The options window shown first when the app is not installed yet. Nothing is installed until Save.
    /// </summary>
    internal sealed class SetupForm : Form
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

        private readonly string _versionText;
        private readonly ModernCheckBox _chkHide;
        private readonly ModernCheckBox _chkStart;
        private readonly ModernButton _btnSave;
        private readonly ModernButton _btnCancel;
        private readonly Label _status;
        private bool _busy;

        /// <summary>True when the install finished successfully.</summary>
        public bool Installed { get; private set; }

        public SetupForm(string versionText)
        {
            _versionText = versionText;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(480, 344);
            Text = "SteelSeries Discord Stream Fix — Setup (v" + versionText + ")";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            BackColor = ColorBackground;
            Font = new Font("Segoe UI", 9F);

            var accentBar = new RoundedPanel
            {
                Location = new Point(32, 28),
                Size = new Size(4, 32),
                FillColor = ColorAccent,
                CornerRadius = 2
            };

            var title = new Label
            {
                AutoSize = false,
                Location = new Point(48, 24),
                Size = new Size(400, 32),
                Text = "SteelSeries Discord Stream Fix",
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            var intro = new Label
            {
                AutoSize = false,
                Location = new Point(32, 70),
                Size = new Size(416, 60),
                Text = "Keeps Discord out of Sonar's virtual microphone. Choose your options and press Save: " +
                       "the app is installed for your Windows account only (no admin rights) and starts " +
                       "protecting Discord right away.",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 9.5F),
                BackColor = Color.Transparent
            };

            var caption = new Label
            {
                AutoSize = false,
                Location = new Point(32, 140),
                Size = new Size(300, 16),
                Text = "PREFERENCES",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            var panel = new RoundedPanel
            {
                Location = new Point(32, 160),
                Size = new Size(416, 88),
                FillColor = ColorSurface,
                CornerRadius = 14
            };

            _chkHide = new ModernCheckBox
            {
                Location = new Point(16, 14),
                Size = new Size(384, 24),
                Text = "Also hide Sonar Chat && Gaming (optional)",
                AccentColor = ColorAccent,
                BorderColor = ColorBorder,
                TextColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9.5F),
                Checked = false
            };
            _chkStart = new ModernCheckBox
            {
                Location = new Point(16, 48),
                Size = new Size(384, 24),
                Text = "Start automatically when I log in",
                AccentColor = ColorAccent,
                BorderColor = ColorBorder,
                TextColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9.5F),
                Checked = true // same suggested default as the app's own first run
            };
            new ToolTip().SetToolTip(_chkHide,
                "Optional. SteelSeries recommends keeping Discord's output on Sonar - Chat; hiding it " +
                "can cut Discord's audio. Turn this off if Discord goes silent.");
            panel.Controls.Add(_chkHide);
            panel.Controls.Add(_chkStart);

            _status = new Label
            {
                AutoSize = false,
                Location = new Point(32, 260),
                Size = new Size(416, 20),
                Text = "Version " + versionText + " · nothing is installed until you press Save",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                BackColor = Color.Transparent
            };

            _btnCancel = new ModernButton
            {
                Text = "Cancel",
                Size = new Size(90, 36),
                Location = new Point(238, 292),
                BaseColor = ColorSurface,
                HoverColor = ColorSurfaceHover,
                TextColor = ColorTextPrimary,
                BorderColor = ColorBorder
            };
            _btnCancel.Click += (s, e) => { if (!_busy) Close(); };

            _btnSave = new ModernButton
            {
                Text = "Save",
                Size = new Size(108, 36),
                Location = new Point(340, 292),
                BaseColor = ColorAccent,
                HoverColor = ColorAccentHover,
                TextColor = Color.White
            };
            _btnSave.Click += BtnSave_Click;

            Controls.Add(accentBar);
            Controls.Add(title);
            Controls.Add(intro);
            Controls.Add(caption);
            Controls.Add(panel);
            Controls.Add(_status);
            Controls.Add(_btnCancel);
            Controls.Add(_btnSave);

            AcceptButton = _btnSave;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !_busy) Close(); };
            ResumeLayout(false);
        }

        private async void BtnSave_Click(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            _btnSave.Enabled = false;
            _btnCancel.Enabled = false;
            _chkHide.Enabled = false;
            _chkStart.Enabled = false;
            _status.ForeColor = ColorTextSecondary;
            _status.Text = "Installing version " + _versionText + "...";

            var settings = new LauncherSettings
            {
                HideSonarChatAndGaming = _chkHide.Checked,
                StartWithWindows = _chkStart.Checked
            };

            string error = await Task.Run(() => Installer.Install(settings));

            _busy = false;
            if (error == null)
            {
                Installed = true;
                Close();
                return;
            }

            _status.ForeColor = ColorDanger;
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
                int rounded = 2;
                DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}

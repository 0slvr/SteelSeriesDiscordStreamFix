using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
    /// <summary>
    /// Opened when the user clicks the "update available" Windows notification.
    /// Three choices: Update now, Skip this version (no more notifications for it), Later (close, nothing changes).
    /// </summary>
    internal sealed class UpdatePromptForm : Form
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

        private readonly string[] _restartArgs;
        private readonly Label _message;
        private readonly ModernButton _btnUpdate;
        private readonly ModernButton _btnSkip;
        private readonly ModernButton _btnLater;
        private bool _busy;

        public UpdatePromptForm(string[] restartArgs)
        {
            _restartArgs = restartArgs ?? Array.Empty<string>();
            string version = AppUpdater.AvailableVersion ?? "?";

            SuspendLayout();
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(480, 220);
            Text = "SteelSeries Discord Stream Fix — Update available";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
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
                Text = "Version " + version + " is available",
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            _message = new Label
            {
                AutoSize = false,
                Location = new Point(32, 76),
                Size = new Size(416, 60),
                Text = "You are running " + CurrentVersionText() + ". Updating takes a few seconds: the app closes, " +
                       "installs the new version and starts again. Nothing is installed unless you choose Update now.",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 9.5F),
                BackColor = Color.Transparent
            };

            _btnLater = new ModernButton
            {
                Text = "Later",
                Size = new Size(90, 36),
                Location = new Point(32, 164),
                BaseColor = ColorSurface,
                HoverColor = ColorSurfaceHover,
                TextColor = ColorTextPrimary,
                BorderColor = ColorBorder
            };
            _btnLater.Click += (s, e) => { if (!_busy) Close(); };

            _btnSkip = new ModernButton
            {
                Text = "Skip this version",
                Size = new Size(140, 36),
                Location = new Point(176, 164),
                BaseColor = ColorSurface,
                HoverColor = ColorSurfaceHover,
                TextColor = ColorTextPrimary,
                BorderColor = ColorBorder
            };
            _btnSkip.Click += (s, e) =>
            {
                if (_busy) return;
                AppUpdater.SkipAvailable();
                Close();
            };

            _btnUpdate = new ModernButton
            {
                Text = "Update now",
                Size = new Size(120, 36),
                Location = new Point(328, 164),
                BaseColor = ColorAccent,
                HoverColor = ColorAccentHover,
                TextColor = Color.White
            };
            _btnUpdate.Click += BtnUpdate_Click;

            Controls.Add(accentBar);
            Controls.Add(title);
            Controls.Add(_message);
            Controls.Add(_btnLater);
            Controls.Add(_btnSkip);
            Controls.Add(_btnUpdate);

            AcceptButton = _btnUpdate;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !_busy) Close(); };
            ResumeLayout(false);
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
            _message.ForeColor = ColorTextSecondary;

            bool ok = await AppUpdater.DownloadAndApplyAsync(status => _message.Text = status, _restartArgs);
            if (ok) return; // the app is closing and restarts as the new version

            _busy = false;
            _message.ForeColor = ColorDanger;
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

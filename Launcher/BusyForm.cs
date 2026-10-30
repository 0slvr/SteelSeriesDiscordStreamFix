using System;
using System.Drawing;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix.Launcher
{
    /// <summary>Small "please wait" window shown while an update runs.</summary>
    internal sealed class BusyForm : Form
    {
        public BusyForm(string text)
        {
            Text = "SteelSeries Discord Stream Fix";
            ClientSize = new Size(380, 90);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            BackColor = Color.FromArgb(22, 22, 29);
            Font = new Font("Segoe UI", 9.5F);

            Controls.Add(new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = Color.FromArgb(240, 240, 244),
                TextAlign = ContentAlignment.MiddleCenter
            });
        }
    }
}

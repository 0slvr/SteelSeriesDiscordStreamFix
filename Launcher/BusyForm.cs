using System;
using System.Drawing;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix.Launcher
{
    /// <summary>Small "please wait" window shown while an update runs (same look as every other window).</summary>
    internal sealed class BusyForm : ThemedForm
    {
        public BusyForm(string text)
        {
            Text = "SteelSeries Discord Stream Fix";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            DesignWidth = 400;
            DesignMinWidth = 300;
            Body.Inset = new Padding(28, 28, 28, 28);

            Body.Add(new WrapLabel { Text = text, Centered = true, Font = Theme.Body, ForeColor = Theme.TextPrimary });
            Body.Add(new ProgressStrip(), 18);
        }
    }
}

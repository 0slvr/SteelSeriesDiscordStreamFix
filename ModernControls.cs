using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
    internal static class DpiHelper
    {
        /// <summary>1.0 at 100% display scaling, 1.5 at 150%, ... for the monitor the control is on.</summary>
        public static float Scale(Control c)
        {
            try { return Math.Max(1f, c.DeviceDpi / 96f); }
            catch { return 1f; }
        }
    }

    internal sealed class ModernButton : Button
    {
        public Color BaseColor { get; set; } = Color.FromArgb(94, 106, 246);
        public Color HoverColor { get; set; } = Color.FromArgb(112, 123, 250);
        public Color TextColor { get; set; } = Color.White;
        public Color? BorderColor { get; set; } = null;
        private bool _hover;
        private bool _pressed;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            BackColor = Color.Transparent;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; _pressed = false; Invalidate(); };
            MouseDown += (s, e) => { _pressed = true; Invalidate(); };
            MouseUp += (s, e) => { _pressed = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);

            Color fill = _pressed ? ControlPaint.Dark(HoverColor, 0.05f) : (_hover ? HoverColor : BaseColor);
            float s = DpiHelper.Scale(this);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min((int)Math.Round(10 * s), Height / 2);

            using (var path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (BorderColor.HasValue)
                {
                    using (var pen = new Pen(BorderColor.Value, Math.Max(1f, s)))
                        g.DrawPath(pen, path);
                }
            }

            // Keyboard focus (Tab): the button is custom-painted, so Windows draws no focus cue by itself.
            if (Focused && ShowFocusCues)
            {
                int inset = (int)Math.Round(3 * s);
                var focusRect = Rectangle.Inflate(rect, -inset, -inset);
                if (focusRect.Width > 4 && focusRect.Height > 4)
                {
                    using (var focusPath = RoundedRect(focusRect, Math.Max(2, radius - inset)))
                    using (var pen = new Pen(TextColor, Math.Max(1f, s)) { DashStyle = DashStyle.Dot })
                        g.DrawPath(pen, focusPath);
                }
            }

            TextRenderer.DrawText(g, Text, Font, rect, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ModernCheckBox : CheckBox
    {
        public Color AccentColor { get; set; } = Color.FromArgb(94, 106, 246);
        public Color BorderColor { get; set; } = Color.FromArgb(58, 58, 72);
        public Color TextColor { get; set; } = Color.White;

        private const int BaseBoxSize = 18; // at 100% scaling

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);

            // Everything is drawn in "100% pixels" and multiplied by the display scale, so the box and the
            // tick stay in proportion to the (already scaled) text on 125% / 150% / 200% screens.
            float s = DpiHelper.Scale(this);
            int boxSize = (int)Math.Round(BaseBoxSize * s);
            int boxY = (Height - boxSize) / 2;
            var boxRect = new Rectangle(0, boxY, boxSize, boxSize);

            using (var path = RoundedRect(boxRect, (int)Math.Round(4 * s)))
            {
                if (Checked)
                {
                    using (var brush = new SolidBrush(AccentColor))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var pen = new Pen(BorderColor, 1.5f * s))
                        g.DrawPath(pen, path);
                }
            }

            if (Checked)
            {
                using (var pen = new Pen(Color.White, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(boxRect.X + 4 * s, boxRect.Y + 9 * s),
                        new PointF(boxRect.X + 7 * s, boxRect.Y + 13 * s),
                        new PointF(boxRect.X + 14 * s, boxRect.Y + 5 * s)
                    });
                }
            }

            // Keyboard focus (Tab): ring around the box, since the control is custom-painted.
            if (Focused && ShowFocusCues)
            {
                int pad = (int)Math.Round(3 * s);
                var ring = Rectangle.Inflate(boxRect, pad, pad);
                using (var ringPath = RoundedRect(ring, (int)Math.Round(7 * s)))
                using (var pen = new Pen(AccentColor, Math.Max(1f, s)))
                    g.DrawPath(pen, ringPath);
            }

            int textLeft = boxSize + (int)Math.Round(10 * s);
            var textRect = new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        private Color _fillColor = Color.FromArgb(38, 38, 48);

        public Color FillColor
        {
            get => _fillColor;
            set { _fillColor = value; BackColor = value; }
        }

        public Color? BorderColor { get; set; } = null;
        public int CornerRadius { get; set; } = 12;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = _fillColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);

            float s = DpiHelper.Scale(this);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(rect, (int)Math.Round(CornerRadius * s)))
            using (var brush = new SolidBrush(FillColor))
            {
                g.FillPath(brush, path);
                if (BorderColor.HasValue)
                {
                    using (var pen = new Pen(BorderColor.Value, Math.Max(1f, s)))
                        g.DrawPath(pen, path);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
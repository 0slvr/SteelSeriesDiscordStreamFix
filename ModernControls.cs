using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
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
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min(10, Height / 2);

            using (var path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (BorderColor.HasValue)
                {
                    using (var pen = new Pen(BorderColor.Value, 1))
                        g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, Text, Font, rect, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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

    internal sealed class ModernCheckBox : CheckBox
    {
        public Color AccentColor { get; set; } = Color.FromArgb(94, 106, 246);
        public Color BorderColor { get; set; } = Color.FromArgb(58, 58, 72);
        public Color TextColor { get; set; } = Color.White;

        private const int BoxSize = 18;

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

            int boxY = (Height - BoxSize) / 2;
            var boxRect = new Rectangle(0, boxY, BoxSize, BoxSize);

            using (var path = RoundedRect(boxRect, 4))
            {
                if (Checked)
                {
                    using (var brush = new SolidBrush(AccentColor))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var pen = new Pen(BorderColor, 1.5f))
                        g.DrawPath(pen, path);
                }
            }

            if (Checked)
            {
                using (var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(boxRect.X + 4, boxRect.Y + 9),
                        new Point(boxRect.X + 7, boxRect.Y + 13),
                        new Point(boxRect.X + 14, boxRect.Y + 5)
                    });
                }
            }

            var textRect = new Rectangle(BoxSize + 10, 0, Width - BoxSize - 10, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Invalidate();
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

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(rect, CornerRadius))
            using (var brush = new SolidBrush(FillColor))
            {
                g.FillPath(brush, path);
                if (BorderColor.HasValue)
                {
                    using (var pen = new Pen(BorderColor.Value, 1))
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
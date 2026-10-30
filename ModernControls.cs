using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

// Shared UI kit. This single file is compiled into BOTH the app and the setup launcher
// (the launcher links it), so every window and dialog looks and behaves the same.
//
// Layout model (why the windows are "flexible"):
//   * No window uses fixed pixel coordinates any more. Content is stacked by VStack / ActionRow,
//     which ask every control for its preferred size at the width it is given.
//   * Text wraps (WrapLabel), so longer text, bigger fonts, translations and any display scaling
//     simply make the window taller instead of clipping.
//   * Windows can be resized; the body scrolls if the screen is too small, the buttons stay pinned.
//   * Numbers written in this kit (gaps, insets, min sizes) are "design pixels" at 100% scaling and are
//     multiplied by the current display scale when laid out.
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

        /// <summary>Converts a size written for 100% scaling into real pixels for this control's monitor.</summary>
        public static int Px(Control c, int designPixels)
        {
            return Scaled(designPixels, Scale(c));
        }

        public static int Scaled(int designPixels, float scale)
        {
            return (int)Math.Round(designPixels * scale);
        }
    }

    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(22, 22, 29);
        public static readonly Color Accent = Color.FromArgb(94, 106, 246);
        public static readonly Color AccentHover = Color.FromArgb(112, 123, 250);
        public static readonly Color TextPrimary = Color.FromArgb(240, 240, 244);
        public static readonly Color TextSecondary = Color.FromArgb(150, 150, 164);
        public static readonly Color Surface = Color.FromArgb(38, 38, 48);
        public static readonly Color SurfaceHover = Color.FromArgb(48, 48, 60);
        public static readonly Color Border = Color.FromArgb(58, 58, 72);
        public static readonly Color Danger = Color.FromArgb(220, 96, 96);
        public static readonly Color Success = Color.FromArgb(110, 200, 140);
        public static readonly Color Warning = Color.FromArgb(226, 168, 72);

        // Shared fonts: created once, live as long as the process, never disposed by controls.
        public static readonly Font Base = new Font("Segoe UI", 9F);
        public static readonly Font Body = new Font("Segoe UI", 9.5F);
        public static readonly Font Small = new Font("Segoe UI", 8.5F);
        public static readonly Font Caption = new Font("Segoe UI", 8F, FontStyle.Bold);
        public static readonly Font Title = new Font("Segoe UI", 15F, FontStyle.Bold);
        public static readonly Font ButtonFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        public static readonly Font Glyph = new Font("Segoe UI", 14F, FontStyle.Bold);

        /// <summary>amount 0 = from, 1 = to.</summary>
        public static Color Blend(Color from, Color to, float amount)
        {
            int r = (int)Math.Round(from.R + (to.R - from.R) * amount);
            int g = (int)Math.Round(from.G + (to.G - from.G) * amount);
            int b = (int)Math.Round(from.B + (to.B - from.B) * amount);
            return Color.FromArgb(r, g, b);
        }
    }

    internal static class Gfx
    {
        public static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            radius = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            if (radius == 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color BackOf(Control c)
        {
            return c.Parent != null ? c.Parent.BackColor : Theme.Background;
        }

        public static int ProposedWidth(Control c, Size proposed, int fallbackDesign)
        {
            if (proposed.Width > 0 && proposed.Width < 100000) return proposed.Width;
            return DpiHelper.Px(c, fallbackDesign);
        }
    }

    internal static class LayoutUtil
    {
        /// <summary>
        /// A control's preferred size changed (text, font, collapsed...). Re-run layout from the outermost
        /// container down so every level re-measures and the window content follows.
        /// </summary>
        public static void Refresh(Control c)
        {
            if (c == null) return;
            var chain = new List<Control>();
            for (Control p = c.Parent; p != null; p = p.Parent) chain.Add(p);
            for (int i = chain.Count - 1; i >= 0; i--) chain[i].PerformLayout();
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Layout containers
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Stacks its children top to bottom. Each child gets the full inner width (or its own preferred width when
    /// added with stretch: false) and the height it asks for at that width.
    /// Inset and the gap given to Add are in design pixels (scaled with the display).
    /// </summary>
    internal class VStack : Panel
    {
        private readonly StackLayout _engine = new StackLayout();
        private readonly HashSet<Control> _natural = new HashSet<Control>();
        private Padding _inset = Padding.Empty;

        public VStack()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Space around the content, in design pixels.</summary>
        public Padding Inset
        {
            get { return _inset; }
            set { _inset = value; PerformLayout(); }
        }

        public override LayoutEngine LayoutEngine { get { return _engine; } }

        /// <summary>Adds a child under the previous one. gapBefore is ignored for the first child.</summary>
        public void Add(Control child, int gapBefore = 0, bool stretch = true)
        {
            child.Margin = new Padding(0, gapBefore, 0, 0);
            if (!stretch) _natural.Add(child);
            Controls.Add(child);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 && proposedSize.Width < 100000
                ? proposedSize.Width
                : Math.Max(Width, DpiHelper.Px(this, 360));
            return new Size(width, Arrange(width, false));
        }

        private int Arrange(int width, bool apply)
        {
            float s = DpiHelper.Scale(this);
            int padL = DpiHelper.Scaled(_inset.Left, s);
            int padR = DpiHelper.Scaled(_inset.Right, s);
            int padT = DpiHelper.Scaled(_inset.Top, s);
            int padB = DpiHelper.Scaled(_inset.Bottom, s);
            int innerW = Math.Max(1, width - padL - padR);

            int y = padT;
            bool first = true;
            for (int i = 0; i < Controls.Count; i++)
            {
                Control c = Controls[i];
                Size pref = c.GetPreferredSize(new Size(innerW, 0));
                if (pref.Width == 0 && pref.Height == 0)
                {
                    // Collapsed child: takes no room.
                    if (apply) c.SetBounds(padL, y, 0, 0);
                    continue;
                }

                if (!first) y += DpiHelper.Scaled(c.Margin.Top, s);
                int w = _natural.Contains(c) ? Math.Min(pref.Width, innerW) : innerW;
                if (apply) c.SetBounds(padL, y, w, pref.Height);
                y += pref.Height;
                first = false;
            }
            return y + padB;
        }

        private void ApplyLayout()
        {
            Arrange(ClientSize.Width, true);
        }

        private sealed class StackLayout : LayoutEngine
        {
            public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
            {
                var stack = container as VStack;
                if (stack != null) stack.ApplyLayout();
                return false;
            }
        }
    }

    /// <summary>
    /// One horizontal row: items pinned to the left, one item that takes the remaining width (and wraps),
    /// items pinned to the right. Everything is centred vertically. If a row with only left + right items
    /// is too narrow, the right group drops to a second line instead of overlapping.
    /// </summary>
    internal sealed class ActionRow : Panel
    {
        private readonly RowLayout _engine = new RowLayout();
        private readonly List<Control> _left = new List<Control>();
        private readonly List<Control> _right = new List<Control>();
        private Control _fill;
        private Padding _inset = Padding.Empty;
        private int _gap = 10;
        private Color? _topBorder;

        public ActionRow()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Space around the content, in design pixels.</summary>
        public Padding Inset
        {
            get { return _inset; }
            set { _inset = value; PerformLayout(); }
        }

        /// <summary>Space between items, in design pixels.</summary>
        public int Gap
        {
            get { return _gap; }
            set { _gap = value; PerformLayout(); }
        }

        /// <summary>Optional 1px line along the top edge.</summary>
        public Color? TopBorder
        {
            get { return _topBorder; }
            set { _topBorder = value; Invalidate(); }
        }

        public override LayoutEngine LayoutEngine { get { return _engine; } }

        public void AddLeft(Control c) { _left.Add(c); Controls.Add(c); }
        public void AddRight(Control c) { _right.Add(c); Controls.Add(c); }
        public void SetFill(Control c) { _fill = c; Controls.Add(c); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 && proposedSize.Width < 100000
                ? proposedSize.Width
                : Math.Max(Width, DpiHelper.Px(this, 360));
            int height = Controls.Count == 0 ? 0 : Arrange(width, false);
            return new Size(width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_topBorder.HasValue && Height > 0)
            {
                using (var pen = new Pen(_topBorder.Value, Math.Max(1f, DpiHelper.Scale(this))))
                    e.Graphics.DrawLine(pen, 0, 0, Width, 0);
            }
        }

        private int Arrange(int width, bool apply)
        {
            float s = DpiHelper.Scale(this);
            int padL = DpiHelper.Scaled(_inset.Left, s);
            int padR = DpiHelper.Scaled(_inset.Right, s);
            int padT = DpiHelper.Scaled(_inset.Top, s);
            int padB = DpiHelper.Scaled(_inset.Bottom, s);
            int gap = DpiHelper.Scaled(_gap, s);
            int innerW = Math.Max(1, width - padL - padR);

            Size[] ls = Preferred(_left, innerW);
            Size[] rs = Preferred(_right, innerW);
            int lw = TotalWidth(ls, gap);
            int rw = TotalWidth(rs, gap);
            int lh = MaxHeight(ls);
            int rh = MaxHeight(rs);

            if (_fill == null && lw > 0 && rw > 0 && lw + gap + rw > innerW)
            {
                int line2 = padT + lh + gap;
                if (apply)
                {
                    Place(_left, ls, padL, padT, lh, gap);
                    Place(_right, rs, padL + innerW - rw, line2, rh, gap);
                }
                return line2 + rh + padB;
            }

            int fillW = 0;
            Size fs = Size.Empty;
            if (_fill != null)
            {
                int used = lw + rw + (lw > 0 ? gap : 0) + (rw > 0 ? gap : 0);
                fillW = Math.Max(1, innerW - used);
                fs = _fill.GetPreferredSize(new Size(fillW, 0));
            }

            int rowH = Math.Max(Math.Max(lh, rh), fs.Height);
            if (apply)
            {
                Place(_left, ls, padL, padT, rowH, gap);
                if (_fill != null)
                {
                    int fx = padL + lw + (lw > 0 ? gap : 0);
                    _fill.SetBounds(fx, padT + (rowH - fs.Height) / 2, fillW, fs.Height);
                }
                Place(_right, rs, padL + innerW - rw, padT, rowH, gap);
            }
            return padT + rowH + padB;
        }

        private static Size[] Preferred(List<Control> items, int innerW)
        {
            var sizes = new Size[items.Count];
            for (int i = 0; i < items.Count; i++)
                sizes[i] = items[i].GetPreferredSize(new Size(innerW, 0));
            return sizes;
        }

        private static int TotalWidth(Size[] sizes, int gap)
        {
            int total = 0;
            int count = 0;
            for (int i = 0; i < sizes.Length; i++)
            {
                if (sizes[i].IsEmpty) continue;
                total += sizes[i].Width;
                count++;
            }
            return count > 1 ? total + gap * (count - 1) : total;
        }

        private static int MaxHeight(Size[] sizes)
        {
            int h = 0;
            for (int i = 0; i < sizes.Length; i++) h = Math.Max(h, sizes[i].Height);
            return h;
        }

        private static void Place(List<Control> items, Size[] sizes, int x, int top, int lineHeight, int gap)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (sizes[i].IsEmpty)
                {
                    items[i].SetBounds(x, top, 0, 0);
                    continue;
                }
                items[i].SetBounds(x, top + (lineHeight - sizes[i].Height) / 2, sizes[i].Width, sizes[i].Height);
                x += sizes[i].Width + gap;
            }
        }

        private void ApplyLayout()
        {
            Arrange(ClientSize.Width, true);
        }

        private sealed class RowLayout : LayoutEngine
        {
            public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
            {
                var row = container as ActionRow;
                if (row != null) row.ApplyLayout();
                return false;
            }
        }
    }

    /// <summary>A rounded "surface" that stacks its children like VStack.</summary>
    internal sealed class Card : VStack
    {
        private Color _fillColor = Theme.Surface;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = _fillColor;
            Inset = new Padding(16, 14, 16, 14);
        }

        public Color FillColor
        {
            get { return _fillColor; }
            set { _fillColor = value; BackColor = value; }
        }

        public int CornerRadius { get; set; } = 14;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Gfx.BackOf(this));
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Gfx.RoundedRect(rect, DpiHelper.Px(this, CornerRadius)))
            using (var brush = new SolidBrush(_fillColor))
                g.FillPath(brush, path);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Text and simple controls
    // ------------------------------------------------------------------------------------------------

    /// <summary>A label that wraps at whatever width it is given and reports the height it needs.</summary>
    internal sealed class WrapLabel : Control
    {
        private bool _centered;

        public WrapLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText; // custom-painted text: tell screen readers it is text
            Font = Theme.Body;
            ForeColor = Theme.TextPrimary;
        }

        public bool Centered
        {
            get { return _centered; }
            set { if (_centered == value) return; _centered = value; Invalidate(); }
        }

        private TextFormatFlags Flags
        {
            get
            {
                return TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                       TextFormatFlags.Top | (_centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = Gfx.ProposedWidth(this, proposedSize, 360);
            string text = string.IsNullOrEmpty(Text) ? " " : Text;
            Size measured = TextRenderer.MeasureText(text, Font, new Size(width, int.MaxValue), Flags);
            return new Size(width, Math.Max(measured.Height, Font.Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Gfx.BackOf(this));
            if (string.IsNullOrEmpty(Text)) return;
            Color color = Enabled ? ForeColor : Theme.Blend(ForeColor, Gfx.BackOf(this), 0.5f);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color, Flags);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); LayoutUtil.Refresh(this); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); LayoutUtil.Refresh(this); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    }

    /// <summary>Window heading: accent bar + title. Wraps if the title is longer than the window.</summary>
    internal sealed class PageHeader : Control
    {
        public PageHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = Theme.Title;
            ForeColor = Theme.TextPrimary;
        }

        private const TextFormatFlags Flags =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.Top | TextFormatFlags.Left;

        private int TextLeft { get { return DpiHelper.Px(this, 4 + 12); } }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = Gfx.ProposedWidth(this, proposedSize, 360);
            string text = string.IsNullOrEmpty(Text) ? " " : Text;
            Size measured = TextRenderer.MeasureText(text, Font,
                new Size(Math.Max(1, width - TextLeft), int.MaxValue), Flags);
            return new Size(width, Math.Max(measured.Height, DpiHelper.Px(this, 32)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Gfx.BackOf(this));

            int barW = DpiHelper.Px(this, 4);
            int barH = Math.Min(Height, DpiHelper.Px(this, 32));
            int barY = Height <= DpiHelper.Px(this, 40) ? (Height - barH) / 2 : 0;
            using (var path = Gfx.RoundedRect(new Rectangle(0, barY, barW, barH), barW / 2))
            using (var brush = new SolidBrush(Theme.Accent))
                g.FillPath(brush, path);

            int textW = Math.Max(1, Width - TextLeft);
            Size measured = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font,
                new Size(textW, int.MaxValue), Flags);
            var textRect = new Rectangle(TextLeft, Math.Max(0, (Height - measured.Height) / 2), textW, measured.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, Flags);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); LayoutUtil.Refresh(this); }
    }

    /// <summary>A link that can be collapsed (takes no room at all) instead of just hidden.</summary>
    internal sealed class ThemedLink : LinkLabel
    {
        private bool _collapsed;

        public ThemedLink(string text, Color color, Color activeColor, Font font)
        {
            AutoSize = true;
            Text = text;
            Font = font;
            LinkColor = color;
            ActiveLinkColor = activeColor;
            VisitedLinkColor = color;
            LinkBehavior = LinkBehavior.HoverUnderline;
            BackColor = Color.Transparent;
        }

        public bool Collapsed
        {
            get { return _collapsed; }
            set
            {
                if (_collapsed == value) return;
                _collapsed = value;
                Visible = !value;
                LayoutUtil.Refresh(this);
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return _collapsed ? Size.Empty : base.GetPreferredSize(proposedSize);
        }
    }

    internal enum ButtonKind { Primary, Secondary }

    internal sealed class ModernButton : Button
    {
        public Color BaseColor { get; set; } = Theme.Accent;
        public Color HoverColor { get; set; } = Theme.AccentHover;
        public Color TextColor { get; set; } = Color.White;
        public Color? BorderColor { get; set; } = null;

        /// <summary>Smallest width / height, in design pixels. The button grows past these when its text needs it.</summary>
        public int MinWidthDesign { get; set; } = 96;
        public int MinHeightDesign { get; set; } = 36;

        private bool _hover;
        private bool _pressed;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.ButtonFont;
            BackColor = Color.Transparent;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; _pressed = false; Invalidate(); };
            MouseDown += (s, e) => { _pressed = true; Invalidate(); };
            MouseUp += (s, e) => { _pressed = false; Invalidate(); };
        }

        public ButtonKind Kind
        {
            set
            {
                if (value == ButtonKind.Primary)
                {
                    BaseColor = Theme.Accent;
                    HoverColor = Theme.AccentHover;
                    TextColor = Color.White;
                    BorderColor = null;
                }
                else
                {
                    BaseColor = Theme.SurfaceHover;
                    HoverColor = Theme.Border;
                    TextColor = Theme.TextPrimary;
                    BorderColor = Theme.Border;
                }
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int width = Math.Max(DpiHelper.Px(this, MinWidthDesign), text.Width + DpiHelper.Px(this, 40));
            int height = Math.Max(DpiHelper.Px(this, MinHeightDesign), text.Height + DpiHelper.Px(this, 16));
            return new Size(width, height);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = Gfx.BackOf(this);
            g.Clear(back);

            Color fill = _pressed ? ControlPaint.Dark(HoverColor, 0.05f) : (_hover ? HoverColor : BaseColor);
            Color textColor = TextColor;
            Color? border = BorderColor;
            if (!Enabled)
            {
                fill = Theme.Blend(BaseColor, back, 0.55f);
                textColor = Theme.Blend(TextColor, back, 0.55f);
                if (border.HasValue) border = Theme.Blend(border.Value, back, 0.55f);
            }

            float s = DpiHelper.Scale(this);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min((int)Math.Round(10 * s), Height / 2);

            using (var path = Gfx.RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (border.HasValue)
                {
                    using (var pen = new Pen(border.Value, Math.Max(1f, s)))
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
                    using (var focusPath = Gfx.RoundedRect(focusRect, Math.Max(2, radius - inset)))
                    using (var pen = new Pen(textColor, Math.Max(1f, s)) { DashStyle = DashStyle.Dot })
                        g.DrawPath(pen, focusPath);
                }
            }

            TextRenderer.DrawText(g, Text, Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); LayoutUtil.Refresh(this); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    internal sealed class ModernCheckBox : CheckBox
    {
        public Color AccentColor { get; set; } = Theme.Accent;
        public Color BorderColor { get; set; } = Theme.Border;
        public Color TextColor { get; set; } = Theme.TextPrimary;

        private const int BaseBoxSize = 18; // at 100% scaling
        private const TextFormatFlags TextFlags =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

        private bool _hover;

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Font = Theme.Body;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        private int TextLeft(float s)
        {
            return (int)Math.Round(BaseBoxSize * s) + (int)Math.Round(10 * s);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            float s = DpiHelper.Scale(this);
            int width = Gfx.ProposedWidth(this, proposedSize, 360);
            int box = (int)Math.Round(BaseBoxSize * s);
            Size text = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font,
                new Size(Math.Max(1, width - TextLeft(s)), int.MaxValue), TextFlags);
            return new Size(width, Math.Max(box, text.Height) + (int)Math.Round(6 * s));
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = Gfx.BackOf(this);
            g.Clear(back);

            // Everything is drawn in "100% pixels" and multiplied by the display scale, so the box and the
            // tick stay in proportion to the text on 125% / 150% / 200% screens.
            float s = DpiHelper.Scale(this);
            int boxSize = (int)Math.Round(BaseBoxSize * s);
            int textLeft = TextLeft(s);
            Size text = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font,
                new Size(Math.Max(1, Width - textLeft), int.MaxValue), TextFlags);

            int contentH = Math.Max(boxSize, text.Height);
            int top = (Height - contentH) / 2;
            int boxY = text.Height > boxSize ? top + Math.Max(0, (Font.Height - boxSize) / 2) : top + (contentH - boxSize) / 2;
            var boxRect = new Rectangle(0, boxY, boxSize, boxSize);

            Color accent = Enabled ? AccentColor : Theme.Blend(AccentColor, back, 0.55f);
            Color outline = _hover && Enabled ? AccentColor : BorderColor;
            Color textColor = Enabled ? TextColor : Theme.Blend(TextColor, back, 0.55f);

            using (var path = Gfx.RoundedRect(boxRect, (int)Math.Round(4 * s)))
            {
                if (Checked)
                {
                    using (var brush = new SolidBrush(accent))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var pen = new Pen(outline, 1.5f * s))
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
                using (var ringPath = Gfx.RoundedRect(ring, (int)Math.Round(7 * s)))
                using (var pen = new Pen(accent, Math.Max(1f, s)))
                    g.DrawPath(pen, ringPath);
            }

            var textRect = new Rectangle(textLeft, top + (contentH - text.Height) / 2, Math.Max(1, Width - textLeft), text.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor, TextFlags);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); LayoutUtil.Refresh(this); }
    }

    /// <summary>Thin animated bar for "please wait" windows.</summary>
    internal sealed class ProgressStrip : Control
    {
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 25 };
        private float _position = -0.4f;

        public ProgressStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            _timer.Tick += (s, e) =>
            {
                _position += 0.02f;
                if (_position > 1f) _position = -0.4f;
                Invalidate();
            };
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(Gfx.ProposedWidth(this, proposedSize, 300), DpiHelper.Px(this, 4));
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); _timer.Start(); }
        protected override void OnHandleDestroyed(EventArgs e) { _timer.Stop(); base.OnHandleDestroyed(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Gfx.BackOf(this));

            var track = new Rectangle(0, 0, Width, Height);
            int radius = Height / 2;
            using (var trackPath = Gfx.RoundedRect(track, radius))
            using (var trackBrush = new SolidBrush(Theme.Surface))
            {
                g.FillPath(trackBrush, trackPath);
                g.SetClip(trackPath);
                int segW = (int)(Width * 0.4f);
                int x = (int)(_position * Width);
                using (var segBrush = new SolidBrush(Theme.Accent))
                    g.FillRectangle(segBrush, x, 0, segW, Height);
                g.ResetClip();
            }
        }
    }

    /// <summary>Round icon used by the themed message box.</summary>
    internal sealed class IconGlyph : Control
    {
        private readonly MessageBoxIcon _icon;

        public IconGlyph(MessageBoxIcon icon)
        {
            _icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        private string Symbol
        {
            get
            {
                if (_icon == MessageBoxIcon.Error) return "\u00D7";
                if (_icon == MessageBoxIcon.Warning) return "!";
                if (_icon == MessageBoxIcon.Question) return "?";
                if (_icon == MessageBoxIcon.Information) return "i";
                return string.Empty;
            }
        }

        private Color Tint
        {
            get
            {
                if (_icon == MessageBoxIcon.Error) return Theme.Danger;
                if (_icon == MessageBoxIcon.Warning) return Theme.Warning;
                return Theme.Accent;
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (Symbol.Length == 0) return Size.Empty;
            int d = DpiHelper.Px(this, 36);
            return new Size(d, d);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Gfx.BackOf(this));
            if (Symbol.Length == 0) return;

            var circle = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(Tint))
                g.FillEllipse(brush, circle);
            TextRenderer.DrawText(g, Symbol, Theme.Glyph, circle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Windows
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Base class for every window of the app: dark title bar, rounded corners, a scrolling body that is laid out
    /// from the content, and a footer with the action buttons that stays pinned to the bottom.
    /// Derived windows only fill Body and Footer.
    /// </summary>
    internal class ThemedForm : Form
    {
        private const TextFormatFlags TipFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.Left | TextFormatFlags.Top;

        private readonly Panel _scroll;
        private bool _sized;
        private bool _fitting;

        protected VStack Body { get; private set; }
        protected ActionRow Footer { get; private set; }
        protected ToolTip Tips { get; private set; }

        /// <summary>Starting width of the content area, in design pixels.</summary>
        protected int DesignWidth { get; set; }
        /// <summary>Narrowest width the user can resize to, in design pixels.</summary>
        protected int DesignMinWidth { get; set; }
        /// <summary>Shortest body height the user can resize to, in design pixels (never more than the content needs).</summary>
        protected int DesignMinBodyHeight { get; set; }

        public ThemedForm()
        {
            AutoScaleMode = AutoScaleMode.None; // sizes come from the layout code, not from designer coordinates
            Font = Theme.Base;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            SizeGripStyle = SizeGripStyle.Hide;
            DesignWidth = 480;
            DesignMinWidth = 400;
            DesignMinBodyHeight = 160;

            Tips = new ToolTip { OwnerDraw = true };
            Tips.Popup += TipPopup;
            Tips.Draw += TipDraw;

            Body = new VStack { Inset = new Padding(28, 24, 28, 20) };
            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            _scroll.Controls.Add(Body);
            _scroll.Layout += (s, e) => FitBody();
            _scroll.HandleCreated += (s, e) =>
            {
                // Dark scroll bar to match the window (Windows 10 1809+; silently ignored elsewhere).
                try { SetWindowTheme(_scroll.Handle, "DarkMode_Explorer", null); } catch { }
            };

            Footer = new ActionRow
            {
                Dock = DockStyle.Bottom,
                Inset = new Padding(28, 14, 28, 16),
                Gap = 10,
                TopBorder = Theme.Border
            };

            // The fill control goes in first so the docked footer is laid out before it.
            Controls.Add(_scroll);
            Controls.Add(Footer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Tips != null) Tips.Dispose();
            base.Dispose(disposing);
        }

        private void FitBody()
        {
            if (_fitting) return;
            int w = _scroll.ClientSize.Width;
            if (w <= 0) return;

            _fitting = true;
            try
            {
                int h = Body.GetPreferredSize(new Size(w, 0)).Height;
                var bounds = new Rectangle(_scroll.AutoScrollPosition, new Size(w, h));
                if (Body.Bounds != bounds) Body.Bounds = bounds;
                var min = new Size(0, h);
                if (_scroll.AutoScrollMinSize != min) _scroll.AutoScrollMinSize = min;
            }
            finally { _fitting = false; }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (Footer != null)
            {
                int h = Footer.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
                if (Footer.Height != h) Footer.Height = h;
            }
            base.OnLayout(levent);
        }

        protected override void OnLoad(EventArgs e)
        {
            if (!_sized)
            {
                _sized = true;
                int chromeW = Width - ClientSize.Width;
                int chromeH = Height - ClientSize.Height;

                int w = DpiHelper.Px(this, DesignWidth);
                int footerH = Footer.GetPreferredSize(new Size(w, 0)).Height;
                int bodyH = Body.GetPreferredSize(new Size(w, 0)).Height;

                Rectangle area = Screen.FromControl(this).WorkingArea;
                int maxClientH = Math.Max(footerH + DpiHelper.Px(this, 120), (int)(area.Height * 0.9) - chromeH);
                int maxClientW = Math.Max(DpiHelper.Px(this, DesignMinWidth), area.Width - chromeW);

                ClientSize = new Size(Math.Min(w, maxClientW), Math.Min(bodyH + footerH, maxClientH));
                MinimumSize = new Size(
                    chromeW + Math.Min(DpiHelper.Px(this, DesignMinWidth), maxClientW),
                    chromeH + footerH + Math.Min(DpiHelper.Px(this, DesignMinBodyHeight), bodyH));
                CenterToScreen();
            }
            base.OnLoad(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Body.PerformLayout();
            _scroll.PerformLayout();
            PerformLayout();
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
                int rounded = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        // Tooltips use the same dark look as the rest of the app and wrap long text.
        private void TipPopup(object sender, PopupEventArgs e)
        {
            string text = Tips.GetToolTip(e.AssociatedControl);
            Size m = TextRenderer.MeasureText(text, Theme.Small, new Size(DpiHelper.Px(this, 320), int.MaxValue), TipFlags);
            int pad = DpiHelper.Px(this, 10);
            e.ToolTipSize = new Size(m.Width + pad * 2, m.Height + pad * 2);
        }

        private void TipDraw(object sender, DrawToolTipEventArgs e)
        {
            int pad = DpiHelper.Px(this, 10);
            using (var brush = new SolidBrush(Theme.SurfaceHover))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.Small,
                new Rectangle(pad, pad, Math.Max(1, e.Bounds.Width - pad * 2), Math.Max(1, e.Bounds.Height - pad * 2)),
                Theme.TextPrimary, TipFlags);
        }
    }

    /// <summary>
    /// Drop-in replacement for MessageBox.Show with the same arguments, drawn in the app's look
    /// (same fonts, colours, buttons and window style as every other window).
    /// </summary>
    internal static class ThemedMessageBox
    {
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(null, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon,
            MessageBoxDefaultButton defaultButton)
        {
            return Show(null, text, caption, buttons, icon, defaultButton);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons,
            MessageBoxIcon icon)
        {
            return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons,
            MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            using (var dialog = new MessageDialog(text, caption, buttons, icon, defaultButton, owner == null))
            {
                dialog.ShowDialog(owner);
                return dialog.Result;
            }
        }

        private sealed class MessageDialog : ThemedForm
        {
            private readonly DialogResult _closeResult;
            private readonly MessageBoxIcon _icon;

            public DialogResult Result { get; private set; }

            public MessageDialog(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon,
                MessageBoxDefaultButton defaultButton, bool noOwner)
            {
                _icon = icon;
                Text = caption ?? string.Empty;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                TopMost = noOwner;          // without an owner the box must not get lost behind other windows
                ShowInTaskbar = noOwner;
                DesignWidth = 440;
                DesignMinWidth = 320;
                Body.Inset = new Padding(28, 26, 28, 22);

                var row = new ActionRow { Gap = 16 };
                if (icon != MessageBoxIcon.None) row.AddLeft(new IconGlyph(icon));
                row.SetFill(new WrapLabel { Text = text ?? string.Empty, Font = Theme.Body, ForeColor = Theme.TextPrimary });
                Body.Add(row);

                DialogResult[] results = ResultsFor(buttons);
                _closeResult = CloseResultFor(buttons);
                int defaultIndex = Math.Min(results.Length - 1,
                    defaultButton == MessageBoxDefaultButton.Button2 ? 1 :
                    defaultButton == MessageBoxDefaultButton.Button3 ? 2 : 0);

                for (int i = 0; i < results.Length; i++)
                {
                    DialogResult r = results[i];
                    var button = new ModernButton
                    {
                        Text = LabelFor(r),
                        Kind = i == defaultIndex ? ButtonKind.Primary : ButtonKind.Secondary
                    };
                    button.Click += (s, e) => Choose(r);
                    Footer.AddRight(button);
                    if (i == defaultIndex) AcceptButton = button;
                }

                KeyPreview = true;
                KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Choose(_closeResult); };
            }

            private void Choose(DialogResult result)
            {
                Result = result;
                Close();
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    if (_icon == MessageBoxIcon.Error) SystemSounds.Hand.Play();
                    else if (_icon == MessageBoxIcon.Warning) SystemSounds.Exclamation.Play();
                    else if (_icon == MessageBoxIcon.Information) SystemSounds.Asterisk.Play();
                    else if (_icon == MessageBoxIcon.Question) SystemSounds.Question.Play();
                }
                catch { }
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                // Closed with the X: same answer as pressing Esc.
                if (Result == DialogResult.None) Result = _closeResult;
                base.OnFormClosing(e);
            }

            private static DialogResult[] ResultsFor(MessageBoxButtons buttons)
            {
                switch (buttons)
                {
                    case MessageBoxButtons.OKCancel: return new[] { DialogResult.OK, DialogResult.Cancel };
                    case MessageBoxButtons.YesNo: return new[] { DialogResult.Yes, DialogResult.No };
                    case MessageBoxButtons.YesNoCancel: return new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel };
                    case MessageBoxButtons.RetryCancel: return new[] { DialogResult.Retry, DialogResult.Cancel };
                    case MessageBoxButtons.AbortRetryIgnore: return new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore };
                    default: return new[] { DialogResult.OK };
                }
            }

            private static DialogResult CloseResultFor(MessageBoxButtons buttons)
            {
                switch (buttons)
                {
                    case MessageBoxButtons.OKCancel:
                    case MessageBoxButtons.YesNoCancel:
                    case MessageBoxButtons.RetryCancel: return DialogResult.Cancel;
                    case MessageBoxButtons.YesNo: return DialogResult.No;
                    case MessageBoxButtons.AbortRetryIgnore: return DialogResult.Ignore;
                    default: return DialogResult.OK;
                }
            }

            private static string LabelFor(DialogResult result)
            {
                switch (result)
                {
                    case DialogResult.Yes: return "Yes";
                    case DialogResult.No: return "No";
                    case DialogResult.Cancel: return "Cancel";
                    case DialogResult.Retry: return "Retry";
                    case DialogResult.Abort: return "Abort";
                    case DialogResult.Ignore: return "Ignore";
                    default: return "OK";
                }
            }
        }
    }

    /// <summary>Short factory methods so every window builds its controls the same way.</summary>
    internal static class Ui
    {
        public static WrapLabel Text(string text)
        {
            return new WrapLabel { Text = text, Font = Theme.Body, ForeColor = Theme.TextSecondary };
        }

        public static WrapLabel Small(string text)
        {
            return new WrapLabel { Text = text, Font = Theme.Small, ForeColor = Theme.TextSecondary };
        }

        public static WrapLabel Caption(string text)
        {
            return new WrapLabel { Text = text, Font = Theme.Caption, ForeColor = Theme.TextSecondary };
        }

        public static ModernButton Button(string text, ButtonKind kind)
        {
            return new ModernButton { Text = text, Kind = kind };
        }

        public static ModernCheckBox Check(string text, bool isChecked = false)
        {
            return new ModernCheckBox { Text = text, Checked = isChecked };
        }

        public static ThemedLink Link(string text, Color color, Color activeColor, Font font = null)
        {
            return new ThemedLink(text, color, activeColor, font ?? Theme.Body);
        }
    }
}

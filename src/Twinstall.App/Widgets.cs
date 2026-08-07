using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Twinstall.App
{
    internal enum ButtonKind { Primary, Secondary, Subtle, Danger }

    /// <summary>
    /// A flat, rounded, theme-aware button. WinForms' stock button is the single most dated
    /// thing on screen, and it cannot be recoloured convincingly — so this draws itself.
    /// </summary>
    internal sealed class FlatButton : Control
    {
        private bool _hover;
        private bool _down;

        internal ButtonKind Kind { get; set; } = ButtonKind.Secondary;

        internal FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
        {
            Kind = kind;
            Text = text;
            Font = Theme.Body;
            Size = new Size(140, 38);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, border, text;
            ResolveColours(out fill, out border, out text);

            using (GraphicsPath path = Theme.RoundedRect(r, 6))
            {
                if (fill != Color.Transparent)
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);

                if (border != Color.Transparent)
                    using (var p = new Pen(border)) g.DrawPath(p, path);
            }

            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void ResolveColours(out Color fill, out Color border, out Color text)
        {
            if (!Enabled)
            {
                fill = Theme.IsDark ? Theme.SurfaceAlt : Theme.SurfaceAlt;
                border = Theme.Border;
                text = Theme.TextMuted;
                return;
            }

            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = _down ? Theme.Darken(Theme.Accent, 0.18f)
                         : _hover ? Theme.Lighten(Theme.Accent, 0.12f) : Theme.Accent;
                    border = Color.Transparent;
                    text = Theme.AccentText;
                    break;

                case ButtonKind.Danger:
                    fill = _down ? Theme.Darken(Theme.Bad, 0.18f)
                         : _hover ? Theme.Lighten(Theme.Bad, 0.10f) : Color.Transparent;
                    border = Theme.Bad;
                    text = (_hover || _down) ? Color.White : Theme.Bad;
                    break;

                case ButtonKind.Subtle:
                    fill = _hover ? Theme.SurfaceAlt : Color.Transparent;
                    border = Color.Transparent;
                    text = Theme.TextMuted;
                    break;

                default:
                    fill = _down ? Theme.SurfaceAlt : _hover ? Theme.SurfaceAlt : Theme.Surface;
                    border = Theme.Border;
                    text = Theme.Text;
                    break;
            }
        }
    }

    /// <summary>
    /// A selectable row: coloured dot or tick, a title, and a quieter second line. Used for
    /// choosing an application and for listing configured accounts.
    /// </summary>
    internal sealed class ChoiceRow : Control
    {
        private bool _hover;

        internal string Title { get; set; } = string.Empty;
        internal string Subtitle { get; set; } = string.Empty;
        internal Color Dot { get; set; } = Color.Transparent;
        internal bool Selected { get; set; }
        internal object Tag2 { get; set; }

        internal ChoiceRow()
        {
            Height = 60;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Selected ? Blend(Theme.Accent, Theme.Surface, 0.14f)
                       : _hover ? Theme.SurfaceAlt : Theme.Surface;

            using (GraphicsPath path = Theme.RoundedRect(r, 8))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
                using (var p = new Pen(Selected ? Theme.Accent : Theme.Border, Selected ? 1.6f : 1f))
                    g.DrawPath(p, path);
            }

            int textLeft = 16;
            if (Dot != Color.Transparent)
            {
                using (var b = new SolidBrush(Dot)) g.FillEllipse(b, 16, (Height - 18) / 2, 18, 18);
                textLeft = 46;
            }

            var titleRect = new Rectangle(textLeft, 10, Width - textLeft - 16, 20);
            TextRenderer.DrawText(g, Title, Theme.BodyStrong, titleRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (!string.IsNullOrEmpty(Subtitle))
            {
                var subRect = new Rectangle(textLeft, 31, Width - textLeft - 16, 18);
                TextRenderer.DrawText(g, Subtitle, Theme.Small, subRect, Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)((a.R * t) + (b.R * (1 - t))),
                (int)((a.G * t) + (b.G * (1 - t))),
                (int)((a.B * t) + (b.B * (1 - t))));
        }
    }

    /// <summary>
    /// A drawn walkthrough of the three clicks needed in Windows Settings.
    ///
    /// Drawn rather than screenshotted on purpose: a picture of the Settings app would be
    /// Microsoft's artwork sitting inside our binary, which the project avoids for exactly the
    /// same reason it never ships Slack's or Claude's logo. A schematic carries the same
    /// information and is ours.
    ///
    /// It exists because "open Settings and pick Twinstall" is not an instruction a newcomer
    /// can follow — that page has two search boxes and the wrong one silently finds nothing.
    /// </summary>
    internal sealed class SettingsHint : Control
    {
        internal string Scheme { get; set; } = "claude";

        internal SettingsHint()
        {
            Height = 168;
            Font = Theme.Body;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int y = 0;
            y = Row(g, y, "1", "Type  " + Scheme + "  into the FIRST search box,",
                    "the one under “Set a default for a file type or link type”.", DrawSearchBox);
            y = Row(g, y, "2", "A row appears saying “Choose a default”.", "Click it.", DrawChooseRow);
            Row(g, y, "3", "Pick Twinstall from the list, then choose Set default.", null, DrawPickRow);
        }

        private int Row(Graphics g, int y, string number, string line1, string line2,
                        Action<Graphics, Rectangle> glyph)
        {
            // numbered disc
            using (var b = new SolidBrush(Theme.Accent))
                g.FillEllipse(b, 0, y + 8, 22, 22);
            TextRenderer.DrawText(g, number, Theme.Small, new Rectangle(0, y + 8, 22, 22), Theme.AccentText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var box = new Rectangle(34, y + 4, 190, 30);
            glyph(g, box);

            var textRect = new Rectangle(238, y + (line2 == null ? 12 : 3), Math.Max(40, Width - 244), 20);
            TextRenderer.DrawText(g, line1, Theme.Body, textRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (line2 != null)
            {
                var r2 = new Rectangle(238, y + 22, Math.Max(40, Width - 244), 20);
                TextRenderer.DrawText(g, line2, Theme.Small, r2, Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            return y + 54;
        }

        private void DrawSearchBox(Graphics g, Rectangle r)
        {
            using (GraphicsPath p = Theme.RoundedRect(r, 5))
            using (var b = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(Theme.Accent, 1.6f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            // magnifier
            using (var pen = new Pen(Theme.TextMuted, 1.5f))
            {
                g.DrawEllipse(pen, r.X + 9, r.Y + 9, 9, 9);
                g.DrawLine(pen, r.X + 17, r.Y + 17, r.X + 21, r.Y + 21);
            }
            TextRenderer.DrawText(g, Scheme, Theme.Body, new Rectangle(r.X + 28, r.Y, r.Width - 34, r.Height),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void DrawChooseRow(Graphics g, Rectangle r)
        {
            using (GraphicsPath p = Theme.RoundedRect(r, 5))
            using (var b = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(Theme.Accent, 1.6f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            using (var pen = new Pen(Theme.TextMuted, 1.6f))
            {
                g.DrawLine(pen, r.X + 10, r.Y + 15, r.X + 20, r.Y + 15);
                g.DrawLine(pen, r.X + 15, r.Y + 10, r.X + 15, r.Y + 20);
            }
            TextRenderer.DrawText(g, "Choose a default", Theme.Small,
                new Rectangle(r.X + 28, r.Y, r.Width - 34, r.Height), Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void DrawPickRow(Graphics g, Rectangle r)
        {
            using (GraphicsPath p = Theme.RoundedRect(r, 5))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, p);

            TextRenderer.DrawText(g, "Twinstall", Theme.BodyStrong,
                new Rectangle(r.X + 12, r.Y, r.Width - 18, r.Height), Theme.AccentText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>One line of the detection result: a tick or cross, and plain language.</summary>
    internal sealed class FactRow : Control
    {
        internal bool Good { get; set; } = true;
        internal string Label { get; set; } = string.Empty;

        internal FactRow(string label, bool good)
        {
            Label = label;
            Good = good;
            Height = 26;
            Font = Theme.Body;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color mark = Good ? Theme.Good : Theme.Bad;
            using (var p = new Pen(mark, 2f))
            {
                if (Good)
                {
                    g.DrawLines(p, new[] { new Point(3, 13), new Point(8, 18), new Point(17, 7) });
                }
                else
                {
                    g.DrawLine(p, 4, 7, 16, 19);
                    g.DrawLine(p, 16, 7, 4, 19);
                }
            }

            TextRenderer.DrawText(g, Label, Font, new Rectangle(28, 0, Width - 28, Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
        }
    }
}

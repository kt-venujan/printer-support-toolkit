using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PrinterToolkit
{
    internal enum ButtonKind { Primary, Secondary, Danger }

    /// <summary>Rounded, animated button with gradient / outline styles and an optional icon glyph.</summary>
    internal class ModernButton : Control
    {
        private readonly Timer anim = new Timer { Interval = 15 };
        private float hover;
        private bool over, down;
        public ButtonKind Kind { get; set; }
        public string Glyph { get; set; }

        public ModernButton(string text, string glyph, ButtonKind kind)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = text;
            Glyph = glyph;
            Kind = kind;
            Font = Theme.Semi10;
            Size = new Size(210, 42);
            Margin = new Padding(6);
            Cursor = Cursors.Hand;
            anim.Tick += (s, e) =>
            {
                float target = over ? 1f : 0f;
                hover += (target - hover) * 0.25f;
                if (Math.Abs(target - hover) < 0.02f) { hover = target; anim.Stop(); }
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; down = false; anim.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            if (down) r.Inflate(-1, -1);
            int alphaMul = Enabled ? 255 : 110;
            Color textColor;

            using (var path = Theme.Round(r, 9))
            {
                if (Kind == ButtonKind.Primary)
                {
                    using (var br = new LinearGradientBrush(r, Theme.Alpha(Theme.Accent, alphaMul), Theme.Alpha(Theme.Accent2, alphaMul), 0f))
                        g.FillPath(br, path);
                    using (var glow = new SolidBrush(Color.FromArgb((int)(45 * hover), 255, 255, 255)))
                        g.FillPath(glow, path);
                    textColor = Theme.Alpha(Color.White, alphaMul);
                }
                else if (Kind == ButtonKind.Danger)
                {
                    using (var br = new SolidBrush(Theme.Alpha(Theme.Danger, (int)(28 + 40 * hover) * alphaMul / 255)))
                        g.FillPath(br, path);
                    using (var pen = new Pen(Theme.Alpha(Theme.Danger, (int)(140 + 100 * hover) * alphaMul / 255), 1.2f))
                        g.DrawPath(pen, path);
                    textColor = Theme.Alpha(Color.FromArgb(254, 202, 202), alphaMul);
                }
                else
                {
                    using (var br = new SolidBrush(Theme.Alpha(Theme.Blend(Theme.CardAlt, Theme.Border, hover), alphaMul)))
                        g.FillPath(br, path);
                    using (var pen = new Pen(Theme.Alpha(Theme.Blend(Theme.Border, Theme.Accent, hover), alphaMul), 1.2f))
                        g.DrawPath(pen, path);
                    textColor = Theme.Alpha(Theme.Text, alphaMul);
                }
            }

            Size ts = TextRenderer.MeasureText(Text, Font);
            int glyphW = string.IsNullOrEmpty(Glyph) ? 0 : 24;
            int total = ts.Width + glyphW;
            int x = Math.Max(8, (Width - total) / 2);
            if (glyphW > 0)
            {
                Color gc = Kind == ButtonKind.Secondary ? Theme.Alpha(Theme.Blend(Theme.Accent, Color.White, hover * 0.4f), alphaMul) : textColor;
                Theme.DrawGlyph(g, Glyph, 11f, gc, new RectangleF(x - 2, 0, 24, Height));
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + glyphW, 0, Width - x - glyphW - 4, Height), textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Sidebar navigation item with animated hover and gradient selection indicator.</summary>
    internal class NavButton : Control
    {
        private readonly Timer anim = new Timer { Interval = 15 };
        private float hover;
        private bool over, selected;
        public string Glyph { get; set; }
        public string Key { get; set; }

        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public NavButton(string key, string text, string glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Key = key; Text = text; Glyph = glyph;
            BackColor = Theme.Sidebar;
            Font = Theme.Semi10;
            Height = 44;
            Cursor = Cursors.Hand;
            anim.Tick += (s, e) =>
            {
                float t = over ? 1f : 0f;
                hover += (t - hover) * 0.25f;
                if (Math.Abs(t - hover) < 0.02f) { hover = t; anim.Stop(); }
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; anim.Start(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.HighQuality(g);
            var r = new RectangleF(10, 3, Width - 20, Height - 6);
            if (selected || hover > 0)
            {
                Color fill = selected ? Theme.CardAlt : Theme.Alpha(Theme.CardAlt, (int)(200 * hover));
                using (var path = Theme.Round(r, 9))
                using (var br = new SolidBrush(fill))
                    g.FillPath(br, path);
            }
            if (selected)
            {
                var bar = new RectangleF(10, 11, 4, Height - 22);
                using (var path = Theme.Round(bar, 2))
                using (var br = new LinearGradientBrush(bar, Theme.Accent, Theme.Accent2, 90f))
                    g.FillPath(br, path);
            }
            Color gc = selected ? Theme.Accent : Theme.Blend(Theme.Muted, Theme.Text, hover);
            Theme.DrawGlyph(g, Glyph, 12f, gc, new RectangleF(16, 0, 30, Height));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(50, 0, Width - 56, Height),
                selected ? Theme.Text : Theme.Blend(Theme.Muted, Theme.Text, hover),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Rounded card container with optional header title and icon.</summary>
    internal class CardPanel : Panel
    {
        private string title;
        public string Glyph { get; set; }
        public string Title { get { return title; } set { title = value; Padding = new Padding(16, string.IsNullOrEmpty(value) ? 16 : 50, 16, 14); Invalidate(); } }
        public Color AccentColor { get; set; }

        public CardPanel(string title, string glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            AccentColor = Theme.Accent;
            Glyph = glyph;
            Title = title;
            Margin = new Padding(8);
            Dock = DockStyle.Fill;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 12))
            {
                using (var br = new SolidBrush(Theme.Card)) g.FillPath(br, path);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            if (!string.IsNullOrEmpty(title))
            {
                int x = 16;
                if (!string.IsNullOrEmpty(Glyph))
                {
                    Theme.DrawGlyph(g, Glyph, 12f, AccentColor, new RectangleF(12, 10, 28, 28));
                    x = 44;
                }
                TextRenderer.DrawText(g, title, Theme.Semi11, new Rectangle(x, 10, Width - x - 12, 26), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control.BackColor == SystemColors.Control || e.Control.BackColor == Theme.Bg) e.Control.BackColor = Theme.Card;
        }
    }

    /// <summary>Large gradient header card on the dashboard showing the detected model.</summary>
    internal class HeroCard : Control
    {
        public string Brand = "Detecting...", Model = "Scanning printers", Category = "", Language = "", Sub = "";
        public string StatusText = "Unknown", ConnText = "Unknown";
        public Color StatusColor = Theme.Muted, ConnColor = Theme.Muted;

        public HeroCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(8);
            Dock = DockStyle.Fill;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 14))
            {
                using (var br = new LinearGradientBrush(r, Color.FromArgb(22, 44, 70), Color.FromArgb(46, 28, 84), 15f))
                    g.FillPath(br, path);
                g.SetClip(path);
                // soft glows
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(-80, -120, 380, 320);
                    using (var pg = new PathGradientBrush(gp) { CenterColor = Theme.Alpha(Theme.Accent, 70), SurroundColors = new[] { Color.Transparent } })
                        g.FillPath(pg, gp);
                }
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(Width - 320, Height - 160, 420, 320);
                    using (var pg = new PathGradientBrush(gp) { CenterColor = Theme.Alpha(Theme.Accent2, 80), SurroundColors = new[] { Color.Transparent } })
                        g.FillPath(pg, gp);
                }
                g.ResetClip();
                using (var pen = new Pen(Theme.Alpha(Theme.Accent, 70))) g.DrawPath(pen, path);
            }

            // Icon bubble
            var circle = new RectangleF(28, (Height - 92) / 2f, 92, 92);
            using (var br = new LinearGradientBrush(circle, Theme.Alpha(Theme.Accent, 60), Theme.Alpha(Theme.Accent2, 60), 45f))
                g.FillEllipse(br, circle);
            using (var pen = new Pen(Theme.Alpha(Color.White, 50), 1.5f)) g.DrawEllipse(pen, circle);
            Theme.DrawGlyph(g, Glyph.Printer, 30f, Color.White, circle);

            int tx = 144;
            TextRenderer.DrawText(g, (Brand ?? "").ToUpperInvariant(), Theme.Semi9, new Point(tx, (int)circle.Y - 6), Theme.Accent, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Model ?? "", Theme.Bold26, new Point(tx - 4, (int)circle.Y + 12), Color.White, TextFormatFlags.NoPrefix);
            int chipY = (int)circle.Y + 68;
            int cx = tx;
            if (!string.IsNullOrEmpty(Category)) cx = DrawChip(g, Category, cx, chipY, Theme.Alpha(Color.White, 30), Theme.Text) + 8;
            if (!string.IsNullOrEmpty(Language)) cx = DrawChip(g, Language, cx, chipY, Theme.Alpha(Theme.Accent, 50), Theme.Accent) + 8;
            if (!string.IsNullOrEmpty(Sub))
                TextRenderer.DrawText(g, Sub, Theme.F9, new Rectangle(cx + 4, chipY, Math.Max(10, Width - cx - 220), 24), Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Status pills
            int px = Width - 24;
            px = DrawPill(g, ConnText, px, 24, ConnColor, Glyph.Usb);
            DrawPill(g, StatusText, Width - 24, 64, StatusColor, Glyph.Printer);
        }

        private int DrawChip(Graphics g, string text, int x, int y, Color bg, Color fg)
        {
            Size s = TextRenderer.MeasureText(text, Theme.Semi9, new Size(300, 24), TextFormatFlags.NoPrefix);
            var r = new RectangleF(x, y, s.Width + 14, 24);
            using (var path = Theme.Round(r, 12))
            using (var br = new SolidBrush(bg)) g.FillPath(br, path);
            TextRenderer.DrawText(g, text, Theme.Semi9, Rectangle.Round(r), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return (int)r.Right;
        }

        private int DrawPill(Graphics g, string text, int right, int y, Color c, string glyph)
        {
            Size s = TextRenderer.MeasureText(text, Theme.Semi10, new Size(300, 32), TextFormatFlags.NoPrefix);
            float w = Math.Max(130, s.Width + 48);
            var r = new RectangleF(right - w, y, w, 32);
            using (var path = Theme.Round(r, 16))
            {
                using (var br = new SolidBrush(Theme.Alpha(c, 40))) g.FillPath(br, path);
                using (var pen = new Pen(Theme.Alpha(c, 160))) g.DrawPath(pen, path);
            }
            using (var br = new SolidBrush(c)) g.FillEllipse(br, r.X + 14, r.Y + 12, 8, 8);
            TextRenderer.DrawText(g, text, Theme.Semi10, new Rectangle((int)r.X + 28, (int)r.Y, (int)w - 32, 32), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return (int)r.X - 10;
        }
    }

    /// <summary>Small metric tile (icon + caption + value).</summary>
    internal class StatTile : Control
    {
        public string Caption, Value = "-", Sub = "", Glyph;
        public Color AccentColor = Theme.Accent;

        public StatTile(string caption, string glyph, Color accent)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Caption = caption; Glyph = glyph; AccentColor = accent;
            Margin = new Padding(4);
            Dock = DockStyle.Fill;
        }

        public void Set(string value, string sub, Color accent)
        {
            Value = value; Sub = sub; AccentColor = accent; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 12))
            {
                using (var br = new SolidBrush(Theme.Card)) g.FillPath(br, path);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            var ic = new RectangleF(14, (Height - 42) / 2f, 42, 42);
            using (var path = Theme.Round(ic, 11))
            using (var br = new SolidBrush(Theme.Alpha(AccentColor, 38))) g.FillPath(br, path);
            Theme.DrawGlyph(g, Glyph, 14f, AccentColor, ic);

            int tx = 68;
            int availW = Math.Max(20, Width - tx - 10);

            // Line 1: Caption
            TextRenderer.DrawText(g, (Caption ?? "").ToUpperInvariant(), Theme.Caps8,
                new Rectangle(tx, 14, availW, 14), Theme.Muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Line 2: Value
            TextRenderer.DrawText(g, Value ?? "", Theme.Semi12,
                new Rectangle(tx, 30, availW, 24), Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Line 3: Sub description
            if (!string.IsNullOrEmpty(Sub))
            {
                TextRenderer.DrawText(g, Sub, Theme.F9,
                    new Rectangle(tx, 56, availW, 18), Theme.Muted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Key / value list drawn as table rows.</summary>
    internal class KeyValueView : Control
    {
        private List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
        public int RowHeight = 30;
        public float KeyRatio = 0.36f;

        public KeyValueView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Dock = DockStyle.Fill;
        }

        public void SetItems(List<KeyValuePair<string, string>> list)
        {
            items = list ?? new List<KeyValuePair<string, string>>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int keyW = (int)(Width * KeyRatio);
            int y = 0;
            using (var pen = new Pen(Theme.Alpha(Theme.Border, 160)))
            {
                foreach (var kv in items)
                {
                    if (y + RowHeight > Height + RowHeight) break;
                    TextRenderer.DrawText(g, kv.Key, Theme.F9, new Rectangle(2, y, keyW - 6, RowHeight), Theme.Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, string.IsNullOrEmpty(kv.Value) ? "-" : kv.Value, Theme.Semi9, new Rectangle(keyW, y, Width - keyW - 2, RowHeight), Theme.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    y += RowHeight;
                    g.DrawLine(pen, 0, y - 1, Width, y - 1);
                }
            }
            if (items.Count == 0)
                TextRenderer.DrawText(g, "No data yet.", Theme.F9, new Point(2, 6), Theme.Muted, TextFormatFlags.NoPrefix);
        }
    }

    internal enum HealthState { Pass, Warn, Fail, Info }

    internal class HealthItem
    {
        public HealthState State; public string Title; public string Detail;
        public HealthItem(HealthState s, string t, string d) { State = s; Title = t; Detail = d; }
    }

    /// <summary>List of health checks with colored status icons.</summary>
    internal class HealthList : Control
    {
        private List<HealthItem> items = new List<HealthItem>();

        public HealthList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Dock = DockStyle.Fill;
        }

        public void SetItems(List<HealthItem> list) { items = list ?? new List<HealthItem>(); Invalidate(); }

        public static Color ColorOf(HealthState s)
        {
            switch (s)
            {
                case HealthState.Pass: return Theme.Success;
                case HealthState.Warn: return Theme.Warning;
                case HealthState.Fail: return Theme.Danger;
                default: return Theme.Info;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.HighQuality(g);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, "Run the diagnostic to see health checks.", Theme.F9, new Point(2, 6), Theme.Muted, TextFormatFlags.NoPrefix);
                return;
            }
            int y = 0; const int rh = 50;
            foreach (var it in items)
            {
                Color c = ColorOf(it.State);
                var ic = new RectangleF(2, y + 9, 30, 30);
                using (var br = new SolidBrush(Theme.Alpha(c, 40))) g.FillEllipse(br, ic);
                string gl = it.State == HealthState.Pass ? Glyph.Check : it.State == HealthState.Fail ? Glyph.Cancel : it.State == HealthState.Warn ? Glyph.Warning : Glyph.Info;
                Theme.DrawGlyph(g, gl, 10f, c, ic);
                TextRenderer.DrawText(g, it.Title, Theme.Semi10, new Rectangle(44, y + 6, Width - 48, 20), Theme.Text,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, it.Detail ?? "", Theme.F9, new Rectangle(44, y + 26, Width - 48, 18), Theme.Muted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += rh;
            }
        }
    }

    /// <summary>Animated connection indicator (pulsing rings when connected).</summary>
    internal class PulseIndicator : Control
    {
        private readonly Timer t = new Timer { Interval = 30 };
        private float phase;
        public Color StateColor = Theme.Muted;
        public string Title = "Monitor stopped", Detail = "Press Start to begin live monitoring.";
        public bool Animate { get { return t.Enabled; } set { t.Enabled = value; Invalidate(); } }

        public PulseIndicator()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Dock = DockStyle.Fill;
            t.Tick += (s, e) => { phase += 0.02f; if (phase > 1f) phase = 0f; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.HighQuality(g);
            float cx = 80, cy = Height / 2f;
            if (t.Enabled)
            {
                for (int i = 0; i < 2; i++)
                {
                    float p = (phase + i * 0.5f) % 1f;
                    float rad = 26 + p * 44;
                    using (var pen = new Pen(Theme.Alpha(StateColor, (int)(180 * (1 - p))), 2f))
                        g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                }
            }
            var core = new RectangleF(cx - 28, cy - 28, 56, 56);
            using (var br = new SolidBrush(Theme.Alpha(StateColor, 60))) g.FillEllipse(br, core);
            using (var pen = new Pen(StateColor, 2f)) g.DrawEllipse(pen, core);
            Theme.DrawGlyph(g, Glyph.Usb, 18f, StateColor, core);
            TextRenderer.DrawText(g, Title, Theme.Semi14, new Rectangle(150, (int)cy - 30, Width - 160, 30), Theme.Text, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Detail, Theme.F9, new Rectangle(150, (int)cy + 2, Width - 160, 40), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Thin animated progress bar (marquee or determinate).</summary>
    internal class ThinProgress : Control
    {
        private readonly Timer t = new Timer { Interval = 16 };
        private float pos;
        private int value = -1;
        public bool Running { get { return t.Enabled; } set { t.Enabled = value; if (!value) this.value = -1; Invalidate(); } }
        public int Value { get { return value; } set { this.value = value; Invalidate(); } }

        public ThinProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 4;
            BackColor = Theme.Bg;
            t.Tick += (s, e) => { pos += 0.012f; if (pos > 1.3f) pos = -0.3f; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (value >= 0)
            {
                int w = (int)(Width * Math.Min(100, value) / 100f);
                if (w > 0)
                    using (var br = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, w), Height), Theme.Accent, Theme.Accent2, 0f))
                        g.FillRectangle(br, 0, 0, w, Height);
                return;
            }
            if (!t.Enabled) return;
            int bw = Width / 3;
            int x = (int)(pos * Width);
            var r = new Rectangle(x, 0, bw, Height);
            using (var br = new LinearGradientBrush(new Rectangle(x - 1, 0, bw + 2, Height), Theme.Accent, Theme.Accent2, 0f))
                g.FillRectangle(br, r);
        }

        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Screenshot thumbnail tile; click to open the image.</summary>
    internal class ThumbTile : Control
    {
        private Image thumb;
        private bool over;
        public string FilePath;

        public ThumbTile(string path)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FilePath = path;
            Size = new Size(230, 178);
            Margin = new Padding(8);
            Cursor = Cursors.Hand;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(fs))
                {
                    float sc = Math.Min(214f / img.Width, 128f / img.Height);
                    thumb = new Bitmap(img, Math.Max(1, (int)(img.Width * sc)), Math.Max(1, (int)(img.Height * sc)));
                }
            }
            catch { thumb = null; }
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true }); } catch { }
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 10))
            {
                using (var br = new SolidBrush(Theme.CardAlt)) g.FillPath(br, path);
                using (var pen = new Pen(over ? Theme.Accent : Theme.Border, over ? 1.6f : 1f)) g.DrawPath(pen, path);
            }
            if (thumb != null)
                g.DrawImage(thumb, (Width - thumb.Width) / 2, 8 + (128 - thumb.Height) / 2, thumb.Width, thumb.Height);
            TextRenderer.DrawText(g, Path.GetFileNameWithoutExtension(FilePath), Theme.F9, new Rectangle(8, 142, Width - 16, 28), Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing) { if (disposing && thumb != null) thumb.Dispose(); base.Dispose(disposing); }
    }
}

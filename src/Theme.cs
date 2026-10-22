using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PrinterToolkit
{
    /// <summary>Central design tokens (colors, fonts, glyphs) for the whole application.</summary>
    internal static class Theme
    {
        // ---- Palette (deep navy dark mode with cyan -> violet accent gradient) ----
        public static readonly Color Bg = Color.FromArgb(11, 15, 25);
        public static readonly Color Sidebar = Color.FromArgb(15, 20, 33);
        public static readonly Color Card = Color.FromArgb(21, 27, 43);
        public static readonly Color CardAlt = Color.FromArgb(28, 35, 56);
        public static readonly Color Border = Color.FromArgb(40, 49, 74);
        public static readonly Color Text = Color.FromArgb(229, 235, 245);
        public static readonly Color Muted = Color.FromArgb(134, 147, 175);
        public static readonly Color Accent = Color.FromArgb(34, 211, 238);
        public static readonly Color Accent2 = Color.FromArgb(139, 92, 246);
        public static readonly Color Success = Color.FromArgb(52, 211, 153);
        public static readonly Color Warning = Color.FromArgb(251, 191, 36);
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);
        public static readonly Color Info = Color.FromArgb(96, 165, 250);

        // ---- Fonts (cached) ----
        public static readonly Font F9 = new Font("Segoe UI", 9f);
        public static readonly Font F10 = new Font("Segoe UI", 10f);
        public static readonly Font F11 = new Font("Segoe UI", 11f);
        public static readonly Font Semi9 = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Semi10 = new Font("Segoe UI Semibold", 10f);
        public static readonly Font Semi11 = new Font("Segoe UI Semibold", 11f);
        public static readonly Font Semi12 = new Font("Segoe UI Semibold", 12f);
        public static readonly Font Semi14 = new Font("Segoe UI Semibold", 14f);
        public static readonly Font Semi18 = new Font("Segoe UI Semibold", 18f);
        public static readonly Font Bold26 = new Font("Segoe UI", 26f, FontStyle.Bold);
        public static readonly Font Caps8 = new Font("Segoe UI Semibold", 8f);
        public static readonly Font Mono9 = new Font("Consolas", 9.5f);

        private static string iconFontName;
        public static string IconFontName
        {
            get
            {
                if (iconFontName == null)
                {
                    iconFontName = "Segoe MDL2 Assets";
                    using (var fonts = new InstalledFontCollection())
                    {
                        foreach (FontFamily fam in fonts.Families)
                        {
                            if (fam.Name == "Segoe Fluent Icons") { iconFontName = fam.Name; break; }
                        }
                    }
                }
                return iconFontName;
            }
        }

        public static Font Icon(float size) { return new Font(IconFontName, size); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }

        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static void DrawGlyph(Graphics g, string glyph, float size, Color color, RectangleF rect)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            using (var f = Icon(size))
            using (var b = new SolidBrush(color))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(glyph, f, b, rect, sf);
            }
        }

        /// <summary>Applies the dark style to a DataGridView.</summary>
        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Card;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = Border;
            g.EnableHeadersVisualStyles = false;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 34;
            g.RowTemplate.Height = 30;
            g.ColumnHeadersDefaultCellStyle.BackColor = CardAlt;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            g.ColumnHeadersDefaultCellStyle.Font = Semi9;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = CardAlt;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            g.DefaultCellStyle.BackColor = Card;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.Font = F9;
            g.DefaultCellStyle.SelectionBackColor = Alpha(Accent, 60);
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(24, 31, 49);
        }

        public static TextBox MakeTextBox(string text)
        {
            return new TextBox
            {
                Text = text,
                BackColor = CardAlt,
                ForeColor = Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = F10
            };
        }

        public static Label MakeLabel(string text, Font font, Color color)
        {
            return new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, BackColor = Color.Transparent };
        }
    }

    /// <summary>Segoe MDL2 / Fluent icon glyph code points.</summary>
    internal static class Glyph
    {
        public const string Home = "\uE80F";
        public const string Printer = "\uE749";
        public const string Diagnostic = "\uE9D9";
        public const string Download = "\uE896";
        public const string Save = "\uE74E";
        public const string Restore = "\uE777";
        public const string Camera = "\uE722";
        public const string Usb = "\uE88E";
        public const string Repair = "\uE90F";
        public const string Settings = "\uE713";
        public const string Refresh = "\uE72C";
        public const string Delete = "\uE74D";
        public const string Info = "\uE946";
        public const string Document = "\uE8A5";
        public const string Sync = "\uE895";
        public const string Check = "\uE73E";
        public const string Warning = "\uE7BA";
        public const string Cancel = "\uE711";
        public const string Play = "\uE768";
        public const string Stop = "\uE71A";
        public const string Folder = "\uE8B7";
        public const string Globe = "\uE774";
        public const string Search = "\uE721";
        public const string Bolt = "\uE945";
        public const string Code = "\uE943";
        public const string Queue = "\uE8FD";
        public const string Pause = "\uE769";
        public const string Network = "\uE839";
        public const string Shield = "\uEA18";
        public const string Package = "\uE7B8";
        public const string Monitor = "\uE7F4";
        public const string Upload = "\uE898";
        public const string Star = "\uE734";
    }
}

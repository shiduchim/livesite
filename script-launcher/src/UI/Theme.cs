using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EKScriptLauncher.UI
{
    /// <summary>Colours, fonts, DPI scaling and small drawing helpers.</summary>
    static class Theme
    {
        public const string Title = "EK Script Launcher";

        public static readonly double Scale = ScreenScale();

        /// <summary>Convert a 96-DPI layout size to pixels on this screen.</summary>
        public static int Dp(double value)
        {
            return (int)Math.Round(value * Scale);
        }

        // Muted blue, intentionally less bright/tiring than standard Revit blue.
        public static readonly Color Blue = Color.FromArgb(70, 101, 128);
        public static readonly Color BlueHover = Color.FromArgb(82, 116, 146);
        public static readonly Color BlueDown = Color.FromArgb(58, 85, 109);
        public static readonly Color BlueSoftText = Color.FromArgb(208, 221, 233);

        public static readonly Color WindowBack = Color.FromArgb(241, 243, 246);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(211, 216, 224);
        public static readonly Color Text = Color.FromArgb(30, 38, 50);
        public static readonly Color TextMuted = Color.FromArgb(104, 112, 125);
        public static readonly Color TextFaint = Color.FromArgb(150, 157, 168);

        public static readonly Color RowHover = Color.FromArgb(245, 247, 250);
        public static readonly Color RowSelected = Color.FromArgb(225, 233, 241);
        public static readonly Color RowDivider = Color.FromArgb(234, 237, 241);
        public static readonly Color MatchBack = Color.FromArgb(255, 224, 138);
        public static readonly Color ButtonHover = Color.FromArgb(234, 238, 243);
        public static readonly Color ButtonDown = Color.FromArgb(222, 228, 235);

        // Badge colours: muted so a long list stays calm, but different enough
        // that neighbouring scripts no longer blend together.
        public static readonly Color[] BadgeColors =
        {
            Color.FromArgb(70, 101, 128),   // slate blue
            Color.FromArgb(38, 124, 118),   // teal
            Color.FromArgb(78, 128, 66),    // green
            Color.FromArgb(168, 112, 30),   // amber
            Color.FromArgb(168, 76, 60),    // brick
            Color.FromArgb(122, 82, 140),   // plum
            Color.FromArgb(74, 84, 160),    // indigo
            Color.FromArgb(148, 78, 108),   // rose
            Color.FromArgb(92, 106, 60),    // olive
            Color.FromArgb(48, 112, 158),   // sky
        };

        public static readonly Font FontUi = new Font("Segoe UI", 9.75f);
        public static readonly Font FontUiBold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 14f);
        public static readonly Font FontSmall = new Font("Segoe UI", 9f);
        public static readonly Font FontSearch = new Font("Segoe UI", 11f);
        public static readonly Font FontName = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font FontDetail = new Font("Segoe UI", 8.75f);
        public static readonly Font FontBadge = new Font("Segoe UI Semibold", 10f);
        public static readonly Font FontEmpty = new Font("Segoe UI", 10.5f);
        public static readonly Font FontLaunch = new Font("Segoe UI", 13f, FontStyle.Bold);
        public static readonly Font FontLaunchSub = new Font("Segoe UI", 9f);

        public const TextFormatFlags TextFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        static double ScreenScale()
        {
            try
            {
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1.0, graphics.DpiX / 96.0);
            }
            catch
            {
                return 1.0;
            }
        }

        public static int TextWidth(string text, Font font, IDeviceContext graphics = null)
        {
            try
            {
                Size max = new Size(32767, 32767);
                return graphics != null
                    ? TextRenderer.MeasureText(graphics, text, font, max, TextFlags).Width
                    : TextRenderer.MeasureText(text, font, max, TextFlags).Width;
            }
            catch
            {
                return (int)(text.Length * font.Size * Scale);
            }
        }

        public static void FillRect(Graphics graphics, Color color, Rectangle rect)
        {
            using (var brush = new SolidBrush(color))
                graphics.FillRectangle(brush, rect);
        }

        public static void FillRounded(Graphics graphics, Color color, Rectangle rect, int radius)
        {
            int d = radius * 2;
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(color))
            {
                path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
                path.CloseFigure();

                SmoothingMode old = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillPath(brush, path);
                graphics.SmoothingMode = old;
            }
        }

        public static void StyleButton(Button button, bool primary = false)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            if (primary)
            {
                button.BackColor = Blue;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = BlueHover;
                button.FlatAppearance.MouseDownBackColor = BlueDown;
            }
            else
            {
                button.BackColor = Surface;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = ButtonHover;
                button.FlatAppearance.MouseDownBackColor = ButtonDown;
            }
        }

        public static void Message(IWin32Window owner, string text, MessageBoxIcon icon, string title = Title)
        {
            var form = owner as Form;
            if (owner != null && (form == null || !form.IsDisposed))
                MessageBox.Show(owner, text, title, MessageBoxButtons.OK, icon);
            else
                MessageBox.Show(text, title, MessageBoxButtons.OK, icon);
        }
    }
}

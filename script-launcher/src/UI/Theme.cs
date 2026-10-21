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

        // One muted accent colour; everything else is neutral grey.
        public static readonly Color Accent = Color.FromArgb(70, 101, 128);
        public static readonly Color AccentHover = Color.FromArgb(82, 116, 146);
        public static readonly Color AccentDown = Color.FromArgb(58, 85, 109);
        public static readonly Color AccentTint = Color.FromArgb(226, 233, 240);

        public static readonly Color WindowBack = Color.FromArgb(243, 244, 246);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(206, 211, 218);
        public static readonly Color Text = Color.FromArgb(32, 36, 42);
        public static readonly Color TextMuted = Color.FromArgb(110, 116, 126);
        public static readonly Color TextFaint = Color.FromArgb(150, 155, 163);
        public static readonly Color RowHover = Color.FromArgb(244, 246, 248);
        public static readonly Color MatchBack = Color.FromArgb(206, 219, 231);
        public static readonly Color ButtonHover = Color.FromArgb(235, 238, 242);
        public static readonly Color ButtonDown = Color.FromArgb(224, 228, 234);

        public static readonly Font FontUi = new Font("Segoe UI", 9f);
        public static readonly Font FontUiBold = new Font("Segoe UI Semibold", 9f);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.25f);
        public static readonly Font FontList = new Font("Segoe UI", 9.75f);
        public static readonly Font FontLaunch = new Font("Segoe UI Semibold", 10f);

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
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
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
                button.BackColor = Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = AccentHover;
                button.FlatAppearance.MouseDownBackColor = AccentDown;
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

        /// <summary>A white box with a 1 px border (accent when focused) holding a borderless text box.</summary>
        public static Panel FieldFrame(TextBox box, Rectangle bounds, int textLeft)
        {
            var frame = new Panel
            {
                BackColor = Surface,
                Bounds = bounds,
                Cursor = Cursors.IBeam,
            };

            box.BorderStyle = BorderStyle.None;
            box.BackColor = Surface;
            box.ForeColor = Text;
            box.Location = new Point(textLeft, (bounds.Height - box.Height) / 2);
            box.Width = bounds.Width - textLeft - Dp(6);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            frame.Controls.Add(box);

            frame.Paint += (s, e) =>
            {
                using (var pen = new Pen(box.Focused ? Accent : Border, 1f))
                    e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1);
            };
            frame.Click += (s, e) => box.Focus();
            box.GotFocus += (s, e) => frame.Invalidate();
            box.LostFocus += (s, e) => frame.Invalidate();
            return frame;
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// EK Print UI kit.
// Plain WinForms + GDI+, C# 5 compatible (Revit 2024 builds with the .NET Framework compiler).
// Every pixel metric goes through Ui.S(), so dialogs are laid out correctly at 100-200% display scale.

namespace EKPrint
{
    internal static class Ui
    {
        public const string Product = "EK Print";
        public const string Version = "6.2";

        public static readonly Color Window = Color.FromArgb(244, 246, 249);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceDisabled = Color.FromArgb(246, 247, 249);
        public static readonly Color FooterBack = Color.FromArgb(237, 240, 244);
        public static readonly Color Border = Color.FromArgb(218, 223, 230);
        public static readonly Color BorderStrong = Color.FromArgb(188, 196, 207);
        public static readonly Color Text = Color.FromArgb(29, 34, 41);
        public static readonly Color Muted = Color.FromArgb(99, 109, 122);
        public static readonly Color Disabled = Color.FromArgb(158, 166, 177);
        public static readonly Color Accent = Color.FromArgb(47, 107, 197);       // matches the ribbon icons
        public static readonly Color AccentHover = Color.FromArgb(38, 93, 175);
        public static readonly Color AccentPressed = Color.FromArgb(30, 77, 147);
        public static readonly Color AccentDisabled = Color.FromArgb(178, 196, 224);
        public static readonly Color Hover = Color.FromArgb(239, 242, 246);
        public static readonly Color Pressed = Color.FromArgb(226, 231, 238);

        public static readonly Font Body = new Font("Segoe UI", 9f);
        public static readonly Font BodySemibold = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font Section = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 12.75f);

        private static float _scale;

        /// <summary>Display scale relative to 96 DPI (1.0, 1.25, 1.5, 2.0 ...).</summary>
        public static float Scale
        {
            get
            {
                if (_scale <= 0f)
                {
                    float s = 1f;
                    try
                    {
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                            s = g.DpiX / 96f;
                    }
                    catch { }
                    if (float.IsNaN(s) || s < 1f) s = 1f;
                    _scale = s;
                }
                return _scale;
            }
        }

        public static int S(int value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d < 1f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Outline rectangle aligned to pixel centres so 1px borders stay crisp.</summary>
        public static RectangleF Outline(Rectangle r)
        {
            return new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
        }

        public static int TextWidth(string text, Font font)
        {
            return TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        public static int LineHeight(Font font)
        {
            return TextRenderer.MeasureText("Ag", font, Size.Empty, TextFormatFlags.NoPadding).Height;
        }

        public static string AssetFolder
        {
            get
            {
                try { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
                catch { return ""; }
            }
        }

        /// <summary>
        /// Loads the best ribbon asset for a logical (96 DPI) size, e.g. SelectPDF at 32 -> SelectPDF_48.png at 150%.
        /// Returns an unlocked copy, or null.
        /// </summary>
        public static Image LoadIcon(string name, int logicalSize)
        {
            try
            {
                string dir = AssetFolder;
                int need = (int)Math.Ceiling(logicalSize * Scale - 0.01);
                int[] sizes = new int[] { 32, 40, 48, 64 };
                string best = null;
                string largest = null;
                foreach (int s in sizes)
                {
                    string path = Path.Combine(dir, s == 32 ? name + ".png" : name + "_" + s.ToString() + ".png");
                    if (!File.Exists(path)) continue;
                    largest = path;
                    if (best == null && s >= need) best = path;
                }
                string pick = best ?? largest;
                if (pick == null) return null;
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(pick)))
                using (Image tmp = Image.FromStream(ms))
                    return new Bitmap(tmp);
            }
            catch
            {
                return null;
            }
        }

        public static void SetCue(TextBox box, string text)
        {
            try
            {
                if (box != null && box.IsHandleCreated)
                    SendMessage(box.Handle, 0x1501, (IntPtr)1, text ?? "");
            }
            catch { }
        }

        public static void ExplorerTheme(Control c)
        {
            try { SetWindowTheme(c.Handle, "Explorer", null); }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);
    }

    /// <summary>Owner-drawn segmented toggle (e.g. Sheets | Views).</summary>
    internal sealed class Segmented : Control
    {
        private string[] _items = new string[0];
        private int _selected = -1;
        private int _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public Segmented(params string[] items)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = Ui.Body;
            Cursor = Cursors.Hand;
            SetItems(items);
        }

        public void SetItems(params string[] items)
        {
            _items = items ?? new string[0];
            Invalidate();
        }

        /// <summary>-1 = nothing selected. Setting this does not raise SelectedIndexChanged.</summary>
        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        public int PreferredWidth
        {
            get
            {
                int total = 0;
                foreach (int w in TextWidths()) total += w;
                return total;
            }
        }

        private int[] TextWidths()
        {
            int pad = Ui.S(16);
            int[] widths = new int[_items.Length];
            for (int i = 0; i < _items.Length; i++)
                widths[i] = Ui.TextWidth(_items[i], Ui.BodySemibold) + pad * 2;
            return widths;
        }

        private Rectangle[] Segments()
        {
            int n = _items.Length;
            Rectangle[] result = new Rectangle[n];
            if (n == 0) return result;
            int[] widths = TextWidths();
            int total = 0;
            foreach (int w in widths) total += w;
            float k = total > 0 ? (float)Width / total : 1f;
            int x = 0;
            for (int i = 0; i < n; i++)
            {
                int w = i == n - 1 ? Width - x : (int)Math.Round(widths[i] * k);
                result[i] = new Rectangle(x, 0, w, Height);
                x += w;
            }
            return result;
        }

        private int IndexAt(Point p)
        {
            Rectangle[] segs = Segments();
            for (int i = 0; i < segs.Length; i++)
                if (segs[i].Contains(p)) return i;
            return -1;
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= _items.Length || index == _selected) return;
            _selected = index;
            Invalidate();
            EventHandler handler = SelectedIndexChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float radius = Ui.S(6);
            using (GraphicsPath outer = Ui.Round(Ui.Outline(ClientRectangle), radius))
            {
                using (SolidBrush b = new SolidBrush(Enabled ? Ui.Surface : Ui.SurfaceDisabled))
                    g.FillPath(b, outer);
                using (Pen p = new Pen(Enabled ? Ui.BorderStrong : Ui.Border))
                    g.DrawPath(p, outer);
            }

            Rectangle[] segs = Segments();
            int inset = Ui.S(3);
            for (int i = 0; i < segs.Length; i++)
            {
                Rectangle s = segs[i];
                bool selected = i == _selected;
                Rectangle pill = Rectangle.Inflate(s, -inset, -inset);
                if (selected)
                {
                    using (GraphicsPath p = Ui.Round(pill, radius - inset * 0.5f))
                    using (SolidBrush b = new SolidBrush(Enabled ? Ui.Accent : Ui.AccentDisabled))
                        g.FillPath(b, p);
                }
                else if (i == _hover && Enabled)
                {
                    using (GraphicsPath p = Ui.Round(pill, radius - inset * 0.5f))
                    using (SolidBrush b = new SolidBrush(Ui.Hover))
                        g.FillPath(b, p);
                }

                if (i > 0 && !selected && i - 1 != _selected && i != _hover && i - 1 != _hover)
                {
                    using (Pen p = new Pen(Ui.Border))
                        g.DrawLine(p, s.X, s.Y + Ui.S(9), s.X, s.Bottom - Ui.S(9));
                }

                Color fore = selected ? Color.White : (Enabled ? Ui.Text : Ui.Disabled);
                TextRenderer.DrawText(g, _items[i], selected ? Ui.BodySemibold : Ui.Body, s, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            if (Focused && ShowFocusCues)
            {
                Rectangle f = _selected >= 0 && _selected < segs.Length ? segs[_selected] : ClientRectangle;
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(f, -inset - 2, -inset - 2));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i != _hover)
            {
                _hover = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            Choose(IndexAt(e.Location));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || _items.Length == 0) return;
            if (e.KeyCode == Keys.Left) { Choose(Math.Max(0, _selected - 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Choose(Math.Min(_items.Length - 1, _selected + 1)); e.Handled = true; }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    /// <summary>Rounded text field with optional search glyph and clear button.</summary>
    internal sealed class InputBox : Control
    {
        private readonly TextBox _box;
        private readonly bool _search;
        private bool _hoverClear;
        private string _cue = "";

        public event EventHandler ValueChanged;

        public InputBox(bool search)
        {
            _search = search;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Surface;
            Cursor = Cursors.IBeam;

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.Font = Ui.Body;
            _box.BackColor = Ui.Surface;
            _box.ForeColor = Ui.Text;
            _box.TextChanged += delegate
            {
                Invalidate();
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            _box.GotFocus += delegate { Invalidate(); };
            _box.LostFocus += delegate { Invalidate(); };
            _box.HandleCreated += delegate { Ui.SetCue(_box, _cue); };
            Controls.Add(_box);
        }

        public TextBox Box { get { return _box; } }

        public string Value
        {
            get { return _box.Text ?? ""; }
            set { _box.Text = value ?? ""; }
        }

        public string Cue
        {
            get { return _cue; }
            set
            {
                _cue = value ?? "";
                Ui.SetCue(_box, _cue);
            }
        }

        public void FocusBox()
        {
            _box.Focus();
            _box.SelectAll();
        }

        private bool ShowClear { get { return _search && Enabled && _box != null && _box.TextLength > 0; } }
        private Rectangle ClearRect { get { return new Rectangle(Width - Height, 0, Height, Height); } }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_box == null) return;
            int left = _search ? Ui.S(32) : Ui.S(10);
            int right = _search ? Height : Ui.S(10);
            _box.Width = Math.Max(10, Width - left - right);
            _box.Location = new Point(left, Math.Max(0, (Height - _box.Height) / 2));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool focused = _box != null && _box.Focused;

            using (GraphicsPath p = Ui.Round(Ui.Outline(ClientRectangle), Ui.S(6)))
            {
                using (SolidBrush b = new SolidBrush(Enabled ? Ui.Surface : Ui.SurfaceDisabled))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(focused ? Ui.Accent : (Enabled ? Ui.BorderStrong : Ui.Border), focused ? Math.Max(1f, Ui.Scale * 1.25f) : 1f))
                    g.DrawPath(pen, p);
            }

            if (_search)
            {
                float cx = Ui.S(15), cy = Height / 2f - Ui.S(1);
                float r = Ui.S(5);
                using (Pen pen = new Pen(Ui.Muted, Math.Max(1.3f, Ui.Scale * 1.4f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                    g.DrawLine(pen, cx + r * 0.72f, cy + r * 0.72f, cx + r * 1.55f, cy + r * 1.55f);
                }
            }

            if (ShowClear)
            {
                Rectangle c = ClearRect;
                float mx = c.X + c.Width / 2f, my = c.Y + c.Height / 2f, k = Ui.S(4);
                if (_hoverClear)
                {
                    using (SolidBrush b = new SolidBrush(Ui.Hover))
                        g.FillEllipse(b, mx - k * 2.2f, my - k * 2.2f, k * 4.4f, k * 4.4f);
                }
                using (Pen pen = new Pen(_hoverClear ? Ui.Text : Ui.Muted, Math.Max(1.3f, Ui.Scale * 1.4f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, mx - k, my - k, mx + k, my + k);
                    g.DrawLine(pen, mx - k, my + k, mx + k, my - k);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = ShowClear && ClearRect.Contains(e.Location);
            if (over != _hoverClear)
            {
                _hoverClear = over;
                Cursor = over ? Cursors.Hand : Cursors.IBeam;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverClear)
            {
                _hoverClear = false;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            if (ShowClear && ClearRect.Contains(e.Location))
                _box.Text = "";
            _box.Focus();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (_box == null) return;
            _box.Enabled = Enabled;
            _box.BackColor = Enabled ? Ui.Surface : Ui.SurfaceDisabled;
            Invalidate();
        }
    }

    /// <summary>Rounded push button. Primary = filled accent, otherwise outlined.</summary>
    internal sealed class UiButton : Button
    {
        private readonly bool _primary;
        private bool _hover;
        private bool _down;

        public UiButton(string text, bool primary)
        {
            _primary = primary;
            Text = text;
            Font = primary ? Ui.BodySemibold : Ui.Body;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
        }

        public int PreferredButtonWidth
        {
            get { return Math.Max(Ui.S(96), Ui.TextWidth(Text, Font) + Ui.S(40)); }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, border, fore;
            if (_primary)
            {
                fill = !Enabled ? Ui.AccentDisabled : (_down ? Ui.AccentPressed : (_hover ? Ui.AccentHover : Ui.Accent));
                border = fill;
                fore = Color.White;
            }
            else
            {
                fill = !Enabled ? Ui.SurfaceDisabled : (_down ? Ui.Pressed : (_hover ? Ui.Hover : Ui.Surface));
                border = Enabled ? Ui.BorderStrong : Ui.Border;
                fore = Enabled ? Ui.Text : Ui.Disabled;
            }

            using (GraphicsPath p = Ui.Round(Ui.Outline(ClientRectangle), Ui.S(6)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }

            if (Focused && ShowFocusCues && Enabled)
            {
                Rectangle f = Rectangle.Inflate(ClientRectangle, -Ui.S(3), -Ui.S(3));
                using (GraphicsPath p = Ui.Round(Ui.Outline(f), Ui.S(4)))
                using (Pen pen = new Pen(_primary ? Color.FromArgb(170, 255, 255, 255) : Color.FromArgb(150, Ui.Accent)))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawPath(pen, p);
                }
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>White header band: icon, title, subtitle.</summary>
    internal sealed class HeaderBar : Control
    {
        private string _title = "";
        private string _subtitle = "";
        private Image _icon;

        public HeaderBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Surface;
            TabStop = false;
        }

        public string Title { get { return _title; } set { _title = value ?? ""; Invalidate(); } }
        public string Subtitle { get { return _subtitle; } set { _subtitle = value ?? ""; Invalidate(); } }
        public Image IconImage { get { return _icon; } set { _icon = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.Surface);
            int x = Ui.S(22);
            if (_icon != null)
            {
                int s = Ui.S(32);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_icon, new Rectangle(x, (Height - s) / 2, s, s));
                x += s + Ui.S(14);
            }

            int titleH = Ui.LineHeight(Ui.Title);
            int subH = _subtitle.Length > 0 ? Ui.LineHeight(Ui.Body) : 0;
            int gap = subH > 0 ? Ui.S(2) : 0;
            int top = (Height - titleH - gap - subH) / 2;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, _title, Ui.Title, new Rectangle(x, top, Width - x - Ui.S(20), titleH), Ui.Text, flags);
            if (subH > 0)
                TextRenderer.DrawText(g, _subtitle, Ui.Body, new Rectangle(x, top + titleH + gap, Width - x - Ui.S(20), subH), Ui.Muted, flags);

            using (Pen p = new Pen(Ui.Border))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _icon != null)
            {
                _icon.Dispose();
                _icon = null;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Footer band holding the dialog buttons.</summary>
    internal sealed class FooterBar : Panel
    {
        public FooterBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Ui.FooterBack;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Ui.Border))
                e.Graphics.DrawLine(p, 0, 0, Width, 0);
        }
    }

    /// <summary>Rounded white card. Children inherit the white background.</summary>
    internal sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Ui.Round(Ui.Outline(ClientRectangle), Ui.S(8)))
            {
                using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);
                using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>Thin rounded progress bar.</summary>
    internal sealed class ProgressLine : Control
    {
        private int _maximum = 1;
        private int _value;

        public ProgressLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        public int Maximum { get { return _maximum; } set { _maximum = Math.Max(1, value); Invalidate(); } }
        public int Value { get { return _value; } set { _value = Math.Max(0, Math.Min(value, _maximum)); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(0, 0, Width, Height);
            float r = Height / 2f;
            using (GraphicsPath p = Ui.Round(track, r))
            using (SolidBrush b = new SolidBrush(Ui.Pressed))
                g.FillPath(b, p);
            float w = Width * (_value / (float)_maximum);
            if (w > 0.5f)
            {
                using (GraphicsPath p = Ui.Round(new RectangleF(0, 0, Math.Max(w, Height), Height), r))
                using (SolidBrush b = new SolidBrush(Ui.Accent))
                    g.FillPath(b, p);
            }
        }
    }

    /// <summary>
    /// Details ListView with crisp DPI-scaled check boxes, roomy rows, Explorer styling,
    /// a stretching column and an empty-state message. Click a row to toggle it.
    /// </summary>
    internal sealed class CheckList : ListView
    {
        private int _fillColumn;
        private bool _fitting;
        private string _emptyText = "";

        public CheckList()
        {
            this.View = System.Windows.Forms.View.Details;
            CheckBoxes = true;
            FullRowSelect = true;
            HideSelection = false;
            MultiSelect = true;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            BorderStyle = BorderStyle.None;
            Font = Ui.Body;
            ForeColor = Ui.Text;
            BackColor = Ui.Surface;
            DoubleBuffered = true;

            int box = Ui.S(16);
            ImageList state = new ImageList();
            state.ColorDepth = ColorDepth.Depth32Bit;
            state.ImageSize = new Size(box, box);
            state.Images.Add(CheckGlyph(box, false));
            state.Images.Add(CheckGlyph(box, true));
            StateImageList = state;

            ImageList rows = new ImageList();   // sets a comfortable row height
            rows.ColorDepth = ColorDepth.Depth32Bit;
            rows.ImageSize = new Size(1, Ui.S(28));
            SmallImageList = rows;
        }

        public string EmptyText
        {
            get { return _emptyText; }
            set { _emptyText = value ?? ""; Invalidate(); }
        }

        public void SetColumns(string[] titles, int[] widths, int fillColumn)
        {
            BeginUpdate();
            try
            {
                Columns.Clear();
                for (int i = 0; i < titles.Length; i++)
                    Columns.Add(titles[i], widths[i]);
                _fillColumn = fillColumn;
            }
            finally { EndUpdate(); }
            FitColumns();
        }

        public void FitColumns()
        {
            if (_fitting || Columns.Count == 0 || !IsHandleCreated) return;
            _fitting = true;
            try
            {
                int other = 0;
                for (int i = 0; i < Columns.Count; i++)
                    if (i != _fillColumn) other += Columns[i].Width;
                int w = Math.Max(Ui.S(140), ClientSize.Width - other);
                if (_fillColumn >= 0 && _fillColumn < Columns.Count && Columns[_fillColumn].Width != w)
                    Columns[_fillColumn].Width = w;
            }
            finally { _fitting = false; }
        }

        private static Bitmap CheckGlyph(int size, bool on)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float pad = Math.Max(1f, (float)Math.Round(size * 0.08f));
                RectangleF r = new RectangleF(pad + 0.5f, pad + 0.5f, size - pad * 2f - 1f, size - pad * 2f - 1f);
                using (GraphicsPath p = Ui.Round(r, size * 0.2f))
                {
                    if (on)
                    {
                        using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
                        using (Pen pen = new Pen(Ui.Accent)) g.DrawPath(pen, p);
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
                        using (Pen pen = new Pen(Color.FromArgb(132, 143, 158), Math.Max(1f, size / 16f))) g.DrawPath(pen, p);
                    }
                }
                if (on)
                {
                    using (Pen pen = new Pen(Color.White, Math.Max(1.6f, size * 0.125f)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        g.DrawLines(pen, new PointF[] {
                            new PointF(size * 0.29f, size * 0.52f),
                            new PointF(size * 0.44f, size * 0.67f),
                            new PointF(size * 0.72f, size * 0.36f) });
                    }
                }
            }
            return bmp;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.ExplorerTheme(this);
            FitColumns();
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            FitColumns();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || Control.ModifierKeys != Keys.None) return;
            ListViewHitTestInfo hit = HitTest(e.Location);
            if (hit == null || hit.Item == null || hit.Location == ListViewHitTestLocations.StateImage) return;
            hit.Item.Checked = !hit.Item.Checked;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_PAINT = 0x000F;
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_LBUTTONDBLCLK = 0x0203;

            // A native double-click would toggle the check box a second time; treat it as a normal click.
            if (m.Msg == WM_LBUTTONDBLCLK) m.Msg = WM_LBUTTONDOWN;

            base.WndProc(ref m);

            if (m.Msg == WM_PAINT && Items.Count == 0 && _emptyText.Length > 0)
            {
                using (Graphics g = CreateGraphics())
                {
                    Rectangle r = new Rectangle(Ui.S(12), Ui.S(44), Math.Max(0, ClientSize.Width - Ui.S(24)), Ui.S(40));
                    TextRenderer.DrawText(g, _emptyText, Ui.Body, r, Ui.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Offline3D.RevitExporter;

/// <summary>
/// Colours from docs/EK_Revit_Addin_UI_Style_Guide.md (ClickTo3D 1.0.0). Every
/// colour used by the exporter windows comes from one of these two palettes.
/// </summary>
internal sealed class EkPalette
{
    public Color Body, TitleBar, Text, Secondary, Field, FieldBorder, FieldFocus, FooterLine, Frame;
    public Color ButtonFill, ButtonBorder, ButtonBottom, DefaultOutline, DefaultBottom, Hover, HoverOutline, Pressed, CloseHover, ClosePressed, Accent, Error;
    public bool Dark;

    private static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    public static readonly EkPalette Light = new EkPalette
    {
        Body = C(0xF0F0F0), TitleBar = C(0xF9F9F9), Text = C(0x1B1B1B), Secondary = C(0x5F6368), Field = C(0xFFFFFF), FieldBorder = C(0xC3C8CF), FieldFocus = C(0x5D75A5),
        FooterLine = C(0xE1E1E1), Frame = C(0xBDBDBD), ButtonFill = C(0xFDFDFD), ButtonBorder = C(0xD0D0D0), ButtonBottom = C(0xBABABA), DefaultOutline = C(0x9DB0D8), DefaultBottom = C(0x8A9ECA),
        Hover = C(0xF2F7FC), HoverOutline = C(0x9DB0D8), Pressed = C(0xE6EFF9), CloseHover = C(0xEBEBEB), ClosePressed = C(0xE0E0E0), Accent = C(0x5D75A5), Error = C(0xB3261E)
    };

    public static readonly EkPalette DarkTheme = new EkPalette
    {
        Dark = true, Body = C(0x2B2B2B), TitleBar = C(0x323232), Text = C(0xE6E6E6), Secondary = C(0xA8A8A8), Field = C(0x1F1F1F), FieldBorder = C(0x575757), FieldFocus = C(0x8EA3CF),
        FooterLine = C(0x3C3C3C), Frame = C(0x4A4A4A), ButtonFill = C(0x383838), ButtonBorder = C(0x555555), ButtonBottom = C(0x4A4A4A), DefaultOutline = C(0x7E93C2), DefaultBottom = C(0x6D82B3),
        Hover = C(0x41454D), HoverOutline = C(0x7E93C2), Pressed = C(0x4B5059), CloseHover = C(0x3D3D3D), ClosePressed = C(0x474747), Accent = C(0x8EA3CF), Error = C(0xF2B8B5)
    };
}

internal static class EkUi
{
    public const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    /// <summary>Lets a borderless window be dragged by a control (the title bar).</summary>
    public static void DragWindow(Form form) { ReleaseCapture(); SendMessage(form.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); }

    public static int S(Control c, double px) => (int)Math.Round(px * c.DeviceDpi / 96.0);

    /// <summary>Segoe UI 12 px (style guide: one size for everything) at the control's DPI.</summary>
    public static Font Font(double px = 12) => new Font("Segoe UI", (float)(px * .75), FontStyle.Regular, GraphicsUnit.Point);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        if (radius <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    public static void Text(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix)
        => TextRenderer.DrawText(g, text, font, bounds, color, flags);

    /// <summary>A field-style box: background, 1 px border, radius 4.</summary>
    public static void Field(Graphics g, Rectangle r, Color fill, Color border, float scale)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), 4 * scale);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(border, Math.Max(1, (int)scale))) g.DrawPath(p, path);
        g.SmoothingMode = SmoothingMode.None;
    }
}

/// <summary>Base for the custom-drawn controls: double buffered, palette aware.</summary>
internal abstract class EkControl : Control
{
    protected EkControl(EkPalette palette)
    {
        Palette = palette;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Font = EkUi.Font(); BackColor = palette.Body; ForeColor = palette.Text;
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public EkPalette Palette { get; set; }
    protected bool Hot, Down;
    protected float Dpi => DeviceDpi / 96f;
    protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hot = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) { Down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { bool was = Down; Down = false; Invalidate(); base.OnMouseUp(e); if (was && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && Enabled) Activate(); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) { Activate(); e.Handled = true; } base.OnKeyDown(e); }
    /// <summary>Click or Space.</summary>
    protected virtual void Activate() => OnClick(EventArgs.Empty);
    /// <summary>For tests and keyboard shortcuts.</summary>
    public void PerformClick() => Activate();
}

/// <summary>Own title bar (height 30): title left, a word action ("Close", "Cancel") right; drags the window.</summary>
internal sealed class EkTitleBar : EkControl
{
    private Rectangle action;
    public string Action { get; set; } = "Close";
    public event Action? ActionClicked;
    public EkTitleBar(EkPalette palette) : base(palette) { Dock = DockStyle.Top; TabStop = false; BackColor = palette.TitleBar; }
    protected override void OnLayout(LayoutEventArgs e) { Height = EkUi.S(this, 30); base.OnLayout(e); }
    private Rectangle ActionBounds(Graphics? g = null)
    {
        using var font = EkUi.Font(11.5);
        int w = TextRenderer.MeasureText(Action, font, Size.Empty, TextFormatFlags.NoPadding).Width + EkUi.S(this, 24);
        return new Rectangle(Width - w, 0, w, Height);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Palette.TitleBar); action = ActionBounds();
        EkUi.Text(g, Text, Font, new Rectangle(EkUi.S(this, 12), 0, action.Left - EkUi.S(this, 12), Height), Palette.Text);
        var mouse = PointToClient(Cursor.Position); bool over = action.Contains(mouse) && Hot && Enabled;
        if (over) using (var b = new SolidBrush(Down ? Palette.ClosePressed : Palette.CloseHover)) g.FillRectangle(b, action);
        using var font = EkUi.Font(11.5);
        EkUi.Text(g, Action, font, action, Enabled ? Palette.Secondary : Color.FromArgb(140, Palette.Secondary), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
    protected override void OnMouseMove(MouseEventArgs e) { Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !ActionBounds().Contains(e.Location) && FindForm() is Form form) { EkUi.DragWindow(form); return; }
        base.OnMouseDown(e);
    }
    protected override void Activate() { if (ActionBounds().Contains(PointToClient(Cursor.Position)) && Enabled) ActionClicked?.Invoke(); }
}

/// <summary>Save/Cancel-style button: height 23, min width 71, radius 4, darker bottom edge; the default one has a soft slate outline.</summary>
internal sealed class EkButton : EkControl
{
    public bool IsDefault { get; set; }
    public EkButton(EkPalette palette, string text) : base(palette) { Text = text; TabStop = true; }
    public override Size GetPreferredSize(Size proposed)
    {
        int w = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + EkUi.S(this, 20);
        return new Size(Math.Max(EkUi.S(this, 71), w), EkUi.S(this, 23));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Palette.Body); float s = Dpi;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = !Enabled ? Palette.ButtonFill : Down ? Palette.Pressed : Hot ? Palette.Hover : Palette.ButtonFill;
        Color border = IsDefault ? Palette.DefaultOutline : Hot && Enabled ? Palette.HoverOutline : Palette.ButtonBorder;
        Color bottom = IsDefault ? Palette.DefaultBottom : Palette.ButtonBottom;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = EkUi.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 4 * s))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            // Bottom edge one pixel darker (Windows 11 buttons), then the outline.
            using (var p = new Pen(bottom, Math.Max(1, s))) { g.SetClip(new Rectangle(0, Height - (int)Math.Ceiling(5 * s), Width, Height)); g.DrawPath(p, path); g.ResetClip(); }
            using (var p = new Pen(border, Math.Max(1, s))) { g.SetClip(new Rectangle(0, 0, Width, Height - (int)Math.Ceiling(2 * s))); g.DrawPath(p, path); g.ResetClip(); }
        }
        g.SmoothingMode = SmoothingMode.None;
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -3, -3), Palette.Text, fill);
        EkUi.Text(g, Text, Font, ClientRectangle, Enabled ? Palette.Text : Palette.Secondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>A quiet text link (no box): secondary colour, underline on hover.</summary>
internal sealed class EkLink : EkControl
{
    public bool Accent { get; set; }
    public EkLink(EkPalette palette, string text) : base(palette) { Text = text; TabStop = true; Cursor = Cursors.Hand; }
    public override Size GetPreferredSize(Size proposed) { var t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding); return new Size(t.Width + EkUi.S(this, 2), Math.Max(t.Height, EkUi.S(this, 18))); }
    protected override void OnTextChanged(EventArgs e) { Size = GetPreferredSize(Size.Empty); Invalidate(); base.OnTextChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Palette.Body);
        Color color = !Enabled ? Color.FromArgb(120, Palette.Secondary) : Accent ? Palette.Accent : Hot ? Palette.Text : Palette.Secondary;
        using var font = Hot && Enabled ? new Font(Font, FontStyle.Underline) : (Font)Font.Clone();
        EkUi.Text(g, Text, font, ClientRectangle, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
    }
}

/// <summary>A drop-down that looks like the style guide's fields (height 23, radius 4) and opens a plain list.</summary>
internal sealed class EkDropDown : EkControl
{
    private readonly List<string> items = new List<string>();
    private int selected = -1;
    public event EventHandler? SelectedIndexChanged;
    public EkDropDown(EkPalette palette) : base(palette) { TabStop = true; }
    public IReadOnlyList<string> Items => items;
    public void SetItems(IEnumerable<string> values, int index) { items.Clear(); items.AddRange(values); selected = -1; SelectedIndex = index; }
    public int SelectedIndex
    {
        get => selected;
        set { int next = items.Count == 0 ? -1 : Math.Max(0, Math.Min(items.Count - 1, value)); if (next == selected) return; selected = next; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string SelectedText => selected >= 0 ? items[selected] : "";
    public override Size GetPreferredSize(Size proposed) => new Size(Width, EkUi.S(this, 23));
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!Enabled) return;
        if (e.KeyCode == Keys.Down && !e.Alt) { SelectedIndex = selected + 1; e.Handled = true; return; }
        if (e.KeyCode == Keys.Up) { SelectedIndex = selected - 1; e.Handled = true; return; }
        if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Down && e.Alt) { Activate(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }
    protected override void Activate()
    {
        if (!Enabled || items.Count == 0) return;
        Focus();
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false, Font = Font, Renderer = new ListRenderer(Palette), BackColor = Palette.Field, ForeColor = Palette.Text, Padding = new Padding(0, EkUi.S(this, 2), 0, EkUi.S(this, 2)) };
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(items[i]) { AutoSize = false, Width = Width - 2, Height = EkUi.S(this, 23), ForeColor = Palette.Text, Font = i == selected ? new Font(Font, FontStyle.Regular) : Font };
            item.Click += (_, __) => SelectedIndex = index;
            menu.Items.Add(item);
        }
        menu.Closed += (_, __) => BeginInvoke(new Action(() => menu.Dispose()));
        menu.Show(this, new Point(0, Height));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Palette.Body); float s = Dpi;
        var r = new Rectangle(0, 0, Width, Height);
        EkUi.Field(g, r, Enabled ? Palette.Field : Palette.Body, Focused ? Palette.FieldFocus : Hot && Enabled ? Palette.HoverOutline : Palette.FieldBorder, s);
        int pad = EkUi.S(this, 7), arrow = EkUi.S(this, 18);
        EkUi.Text(g, SelectedText, Font, new Rectangle(pad, 0, Width - pad - arrow, Height), Enabled ? Palette.Text : Palette.Secondary);
        // Chevron
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = Width - arrow / 2f - 2 * s, cy = Height / 2f;
        using (var p = new Pen(Palette.Secondary, 1.3f * s)) g.DrawLines(p, new[] { new PointF(cx - 4 * s, cy - 2 * s), new PointF(cx, cy + 2 * s), new PointF(cx + 4 * s, cy - 2 * s) });
        g.SmoothingMode = SmoothingMode.None;
    }

    private sealed class ListRenderer : ToolStripProfessionalRenderer
    {
        private readonly EkPalette palette;
        public ListRenderer(EkPalette palette) : base(new Colors(palette)) { this.palette = palette; RoundedEdges = false; }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var b = new SolidBrush(e.Item.Selected ? palette.Hover : palette.Field);
            e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = palette.Text; base.OnRenderItemText(e); }
        private sealed class Colors : ProfessionalColorTable
        {
            private readonly EkPalette p; public Colors(EkPalette p) { this.p = p; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground => p.Field;
            public override Color MenuBorder => p.FieldBorder;
            public override Color MenuItemBorder => p.Hover;
            public override Color MenuItemSelected => p.Hover;
            public override Color ImageMarginGradientBegin => p.Field;
            public override Color ImageMarginGradientMiddle => p.Field;
            public override Color ImageMarginGradientEnd => p.Field;
        }
    }
}

/// <summary>A 13 px check box in the field colours; checked uses the slate accent.</summary>
internal sealed class EkCheckBox : EkControl
{
    private bool isChecked;
    public event EventHandler? CheckedChanged;
    public EkCheckBox(EkPalette palette, string text, bool value) : base(palette) { Text = text; isChecked = value; TabStop = true; }
    public bool Checked { get => isChecked; set { if (isChecked == value) return; isChecked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    public override Size GetPreferredSize(Size proposed) => new Size(EkUi.S(this, 20) + TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + EkUi.S(this, 4), EkUi.S(this, 24));
    protected override void Activate() { if (Enabled) Checked = !Checked; base.Activate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Palette.Body); float s = Dpi;
        int box = EkUi.S(this, 13), y = (Height - box) / 2;
        var r = new Rectangle(0, y, box, box);
        Color border = !Enabled ? Palette.FieldBorder : isChecked ? Palette.Accent : Hot ? Palette.FieldFocus : Color.FromArgb(Palette.Dark ? 0x80 : 0x8D, Palette.Dark ? 0x80 : 0x93, Palette.Dark ? 0x80 : 0x9B);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = EkUi.Rounded(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), 2 * s))
        {
            // Disabled but checked stays readable: grey box with the tick.
            using (var b = new SolidBrush(isChecked ? (Enabled ? Palette.Accent : Palette.FieldBorder) : Palette.Field)) g.FillPath(b, path);
            using (var p = new Pen(border, Math.Max(1, s))) g.DrawPath(p, path);
        }
        if (isChecked)
            using (var p = new Pen(Palette.Dark ? Palette.Body : Color.White, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(r.X + 3f * s, r.Y + 6.8f * s), new PointF(r.X + 5.5f * s, r.Y + 9.3f * s), new PointF(r.X + 10f * s, r.Y + 3.8f * s) });
        g.SmoothingMode = SmoothingMode.None;
        var text = new Rectangle(box + EkUi.S(this, 7), 0, Width - box - EkUi.S(this, 7), Height);
        EkUi.Text(g, Text, Font, text, Enabled ? Palette.Text : Palette.Secondary);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(text, 0, -3));
    }
}

/// <summary>A borderless window in the style guide's frame: own title bar, 1 px frame, no shadow, Esc and Alt+F4 work.</summary>
internal class EkWindow : Form
{
    protected readonly EkPalette Palette;
    protected readonly EkTitleBar TitleBar;
    public EkWindow(EkPalette palette, string title, string action)
    {
        Palette = palette;
        AutoScaleMode = AutoScaleMode.None; // sizes come from DeviceDpi (EkUi.S)
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; KeyPreview = true;
        Font = EkUi.Font(); BackColor = palette.Body; ForeColor = palette.Text; Text = title;
        DoubleBuffered = true; Padding = new Padding(1);
        TitleBar = new EkTitleBar(palette) { Text = title, Action = action };
        Controls.Add(TitleBar);
    }
    protected int S(double px) => EkUi.S(this, px);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Palette.Frame);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
    /// <summary>Esc and Alt+F4 run the title bar action (Close or Cancel).</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape || keyData == (Keys.Alt | Keys.F4)) { OnTitleAction(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected virtual void OnTitleAction() { }
    protected void CenterOn(IntPtr owner)
    {
        var area = NativeWindows.WorkingArea(owner, out var ownerBounds);
        int x = ownerBounds.Width > 0 ? ownerBounds.Left + (ownerBounds.Width - Width) / 2 : area.Left + (area.Width - Width) / 2;
        int y = ownerBounds.Height > 0 ? ownerBounds.Top + (ownerBounds.Height - Height) / 2 : area.Top + (area.Height - Height) / 2;
        Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - Height)));
    }
}

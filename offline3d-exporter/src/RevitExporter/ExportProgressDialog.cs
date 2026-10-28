using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Offline3D.RevitExporter;

internal sealed class ExportProgressCounts
{
    public long Elements { get; set; }
    public long PlacedTriangles { get; set; }
    public long Reused { get; set; }
    public string? Current { get; set; }
}

/// <summary>
/// Progress window, layout "P2" of docs/mockups/export-dialog-options.png in the EK style:
/// the four steps, one bar for the whole export, time left and the projected PDF size.
/// "Cancel" in the title bar (and Esc) cancels.
///
/// Revit work stays on its API/UI thread. The caller pumps this form between bounded
/// chunks; no background thread ever calls the Revit API. While it is shown, Revit's main
/// window is disabled so no other command or model edit can re-enter during the
/// temporary transactions.
/// </summary>
internal sealed class ExportProgressDialog : EkWindow
{
    private static readonly string[] Steps = { "Prepare export view", "Collect model geometry", "Write 3D model", "Export PDF pages" };
    private readonly Stopwatch total = Stopwatch.StartNew();
    private readonly List<string> timing = new List<string>();
    private readonly Timer timer = new Timer { Interval = 250 };
    private readonly Body body;
    private string phase = "Preparing export";
    private long phaseStart, lastPump = -1000;
    private IntPtr disabledOwner;
    private bool completed, ownerRestored;
    public bool CancelRequested { get; private set; }
    public TimeSpan Elapsed => total.Elapsed;
    public IReadOnlyList<string> TimingLog => timing.Concat(new[] { Stamp(phase, total.ElapsedMilliseconds - phaseStart) }).ToArray();
    /// <summary>Overall progress shown by the steps, bar and time-left line.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ExportProgressModel? Model { get; set; }

    public ExportProgressDialog(EkPalette? palette = null) : this(palette ?? EkPalette.Light, "Exporting 3D PDF") { }

    private ExportProgressDialog(EkPalette palette, string title) : base(palette, title, "Cancel")
    {
        body = new Body(this, palette) { Dock = DockStyle.Fill };
        Controls.Add(body); body.BringToFront();
        TitleBar.ActionClicked += RequestCancel;
        timer.Tick += (_, __) => { Model?.Update(); body.Invalidate(); }; timer.Start();
    }

    public new void Show(IWin32Window owner)
    {
        CreateControl();
        ClientSize = new Size(S(367), S(248));
        CenterOn(owner.Handle);
        base.Show(owner);
        // DoEvents is needed to process Cancel while CustomExporter calls back.
        if (owner.Handle != IntPtr.Zero && NativeWindows.IsWindowEnabled(owner.Handle)) { disabledOwner = owner.Handle; NativeWindows.EnableWindow(disabledOwner, false); ownerRestored = false; }
        BringToFront(); Pump(phase);
    }

    private ExportProgressCounts? counts;

    public bool Pump(string stage, ExportProgressCounts? values = null)
    {
        if (IsDisposed || CancelRequested) return false;
        long now = total.ElapsedMilliseconds;
        bool changed = !string.Equals(phase, stage, StringComparison.Ordinal);
        if (changed) { timing.Add(Stamp(phase, now - phaseStart)); phase = stage; phaseStart = now; counts = null; }
        if (values != null) counts = values;
        if (changed || now - lastPump >= 100)
        {
            Model?.Update(); lastPump = now;
            if (Visible) { body.Invalidate(); Update(); Application.DoEvents(); }
        }
        return !CancelRequested && !IsDisposed;
    }

    public void Complete() { completed = true; RestoreOwner(); Close(); }
    public new void Hide() { RestoreOwner(); base.Hide(); }
    protected override void OnTitleAction() => RequestCancel();
    private void RequestCancel() { if (CancelRequested) return; CancelRequested = true; TitleBar.Action = "Cancelling…"; TitleBar.Enabled = false; TitleBar.Invalidate(); body.Invalidate(); }
    private static string Stamp(string phase, long milliseconds) => phase + " | " + milliseconds + " ms";

    private void RestoreOwner()
    {
        if (ownerRestored || disabledOwner == IntPtr.Zero) return;
        NativeWindows.EnableWindow(disabledOwner, true); ownerRestored = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!completed && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; RequestCancel(); }
        else RestoreOwner();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { RestoreOwner(); timer.Dispose(); }
        base.Dispose(disposing);
    }

    /// <summary>Steps, bar and the time/size line, drawn in one pass.</summary>
    private sealed class Body : EkControl
    {
        private readonly ExportProgressDialog owner;
        public Body(ExportProgressDialog owner, EkPalette palette) : base(palette) { this.owner = owner; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Palette.Body); float s = Dpi;
            int S(double px) => EkUi.S(this, px);
            var model = owner.Model;
            int current = model == null ? 0 : Math.Min(Steps.Length, (int)model.Stage);
            int x = S(18), y = S(14), right = Width - S(32);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Steps.Length; i++)
            {
                bool done = i < current, now = i == current && !owner.CancelRequested;
                var dot = new RectangleF(x + .5f, y + (S(22) - S(10)) / 2f + .5f, S(10) - 1, S(10) - 1);
                using (var fill = new SolidBrush(done ? Palette.Accent : Palette.Field)) g.FillEllipse(fill, dot);
                using (var pen = new Pen(done || now ? Palette.Accent : Palette.FieldBorder, now ? 2 * s : s)) g.DrawEllipse(pen, now ? RectangleF.Inflate(dot, -.5f * s, -.5f * s) : dot);
                var text = new Rectangle(x + S(18), y, right - x - S(18), S(22));
                EkUi.Text(g, Steps[i], Font, text, done || now ? Palette.Text : Palette.Secondary);
                if (now && owner.counts is ExportProgressCounts c && c.Elements > 0)
                {
                    int w = TextRenderer.MeasureText(Steps[i], Font, Size.Empty, TextFormatFlags.NoPadding).Width + S(10);
                    EkUi.Text(g, c.Elements.ToString("N0", CultureInfo.CurrentCulture) + " elements", Font, new Rectangle(text.X + w, y, text.Width - w, S(22)), Palette.Secondary);
                }
                y += S(22);
            }
            // The bar: 6 px, rounded, whole export.
            y += S(10);
            var track = new RectangleF(x, y, right - x, S(6));
            using (var path = EkUi.Rounded(track, S(3))) using (var b = new SolidBrush(Palette.FooterLine)) g.FillPath(b, path);
            double fraction = model?.Fraction ?? 0;
            if (fraction > 0)
                using (var path = EkUi.Rounded(new RectangleF(track.X, track.Y, Math.Max(S(6), (float)(track.Width * fraction)), track.Height), S(3)))
                using (var b = new SolidBrush(Palette.Accent)) g.FillPath(b, path);
            g.SmoothingMode = SmoothingMode.None;
            y += S(16);
            string left = owner.CancelRequested ? "Cancelling at the next safe point…" : model == null ? owner.phase : ExportProgressModel.FormatTimeLeft(model.SecondsLeft) is var t && t.Length > 0 ? t : "Estimating time left…";
            string size = model?.ProjectedPdfBytes is long bytes ? "PDF about " + ExportProgressModel.FormatBytes(bytes) : "";
            EkUi.Text(g, left, Font, new Rectangle(x, y, right - x, S(20)), Palette.Secondary);
            EkUi.Text(g, size, Font, new Rectangle(x, y, right - x, S(20)), Palette.Secondary, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            // What Revit is working on (the step text the command reports).
            EkUi.Text(g, owner.counts?.Current ?? owner.phase, Font, new Rectangle(x, y + S(22), right - x, S(20)), Palette.Secondary);
        }
    }
}

using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;
using Button = System.Windows.Forms.Button;
using Label = System.Windows.Forms.Label;
using Timer = System.Windows.Forms.Timer;

namespace Offline3D.RevitExporter;

internal sealed class ExportProgressCounts
{
    public long Elements { get; set; }
    public long PlacedTriangles { get; set; }
    public long Reused { get; set; }
    public string? Current { get; set; }
}

/// <summary>Revit work stays on its API/UI thread. The caller pumps this form
/// between bounded chunks; no background thread ever calls the Revit API.
/// While it is shown, Revit's main window is disabled so no other command or
/// model edit can re-enter during the temporary transactions.</summary>
internal sealed class ExportProgressDialog : Form
{
    private readonly Label phaseLabel = new Label();
    private readonly Label counts = new Label();
    private readonly Label current = new Label();
    private readonly Label elapsedLabel = new Label();
    private readonly ProgressBar bar = new ProgressBar { Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 1000 };
    private readonly Button cancel = new Button();
    private readonly Stopwatch total = Stopwatch.StartNew();
    private readonly List<string> timing = new List<string>();
    private readonly Timer timer = new Timer { Interval = 250 };
    private string phase = "Preparing export";
    private long phaseStart, lastPump = -1000;
    private IntPtr disabledOwner;
    private bool completed, ownerRestored;
    public bool CancelRequested { get; private set; }
    /// <summary>Overall progress shown by the bar and the time-left line; null shows only the stage.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ExportProgressModel? Model { get; set; }
    public TimeSpan Elapsed => total.Elapsed;
    public IReadOnlyList<string> TimingLog => timing.Concat(new[] { Stamp(phase, total.ElapsedMilliseconds - phaseStart) }).ToArray();

    public ExportProgressDialog(string title = "Export 3D PDF")
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None; // sizes are computed from DeviceDpi below
        Font = new Font("Segoe UI", 9f); Text = title; BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; ControlBox = true;
        int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(S(18), S(16), S(18), S(14)) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (var label in new[] { phaseLabel, counts, current, elapsedLabel }) { label.AutoSize = true; label.Anchor = AnchorStyles.Left | AnchorStyles.Right; label.Margin = new Padding(0, 0, 0, S(6)); }
        phaseLabel.Text = phase; phaseLabel.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        current.ForeColor = Color.FromArgb(90, 90, 90); current.AutoEllipsis = true; current.AutoSize = false; current.Height = S(20);
        elapsedLabel.ForeColor = Color.FromArgb(90, 90, 90);
        layout.Controls.Add(phaseLabel, 0, 0); layout.Controls.Add(counts, 0, 1); layout.Controls.Add(current, 0, 2);
        bar.Anchor = AnchorStyles.Left | AnchorStyles.Right; bar.Height = S(10); bar.Margin = new Padding(0, S(4), 0, S(8));
        layout.Controls.Add(bar, 0, 3); layout.Controls.Add(elapsedLabel, 0, 4);
        var footer = new TableLayoutPanel { Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, S(8), 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var note = new Label { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Text = "Some Revit steps must finish before Cancel takes effect.", ForeColor = Color.FromArgb(110, 110, 110), Margin = new Padding(0, S(6), S(12), 0) };
        cancel.AutoSize = true; cancel.MinimumSize = new Size(S(96), S(30)); cancel.Text = "Cancel"; cancel.Click += (_, __) => RequestCancel(); CancelButton = cancel;
        footer.Controls.Add(note, 0, 0); footer.Controls.Add(cancel, 1, 0);
        layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout);
        ClientSize = new Size(S(520), S(220));
        ResumeLayout(true);
        timer.Tick += (_, __) => { Model?.Update(); UpdateElapsed(); }; timer.Start(); UpdateElapsed();
    }

    public new void Show(IWin32Window owner)
    {
        CenterOn(owner.Handle);
        base.Show(owner);
        // DoEvents is needed to process Cancel while CustomExporter calls back.
        if (owner.Handle != IntPtr.Zero && NativeWindows.IsWindowEnabled(owner.Handle)) { disabledOwner = owner.Handle; NativeWindows.EnableWindow(disabledOwner, false); ownerRestored = false; }
        BringToFront(); Pump(phase);
    }

    private void CenterOn(IntPtr owner)
    {
        var area = NativeWindows.WorkingArea(owner, out var ownerBounds);
        int x = ownerBounds.Width > 0 ? ownerBounds.Left + (ownerBounds.Width - Width) / 2 : area.Left + (area.Width - Width) / 2;
        int y = ownerBounds.Height > 0 ? ownerBounds.Top + (ownerBounds.Height - Height) / 2 : area.Top + (area.Height - Height) / 2;
        Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - Height)));
    }

    public bool Pump(string stage, ExportProgressCounts? values = null)
    {
        if (IsDisposed || CancelRequested) return false;
        long now = total.ElapsedMilliseconds;
        bool changed = !string.Equals(phase, stage, StringComparison.Ordinal);
        if (changed) { timing.Add(Stamp(phase, now - phaseStart)); phase = stage; phaseStart = now; }
        if (changed || now - lastPump >= 100)
        {
            if (!CancelRequested) phaseLabel.Text = phase;
            counts.Text = values == null ? "" : values.Elements.ToString("N0", CultureInfo.CurrentCulture) + " elements · " + values.PlacedTriangles.ToString("N0", CultureInfo.CurrentCulture) + " triangles" + (values.Reused > 0 ? " · " + values.Reused.ToString("N0", CultureInfo.CurrentCulture) + " repeated items reused" : "");
            current.Text = values?.Current ?? "";
            Model?.Update(); UpdateElapsed(); lastPump = now;
            if (Visible) { Refresh(); Application.DoEvents(); }
        }
        return !CancelRequested && !IsDisposed;
    }

    public void Complete() { completed = true; RestoreOwner(); Close(); }
    public new void Hide() { RestoreOwner(); base.Hide(); }
    private void RequestCancel() { CancelRequested = true; cancel.Enabled = false; cancel.Text = "Cancelling…"; phaseLabel.Text = "Cancelling at the next safe point…"; }
    private void UpdateElapsed()
    {
        string elapsed = "Elapsed " + total.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        if (Model == null) { elapsedLabel.Text = elapsed; return; }
        bar.Value = Math.Max(0, Math.Min(1000, (int)Math.Round(Model.Fraction * 1000)));
        string left = ExportProgressModel.FormatTimeLeft(Model.SecondsLeft);
        string size = Model.ProjectedPdfBytes is long bytes ? "PDF about " + ExportProgressModel.FormatBytes(bytes) : "";
        elapsedLabel.Text = string.Join(" · ", new[] { (int)Math.Round(Model.Fraction * 100) + " %", left == "" ? "Estimating time left…" : left, size, elapsed }.Where(t => t.Length > 0));
    }
    private static string Stamp(string phase, long milliseconds) => phase + " | " + milliseconds + " ms";
    private void RestoreOwner()
    {
        if (ownerRestored) return; ownerRestored = true;
        if (disabledOwner != IntPtr.Zero) { NativeWindows.EnableWindow(disabledOwner, true); disabledOwner = IntPtr.Zero; }
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { if (!completed && !CancelRequested) { RequestCancel(); e.Cancel = true; } base.OnFormClosing(e); }
    protected override void OnFormClosed(FormClosedEventArgs e) { RestoreOwner(); base.OnFormClosed(e); }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Stop(); timer.Dispose(); RestoreOwner(); } base.Dispose(disposing); }
}

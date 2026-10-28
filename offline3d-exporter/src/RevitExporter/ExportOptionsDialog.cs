using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;
using Button = System.Windows.Forms.Button;
using ComboBox = System.Windows.Forms.ComboBox;
using Control = System.Windows.Forms.Control;
using Label = System.Windows.Forms.Label;
using PictureBox = System.Windows.Forms.PictureBox;
using Panel = System.Windows.Forms.Panel;
using Timer = System.Windows.Forms.Timer;

namespace Offline3D.RevitExporter;

/// <summary>Very low: lowest Revit tessellation, Coarse detail, no precomputed edges, parts under 2" skipped.</summary>
internal enum ExportDetail { VeryLow, Low, Medium, High, Ultra }

/// <summary>A phase or phase filter offered in the options.</summary>
internal sealed class NamedChoice { public string Name = ""; public Autodesk.Revit.DB.ElementId Id = Autodesk.Revit.DB.ElementId.InvalidElementId; public override string ToString() => Name; }

internal enum OptionsOutcome { Cancel, Export }

/// <summary>Scope options a preview is rendered for.</summary>
internal sealed class PreviewRequest
{
    public bool OutsideCrop { get; set; }
    public bool AllUp { get; set; }
    public bool AllDown { get; set; }
    public bool IncludeAllCategories { get; set; }
    public Autodesk.Revit.DB.ElementId Phase { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
    public Autodesk.Revit.DB.ElementId PhaseFilter { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
    public Size PixelSize { get; set; }
    public string Key => OutsideCrop + ":" + AllUp + ":" + AllDown + ":" + IncludeAllCategories + ":" + Phase + ":" + PhaseFilter;
}

/// <summary>
/// Export options with an integrated, optional Revit preview.
///
/// Revit API safety: the dialog is shown modeless with Revit's main window
/// disabled (the same pattern as the progress window). <see cref="RunModal"/>
/// pumps messages itself; when the user asks for a preview, the Revit work runs
/// from that loop, between message pumps, never from a WinForms event handler
/// and never on another thread. The dialog stays on screen and shows its own
/// loading state instead of a separate popup.
/// </summary>
internal sealed class ExportOptionsDialog : Form
{
    // Options
    private readonly CheckBox outsideCrop = new CheckBox { Text = "Include geometry outside the crop", AutoSize = true };
    private readonly CheckBox allUp = new CheckBox { Text = "All up", AutoSize = true };
    private readonly CheckBox allDown = new CheckBox { Text = "All down", AutoSize = true };
    private readonly CheckBox categories = new CheckBox { Text = "Include categories hidden in the view", Checked = true, AutoSize = true };
    private readonly TextBox parameterNames = new TextBox { Text = "Mark,Comments,Type Mark" };
    private readonly ComboBox detail = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox phase = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox phaseFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label detailHint = new Label();
    private readonly CheckBox skipTiny = new CheckBox { Text = "Skip parts smaller than", AutoSize = true };
    private readonly NumericUpDown tinySize = new NumericUpDown { DecimalPlaces = 1, Minimum = .1m, Maximum = 100, Value = 2, Increment = .5m, Enabled = false };
    private readonly Label scopeSummary = new Label();
    private readonly Label estimate = new Label();

    // Preview area
    private readonly Panel previewHost = new Panel();
    private readonly PictureBox picture = new PictureBox();
    private readonly TableLayoutPanel overlay = new TableLayoutPanel();
    private readonly Label overlayTitle = new Label(), overlayText = new Label();
    private readonly ProgressBar overlayBar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
    private readonly Button overlayButton = new Button();
    private readonly Panel staleBanner = new Panel();
    private readonly Button previewButton = new Button();

    // Frame
    private readonly TableLayoutPanel root = new TableLayoutPanel();
    private readonly TableLayoutPanel header = new TableLayoutPanel();
    private readonly TableLayoutPanel previewColumn = new TableLayoutPanel();
    private readonly Panel optionsScroller = new Panel();
    private readonly TableLayoutPanel footer = new TableLayoutPanel();
    private readonly Label status = new Label();
    private readonly Button exportButton = new Button(), quickButton = new Button(), cancelButton = new Button();
    private readonly Timer ticker = new Timer { Interval = 250 };

    private readonly string viewName, bottomLabel, topLabel;
    private readonly int candidateCount;
    private readonly bool planHeight;
    private bool? narrow;
    private bool finished, previewWanted, busy, cancelAfterPreview;
    private string? shownKey;
    private DateTime busySince;

    public OptionsOutcome Outcome { get; private set; } = OptionsOutcome.Cancel;
    public bool OutsideCrop => outsideCrop.Checked;
    public bool AllUp => allUp.Checked;
    public bool AllDown => allDown.Checked;
    public bool IncludeAllCategories => categories.Checked;
    public string[] ParameterNames => parameterNames.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Take(20).ToArray();
    public ExportDetail DetailPreset => (ExportDetail)Math.Max(0, detail.SelectedIndex);
    public int TessellationLevel => new[] { 0, 4, 8, 12, 15 }[(int)DetailPreset];
    /// <summary>Very low always skips parts under 2" (checked before Revit tessellates them).</summary>
    public double MinimumPartFeet => Math.Max(skipTiny.Checked ? (double)tinySize.Value / 30.48 : 0, DetailPreset == ExportDetail.VeryLow ? 2.0 / 12 : 0);
    public Autodesk.Revit.DB.ElementId Phase => (phase.SelectedItem as NamedChoice)?.Id ?? Autodesk.Revit.DB.ElementId.InvalidElementId;
    public Autodesk.Revit.DB.ElementId PhaseFilter => (phaseFilter.SelectedItem as NamedChoice)?.Id ?? Autodesk.Revit.DB.ElementId.InvalidElementId;
    public string PhaseFilterName => (phaseFilter.SelectedItem as NamedChoice)?.Name ?? "";
    public string PreviewDiagnostics { get; private set; } = "";

    private int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    public ExportOptionsDialog(string viewName, string bottomLabel, string topLabel, int candidateCount, bool planHeight, NamedChoice[] phases, int phaseIndex, NamedChoice[] filters, int filterIndex)
    {
        phase.Items.AddRange(phases); phaseFilter.Items.AddRange(filters);
        if (phases.Length > 0) phase.SelectedIndex = Math.Max(0, Math.Min(phases.Length - 1, phaseIndex));
        if (filters.Length > 0) phaseFilter.SelectedIndex = Math.Max(0, Math.Min(filters.Length - 1, filterIndex));
        this.viewName = viewName; this.bottomLabel = bottomLabel; this.topLabel = topLabel; this.candidateCount = Math.Max(0, candidateCount); this.planHeight = planHeight;
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None; // every size below is computed from DeviceDpi
        Font = new Font("Segoe UI", 9f);
        Text = "Export 3D PDF"; BackColor = Color.White;
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = true;
        FormBorderStyle = FormBorderStyle.Sizable; KeyPreview = true;

        BuildHeader(); BuildPreview(); BuildOptions(); BuildFooter();
        root.Dock = DockStyle.Fill; root.Padding = new Padding(S(16), S(12), S(16), S(12));
        Controls.Add(root);
        allUp.Enabled = allDown.Enabled = planHeight;
        if (!planHeight) { allUp.Text = "All up (plans only)"; allDown.Text = "All down (plans only)"; }

        bool confirming = false;
        outsideCrop.CheckedChanged += (_, __) =>
        {
            if (confirming) return;
            if (outsideCrop.Checked && MessageBox.Show(this, "Geometry outside the view crop can make a very large file and a long export. Keep this off for fast team review PDFs.\n\nInclude geometry outside the crop?", "Confirm large export", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            { confirming = true; outsideCrop.Checked = false; confirming = false; }
            OptionsChanged();
        };
        allUp.CheckedChanged += (_, __) => OptionsChanged();
        allDown.CheckedChanged += (_, __) => OptionsChanged();
        categories.CheckedChanged += (_, __) => OptionsChanged();
        phase.SelectedIndexChanged += (_, __) => OptionsChanged();
        phaseFilter.SelectedIndexChanged += (_, __) => OptionsChanged();
        detail.SelectedIndexChanged += (_, __) => { UpdateDetailHint(); UpdateEstimate(); };
        skipTiny.CheckedChanged += (_, __) => tinySize.Enabled = skipTiny.Checked;
        Resize += (_, __) => ApplyLayout();
        ticker.Tick += (_, __) => { if (busy) overlayText.Text = BusyText(); };
        ShowOverlay("Preview is optional", "Generate a Revit snapshot of the export area to check the crop and height before exporting.", "Generate preview", busyState: false);
        UpdateScopeSummary(); UpdateDetailHint(); UpdateEstimate();
        ResumeLayout(false);
    }

    // ------------------------------------------------------------------ building

    private Label SmallLabel(string text, int bottom = 6) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = Color.FromArgb(96, 96, 96), Margin = new Padding(0, 0, 0, S(bottom)) };

    private Label SectionLabel(string text) => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(30, 30, 30), Margin = new Padding(0, S(10), 0, S(4)) };

    private void BuildHeader()
    {
        header.ColumnCount = 1; header.AutoSize = true; header.Dock = DockStyle.Fill; header.Margin = new Padding(0, 0, 0, S(8));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = new Label { Text = "Export 3D PDF", AutoSize = true, Font = new Font(Font.FontFamily, 13f, FontStyle.Bold), Margin = new Padding(0) };
        var subtitle = new Label { Text = viewName, AutoSize = false, AutoEllipsis = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = S(20), ForeColor = Color.FromArgb(90, 90, 90), Margin = new Padding(0, S(2), 0, 0) };
        header.Controls.Add(title, 0, 0); header.Controls.Add(subtitle, 0, 1);
    }

    private void BuildPreview()
    {
        previewHost.Dock = DockStyle.Fill; previewHost.BackColor = Color.White; previewHost.BorderStyle = BorderStyle.FixedSingle; previewHost.Margin = new Padding(0);
        picture.Dock = DockStyle.Fill; picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.White;

        overlay.ColumnCount = 1; overlay.AutoSize = true; overlay.AutoSizeMode = AutoSizeMode.GrowAndShrink; overlay.BackColor = Color.White; overlay.Padding = new Padding(S(16));
        overlayTitle.AutoSize = true; overlayTitle.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold); overlayTitle.Anchor = AnchorStyles.None; overlayTitle.Margin = new Padding(0, 0, 0, S(6));
        overlayText.AutoSize = true; overlayText.MaximumSize = new Size(S(380), 0); overlayText.TextAlign = ContentAlignment.MiddleCenter; overlayText.Anchor = AnchorStyles.None; overlayText.ForeColor = Color.FromArgb(80, 80, 80); overlayText.Margin = new Padding(0, 0, 0, S(10));
        overlayBar.Size = new Size(S(240), S(8)); overlayBar.Anchor = AnchorStyles.None; overlayBar.Margin = new Padding(0, 0, 0, S(10));
        overlayButton.AutoSize = true; overlayButton.MinimumSize = new Size(S(140), S(30)); overlayButton.Anchor = AnchorStyles.None;
        overlayButton.Click += (_, __) => RequestPreview();
        overlay.Controls.Add(overlayTitle, 0, 0); overlay.Controls.Add(overlayText, 0, 1); overlay.Controls.Add(overlayBar, 0, 2); overlay.Controls.Add(overlayButton, 0, 3);

        staleBanner.Dock = DockStyle.Top; staleBanner.Height = S(36); staleBanner.Padding = new Padding(S(10), S(4), S(4), S(4));
        staleBanner.BackColor = Color.FromArgb(255, 244, 214); staleBanner.Visible = false;
        var staleText = new Label { Text = "Options changed since this preview.", AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var staleButton = new Button { Text = "Update preview", AutoSize = true, Dock = DockStyle.Right, UseVisualStyleBackColor = true };
        staleButton.Click += (_, __) => RequestPreview();
        staleBanner.Controls.Add(staleText); staleBanner.Controls.Add(staleButton);

        previewHost.Controls.Add(picture); previewHost.Controls.Add(staleBanner); previewHost.Controls.Add(overlay);
        overlay.BringToFront(); staleBanner.BringToFront();
        previewHost.Resize += (_, __) => CenterOverlay();
        overlay.SizeChanged += (_, __) => CenterOverlay();

        previewButton.Text = "Generate preview"; previewButton.AutoSize = true; previewButton.MinimumSize = new Size(S(130), S(28)); previewButton.Margin = new Padding(0, S(8), S(10), 0);
        previewButton.Click += (_, __) => RequestPreview();
        var note = SmallLabel("Optional Revit snapshot of the export area. Levels, grids and reference planes are hidden. Changing options does not re-render it automatically.", 0);
        note.Margin = new Padding(0, S(10), 0, 0);
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(previewButton, 0, 0); row.Controls.Add(note, 1, 0);

        previewColumn.ColumnCount = 1; previewColumn.RowCount = 2; previewColumn.Dock = DockStyle.Fill; previewColumn.Margin = new Padding(0);
        previewColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewColumn.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewColumn.Controls.Add(previewHost, 0, 0); previewColumn.Controls.Add(row, 0, 1);
    }

    private void BuildOptions()
    {
        var options = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 0, S(6), 0), Margin = new Padding(0) };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control) { if (control.Margin == DefaultMargin) control.Margin = new Padding(0, 0, 0, S(4)); options.Controls.Add(control); }

        var area = SectionLabel("Export area"); area.Margin = new Padding(0, 0, 0, S(4)); Add(area);
        scopeSummary.AutoSize = true; scopeSummary.Anchor = AnchorStyles.Left | AnchorStyles.Right; scopeSummary.Margin = new Padding(0, 0, 0, S(6)); Add(scopeSummary);
        var heights = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0) };
        allUp.Margin = new Padding(0, 0, S(16), S(2)); allDown.Margin = new Padding(0, 0, 0, S(2));
        heights.Controls.Add(allUp); heights.Controls.Add(allDown); Add(heights);
        Add(SmallLabel("Height only: the plan crop is never widened."));
        outsideCrop.Margin = new Padding(0, S(2), 0, S(2)); Add(outsideCrop);
        Add(SmallLabel("Off by default. Asks for confirmation; can make very large files."));

        Add(SectionLabel("Content"));
        categories.Margin = new Padding(0, 0, 0, S(2)); Add(categories);
        foreach (var (text, box) in new[] { ("Phase", phase), ("Phase filter", phaseFilter) })
        {
            Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, S(4), 0, S(2)) });
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right; box.Margin = new Padding(0, 0, 0, S(4)); Add(box);
        }
        Add(SmallLabel("\"Show Complete\" leaves out demolished elements (shown red with \"Show All\")."));
        Add(new Label { Text = "Extra properties (comma separated)", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, S(2), 0, S(2)) });
        parameterNames.Anchor = AnchorStyles.Left | AnchorStyles.Right; parameterNames.Margin = new Padding(0, 0, 0, S(4)); Add(parameterNames);

        Add(SectionLabel("Detail"));
        detail.Items.AddRange(new object[] { "Very low — fewest polygons, fastest", "Low — smaller and faster", "Medium — balanced (recommended)", "High — more curve detail", "Ultra — highest detail" });
        detail.SelectedIndex = (int)ExportDetail.Medium; detail.Anchor = AnchorStyles.Left | AnchorStyles.Right; detail.Margin = new Padding(0, 0, 0, S(4)); Add(detail);
        detailHint.AutoSize = true; detailHint.Anchor = AnchorStyles.Left | AnchorStyles.Right; detailHint.ForeColor = Color.FromArgb(96, 96, 96); detailHint.Margin = new Padding(0, 0, 0, S(6)); Add(detailHint);
        var tiny = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 0, S(2)) };
        tiny.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tiny.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(64))); tiny.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        skipTiny.Margin = new Padding(0, S(3), S(8), 0); tinySize.Dock = DockStyle.Fill; tinySize.Margin = new Padding(0);
        tiny.Controls.Add(skipTiny, 0, 0); tiny.Controls.Add(tinySize, 1, 0); tiny.Controls.Add(new Label { Text = "cm", AutoSize = true, Margin = new Padding(S(4), S(4), 0, 0) }, 2, 0);
        Add(tiny);
        Add(SmallLabel("Off by default; nothing is skipped unless you turn it on."));

        Add(SectionLabel("Estimated size"));
        estimate.AutoSize = true; estimate.Anchor = AnchorStyles.Left | AnchorStyles.Right; estimate.Font = new Font(Font, FontStyle.Bold); estimate.Margin = new Padding(0, 0, 0, S(2)); Add(estimate);
        Add(SmallLabel("A rough estimate. The real size and triangle counts are measured during export.", 0));

        optionsScroller.Dock = DockStyle.Fill; optionsScroller.Margin = new Padding(0); optionsScroller.Padding = new Padding(0);
        optionsScroller.Controls.Add(options);
        // Vertical scrolling only: content is laid out to the panel's width.
        optionsScroller.AutoScroll = false;
        optionsScroller.HorizontalScroll.Enabled = false; optionsScroller.HorizontalScroll.Visible = false; optionsScroller.HorizontalScroll.Maximum = 0;
        optionsScroller.AutoScroll = true;
    }

    private void BuildFooter()
    {
        footer.ColumnCount = 2; footer.AutoSize = true; footer.Dock = DockStyle.Fill; footer.Margin = new Padding(0, S(10), 0, 0);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.AutoSize = false; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = Color.FromArgb(96, 96, 96);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        foreach (var b in new[] { quickButton, exportButton, cancelButton }) { b.AutoSize = true; b.MinimumSize = new Size(S(104), S(32)); b.Margin = new Padding(S(8), 0, 0, 0); }
        quickButton.Text = "Quick export"; exportButton.Text = "Export…"; cancelButton.Text = "Cancel";
        exportButton.Font = new Font(Font, FontStyle.Bold);
        quickButton.Click += (_, __) => { detail.SelectedIndex = (int)ExportDetail.VeryLow; Finish(OptionsOutcome.Export); };
        exportButton.Click += (_, __) => Finish(OptionsOutcome.Export);
        cancelButton.Click += (_, __) => Finish(OptionsOutcome.Cancel);
        AcceptButton = exportButton; CancelButton = cancelButton;
        buttons.Controls.Add(quickButton); buttons.Controls.Add(exportButton); buttons.Controls.Add(cancelButton);
        footer.Controls.Add(status, 0, 0); footer.Controls.Add(buttons, 1, 0);
        var tip = new ToolTip();
        tip.SetToolTip(quickButton, "Export now with Very low detail: coarse geometry, colours and properties kept, no precomputed edges, parts under 2 in skipped.");
    }

    /// <summary>Two columns when there is room; preview above options otherwise.</summary>
    private void ApplyLayout()
    {
        bool wantNarrow = ClientSize.Width < S(780);
        if (narrow == wantNarrow) return;
        narrow = wantNarrow;
        root.SuspendLayout();
        root.Controls.Clear(); root.ColumnStyles.Clear(); root.RowStyles.Clear();
        if (!wantNarrow)
        {
            root.ColumnCount = 2; root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(330)));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(header, 0, 0); root.SetColumnSpan(header, 2);
            previewColumn.Margin = new Padding(0, 0, S(16), 0);
            root.Controls.Add(previewColumn, 0, 1); root.Controls.Add(optionsScroller, 1, 1);
            root.Controls.Add(footer, 0, 2); root.SetColumnSpan(footer, 2);
        }
        else
        {
            root.ColumnCount = 1; root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previewColumn.Margin = new Padding(0, 0, 0, S(10));
            root.Controls.Add(header, 0, 0); root.Controls.Add(previewColumn, 0, 1); root.Controls.Add(optionsScroller, 0, 2); root.Controls.Add(footer, 0, 3);
        }
        root.ResumeLayout(true);
    }

    private void CenterOverlay()
    {
        overlay.Location = new Point(Math.Max(0, (previewHost.ClientSize.Width - overlay.Width) / 2), Math.Max(0, (previewHost.ClientSize.Height - overlay.Height) / 2));
    }

    // ------------------------------------------------------------------ state

    private void ShowOverlay(string title, string text, string? button, bool busyState)
    {
        overlayTitle.Text = title; overlayText.Text = text;
        overlayBar.Visible = busyState;
        overlayButton.Visible = button != null; if (button != null) overlayButton.Text = button;
        overlay.Visible = true; overlay.BringToFront(); CenterOverlay();
    }

    private string BusyText() => "Revit is rendering the export area. This window stays open; the image appears here when Revit finishes (" + (int)(DateTime.Now - busySince).TotalSeconds + " s).";

    private void OptionsChanged()
    {
        UpdateScopeSummary(); UpdateEstimate();
        staleBanner.Visible = shownKey != null && shownKey != CurrentRequest().Key && !busy;
    }

    private void UpdateScopeSummary()
    {
        scopeSummary.Text = (OutsideCrop ? "Outside crop included (confirmed)" : "View crop") + "\nBottom: " + (AllDown ? "all levels below" : bottomLabel) + "\nTop: " + (AllUp ? "all levels above" : topLabel);
    }

    private void UpdateDetailHint()
    {
        detailHint.Text = DetailPreset switch
        {
            ExportDetail.VeryLow => "Coarse Revit geometry with the fewest polygons; no precomputed edges; parts under 2 in are skipped. Fastest.",
            ExportDetail.Low => "Fewer triangles on curved surfaces.",
            ExportDetail.Medium => "Standard Revit tessellation. Recommended for team review.",
            ExportDetail.High => "Fine Revit detail; larger files and longer export.",
            _ => "Maximum tessellation; can be slow and large."
        };
    }

    private void UpdateEstimate()
    {
        long perElement = new[] { 256L, 768L, 1536L, 3072L, 6144L }[(int)DetailPreset];
        double formatFactor = OutsideCrop ? 1 : AllUp || AllDown ? .6 : .35;
        double typical = 2 * 1048576d + candidateCount * perElement * formatFactor;
        long min = (long)Math.Max(1048576, typical * .35), max = (long)Math.Max(2 * 1048576, typical * 2.5);
        estimate.Text = FormatSize(min) + " – " + FormatSize(max);
    }

    private static string FormatSize(long bytes) => bytes >= 1073741824 ? (bytes / 1073741824d).ToString("0.0", CultureInfo.CurrentCulture) + " GB" : (bytes / 1048576d).ToString("0", CultureInfo.CurrentCulture) + " MB";

    private PreviewRequest CurrentRequest() => new PreviewRequest { OutsideCrop = OutsideCrop, AllUp = AllUp, AllDown = AllDown, IncludeAllCategories = IncludeAllCategories, Phase = Phase, PhaseFilter = PhaseFilter, PixelSize = picture.ClientSize };

    private void RequestPreview() { if (!busy) previewWanted = true; }

    private void Finish(OptionsOutcome outcome)
    {
        if (busy) { if (outcome == OptionsOutcome.Cancel) { cancelAfterPreview = true; status.Text = "Cancelling after Revit finishes the preview…"; } return; }
        Outcome = outcome; finished = true;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        foreach (var c in new Control[] { outsideCrop, categories, parameterNames, detail, skipTiny, previewButton, quickButton, exportButton }) c.Enabled = !value;
        allUp.Enabled = allDown.Enabled = !value && planHeight;
        tinySize.Enabled = !value && skipTiny.Checked;
        cancelButton.Enabled = true;
        if (value) { busySince = DateTime.Now; ticker.Start(); } else ticker.Stop();
        UseWaitCursor = value;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing with the window button means Cancel; the loop hides the form.
        if (!finished) { e.Cancel = true; Finish(OptionsOutcome.Cancel); }
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5) { RequestPreview(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ------------------------------------------------------------------ running

    /// <param name="generatePreview">Renders the preview with the Revit API and
    /// returns the image path. Called from this method's loop only. The second
    /// argument reports progress text and keeps the window painted.</param>
    public OptionsOutcome RunModal(IWin32Window owner, Func<PreviewRequest, Action<string>, string> generatePreview)
    {
        var area = NativeWindows.WorkingArea(owner.Handle, out var ownerBounds);
        int width = Math.Min(S(1080), area.Width - S(32)), height = Math.Min(S(700), area.Height - S(32));
        MinimumSize = new Size(Math.Min(S(560), area.Width), Math.Min(S(460), area.Height));
        Size = new Size(Math.Max(MinimumSize.Width, width), Math.Max(MinimumSize.Height, height));
        var center = ownerBounds.Width > 0 ? new Point(ownerBounds.Left + ownerBounds.Width / 2, ownerBounds.Top + ownerBounds.Height / 2) : new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
        Location = new Point(Math.Max(area.Left, Math.Min(center.X - Width / 2, area.Right - Width)), Math.Max(area.Top, Math.Min(center.Y - Height / 2, area.Bottom - Height)));
        ApplyLayout();

        bool disabled = false;
        if (owner.Handle != IntPtr.Zero && NativeWindows.IsWindowEnabled(owner.Handle)) { NativeWindows.EnableWindow(owner.Handle, false); disabled = true; }
        try
        {
            Show(owner); Activate(); exportButton.Focus();
            while (!finished && !IsDisposed)
            {
                Application.DoEvents();
                if (previewWanted && !finished) { previewWanted = false; Preview(generatePreview); continue; }
                if (!finished) NativeWindows.WaitMessage();
            }
        }
        finally
        {
            // Re-enable Revit before hiding so Windows returns focus to it.
            if (disabled) NativeWindows.EnableWindow(owner.Handle, true);
            if (!IsDisposed) Hide();
        }
        return Outcome;
    }

    private void Preview(Func<PreviewRequest, Action<string>, string> generatePreview)
    {
        var request = CurrentRequest();
        if (request.PixelSize.Width < 50 || request.PixelSize.Height < 50) request.PixelSize = new Size(900, 600);
        SetBusy(true); staleBanner.Visible = false;
        ShowOverlay("Generating preview…", BusyText(), null, busyState: true);
        status.Text = "Rendering the preview in Revit…";
        Refresh();
        try
        {
            string path = generatePreview(request, message =>
            {
                status.Text = message; overlayText.Text = message; Refresh(); Application.DoEvents();
                if (cancelAfterPreview) throw new OperationCanceledException("Preview cancelled");
            });
            LoadImage(path);
            shownKey = request.Key; overlay.Visible = false;
            status.Text = "Preview ready. It shows the scope selected when it was generated.";
            previewButton.Text = "Update preview";
        }
        catch (OperationCanceledException)
        {
            ShowOverlay("Preview cancelled", "You can still export without a preview.", "Generate preview", busyState: false);
            status.Text = "";
        }
        catch (Exception ex)
        {
            // The caller has logged the details; the dialog stays usable.
            ShowOverlay("Preview could not be generated", ex.Message + "\n\nYou can still export without a preview.", "Try again", busyState: false);
            status.Text = "Preview failed; details are in the export log.";
        }
        finally
        {
            SetBusy(false);
            OptionsChanged();
            if (cancelAfterPreview) { Outcome = OptionsOutcome.Cancel; finished = true; }
        }
    }

    private void LoadImage(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The generated preview image is missing.", path);
        // Clone before closing the stream so the temporary file is not locked.
        using (var stream = File.OpenRead(path))
        using (var image = Image.FromStream(stream))
        {
            var bitmap = new Bitmap(image); bitmap.SetResolution(96, 96);
            var old = picture.Image; picture.Image = bitmap; old?.Dispose();
            PreviewDiagnostics = "Loaded: " + path + " | Device DPI: " + DeviceDpi + " | control: " + picture.ClientSize.Width + "x" + picture.ClientSize.Height + " | PNG pixels: " + image.Width + "x" + image.Height;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ticker.Dispose(); var image = picture.Image; picture.Image = null; image?.Dispose(); }
        base.Dispose(disposing);
    }
}

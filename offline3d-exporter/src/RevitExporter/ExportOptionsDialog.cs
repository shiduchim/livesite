using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Offline3D.RevitExporter;

/// <summary>Very low: lowest Revit tessellation, Coarse detail, no precomputed edges, parts under 2" skipped.</summary>
internal enum ExportDetail { VeryLow, Low, Medium, High, Ultra }

/// <summary>A phase filter offered in the options: name and Revit element id value (-1 = none). No Revit types, so the window can be tested without Revit.</summary>
internal sealed class NamedChoice { public string Name = ""; public long Id = -1; public override string ToString() => Name; }

internal enum OptionsOutcome { Cancel, Export }

/// <summary>Scope options a preview is rendered for.</summary>
internal sealed class PreviewRequest
{
    public bool OutsideCrop { get; set; }
    public bool AllUp { get; set; }
    public bool AllDown { get; set; }
    public bool IncludeAllCategories { get; set; }
    /// <summary>Element id values; -1 keeps the view's phase / means no phase filter.</summary>
    public long Phase { get; set; } = -1;
    public long PhaseFilter { get; set; } = -1;
    public Size PixelSize { get; set; }
    public string Key => OutsideCrop + ":" + AllUp + ":" + AllDown + ":" + IncludeAllCategories + ":" + Phase + ":" + PhaseFilter;
}

/// <summary>
/// Export options (layout "B" of docs/mockups/export-dialog-options.png, with both check
/// boxes), styled by docs/EK_Revit_Addin_UI_Style_Guide.md: preview on the left; Height,
/// Phase filter and Detail; the two scope check boxes; the size estimate; Reset, Cancel
/// and Export.
///
/// Revit API safety (unchanged from 0.5.3): the window is shown modeless with Revit's
/// main window disabled. <see cref="RunModal"/> pumps messages itself; when the user asks
/// for a preview, the Revit work runs from that loop, between message pumps, never from
/// a WinForms event handler and never on another thread.
/// </summary>
internal sealed class ExportOptionsDialog : EkWindow
{
    public const string DefaultParameters = "Mark,Comments,Type Mark";
    private static readonly string[] DetailNames = { "Very low", "Low", "Medium", "High", "Very high" };

    private readonly EkDropDown height, phaseFilter, detail;
    private readonly EkCheckBox categories, outsideCrop;
    private readonly Label estimate = new Label(), viewLabel = new Label();
    private readonly PreviewPane preview;
    private readonly EkLink previewLink, reset;
    private readonly Label previewCaption = new Label();
    private readonly EkButton exportButton, cancelButton;
    private readonly Panel content = new Panel(), footer = new Panel();
    private readonly System.Windows.Forms.Timer ticker = new System.Windows.Forms.Timer { Interval = 500 };

    private readonly NamedChoice[] filters;
    private readonly int viewFilterIndex, candidateCount;
    private readonly bool planHeight;
    private bool finished, previewWanted, busy, cancelAfterPreview, confirming;
    private string? shownKey;
    private DateTime busySince;

    public OptionsOutcome Outcome { get; private set; } = OptionsOutcome.Cancel;
    public bool AllUp => planHeight && height.SelectedIndex is 1 or 3;
    public bool AllDown => planHeight && height.SelectedIndex is 2 or 3;
    public bool OutsideCrop => outsideCrop.Checked;
    public bool IncludeAllCategories => categories.Checked;
    public string[] ParameterNames => DefaultParameters.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    public ExportDetail DetailPreset => (ExportDetail)Math.Max(0, detail.SelectedIndex);
    public int TessellationLevel => new[] { 0, 4, 8, 12, 15 }[(int)DetailPreset];
    /// <summary>Very low skips parts under 2" (checked before Revit tessellates them).</summary>
    public double MinimumPartFeet => DetailPreset == ExportDetail.VeryLow ? 2.0 / 12 : 0;
    /// <summary>Always the plan view's own phase (-1 keeps it); only the phase filter is offered.</summary>
    public long Phase => -1;
    public long PhaseFilter => phaseFilter.SelectedIndex >= 0 && phaseFilter.SelectedIndex < filters.Length ? filters[phaseFilter.SelectedIndex].Id : -1;
    public string PhaseFilterName => phaseFilter.SelectedText;
    public string PreviewDiagnostics { get; private set; } = "";

    /// <param name="filters">"None" first, then the document's phase filters.</param>
    /// <param name="filterIndex">Selected at start (the remembered filter, else the view's).</param>
    /// <param name="viewFilterIndex">The view's own filter, used by Reset.</param>
    public ExportOptionsDialog(EkPalette palette, string viewName, string bottomLabel, string topLabel, int candidateCount, bool planHeight,
        NamedChoice[] filters, int filterIndex, int viewFilterIndex, ExportDetail initialDetail, bool includeHiddenCategories)
        : base(palette, "Export 3D PDF", "Close")
    {
        this.filters = filters; this.viewFilterIndex = Math.Max(0, viewFilterIndex); this.candidateCount = Math.Max(0, candidateCount); this.planHeight = planHeight;
        SuspendLayout();
        height = new EkDropDown(palette); phaseFilter = new EkDropDown(palette); detail = new EkDropDown(palette);
        height.SetItems(planHeight ? new[] { "View range", "All levels above", "All levels below", "All levels" } : new[] { "View crop" }, 0);
        height.Enabled = planHeight;
        new ToolTip().SetToolTip(height, "View range: " + bottomLabel + " to " + topLabel);
        phaseFilter.SetItems(filters.Select(f => f.Name), filterIndex);
        detail.SetItems(DetailNames, (int)initialDetail);
        categories = new EkCheckBox(palette, "Include categories hidden in the view", includeHiddenCategories);
        outsideCrop = new EkCheckBox(palette, "Include geometry outside the crop", false);
        preview = new PreviewPane(palette);
        previewLink = new EkLink(palette, "Generate") { Accent = true };
        reset = new EkLink(palette, "Reset");
        cancelButton = new EkButton(palette, "Cancel"); exportButton = new EkButton(palette, "Export…") { IsDefault = true };

        foreach (var label in new[] { estimate, viewLabel, previewCaption }) { label.AutoSize = false; label.ForeColor = palette.Secondary; label.BackColor = palette.Body; label.TextAlign = ContentAlignment.MiddleLeft; label.AutoEllipsis = true; label.UseMnemonic = false; }
        viewLabel.Text = viewName;
        previewCaption.Text = "Preview";

        content.BackColor = palette.Body; footer.BackColor = palette.Body;
        content.Controls.AddRange(new Control[] { preview, previewCaption, previewLink, viewLabel, Row("Height"), height, Row("Phase filter"), phaseFilter, Row("Detail"), detail, categories, outsideCrop, estimate });
        footer.Controls.AddRange(new Control[] { reset, cancelButton, exportButton });
        footer.Paint += (_, e) => { using var pen = new Pen(palette.FooterLine); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        Controls.Add(content); Controls.Add(footer);
        content.BringToFront();

        TitleBar.ActionClicked += () => Finish(OptionsOutcome.Cancel);
        exportButton.Click += (_, __) => Finish(OptionsOutcome.Export);
        cancelButton.Click += (_, __) => Finish(OptionsOutcome.Cancel);
        reset.Click += (_, __) => ResetOptions();
        previewLink.Click += (_, __) => RequestPreview();
        preview.Click += (_, __) => { if (preview.Image == null) RequestPreview(); };
        height.SelectedIndexChanged += (_, __) => OptionsChanged();
        phaseFilter.SelectedIndexChanged += (_, __) => OptionsChanged();
        categories.CheckedChanged += (_, __) => OptionsChanged();
        detail.SelectedIndexChanged += (_, __) => UpdateEstimate();
        outsideCrop.CheckedChanged += (_, __) =>
        {
            if (confirming) return;
            if (outsideCrop.Checked && MessageBox.Show(this, "Geometry outside the view crop can make a very large file and a long export.\n\nInclude geometry outside the crop?", "Export 3D PDF", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            { confirming = true; outsideCrop.Checked = false; confirming = false; }
            OptionsChanged();
        };
        ticker.Tick += (_, __) => { if (busy) preview.Message = "Revit is rendering the preview… " + (int)(DateTime.Now - busySince).TotalSeconds + " s"; };
        UpdateEstimate(); UpdatePreviewCaption();
        ResumeLayout(false);
    }

    private Label Row(string text) => new Label { Text = text, AutoSize = false, ForeColor = Palette.Text, BackColor = Palette.Body, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Tag = "row" };

    // ------------------------------------------------------------------ layout (DIP, style guide)

    private void LayoutWindow()
    {
        ClientSize = new Size(S(640), S(352));
        int footerHeight = S(47);
        footer.SetBounds(1, ClientSize.Height - 1 - footerHeight, ClientSize.Width - 2, footerHeight);
        content.SetBounds(1, TitleBar.Bottom, ClientSize.Width - 2, footer.Top - TitleBar.Bottom);
        // Footer: padding left 30, right 26; Cancel then Export, 16 apart.
        foreach (var b in new[] { cancelButton, exportButton }) b.Size = b.GetPreferredSize(Size.Empty);
        int y = (footerHeight - exportButton.Height) / 2;
        exportButton.Location = new Point(footer.Width - S(26) - exportButton.Width, y);
        cancelButton.Location = new Point(exportButton.Left - S(16) - cancelButton.Width, y);
        reset.Size = reset.GetPreferredSize(Size.Empty); reset.Location = new Point(S(30), (footerHeight - reset.Height) / 2);
        // Content: padding left 18, top 14, right 12. Preview 300 x 225 on the left.
        int left = S(18), top = S(14);
        preview.SetBounds(left, top, S(300), S(225));
        previewCaption.SetBounds(left, preview.Bottom + S(4), S(56), S(20));
        previewCaption.Width = TextRenderer.MeasureText(previewCaption.Text, Font).Width;
        previewLink.Size = previewLink.GetPreferredSize(Size.Empty); previewLink.Location = new Point(previewCaption.Right + S(2), previewCaption.Top + (previewCaption.Height - previewLink.Height) / 2);
        int x = preview.Right + S(18), labelWidth = S(86), right = content.Width - S(12), fieldWidth = right - x - labelWidth;
        viewLabel.SetBounds(x, top - S(3), right - x, S(20));
        int row = top + S(22);
        foreach (var (label, field) in Rows())
        {
            label.SetBounds(x, row, labelWidth, S(34)); field.SetBounds(x + labelWidth, row + (S(34) - S(23)) / 2, fieldWidth, S(23));
            row += S(34);
        }
        row += S(6);
        foreach (var box in new[] { categories, outsideCrop }) { box.SetBounds(x, row, right - x, S(26)); row += S(26); }
        estimate.SetBounds(x, row + S(6), right - x, S(20));
    }

    private IEnumerable<(Label, Control)> Rows()
    {
        var labels = content.Controls.OfType<Label>().Where(l => (string?)l.Tag == "row").ToArray();
        return new (Label, Control)[] { (labels[0], height), (labels[1], phaseFilter), (labels[2], detail) };
    }

    // ------------------------------------------------------------------ state

    private void ResetOptions()
    {
        if (busy) return;
        height.SelectedIndex = 0; phaseFilter.SelectedIndex = viewFilterIndex; detail.SelectedIndex = (int)ExportDetail.Medium;
        categories.Checked = true; confirming = true; outsideCrop.Checked = false; confirming = false;
        OptionsChanged();
    }

    private void OptionsChanged() { UpdateEstimate(); UpdatePreviewCaption(); }

    private void UpdatePreviewCaption()
    {
        bool stale = shownKey != null && shownKey != CurrentRequest().Key;
        previewCaption.Text = busy ? "Preview" : stale ? "Preview · options changed ·" : "Preview ·";
        previewLink.Text = preview.Image == null && !busy ? "Generate" : "Update";
        previewLink.Visible = !busy;
        if (IsHandleCreated) LayoutWindow();
    }

    /// <summary>A range from the element count near the crop; the exact size is measured during export.</summary>
    private void UpdateEstimate()
    {
        long perElement = new[] { 256L, 768L, 1536L, 3072L, 6144L }[(int)DetailPreset];
        double scope = OutsideCrop ? 1 : AllUp || AllDown ? .6 : .35;
        double typical = 2 * 1048576d + candidateCount * perElement * scope;
        long min = (long)Math.Max(1048576, typical * .35), max = (long)Math.Max(2 * 1048576, typical * 2.5);
        estimate.Text = "Estimated PDF size  " + ExportProgressModel.FormatBytes(min) + " – " + ExportProgressModel.FormatBytes(max);
    }

    private PreviewRequest CurrentRequest() => new PreviewRequest { OutsideCrop = OutsideCrop, AllUp = AllUp, AllDown = AllDown, IncludeAllCategories = IncludeAllCategories, Phase = Phase, PhaseFilter = PhaseFilter, PixelSize = preview.ClientSize };

    private void RequestPreview() { if (!busy) previewWanted = true; }

    protected override void OnTitleAction() => Finish(OptionsOutcome.Cancel);

    private void Finish(OptionsOutcome outcome)
    {
        if (busy) { if (outcome == OptionsOutcome.Cancel) { cancelAfterPreview = true; preview.Message = "Cancelling after Revit finishes the preview…"; } return; }
        Outcome = outcome; finished = true;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        foreach (var c in new Control[] { height, phaseFilter, detail, categories, outsideCrop, reset, exportButton }) c.Enabled = !value;
        height.Enabled = !value && planHeight;
        if (value) { busySince = DateTime.Now; ticker.Start(); } else ticker.Stop();
        UseWaitCursor = value;
        UpdatePreviewCaption();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing from outside means Cancel; the loop hides the form.
        if (!finished) { e.Cancel = true; Finish(OptionsOutcome.Cancel); }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && !busy && !(ActiveControl is EkDropDown)) { Finish(OptionsOutcome.Export); return true; }
        if (keyData == Keys.F5) { RequestPreview(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------ running

    /// <param name="generatePreview">Renders the preview with the Revit API and
    /// returns the image path. Called from this method's loop only. The second
    /// argument reports progress text and keeps the window painted.</param>
    public OptionsOutcome RunModal(IWin32Window owner, Func<PreviewRequest, Action<string>, string> generatePreview)
    {
        CreateControl(); LayoutWindow(); CenterOn(owner.Handle);
        bool disabled = false;
        if (owner.Handle != IntPtr.Zero && NativeWindows.IsWindowEnabled(owner.Handle)) { NativeWindows.EnableWindow(owner.Handle, false); disabled = true; }
        try
        {
            ActiveControl = exportButton;
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

    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); LayoutWindow(); }

    private void Preview(Func<PreviewRequest, Action<string>, string> generatePreview)
    {
        var request = CurrentRequest();
        if (request.PixelSize.Width < 50 || request.PixelSize.Height < 50) request.PixelSize = new Size(900, 675);
        SetBusy(true);
        preview.Busy = true; preview.Message = "Revit is rendering the preview…";
        Refresh();
        try
        {
            string path = generatePreview(request, message =>
            {
                preview.Message = message; preview.Refresh(); Application.DoEvents();
                if (cancelAfterPreview) throw new OperationCanceledException("Preview cancelled");
            });
            LoadImage(path);
            shownKey = request.Key; preview.Message = "";
        }
        catch (OperationCanceledException) { preview.Message = "Preview cancelled."; }
        catch (Exception ex) { preview.Message = "The preview could not be generated: " + ex.Message; } // the caller has logged the details
        finally
        {
            preview.Busy = false;
            SetBusy(false);
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
            var old = preview.Image; preview.Image = bitmap; old?.Dispose();
            PreviewDiagnostics = "Loaded: " + path + " | Device DPI: " + DeviceDpi + " | control: " + preview.ClientSize.Width + "x" + preview.ClientSize.Height + " | PNG pixels: " + image.Width + "x" + image.Height;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ticker.Dispose(); var image = preview.Image; preview.Image = null; image?.Dispose(); }
        base.Dispose(disposing);
    }

    /// <summary>The preview box: a field-coloured frame showing the Revit snapshot, or a short message.</summary>
    private sealed class PreviewPane : EkControl
    {
        private Image? image; private string message = "No preview yet";
        public bool Busy { get; set; }
        public PreviewPane(EkPalette palette) : base(palette) { TabStop = false; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Image? Image { get => image; set { image = value; Invalidate(); } }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Message { get => message; set { message = value ?? ""; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Palette.Body);
            EkUi.Field(g, ClientRectangle, Palette.Field, Palette.FieldBorder, Dpi);
            var inner = Rectangle.Inflate(ClientRectangle, -EkUi.S(this, 6), -EkUi.S(this, 6));
            if (image != null && !Busy)
            {
                double k = Math.Min(inner.Width / (double)image.Width, inner.Height / (double)image.Height);
                int w = (int)(image.Width * k), h = (int)(image.Height * k);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, new Rectangle(inner.X + (inner.Width - w) / 2, inner.Y + (inner.Height - h) / 2, w, h));
            }
            string text = Busy || image == null ? (message.Length > 0 ? message : "No preview yet") : message;
            if (text.Length > 0 && (Busy || image == null))
                EkUi.Text(g, text, Font, Rectangle.Inflate(inner, -EkUi.S(this, 12), 0), Palette.Secondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}

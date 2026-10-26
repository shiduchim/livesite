using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Form=System.Windows.Forms.Form;
using Button=System.Windows.Forms.Button;
using ComboBox=System.Windows.Forms.ComboBox;
using Control=System.Windows.Forms.Control;
using Label=System.Windows.Forms.Label;
using PictureBox=System.Windows.Forms.PictureBox;

namespace Offline3D.RevitExporter;


internal enum ExportDetail { Quick,Low,Medium,High,Ultra }

/// <summary>A cheap native orthographic view preview. It does not tessellate the
/// model or decode DWF; the same prepared view can be reused for the export.</summary>
internal sealed class ExportPreviewDialog:Form
{
    private readonly CheckBox outsideCrop=new CheckBox{Text="Include geometry outside the view crop",AutoSize=true,MaximumSize=new Size(306,0)};
    private readonly CheckBox allUp=new CheckBox{Text="All up",AutoSize=true};
    private readonly CheckBox allDown=new CheckBox{Text="All down",AutoSize=true};
    public bool OutsideCrop=>outsideCrop.Checked;
    public bool AllUp=>allUp.Checked;
    public bool AllDown=>allDown.Checked;
    public bool PreviewRequested {get;private set;}

    private readonly CheckBox categories=new CheckBox{Text="Include equipment/categories hidden on the 2D plan",Checked=true,AutoSize=true,MaximumSize=new Size(306,0)};
    private readonly TextBox parameterNames=new TextBox{Text="Mark,Comments,Type Mark",Width=306};
    public bool EntireModel=>OutsideCrop;
    public bool IncludeAllCategories=>categories.Checked;
    public string[] ParameterNames=>parameterNames.Text.Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).Take(20).ToArray();
    public event Action<bool>? ScopeChanged;
    public Size PreviewClientSize=>preview.ClientSize;
    public string PreviewDiagnostics {get;private set;}="";
    public void BeginPreview()=>LoadPreview(null,"Generating the Revit preview…\r\nThe image will appear here when rendering finishes.");
    private readonly ComboBox detail=new ComboBox();
    private readonly Label estimate=new Label();
    private readonly PictureBox preview=new PictureBox();
    private readonly CheckBox skipTiny=new CheckBox{Text="Skip tiny parts",AutoSize=true};
    private readonly NumericUpDown tinySize=new NumericUpDown{DecimalPlaces=2,Minimum=.01m,Maximum=100,Value=2,Width=100};
    public double MinimumPartFeet=>skipTiny.Checked?(double)tinySize.Value/30.48:0;
    private readonly int candidateCount;
    public ExportDetail DetailPreset=>(ExportDetail)Math.Max(0,detail.SelectedIndex);
    public int TessellationLevel=>new[]{1,4,8,12,15}[(int)DetailPreset];
    public long EstimatedMinimumBytes {get;private set;}
    public long EstimatedMaximumBytes {get;private set;}

    public ExportPreviewDialog(string? previewImagePath,string planName,string bottomLabel,string topLabel,int candidateCount,bool planHeight=true)
    {
        this.candidateCount=Math.Max(0,candidateCount);
        SuspendLayout();AutoScaleDimensions=new SizeF(96,96);
        Text="Export preview";Width=1120;Height=800;MinimumSize=new Size(760,520);
        StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;MinimizeBox=false;
        AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",9.5f);
        BackColor=Color.White;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(18)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        var title=new Label{AutoSize=true,Margin=new Padding(0,0,0,12),Text="Preview your 3D export",Font=new Font(Font.FontFamily,15,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
        layout.Controls.Add(title,0,0);layout.SetColumnSpan(title,2);
        var scope=new Label{AutoSize=true,Margin=new Padding(0,0,0,12),Text=planName+"\r\nView crop · Bottom: "+bottomLabel+" · Top: "+topLabel,Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=Color.FromArgb(65,65,65)};
        layout.Controls.Add(scope,0,1);layout.SetColumnSpan(scope,2);
        var imagePanel=new TableLayoutPanel{Dock=DockStyle.Fill,MinimumSize=Size.Empty,RowCount=3,ColumnCount=1,Margin=new Padding(0,0,18,0)};
        imagePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        imagePanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));imagePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));imagePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preview.MinimumSize=Size.Empty;preview.Dock=DockStyle.Fill;preview.SizeMode=PictureBoxSizeMode.Zoom;preview.BackColor=Color.White;preview.BorderStyle=BorderStyle.FixedSingle;
        imagePanel.Controls.Add(preview,0,0);
        var imageNote=new Label{AutoSize=true,Text="Optional native snapshot. Levels and reference graphics are hidden. Rendering can take time; changing options does not regenerate it automatically.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(90,90,90),Padding=new Padding(0,8,0,0)};
        var generate=new Button{Text="Generate preview",AutoSize=true,Anchor=AnchorStyles.Left,Padding=new Padding(10,5,10,5)};generate.Click+=(_,__)=>{PreviewRequested=true;DialogResult=DialogResult.Retry;};imagePanel.Controls.Add(generate,0,1);imagePanel.Controls.Add(imageNote,0,2);layout.Controls.Add(imagePanel,0,2);
        var options=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(0)};
        layout.Controls.Add(options,1,2);
        AddLabel(options,"Export area",true);
        AddLabel(options,"The view crop is the default horizontal boundary. Height normally reaches the nearest levels below and above.",false,10);
        var heights=new FlowLayoutPanel{AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.LeftToRight};allUp.Enabled=allDown.Enabled=planHeight;heights.Controls.Add(allUp);heights.Controls.Add(allDown);options.Controls.Add(heights);
        AddLabel(options,"All up/down changes height only; it never expands the plan crop.",false,10);
        options.Controls.Add(outsideCrop);
        options.Controls.Add(categories);AddLabel(options,"Includes loaded, visible Revit links. Phase, explicit element hides and workset settings are retained.",false,14);
        AddLabel(options,"Additional properties (comma separated)",true);options.Controls.Add(parameterNames);
        AddLabel(options,"Creates a clickable 3D PDF for Offline 3D PDF Viewer.",false,14);
        AddLabel(options,"Optional size filtering",true);options.Controls.Add(skipTiny);options.Controls.Add(tinySize);AddLabel(options,"Maximum part dimension in cm; off by default. No parts are skipped unless enabled.",false,14);AddLabel(options,"Export detail",true);
        detail.DropDownStyle=ComboBoxStyle.DropDownList;detail.Width=306;
        detail.Items.AddRange(new object[]{"Quick — low polygon, fastest","Low — smaller and faster","Medium — balanced","High — more detail","Ultra — highest detail"});detail.SelectedIndex=(int)ExportDetail.Medium;options.Controls.Add(detail);
        AddLabel(options,"Detail affects curved geometry and file size. Source model colors are retained. Quick uses Coarse geometry and skips precomputed feature edges.",false,14);
        AddLabel(options,"Approximate host project element count: "+this.candidateCount.ToString("N0",CultureInfo.CurrentCulture),false,20);
        estimate.AutoSize=true;estimate.MaximumSize=new Size(306,0);estimate.Font=new Font(Font,FontStyle.Bold);estimate.Margin=new Padding(0,0,0,8);options.Controls.Add(estimate);
        AddLabel(options,"This is an estimate based on the export area and detail. Complex families can change the result substantially. Exact size is known after export.",false,18);
        AddLabel(options,"Geometry outside the crop is excluded. Boundary-crossing triangles remain whole for clean viewer cuts.",false,0);
        var buttons=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,15,0,0)};
        var cancel=new Button{AutoSize=true,Padding=new Padding(12,5,12,5),Text="Cancel",DialogResult=DialogResult.Cancel,Width=100,Height=32,Margin=new Padding(8,0,0,0)};
        var export=new Button{AutoSize=true,Padding=new Padding(12,5,12,5),Text="Export",DialogResult=DialogResult.OK,Width=120,Height=32,Margin=new Padding(8,0,0,0)};
        var quick=new Button{AutoSize=true,Padding=new Padding(12,5,12,5),Text="Quick export",Width=120,Height=32,Margin=new Padding(8,0,0,0)};quick.Click+=(_,__)=>{detail.SelectedIndex=(int)ExportDetail.Quick;DialogResult=DialogResult.OK;};
        buttons.Controls.Add(cancel);buttons.Controls.Add(export);buttons.Controls.Add(quick);layout.Controls.Add(buttons,0,3);layout.SetColumnSpan(buttons,2);AcceptButton=export;CancelButton=cancel;
        void Changed(){scope.Text=planName+"\r\n"+(OutsideCrop?"Outside crop included (confirmed)":"View crop only")+" · Bottom: "+(AllDown?"All down":bottomLabel)+" · Top: "+(AllUp?"All up":topLabel);LoadPreview(null);ScopeChanged?.Invoke(OutsideCrop);UpdateEstimate();}
        bool confirming=false;outsideCrop.CheckedChanged+=(_,__)=>{if(confirming)return;if(outsideCrop.Checked&&MessageBox.Show(this,"Including geometry outside the view crop can create a huge model and take a long time. Keep this unchecked for fast team review PDFs. Include geometry outside the crop?","Confirm large export",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){confirming=true;outsideCrop.Checked=false;confirming=false;}Changed();};
        allUp.CheckedChanged+=(_,__)=>Changed();allDown.CheckedChanged+=(_,__)=>Changed();detail.SelectedIndexChanged+=(_,__)=>UpdateEstimate();categories.CheckedChanged+=(_,__)=>Changed();
        bool resizing=false;options.ClientSizeChanged+=(_,__)=>{if(resizing)return;resizing=true;try{int width=Math.Max(120,options.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-12);foreach(Control control in options.Controls){if(control is Label||control is CheckBox)control.MaximumSize=new Size(width,0);else if(control is ComboBox||control is TextBox)control.Width=width;}}finally{resizing=false;}};
        LoadPreview(previewImagePath);UpdateEstimate();ResumeLayout(true);
    }

    protected override void OnShown(EventArgs e){PreviewRequested=false;base.OnShown(e);var area=Screen.FromControl(this).WorkingArea;int width=Math.Max(320,area.Width-32),height=Math.Max(300,area.Height-32);MinimumSize=new Size(Math.Min(MinimumSize.Width,width),Math.Min(MinimumSize.Height,height));Size=new Size(Math.Min(Width,width),Math.Min(Height,height));Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));}

    public void LoadPreview(string? path,string? message=null)
    {
        preview.Controls.Clear();
        if(path!=null&&!File.Exists(path))throw new FileNotFoundException("The generated preview image is missing.",path);
        if(string.IsNullOrEmpty(path)){var old=preview.Image;preview.Image=null;old?.Dispose();
            preview.Controls.Add(new Label{Text=message??"Choose export options, then Export.\r\nGenerate preview is optional and may take time.",TextAlign=ContentAlignment.MiddleCenter,Dock=DockStyle.Fill,ForeColor=Color.DimGray});return;
        }
        // Clone before closing the stream; do not lock the temporary image file.
        using(var stream=File.OpenRead(path))using(var image=Image.FromStream(stream)){var old=preview.Image;PreviewDiagnostics="Loaded: "+path+" | Device DPI: "+DeviceDpi+" | control: "+preview.ClientSize.Width+"x"+preview.ClientSize.Height+" | PNG pixels: "+image.Width+"x"+image.Height+" | original PNG DPI: "+image.HorizontalResolution;var bitmap=new Bitmap(image);bitmap.SetResolution(96,96);preview.Image=bitmap;old?.Dispose();}preview.Invalidate();preview.Update();
    }
    private static void AddLabel(FlowLayoutPanel panel,string text,bool bold,int bottom=8)
    {
        var label=new Label{Text=text,AutoSize=true,MaximumSize=new Size(306,0),Margin=new Padding(0,0,0,bottom)};
        if(bold)label.Font=new Font(panel.Font,FontStyle.Bold);
        panel.Controls.Add(label);
    }
    private void UpdateEstimate()
    {
        long perElement=new[]{256L,768L,1536L,3072L,6144L}[(int)DetailPreset];
        double formatFactor=EntireModel?1:.35;
        // Deliberately broad range, not a claim to have serialized/tessellated a
        // second copy of the model while the user waits for a preview.
        double typical=2*1048576d+candidateCount*perElement*formatFactor;
        EstimatedMinimumBytes=(long)Math.Max(1048576,typical*.35);
        EstimatedMaximumBytes=(long)Math.Max(2*1048576,typical*2.5);
        estimate.Text="Estimated file size\r\n"+FormatSize(EstimatedMinimumBytes)+"–"+FormatSize(EstimatedMaximumBytes)+" (approx.)";

    }
    private static string FormatSize(long bytes)=>bytes>=1073741824?(bytes/1073741824d).ToString("0.0",CultureInfo.CurrentCulture)+" GB":(bytes/1048576d).ToString("0.0",CultureInfo.CurrentCulture)+" MB";
    protected override void Dispose(bool disposing){if(disposing){var image=preview.Image;preview.Image=null;image?.Dispose();}base.Dispose(disposing);}
}

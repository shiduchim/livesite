using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Form=System.Windows.Forms.Form;
using Button=System.Windows.Forms.Button;
using Label=System.Windows.Forms.Label;
using Timer=System.Windows.Forms.Timer;

namespace Offline3D.RevitExporter;

/// <summary>Revit work stays on its API/UI thread. The caller pumps this form
/// between bounded chunks; no background thread ever calls the Revit API.</summary>
internal sealed class ExportProgressDialog:Form
{
    private readonly Label phaseLabel=new Label();
    private readonly Label counts=new Label();
    private readonly Label elapsedLabel=new Label();
    private readonly Button cancel=new Button();
    private readonly Stopwatch total=Stopwatch.StartNew();
    private readonly List<string> timing=new List<string>();
    private readonly Timer timer=new Timer{Interval=250};
    private string phase="Preparing export";
    private long phaseStart;
    private long lastPump=-1000;
    private int? itemCount;
    private IntPtr disabledOwner;
    private bool completed,ownerRestored;
    public bool CancelRequested {get;private set;}
    public TimeSpan Elapsed=>total.Elapsed;
    public IReadOnlyList<string> TimingLog=>timing.Concat(new[]{Stamp(phase,total.ElapsedMilliseconds-phaseStart,itemCount)}).ToArray();

    public ExportProgressDialog(string title="Export 3D PDF")
    {
        SuspendLayout();
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        Font=new Font("Segoe UI",9.5f);Text=title;ClientSize=new Size(580,280);
        MinimumSize=new Size(460,260);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowOnly;
        FormBorderStyle=FormBorderStyle.Sizable;
        StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;MinimizeBox=false;MaximizeBox=false;BackColor=Color.White;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=6,Padding=new Padding(18)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int row=0;row<6;row++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach(var label in new[]{phaseLabel,counts,elapsedLabel}){label.AutoSize=true;label.Dock=DockStyle.Fill;label.Margin=new Padding(0,0,0,8);}
        phaseLabel.Text=phase;phaseLabel.Font=new Font(Font,FontStyle.Bold);
        layout.Controls.Add(phaseLabel,0,0);layout.Controls.Add(counts,0,1);layout.Controls.Add(elapsedLabel,0,2);
        var bar=new ProgressBar{Dock=DockStyle.Fill,Height=12,Margin=new Padding(0,4,0,14),Style=ProgressBarStyle.Marquee,MarqueeAnimationSpeed=30};layout.Controls.Add(bar,0,3);
        var note=new Label{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0),Text="Some native Revit export steps must finish before cancellation can be applied.",ForeColor=Color.DimGray};layout.Controls.Add(note,0,4);
        var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=new Padding(0,16,0,0)};
        cancel.AutoSize=true;cancel.AutoSizeMode=AutoSizeMode.GrowAndShrink;cancel.MinimumSize=new Size(115,32);cancel.Padding=new Padding(14,5,14,5);cancel.Margin=new Padding(0);cancel.Text="Cancel";cancel.Click+=(_,__)=>RequestCancel();CancelButton=cancel;
        buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,5);Controls.Add(layout);ResumeLayout(true);
        timer.Tick+=(_,__)=>UpdateElapsed();timer.Start();UpdateElapsed();
    }
    public new void Show(IWin32Window owner)
    {
        base.Show(owner);
        // DoEvents is needed to process Cancel while CustomExporter calls back.
        // Disable only this command's owner to prevent another Revit command or
        // model edit reentering while a temporary view/transaction group exists.
        if(owner.Handle!=IntPtr.Zero&&IsWindowEnabled(owner.Handle)){
            disabledOwner=owner.Handle;EnableWindow(disabledOwner,false);
        }
        BringToFront();Pump(phase);
    }
    public bool Pump(string stage,int? count=null,long? triangles=null,long? uniqueTriangles=null)
    {
        if(IsDisposed||CancelRequested)return false;
        long now=total.ElapsedMilliseconds;
        bool changed=!string.Equals(phase,stage,StringComparison.Ordinal);
        if(changed){timing.Add(Stamp(phase,now-phaseStart,itemCount));phase=stage;phaseStart=now;}
        itemCount=count;
        if(changed||now-lastPump>=80){
            phaseLabel.Text=phase;
            counts.Text=count.HasValue?count.Value.ToString("N0")+(triangles.HasValue?" elements processed":" items processed"):"";if(triangles.HasValue)counts.Text+="\r\nTriangles: "+triangles.Value.ToString("N0")+" placed / "+(uniqueTriangles??0).ToString("N0")+" unique stored";
            UpdateElapsed();lastPump=now;Refresh();Application.DoEvents();
        }
        return !CancelRequested&&!IsDisposed;
    }
    public void Complete(){completed=true;Close();}
    private void RequestCancel(){CancelRequested=true;cancel.Enabled=false;cancel.Text="Cancelling…";phaseLabel.Text="Cancellation requested";}
    private void UpdateElapsed(){elapsedLabel.Text="Elapsed: "+total.Elapsed.ToString(@"hh\:mm\:ss");}
    private static string Stamp(string phase,long milliseconds,int? count)=>phase+" | "+milliseconds+" ms"+(count.HasValue?" | "+count.Value+" items":"");
    private void RestoreOwner(){if(ownerRestored)return;ownerRestored=true;if(disabledOwner!=IntPtr.Zero){EnableWindow(disabledOwner,true);disabledOwner=IntPtr.Zero;}}
    protected override void OnFormClosing(FormClosingEventArgs e){if(!completed&&!CancelRequested)RequestCancel();base.OnFormClosing(e);}
    protected override void OnFormClosed(FormClosedEventArgs e){RestoreOwner();base.OnFormClosed(e);}
    protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();RestoreOwner();}base.Dispose(disposing);}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool EnableWindow(IntPtr window,[MarshalAs(UnmanagedType.Bool)]bool enabled);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowEnabled(IntPtr window);
}

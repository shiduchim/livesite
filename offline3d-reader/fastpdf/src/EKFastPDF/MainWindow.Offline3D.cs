using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Offline3D;
using EKFastPDF.Viewer;
using Offline3D;
namespace EKFastPDF;

/// <summary>
/// ClickTo3D. The toolbar button arms 3D: the pointer turns blue and a dashed box
/// follows it over the plan, while the 3D page and model load in the background.
/// Only a click on the plan opens the 3D panel. Closing hides the panel but keeps
/// it loaded for the document, so it reopens instantly; it is disposed when the
/// document changes or the window closes.
/// </summary>
public partial class MainWindow
{
    private CancellationTokenSource? boxLifetime;
    private BoxPanel? boxPanel;
    private DocumentSession? boxSession;
    private PdfPageMap? boxPageMap;
    private BoxFootprintOverlay? boxFootprint;
    private Window? floatingBox;
    private bool closing3D,boxArmed,boxShown,boxFloating;
    private (double U,double V)? boxPendingClick;
    private static Cursor? clickTo3DCursor;
    private static Cursor ClickTo3DCursor=>clickTo3DCursor??=LoadCursor();
    private static Cursor LoadCursor(){
        try{using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ClickTo3D.cur"))!.Stream;return new Cursor(stream,true);}
        catch(Exception ex) when (ex is IOException or ArgumentException or NullReferenceException or System.ComponentModel.Win32Exception){return Cursors.Hand;}
    }
    private static readonly Brush ArmedBackground=new SolidColorBrush(Color.FromRgb(47,111,222)),ArmedBorder=new SolidColorBrush(Color.FromRgb(28,74,160));

    private void Box3D_Click(object sender,RoutedEventArgs e)
    {
        if(boxArmed||boxShown){Close3D();return;}
        Arm3D();
    }

    private async void Arm3D()
    {
        var session=_active;if(session?.IsReady!=true)return;
        boxArmed=true;boxPendingClick=null;
        Viewport.IdleCursor=ClickTo3DCursor;Viewport.PageClicked+=BoxPageClicked;PreviewKeyDown+=BoxEscape;
        SyncBoxButton();StatusText.Text="ClickTo3D · click a spot on the plan to open it in 3D · Esc to cancel";
        if(boxPanel!=null&&ReferenceEquals(boxSession,session)){AttachFootprint();return;}
        Discard3D(keepArmed:true);
        boxLifetime=new CancellationTokenSource();var token=boxLifetime.Token;boxSession=session;
        try {
            // Nothing in PDF open/render/print is changed; this work begins on explicit activation.
            var packageTask=Task.Run(()=>new PdfPackageService().Read(session.FilePath),token);
            var mapTask=_services.Pool.GetPageMapAsync(session.DocId,0);
            await Task.WhenAll(packageTask,mapTask);token.ThrowIfCancellationRequested();
            if(session.IsClosed||!ReferenceEquals(session,_active))return;
            var package=await packageTask;boxPageMap=await mapTask;
            var panel=new BoxPanel(package);boxPanel=panel;
            boxFootprint=new BoxFootprintOverlay(boxPageMap,(edge,x,y)=>boxPanel?.ResizeEdgeFromPdf(edge,x,y),()=>boxPanel?.CommitResize());
            panel.HideRequested+=Close3D;panel.Status+=text=>StatusText.Text=text;
            panel.UndockRequested+=ToggleBoxDock;
            panel.FootprintChanged+=points=>{if(boxShown){boxFootprint?.SetPoints(points);Viewport.RefreshOverlays();}};
            if(boxArmed)AttachFootprint();
            // The page loads (and takes the model) inside the zero-width column.
            BoxHost.Content=panel.Control;
            await panel.InitializeAsync();token.ThrowIfCancellationRequested();
            if(package.Manifest.Synthetic&&boxArmed)StatusText.Text="SYNTHETIC DEMO · "+StatusText.Text;
            if(boxPendingClick is var (u,v)){boxPendingClick=null;OpenAt(u,v);}
        }catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested){Discard3D();StatusText.Text="3D unavailable: "+ex.Message;}}
    }

    private void AttachFootprint(){
        if(boxFootprint==null)return;
        boxFootprint.Preview=boxShown?null:(x,y)=>boxPanel?.PreviewFootprint(x,y);
        if(!Viewport.Overlays.Contains(boxFootprint))Viewport.Overlays.Add(boxFootprint);
        Viewport.RefreshOverlays();
    }

    private void BoxPageClicked(int page,double u,double v)
    {
        if(page!=0||!(boxArmed||boxShown))return;
        if(boxPanel==null||boxPageMap==null){boxPendingClick=(u,v);StatusText.Text="Opening 3D…";ShowBoxColumn();return;}
        OpenAt(u,v);
    }

    private void OpenAt(double u,double v)
    {
        if(boxPanel==null||boxPageMap==null)return;
        try{
            var (x,y)=boxPageMap.Apply(u,v);bool first=!boxShown;
            // Shown first, so the footprint raised by SetPoint is drawn.
            if(first){boxShown=true;boxArmed=false;ShowBoxColumn();AttachFootprint();SyncBoxButton();StatusText.Text="Click the plan again to move the box · drag its blue arrows to stretch it";}
            boxPanel.SetPoint(x,y,fit:first);
        }
        catch(Exception ex){StatusText.Text=ex.Message;}
    }

    private void ShowBoxColumn()
    {
        if(floatingBox!=null)return;
        if(boxFloating&&boxPanel!=null){Undock();return;}
        BoxColumn.Width=new GridLength(Math.Max(360,Math.Min(820,ActualWidth*.5)));BoxDividerColumn.Width=new GridLength(6);BoxDivider.Visibility=Visibility.Visible;
        if(boxPanel!=null&&BoxHost.Content==null)BoxHost.Content=boxPanel.Control;
        boxPanel?.SetDocked(true);
    }

    private void HideBoxColumn(){BoxColumn.Width=new GridLength(0);BoxDividerColumn.Width=new GridLength(0);BoxDivider.Visibility=Visibility.Collapsed;}

    private void BoxEscape(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&boxArmed&&!boxShown){Close3D();e.Handled=true;}}

    private void SyncBoxButton()
    {
        if(boxArmed){Box3DButton.Content="ClickTo3D";Box3DButton.Background=ArmedBackground;Box3DButton.BorderBrush=ArmedBorder;Box3DButton.Foreground=Brushes.White;Box3DButton.ToolTip="Click a spot on the plan to open it in 3D (Esc cancels)";}
        else{Box3DButton.ClearValue(BackgroundProperty);Box3DButton.ClearValue(BorderBrushProperty);Box3DButton.ClearValue(ForegroundProperty);Box3DButton.Content=boxShown?"Close 3D":"ClickTo3D";Box3DButton.ToolTip=boxShown?"Close the 3D view":"Open a spot of the plan in 3D";}
    }

    /// <summary>Hides 3D and leaves arming. The panel stays loaded for this document.</summary>
    private void Close3D()
    {
        if(closing3D)return;closing3D=true;
        try{
            boxArmed=boxShown=false;boxPendingClick=null;
            Viewport.PageClicked -= BoxPageClicked;PreviewKeyDown-=BoxEscape;
            Viewport.CancelOverlayInteraction();Viewport.IdleCursor=null;
            if(floatingBox!=null){var window=floatingBox;floatingBox=null;window.Content=null;window.Close();if(boxPanel!=null)BoxHost.Content=boxPanel.Control;}
            HideBoxColumn();
            if(boxFootprint!=null){boxFootprint.SetPoints(null);boxFootprint.Preview=null;Viewport.Overlays.Remove(boxFootprint);Viewport.RefreshOverlays();}
            SyncBoxButton();RefreshState();
        }finally{closing3D=false;}
    }

    /// <summary>Closes 3D and disposes the panel (document changed, window closing, or a failure).</summary>
    private void Discard3D(bool keepArmed=false)
    {
        if(!keepArmed)Close3D();
        boxLifetime?.Cancel();boxLifetime?.Dispose();boxLifetime=null;
        if(boxFootprint!=null){Viewport.Overlays.Remove(boxFootprint);boxFootprint=null;Viewport.RefreshOverlays();}
        BoxHost.Content=null;boxPanel?.Dispose();boxPanel=null;boxSession=null;boxPageMap=null;
    }

    private void ToggleBoxDock(){
        if(boxPanel==null)return;
        if(floatingBox!=null){var window=floatingBox;floatingBox=null;window.Content=null;window.Close();boxFloating=false;DockBox();return;}
        boxFloating=true;Undock();
    }
    private void Undock(){
        if(boxPanel==null)return;
        BoxHost.Content=null;HideBoxColumn();
        var floating=new Window{Title="Offline 3D PDF — 3D view",Width=1100,Height=850,MinWidth=450,MinHeight=450,Owner=this,Content=boxPanel.Control,WindowStartupLocation=WindowStartupLocation.CenterOwner};floatingBox=floating;
        floating.Closed+=(_,_)=>{if(ReferenceEquals(floatingBox,floating)){floating.Content=null;floatingBox=null;boxFloating=false;if(!closing3D)DockBox();}};
        floating.Show();boxPanel.SetDocked(false);
    }
    private void DockBox(){if(boxPanel==null)return;BoxHost.Content=boxPanel.Control;if(boxShown)ShowBoxColumn();boxPanel.SetDocked(true);}
}

using System.IO;
using System.Windows;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Offline3D;
using Offline3D;
namespace EKFastPDF;
public partial class MainWindow
{
    private CancellationTokenSource? boxLifetime;
    private BoxPanel? boxPanel;
    private PdfPageMap? boxPageMap;
    private BoxFootprintOverlay? boxFootprint;
    private Window? floatingBox;
    private bool closing3D;
    private async void Box3D_Click(object sender,RoutedEventArgs e)
    {
        if(boxLifetime!=null){Close3D();return;}
        var session=_active;if(session?.IsReady!=true)return;
        boxLifetime=new CancellationTokenSource();var token=boxLifetime.Token;
        Box3DButton.Content="Hide 3D box";StatusText.Text="Validating 3D package…";
        try {
            // Nothing in PDF open/render/print is changed; this work begins on explicit activation.
            var packageTask=Task.Run(()=>new PdfPackageService().Read(session.FilePath),token);
            var mapTask=_services.Pool.GetPageMapAsync(session.DocId,0);
            await Task.WhenAll(packageTask,mapTask);token.ThrowIfCancellationRequested();
            if(session.IsClosed||!ReferenceEquals(session,_active))return;
            var package=await packageTask;boxPageMap=await mapTask;
            boxFootprint=new BoxFootprintOverlay(boxPageMap,(edge,x,y)=>boxPanel?.ResizeEdgeFromPdf(edge,x,y));Viewport.Overlays.Add(boxFootprint);
            var panel=new BoxPanel(package);boxPanel=panel;
            panel.HideRequested+=Close3D;panel.Status+=text=>StatusText.Text=text;
            panel.UndockRequested+=ToggleBoxDock;
            panel.FootprintChanged+=points=>{boxFootprint?.SetPoints(points);Viewport.RefreshOverlays();};
            BoxColumn.Width=new GridLength(Math.Max(360,Math.Min(820,ActualWidth*.5)));BoxDividerColumn.Width=new GridLength(6);BoxDivider.Visibility=Visibility.Visible;BoxHost.Content=panel.Control;
            await panel.InitializeAsync();token.ThrowIfCancellationRequested();
            Viewport.PageClicked += BoxPageClicked;
            StatusText.Text=package.Manifest.Synthetic?"SYNTHETIC DEMO · Click inside the plan for a 3D box":"Click inside the registered plan for a 3D box";
        }catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested){Close3D();StatusText.Text="3D unavailable: "+ex.Message;}}
    }
    private void BoxPageClicked(int page,double u,double v)
    {
        if(page!=0||boxPanel==null||boxPageMap==null||boxLifetime?.IsCancellationRequested!=false)return;
        try{var (x,y)=boxPageMap.Apply(u,v);boxPanel.SetPoint(x,y);}
        catch(Exception ex){StatusText.Text=ex.Message;}
    }
    private void Close3D()
    {
        if(closing3D)return;closing3D=true;
        Viewport.PageClicked -= BoxPageClicked;
        Viewport.CancelOverlayInteraction();
        boxLifetime?.Cancel();boxLifetime?.Dispose();boxLifetime=null;
        if(floatingBox!=null){var window=floatingBox;floatingBox=null;window.Content=null;window.Close();}
        BoxHost.Content=null;boxPanel?.Dispose();boxPanel=null;boxPageMap=null;
        if(boxFootprint!=null){Viewport.Overlays.Remove(boxFootprint);boxFootprint=null;Viewport.RefreshOverlays();}
        BoxColumn.Width=new GridLength(0);BoxDividerColumn.Width=new GridLength(0);BoxDivider.Visibility=Visibility.Collapsed;Box3DButton.Content="3D box";closing3D=false;
    }
    private void ToggleBoxDock(){
        if(boxPanel==null)return;
        if(floatingBox!=null){var window=floatingBox;floatingBox=null;window.Content=null;window.Close();DockBox();return;}
        BoxHost.Content=null;BoxColumn.Width=new GridLength(0);BoxDividerColumn.Width=new GridLength(0);BoxDivider.Visibility=Visibility.Collapsed;
        var floating=new Window{Title="Offline 3D PDF — 3D view",Width=1100,Height=850,MinWidth=450,MinHeight=450,Owner=this,Content=boxPanel.Control,WindowStartupLocation=WindowStartupLocation.CenterOwner};floatingBox=floating;
        floating.Closed+=(_,_)=>{if(ReferenceEquals(floatingBox,floating)){floating.Content=null;floatingBox=null;if(!closing3D)DockBox();}};
        floating.Show();boxPanel.SetDocked(false);
    }
    private void DockBox(){if(boxPanel==null)return;BoxColumn.Width=new GridLength(Math.Max(360,Math.Min(820,ActualWidth*.5)));BoxDividerColumn.Width=new GridLength(6);BoxDivider.Visibility=Visibility.Visible;BoxHost.Content=boxPanel.Control;boxPanel.SetDocked(true);}
}

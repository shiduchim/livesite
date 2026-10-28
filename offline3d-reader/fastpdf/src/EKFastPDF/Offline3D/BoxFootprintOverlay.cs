using System.Windows;
using System.Windows.Media;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Viewer;
using Offline3D;

namespace EKFastPDF.Offline3D;

/// <summary>A live overlay; never edits or rerasterizes the PDF.</summary>
internal sealed class BoxFootprintOverlay:IInteractiveViewportOverlay
{
    private readonly PdfPageMap map;
    private Point3[]? points;
    private readonly Action<int,double,double> resize;
    private int drag=-1;
    private Vector grabOffset;
    private static readonly Pen Outline=new(new SolidColorBrush(Color.FromRgb(45,99,186)),1.25);
    private static readonly Brush GripFill=new SolidColorBrush(Color.FromRgb(114,149,209));
    private static readonly Brush GripStroke=new SolidColorBrush(Color.FromRgb(41,76,127));
    private static readonly Brush Fill=new SolidColorBrush(Color.FromArgb(30,25,118,210));
    public BoxFootprintOverlay(PdfPageMap map,Action<int,double,double> resize){this.map=map;this.resize=resize;}
    public void SetPoints(Point3[] value){points=value;}
    private Point[] ScreenPoints(OverlayContext context){
        if(points==null)return Array.Empty<Point>();
        var page=context.Session.Layout!.PageRect(0);
        Point Display(Point3 p){var (u,v)=map.Unapply(p.X,p.Y);var r=context.PageToScreen(0,u*page.Width,v*page.Height,u*page.Width,v*page.Height);return r.TopLeft;}
        return points.Select(Display).ToArray();
    }
    public bool TryBeginDrag(Point point,OverlayContext context){
        var polygon=ScreenPoints(context);if(polygon.Length!=4)return false;
        var nearest=Enumerable.Range(0,4).Select(edge=>{var a=polygon[edge];var b=polygon[(edge+1)%4];var mid=new Point((a.X+b.X)/2,(a.Y+b.Y)/2);return new{edge,mid,distance=(point-mid).Length};}).OrderBy(x=>x.distance).First();if(nearest.distance>23*context.DpiScale)return false;drag=nearest.edge;grabOffset=nearest.mid-point;return true;
    }
    public void DragTo(Point point,OverlayContext context){
        if(drag<0)return;point+=grabOffset;var page=context.ScreenToPage(point.X,point.Y);if(page==null||page.Value.Page!=0)return;var rect=context.Session.Layout!.PageRect(0);var (x,y)=map.Apply(page.Value.X/rect.Width,page.Value.Y/rect.Height);resize(drag,x,y);
    }
    public void EndDrag(){drag=-1;}
    public void Render(DrawingContext dc,OverlayContext context){
        // Pointer is already the mouse hotspot in device pixels. The viewport
        // applies its inverse-DPI drawing transform; offsetting or scaling the
        // center here would move the dot away from the arrow tip.
        if(context.Pointer is Point pointer){double dpi=context.DpiScale;dc.DrawEllipse(Brushes.DodgerBlue,new Pen(Brushes.White,1.5*dpi),pointer,3.5*dpi,3.5*dpi);}
        if(!context.VisiblePages.Contains(0))return;var polygon=ScreenPoints(context);if(polygon.Length!=4)return;var page=context.Session.Layout!.PageRect(0);var geometry=new StreamGeometry();
        using(var drawing=geometry.Open()){drawing.BeginFigure(polygon[0],true,true);drawing.PolyLineTo(polygon.Skip(1).ToArray(),true,true);}
        dc.PushClip(new RectangleGeometry(context.PageToScreen(0,0,0,page.Width,page.Height)));dc.DrawGeometry(Fill,Outline,geometry);
        for(int edge=0;edge<4;edge++){var a=polygon[edge];var b=polygon[(edge+1)%4];var along=b-a;along.Normalize();var normal=new Vector(-along.Y,along.X);var mid=new Point((a.X+b.X)/2,(a.Y+b.Y)/2);foreach(int sign in new[]{-1,1}){var tip=mid+normal*(sign*10*context.DpiScale);var center=mid+normal*(sign*3*context.DpiScale);var arrow=new StreamGeometry();using(var drawing=arrow.Open()){drawing.BeginFigure(tip,true,true);drawing.LineTo(center+along*(4.5*context.DpiScale),true,false);drawing.LineTo(center-along*(4.5*context.DpiScale),true,false);}dc.DrawGeometry(GripFill,new Pen(GripStroke,context.DpiScale),arrow);}}
        dc.Pop();
    }
}

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
    private readonly Action commit;
    /// <summary>While set, a dashed box follows the pointer: where a click would put the 3D box.</summary>
    public Func<double,double,Point3[]?>? Preview{get;set;}
    private static readonly Pen PreviewOutline=new(new SolidColorBrush(Color.FromRgb(47,111,222)),1.5){DashStyle=new DashStyle(new double[]{4,3},0)};
    private static readonly Brush PreviewFill=new SolidColorBrush(Color.FromArgb(22,47,111,222));
    private int drag=-1;
    private Vector grabOffset;
    private static readonly Pen Outline=new(new SolidColorBrush(Color.FromRgb(45,99,186)),1.25);
    private static readonly Brush GripFill=new SolidColorBrush(Color.FromRgb(114,149,209));
    private static readonly Brush GripStroke=new SolidColorBrush(Color.FromRgb(41,76,127));
    private static readonly Brush Fill=new SolidColorBrush(Color.FromArgb(30,25,118,210));
    public BoxFootprintOverlay(PdfPageMap map,Action<int,double,double> resize,Action commit){this.map=map;this.resize=resize;this.commit=commit;}
    public void SetPoints(Point3[]? value){points=value;}
    private Point[] ScreenPoints(OverlayContext context)=>ScreenPoints(points,context);
    private Point[] ScreenPoints(Point3[]? points,OverlayContext context){
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
    public void EndDrag(){if(drag>=0)commit();drag=-1;}
    public void Render(DrawingContext dc,OverlayContext context){
        if(!context.VisiblePages.Contains(0))return;var page=context.Session.Layout!.PageRect(0);
        // The blue pointer is the system cursor (Assets/ClickTo3D.cur). Before the
        // first click, a dashed box shows where the 3D box will go.
        if(Preview!=null&&context.Pointer is Point pointer&&context.ScreenToPage(pointer.X,pointer.Y) is {Page:0} at){
            var (px,py)=map.Apply(at.X/page.Width,at.Y/page.Height);var preview=ScreenPoints(Preview(px,py),context);
            if(preview.Length==4){var shape=new StreamGeometry();using(var drawing=shape.Open()){drawing.BeginFigure(preview[0],true,true);drawing.PolyLineTo(preview.Skip(1).ToArray(),true,true);}var pen=PreviewOutline.Clone();pen.Thickness*=context.DpiScale;dc.PushClip(new RectangleGeometry(context.PageToScreen(0,0,0,page.Width,page.Height)));dc.DrawGeometry(PreviewFill,pen,shape);dc.Pop();}
        }
        var polygon=ScreenPoints(context);if(polygon.Length!=4)return;var geometry=new StreamGeometry();
        using(var drawing=geometry.Open()){drawing.BeginFigure(polygon[0],true,true);drawing.PolyLineTo(polygon.Skip(1).ToArray(),true,true);}
        dc.PushClip(new RectangleGeometry(context.PageToScreen(0,0,0,page.Width,page.Height)));dc.DrawGeometry(Fill,Outline,geometry);
        for(int edge=0;edge<4;edge++){var a=polygon[edge];var b=polygon[(edge+1)%4];var along=b-a;along.Normalize();var normal=new Vector(-along.Y,along.X);var mid=new Point((a.X+b.X)/2,(a.Y+b.Y)/2);foreach(int sign in new[]{-1,1}){var tip=mid+normal*(sign*10*context.DpiScale);var center=mid+normal*(sign*3*context.DpiScale);var arrow=new StreamGeometry();using(var drawing=arrow.Open()){drawing.BeginFigure(tip,true,true);drawing.LineTo(center+along*(4.5*context.DpiScale),true,false);drawing.LineTo(center-along*(4.5*context.DpiScale),true,false);}dc.DrawGeometry(GripFill,new Pen(GripStroke,context.DpiScale),arrow);}}
        dc.Pop();
    }
}

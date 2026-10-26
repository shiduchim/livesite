using Autodesk.Revit.DB;
namespace Offline3D.RevitExporter;

internal sealed class ExportRegion
{
    public Autodesk.Revit.DB.View Source {get;private set;}=null!;
    public Viewport Viewport {get;private set;}=null!;
    public BoundingBoxXYZ Box {get;private set;}=null!;
    public CropClipper BroadPhase {get;private set;}=null!;
    public ExportVolume Volume {get;private set;}=null!;
    public Point3 Min=>BroadPhase.Min;
    public Point3 Max=>BroadPhase.Max;
    public string TopLabel {get;private set;}="";
    public string BottomLabel {get;private set;}="";
    public ViewRegion Mapping {get;private set;}=null!;
    private BoundingBoxXYZ originalBox=null!;
    private Matrix4 graphics=null!;
    public static ExportRegion Create(Document doc,Autodesk.Revit.DB.View source,Viewport viewport,Matrix4 graphics)
    {
        BoundingBoxXYZ box;string top="Crop depth",bottom="Crop depth";double level=0;
        if(source is ViewPlan plan){
            var loops=source.GetCropRegionShapeManager().GetCropShape();
            if(loops.Count!=1||loops[0].Count()!=4||loops[0].Any(c=>!(c is Line)))throw new ExportSetupException("Use one rectangular unsplit crop in "+source.Name+".");
            var corners=loops[0].Select(c=>c.GetEndPoint(0)).ToArray();var s=ExportScope.From(doc,plan,corners);box=s.SectionBox;top=s.TopLabel;bottom=s.BottomLabel;level=s.LevelFeet;
        }else if(source is View3D three){
            box=three.IsSectionBoxActive?three.GetSectionBox():VisibleBox(doc,source);top="3D view bounds";bottom=top;
        }else box=source.CropBox;
        // Use the actual view crop and adjacent levels, without expanding it
        // into extra model area. Keep the oriented frame for rotated crops.
        var points=BoxPoints(box).ToArray();var min=new Point3(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z));var max=new Point3(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z));
        var polygon=new[]{new Point3(min.X,min.Y),new Point3(max.X,min.Y),new Point3(max.X,max.Y),new Point3(min.X,max.Y)};
        var transforms=source.GetModelToProjectionTransforms();if(transforms.Count!=1)throw new ExportSetupException("Split projection regions are not supported: "+source.Name);
        var toSheet=Adapter.Matrix(viewport.GetProjectionToSheetTransform())*Adapter.Matrix(transforms[0].GetModelToProjectionTransform());
        var outline=viewport.GetBoxOutline();var a=outline.MinimumPoint;var b=outline.MaximumPoint;
        var center=new Point3((min.X+max.X)/2,(min.Y+max.Y)/2,(min.Z+max.Z)/2);
        var origin=source is ViewPlan?new Point3(center.X,center.Y,level):center;
        var normal=source is ViewPlan?new Point3(0,0,1):Adapter.Point(source.ViewDirection);
        var gp=points.Select(p=>graphics.Apply(Adapter.Point(p))).ToArray();
        return new ExportRegion{Source=source,Viewport=viewport,Box=box,originalBox=box,graphics=graphics,Volume=new ExportVolume(Adapter.Point(box.Min),Adapter.Point(box.Max),Adapter.Matrix(box.Transform)),BroadPhase=new CropClipper(polygon,min.Z,max.Z),TopLabel=top,BottomLabel=bottom,
            Mapping=new ViewRegion{ViewId=source.UniqueId,Name=source.Name,Kind=source.ViewType.ToString(),ModelToSheet=toSheet,CropSheet=new[]{a.X,a.Y,b.X,a.Y,b.X,b.Y,a.X,b.Y},PlaneOrigin=new[]{origin.X,origin.Y,origin.Z},PlaneNormal=new[]{normal.X,normal.Y,normal.Z},LevelFeet=source is ViewPlan?level:center.Z,BoundsGraphics=new[]{gp.Min(p=>p.X),gp.Min(p=>p.Y),gp.Min(p=>p.Z),gp.Max(p=>p.X),gp.Max(p=>p.Y),gp.Max(p=>p.Z)}}};
    }
    public void ConfigureBounds(bool outside,bool allUp,bool allDown,BoundingBoxXYZ? modelBounds)
    {
        var lo=originalBox.Min;var hi=originalBox.Max;
        if(modelBounds!=null&&(outside||allUp||allDown)){
            var inverse=originalBox.Transform.Inverse;var p=BoxPoints(modelBounds).Select(inverse.OfPoint).ToArray();
            if(Source is ViewPlan){var bounds=ExportVolume.PlanBounds(Adapter.Point(lo),Adapter.Point(hi),new Point3(p.Min(v=>v.X),p.Min(v=>v.Y),p.Min(v=>v.Z)),new Point3(p.Max(v=>v.X),p.Max(v=>v.Y),p.Max(v=>v.Z)),outside,allUp,allDown);lo=new XYZ(bounds[0].X,bounds[0].Y,bounds[0].Z);hi=new XYZ(bounds[1].X,bounds[1].Y,bounds[1].Z);}
            else if(outside){lo=new XYZ(p.Min(v=>v.X),p.Min(v=>v.Y),p.Min(v=>v.Z));hi=new XYZ(p.Max(v=>v.X),p.Max(v=>v.Y),p.Max(v=>v.Z));}
        }
        Box=new BoundingBoxXYZ{Transform=originalBox.Transform,Min=lo,Max=hi};Volume=new ExportVolume(Adapter.Point(lo),Adapter.Point(hi),Adapter.Matrix(Box.Transform));
        var host=BoxPoints(Box).Select(Adapter.Point).ToArray();var min=new Point3(host.Min(p=>p.X),host.Min(p=>p.Y),host.Min(p=>p.Z));var max=new Point3(host.Max(p=>p.X),host.Max(p=>p.Y),host.Max(p=>p.Z));BroadPhase=new CropClipper(new[]{new Point3(min.X,min.Y),new Point3(max.X,min.Y),new Point3(max.X,max.Y),new Point3(min.X,max.Y)},min.Z,max.Z);
        var gp=host.Select(graphics.Apply).ToArray();Mapping.BoundsGraphics=new[]{gp.Min(p=>p.X),gp.Min(p=>p.Y),gp.Min(p=>p.Z),gp.Max(p=>p.X),gp.Max(p=>p.Y),gp.Max(p=>p.Z)};
    }
    public bool Intersects(BoundingBoxXYZ? elementBox,Matrix4 frame)
    {
        if(elementBox==null)return true;var points=BoxPoints(elementBox).Select(p=>frame.Apply(Adapter.Point(p))).ToArray();
        if(!BroadPhase.IntersectsBounds(new Point3(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z)),new Point3(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z))))return false;
        var inverse=Adapter.Matrix(Box.Transform.Inverse);var local=points.Select(inverse.Apply).ToArray();return local.Max(p=>p.X)>=Box.Min.X&&local.Min(p=>p.X)<=Box.Max.X&&local.Max(p=>p.Y)>=Box.Min.Y&&local.Min(p=>p.Y)<=Box.Max.Y&&local.Max(p=>p.Z)>=Box.Min.Z&&local.Min(p=>p.Z)<=Box.Max.Z;
    }
    public ICollection<ElementId> Candidates(Document doc)=>new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(new Outline(new XYZ(Min.X,Min.Y,Min.Z),new XYZ(Max.X,Max.Y,Max.Z)))).ToElementIds();
    internal static IEnumerable<XYZ> BoxPoints(BoundingBoxXYZ box){for(int i=0;i<8;i++)yield return box.Transform.OfPoint(new XYZ((i&1)==0?box.Min.X:box.Max.X,(i&2)==0?box.Min.Y:box.Max.Y,(i&4)==0?box.Min.Z:box.Max.Z));}
    private static BoundingBoxXYZ VisibleBox(Document doc,Autodesk.Revit.DB.View view){
        var points=new FilteredElementCollector(doc,view.Id).WhereElementIsNotElementType().Select(e=>e.get_BoundingBox(view)).Where(b=>b!=null).SelectMany(b=>BoxPoints(b!)).ToArray();
        if(points.Length==0)throw new ExportSetupException("No visible model bounds in "+view.Name);
        return new BoundingBoxXYZ{Min=new XYZ(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z)),Max=new XYZ(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z))};
    }
}

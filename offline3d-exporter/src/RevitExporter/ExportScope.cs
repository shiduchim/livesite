using Autodesk.Revit.DB;

namespace Offline3D.RevitExporter;

/// <summary>One crop extrusion between nearest distinct project levels.</summary>
internal sealed class ExportScope
{
    public double LevelFeet{get;private set;}
    public double MinZ{get;private set;}
    public double MaxZ{get;private set;}
    public double AboveFeet=>MaxZ-LevelFeet;
    public double BelowFeet=>LevelFeet-MinZ;
    public string TopLabel{get;private set;}="";
    public string BottomLabel{get;private set;}="";
    public bool TopFallback{get;private set;}
    public bool BottomFallback{get;private set;}
    public BoundingBoxXYZ SectionBox{get;private set;}=null!;
    public CropClipper Clipper{get;private set;}=null!;

    public static ExportScope From(Document doc,ViewPlan plan,XYZ[] corners,double fallbackAboveFeet=10,double fallbackBelowFeet=10)
    {
        if(plan.GenLevel==null)throw new ExportSetupException("A floor plan with a known level is required.");
        if(corners==null||corners.Length<3)throw new ExportSetupException("The crop boundary is missing.");
        var levels=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(l=>new LevelDatum(l.Name,l.ProjectElevation));
        var range=AdjacentLevelRange.Select(plan.GenLevel.ProjectElevation,levels,fallbackAboveFeet,fallbackBelowFeet);
        var polygon=corners.Select(p=>new Point3(p.X,p.Y,p.Z)).ToArray();var clipper=new CropClipper(polygon,range.MinZ,range.MaxZ);
        var first=corners[1]-corners[0];var x=new XYZ(first.X,first.Y,0).Normalize();var y=XYZ.BasisZ.CrossProduct(x);
        var origin=new XYZ(corners.Average(p=>p.X),corners.Average(p=>p.Y),0);
        var frame=Transform.Identity;frame.Origin=origin;frame.BasisX=x;frame.BasisY=y;frame.BasisZ=XYZ.BasisZ;
        var inverse=frame.Inverse;var local=corners.Select(inverse.OfPoint).ToArray();
        var section=new BoundingBoxXYZ{Transform=frame,Min=new XYZ(local.Min(p=>p.X),local.Min(p=>p.Y),range.MinZ),Max=new XYZ(local.Max(p=>p.X),local.Max(p=>p.Y),range.MaxZ)};
        return new ExportScope{LevelFeet=range.LevelFeet,MinZ=range.MinZ,MaxZ=range.MaxZ,TopLabel=range.TopLabel,BottomLabel=range.BottomLabel,TopFallback=range.TopFallback,BottomFallback=range.BottomFallback,SectionBox=section,Clipper=clipper};
    }
}

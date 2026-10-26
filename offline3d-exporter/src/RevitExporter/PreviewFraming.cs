using Autodesk.Revit.DB;
namespace Offline3D.RevitExporter;
internal static class PreviewFraming
{
    public static void Apply(Document doc,View3D view,BoundingBoxXYZ? region,System.Drawing.Size previewSize,Action<string> warn){
        view.AreAnnotationCategoriesHidden=true;
        foreach(var category in new[]{BuiltInCategory.OST_Levels,BuiltInCategory.OST_Grids,BuiltInCategory.OST_CLines,BuiltInCategory.OST_VolumeOfInterest,BuiltInCategory.OST_SectionBox}){var id=new ElementId(category);if(view.CanCategoryBeHidden(id))view.SetCategoryHidden(id,true);}
        // Preview-only: linked datums inherit these host category hides.
        foreach(var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())try{view.SetLinkOverrides(link.Id,new RevitLinkGraphicsSettings{LinkVisibilityType=LinkVisibility.ByHostView});}catch(Autodesk.Revit.Exceptions.InvalidOperationException){}catch(Autodesk.Revit.Exceptions.ArgumentException){}
        doc.Regenerate();var bounds=region??ModelBounds(doc,view);if(bounds==null){warn("No model geometry bounds were found for preview framing.");return;}
        var crop=view.CropBox;var inverse=crop.Transform.Inverse;var corners=ExportRegion.BoxPoints(bounds).Select(inverse.OfPoint).ToArray();double minX=corners.Min(p=>p.X),maxX=corners.Max(p=>p.X),minY=corners.Min(p=>p.Y),maxY=corners.Max(p=>p.Y),z=corners.Min(p=>p.Z),Z=corners.Max(p=>p.Z);double aspect=previewSize.Height>0?(double)previewSize.Width/previewSize.Height:1.5,w=Math.Max(.01,maxX-minX)*1.12,h=Math.Max(.01,maxY-minY)*1.12;aspect=Math.Max(.2,Math.Min(5,aspect));w=Math.Max(w,h*aspect);h=Math.Max(h,w/aspect);double x=(minX+maxX)/2,y=(minY+maxY)/2;
        crop.Min=new XYZ(x-w/2,y-h/2,z-2);crop.Max=new XYZ(x+w/2,y+h/2,Z+2);view.CropBox=crop;view.CropBoxActive=true;view.CropBoxVisible=false;
    }
    internal static BoundingBoxXYZ? ModelBounds(Document doc,View3D view){
        XYZ? min=null,max=null;
        void Include(XYZ p){min=min==null?p:new XYZ(Math.Min(min.X,p.X),Math.Min(min.Y,p.Y),Math.Min(min.Z,p.Z));max=max==null?p:new XYZ(Math.Max(max.X,p.X),Math.Max(max.Y,p.Y),Math.Max(max.Z,p.Z));}
        var linkBounds=new Dictionary<Document,BoundingBoxXYZ?>();
        BoundingBoxXYZ? LinkBounds(Document linked){if(linkBounds.TryGetValue(linked,out var b))return b;linkBounds.Add(linked,null);XYZ? lo=null,hi=null;foreach(var e in new FilteredElementCollector(linked).WhereElementIsNotElementType()){if(e.Category?.CategoryType!=CategoryType.Model||e.ViewSpecific||e is DatumPlane||e is Group||e.Category.Id.Value==(long)BuiltInCategory.OST_VolumeOfInterest||e.Category.Id.Value==(long)BuiltInCategory.OST_SectionBox||e.Category.Id.Value==(long)BuiltInCategory.OST_CLines)continue;var nested=e as RevitLinkInstance;var nestedDoc=nested?.GetLinkDocument();var box=nested==null?e.get_BoundingBox(null):nestedDoc==null?null:LinkBounds(nestedDoc);if(box==null)continue;foreach(var raw in ExportRegion.BoxPoints(box)){var p=nested==null?raw:nested.GetTotalTransform().OfPoint(raw);lo=lo==null?p:new XYZ(Math.Min(lo.X,p.X),Math.Min(lo.Y,p.Y),Math.Min(lo.Z,p.Z));hi=hi==null?p:new XYZ(Math.Max(hi.X,p.X),Math.Max(hi.Y,p.Y),Math.Max(hi.Z,p.Z));}}b=lo==null?null:new BoundingBoxXYZ{Min=lo,Max=hi!};linkBounds[linked]=b;return b;}
        foreach(var e in new FilteredElementCollector(doc,view.Id).WhereElementIsNotElementType()){
            if(e.Category?.CategoryType!=CategoryType.Model||e.ViewSpecific||e is DatumPlane||e is Group||e.Category.Id.Value==(long)BuiltInCategory.OST_VolumeOfInterest||e.Category.Id.Value==(long)BuiltInCategory.OST_SectionBox||e.Category.Id.Value==(long)BuiltInCategory.OST_CLines)continue;
            if(e is RevitLinkInstance link){var linked=link.GetLinkDocument();if(linked==null)continue;var box=LinkBounds(linked);if(box==null)continue;foreach(var p in ExportRegion.BoxPoints(box))Include(link.GetTotalTransform().OfPoint(p));}
            else {var box=e.get_BoundingBox(view);if(box!=null)foreach(var p in ExportRegion.BoxPoints(box))Include(p);}
        }
        return min==null?null:new BoundingBoxXYZ{Min=min,Max=max!};
    }
}

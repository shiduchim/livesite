using Autodesk.Revit.DB;

namespace Offline3D.RevitExporter;

/// <summary>Link graphic settings are optional. Some supported model views do
/// not implement this API; retain the temporary view's By Host View rendering
/// instead of aborting geometry export. Element/link hides are copied separately.</summary>
internal static class LinkOverrideTransfer
{
    public static void Copy(Document doc,Autodesk.Revit.DB.View source,Autodesk.Revit.DB.View target,Action<string> warn)
    {
        if(!source.AreGraphicsOverridesAllowed()||!target.AreGraphicsOverridesAllowed()){
            warn("Link graphics overrides are unavailable in this view. Loaded visible links use By Host View rendering.");
            return;
        }
        bool canRead=true;
        foreach(var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()){
            RevitLinkGraphicsSettings? settings=null;
            if(canRead){
                try{
                    // A null instance override inherits its link-type settings.
                    settings=source.GetLinkOverrides(link.Id)??source.GetLinkOverrides(link.GetTypeId());
                }catch(Autodesk.Revit.Exceptions.InvalidOperationException){
                    canRead=false;
                    warn("The source view does not support link graphical overrides. Loaded visible links use By Host View rendering.");
                }catch(Autodesk.Revit.Exceptions.ArgumentException){
                    warn("A link graphics override could not be read. Using By Host View rendering for link "+link.Id+".");
                }
            }
            if(settings?.LinkVisibilityType==LinkVisibility.Custom){
                settings=null;
                warn("Custom link graphics cannot be transferred by this Revit API. Using By Host View rendering for link "+link.Id+".");
            }
            settings=settings??ByHost();
            try{target.SetLinkOverrides(link.Id,settings);}
            catch(Autodesk.Revit.Exceptions.InvalidOperationException){
                warn("The temporary 3D view does not support link graphical overrides. Its default By Host View rendering is retained.");
                return;
            }
            catch(Autodesk.Revit.Exceptions.ArgumentException){
                // Linked-view settings can be valid in a plan yet invalid in 3D.
                // Reset explicitly so a previous region's settings cannot leak.
                warn("A source link graphics override is incompatible with the temporary 3D view. Using By Host View rendering for link "+link.Id+".");
                try{target.SetLinkOverrides(link.Id,ByHost());}
                catch(Autodesk.Revit.Exceptions.InvalidOperationException){
                    warn("The temporary 3D view does not support link graphical overrides. Its default rendering is retained.");
                    return;
                }
                catch(Autodesk.Revit.Exceptions.ArgumentException){
                    warn("Link graphics could not be reset for link "+link.Id+". The temporary view's existing rendering is retained.");
                }
            }
        }
    }
    private static RevitLinkGraphicsSettings ByHost()=>new RevitLinkGraphicsSettings{LinkVisibilityType=LinkVisibility.ByHostView};
}

using System.Collections;
namespace Autodesk.Revit.Exceptions {
 public class InvalidOperationException:System.Exception {}
 public class ArgumentException:System.Exception {}
}
namespace Autodesk.Revit.DB {
 public enum LinkVisibility {ByHostView,ByLinkView,Custom}
 public sealed class ElementId {public int Value;public ElementId(int value){Value=value;}public override string ToString()=>Value.ToString();}
 public sealed class RevitLinkInstance {public ElementId Id;public RevitLinkInstance(int id){Id=new ElementId(id);}public ElementId GetTypeId()=>new ElementId(Id.Value+100);}
 public sealed class Document {public RevitLinkInstance[] Links={new(1),new(2)};}
 public sealed class RevitLinkGraphicsSettings {public LinkVisibility LinkVisibilityType{get;set;}}
 public sealed class View {
  public bool Allowed=true;public int Reads;public List<(int,LinkVisibility)> Writes=new();
  public Func<ElementId,RevitLinkGraphicsSettings?> Read=_=>null;public Action<ElementId,RevitLinkGraphicsSettings>? Write;
  public bool AreGraphicsOverridesAllowed()=>Allowed;
  public RevitLinkGraphicsSettings? GetLinkOverrides(ElementId id){Reads++;return Read(id);}
  public void SetLinkOverrides(ElementId id,RevitLinkGraphicsSettings value){Write?.Invoke(id,value);Writes.Add((id.Value,value.LinkVisibilityType));}
 }
 public sealed class FilteredElementCollector:IEnumerable {private readonly Document doc;public FilteredElementCollector(Document value){doc=value;}public FilteredElementCollector OfClass(Type t)=>this;public IEnumerator GetEnumerator()=>doc.Links.GetEnumerator();}
}

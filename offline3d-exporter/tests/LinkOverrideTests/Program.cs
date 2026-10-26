using Autodesk.Revit.DB;
using Offline3D.RevitExporter;
using Unsupported=Autodesk.Revit.Exceptions.InvalidOperationException;
using BadArgument=Autodesk.Revit.Exceptions.ArgumentException;
int count=0;
void Test(string name,Action f){f();Console.WriteLine("PASS "+name);count++;}
void Assert(bool b){if(!b)throw new Exception("Assertion failed");}
var doc=new Document();
Test("reported unsupported source-view exception falls back for every link",()=>{var source=new View{Read=_=>throw new Unsupported()};var target=new View();var warnings=new List<string>();LinkOverrideTransfer.Copy(doc,source,target,warnings.Add);Assert(source.Reads==1&&target.Writes.Count==2&&target.Writes.All(w=>w.Item2==LinkVisibility.ByHostView)&&warnings.Count==1);});
Test("unsupported target view does not abort geometry preparation",()=>{int calls=0;var target=new View{Write=(_,_)=>{calls++;throw new Unsupported();}};var warnings=new List<string>();LinkOverrideTransfer.Copy(doc,new View(),target,warnings.Add);Assert(calls==1&&warnings.Count==1);});
Test("absent instance overrides inherit type settings",()=>{var source=new View{Read=id=>id.Value>100?new(){LinkVisibilityType=LinkVisibility.ByLinkView}:null};var target=new View();LinkOverrideTransfer.Copy(doc,source,target,_=>{});Assert(source.Reads==4&&target.Writes.All(w=>w.Item2==LinkVisibility.ByLinkView));});
Test("absent instance and type overrides use nonnull By Host View settings",()=>{var target=new View();LinkOverrideTransfer.Copy(doc,new View(),target,_=>{});Assert(target.Writes.Count==2&&target.Writes.All(w=>w.Item2==LinkVisibility.ByHostView));});
Test("custom settings use documented API fallback",()=>{var target=new View();var warnings=new List<string>();LinkOverrideTransfer.Copy(doc,new View{Read=_=>new(){LinkVisibilityType=LinkVisibility.Custom}},target,warnings.Add);Assert(target.Writes.All(w=>w.Item2==LinkVisibility.ByHostView)&&warnings.Count==2);});
Test("incompatible plan-linked settings are reset before the next region",()=>{var target=new View{Write=(_,s)=>{if(s.LinkVisibilityType==LinkVisibility.ByLinkView)throw new BadArgument();}};var source=new View{Read=_=>new(){LinkVisibilityType=LinkVisibility.ByLinkView}};LinkOverrideTransfer.Copy(doc,source,target,_=>{});Assert(target.Writes.Count==2&&target.Writes.All(w=>w.Item2==LinkVisibility.ByHostView));});
Test("generic graphics capability rejection avoids link API calls",()=>{var source=new View{Allowed=false};var target=new View();LinkOverrideTransfer.Copy(doc,source,target,_=>{});Assert(source.Reads==0&&target.Writes.Count==0);});
Test("one invalid link does not drop other links",()=>{var source=new View{Read=id=>id.Value==1?throw new BadArgument():new(){LinkVisibilityType=LinkVisibility.ByLinkView}};var target=new View();LinkOverrideTransfer.Copy(doc,source,target,_=>{});Assert(target.Writes.Count==2&&target.Writes[0].Item2==LinkVisibility.ByHostView&&target.Writes[1].Item2==LinkVisibility.ByLinkView);});
Test("cancellation is not swallowed as an optional graphics failure",()=>{try{LinkOverrideTransfer.Copy(doc,new View{Read=_=>throw new OperationCanceledException()},new View(),_=>{});}catch(OperationCanceledException){return;}throw new Exception("Cancellation swallowed");});
Test("unexpected failures remain visible",()=>{try{LinkOverrideTransfer.Copy(doc,new View{Read=_=>throw new InvalidDataException()},new View(),_=>{});}catch(InvalidDataException){return;}throw new Exception("Unexpected error swallowed");});
Console.WriteLine(count+" link-override fallback checks passed with API test doubles; not native Revit execution.");

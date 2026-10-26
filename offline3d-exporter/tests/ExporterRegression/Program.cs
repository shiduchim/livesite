using System.Reflection;
using Offline3D.RevitExporter;
int passed=0;
void Check(string name,Func<bool> f){if(!f())throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
string payload=Path.GetFullPath(args[0]),local=AppContext.BaseDirectory;
string net48=Path.Combine(payload,"Exporters","2024-net48");
Check("one viewport without selection is automatic",()=>AutomaticPlanChoice.SelectIndex(new[]{21},Array.Empty<int>())==0);
Check("one viewport ignores titleblock or unrelated selection",()=>AutomaticPlanChoice.SelectIndex(new[]{21},new[]{99,100})==0);
Check("one viewport already selected is automatic",()=>AutomaticPlanChoice.SelectIndex(new[]{21},new[]{21})==0);
Check("multiple plans honor one selected viewport",()=>AutomaticPlanChoice.SelectIndex(new[]{21,22},new[]{99,22})==1);
Check("multiple plans without selection ask for a plan",()=>AutomaticPlanChoice.SelectIndex(new[]{21,22},Array.Empty<int>())==-1);
Check("multiple selected plans require chooser",()=>AutomaticPlanChoice.SelectIndex(new[]{21,22},new[]{21,22})==-1);
Check("no floor plans produce no automatic choice",()=>AutomaticPlanChoice.SelectIndex(Array.Empty<int>(),new[]{21})==-1);
Console.WriteLine(passed+" selection regression checks passed. Revit UI execution remains untested.");

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Usage: ExporterAssemblyAudit <folder with one sub-folder per variant: 2024-net48, 2025-net8.0-windows, ...>
// Reads metadata only; nothing is loaded or executed.
int passed = 0, failed = 0;
void Check(string name, bool ok) { if (ok) { passed++; Console.WriteLine("PASS " + name); } else { failed++; Console.WriteLine("FAIL " + name); } }
string root = Path.GetFullPath(args[0]);
var variants = new[] { ("2024", "net48", ".NETFramework,Version=v4.8"), ("2025", "net8.0-windows", ".NETCoreApp,Version=v8.0"), ("2025", "net10.0-windows", ".NETCoreApp,Version=v10.0"), ("2026", "net8.0-windows", ".NETCoreApp,Version=v8.0"), ("2026", "net10.0-windows", ".NETCoreApp,Version=v10.0"), ("2027", "net10.0-windows", ".NETCoreApp,Version=v10.0") };
var allowed = new HashSet<string>(StringComparer.Ordinal) { "RevitAPI", "RevitAPIUI", "mscorlib", "System", "System.Core", "System.Drawing", "System.Windows.Forms", "System.Runtime", "System.Collections", "System.ComponentModel.Primitives", "System.Diagnostics.Process", "System.Drawing.Common", "System.Drawing.Primitives", "System.Linq", "System.Memory", "System.Security.Cryptography", "System.Text.Encoding.Extensions", "System.Threading", "System.Windows.Forms.Primitives", "System.Runtime.InteropServices", "System.Collections.Concurrent", "System.ComponentModel", "System.Console", "System.Diagnostics.TraceSource", "Microsoft.Win32.Primitives", "System.ComponentModel.TypeConverter", "System.Collections.NonGeneric", "System.Private.Windows.Core", "PresentationCore", "WindowsBase", "System.Xaml", "Microsoft.Win32.Registry", "Microsoft.Win32.Primitives" };
var required = new[] { "Offline3D.RevitExporter.ExportCommand", "Offline3D.RevitExporter.RibbonApplication", "Offline3D.RevitExporter.ExportAvailability", "Offline3D.RevitExporter.RevitGeometryContext", "Offline3D.RevitExporter.GeometryStream", "Offline3D.RevitExporter.SceneWriter", "Offline3D.RevitExporter.MeshBuilder", "Offline3D.RevitExporter.FeatureEdgeBuilder", "Offline3D.RevitExporter.ExportOptionsDialog", "Offline3D.RevitExporter.ExportProgressDialog", "Offline3D.RevitExporter.ExportTrace", "Offline3D.Contract", "Offline3D.Registration", "Offline3D.ElementIdentity", "Offline3D.ExportVolume", "Offline3D.CropMeshSelection" };
foreach (var (year, framework, tfm) in variants)
{
    string id = year + "-" + framework, dir = Path.Combine(root, id), dll = Path.Combine(dir, "Offline3DPrototype.Revit" + year + ".dll");
    Check(id + " exporter DLL exists", File.Exists(dll));
    if (!File.Exists(dll)) continue;
    var extra = Directory.GetFiles(dir).Select(Path.GetFileName).Where(f => !f!.StartsWith("Offline3DPrototype.Revit" + year + ".", StringComparison.Ordinal)).ToList();
    Check(id + " folder holds only its exporter (no RevitAPI, JSON or Core DLLs): " + (extra.Count == 0 ? "ok" : string.Join(", ", extra)), extra.Count == 0);
    using var stream = File.OpenRead(dll); using var pe = new PEReader(stream);
    Check(id + " is a managed x64 DLL", pe.HasMetadata && pe.PEHeaders.CoffHeader.Machine == Machine.Amd64);
    var md = pe.GetMetadataReader();
    var asm = md.GetAssemblyDefinition();
    Check(id + " assembly identity Offline3DPrototype.Revit" + year + " 0.5.4.0", md.GetString(asm.Name) == "Offline3DPrototype.Revit" + year && asm.Version == new Version(0, 5, 4, 0));
    var refs = md.AssemblyReferences.Select(h => md.GetAssemblyReference(h)).Select(r => (Name: md.GetString(r.Name), r.Version)).ToList();
    Console.WriteLine("REFERENCES " + id + ": " + string.Join(", ", refs.Select(r => r.Name + "/" + r.Version)));
    Check(id + " has no Core, System.Text.Json, PdfSharp or NuGet assembly references", !refs.Any(r => r.Name.Contains("Json") || r.Name.Contains("Core.") || r.Name == "Core" || r.Name.StartsWith("PdfSharp") || r.Name.StartsWith("Offline3D")));
    Check(id + " references only Revit APIs and framework assemblies", refs.All(r => allowed.Contains(r.Name)));
    var api = refs.FirstOrDefault(r => r.Name == "RevitAPI");
    Check(id + " RevitAPI reference matches Revit " + year, api.Version != null && api.Version.Major == int.Parse(year) - 2000);
    string? target = null;
    foreach (var h in asm.GetCustomAttributes())
    {
        var a = md.GetCustomAttribute(h);
        if (a.Constructor.Kind != HandleKind.MemberReference) continue;
        var ctor = md.GetMemberReference((MemberReferenceHandle)a.Constructor);
        var type = md.GetTypeReference((TypeReferenceHandle)ctor.Parent);
        if (md.GetString(type.Name) != "TargetFrameworkAttribute") continue;
        var blob = md.GetBlobReader(a.Value); blob.ReadUInt16(); target = blob.ReadSerializedString();
    }
    Check(id + " targets " + tfm + " (found " + target + ")", target == tfm);
    var types = new HashSet<string>(md.TypeDefinitions.Select(h => md.GetTypeDefinition(h)).Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name)));
    foreach (var name in required) Check(id + " contains " + name, types.Contains(name));
    Check(id + " no longer contains the v0.5.2 MeshContext or SpoolSceneWriter", !types.Contains("Offline3D.RevitExporter.MeshContext") && !types.Contains("Offline3D.SpoolSceneWriter"));
}
Console.WriteLine(passed + " assembly audit checks passed, " + failed + " failed. Metadata only; not a Revit load test.");
return failed == 0 ? 0 : 1;

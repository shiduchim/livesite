using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace EKRibbon
{
    // Standard Revit API PushButtons expose AssemblyName and ClassName.
    // Use those values for ordinary external commands instead of trying to
    // post a potentially non-postable Autodesk.Windows ribbon-control ID.
    internal static class ExternalCommands
    {
        internal const string Prefix = "EXTERNAL:";
        static bool building;
        static readonly Dictionary<int,List<string>> buildInventory = new Dictionary<int,List<string>>();
        internal static void BeginBuild(){buildInventory.Clear();building=true;}
        internal static void EndBuild(){building=false;buildInventory.Clear();}
        internal static bool IsExternal(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }
        internal static string IdFor(string dll, string className)
        {
            return Prefix + Path.GetFileName(dll).ToUpperInvariant() + "|" + className.Trim();
        }
        static IEnumerable<string> YearManifests(int year)
        {
            foreach(string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                           Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) })
            {
                if(string.IsNullOrWhiteSpace(root))continue;
                var folder = Path.Combine(root, "Autodesk", "Revit", "Addins", year.ToString());
                if(!Directory.Exists(folder))continue;
                string[] files;
                try { files = Directory.GetFiles(folder, "*.addin", SearchOption.TopDirectoryOnly); }
                catch (Exception ex) { Log.ErrorOnce("Enumerate add-in folder " + folder, ex); continue; }
                foreach(string file in files)yield return file;
            }
        }
        static List<string> InstalledAssemblies(int year)
        {
            if(building && buildInventory.TryGetValue(year,out var cached))return cached;
            var result=ManifestPaths.Read(YearManifests(year), (where, ex) =>
                Log.ErrorOnce("Revit " + year + " " + where, ex));
            if(building)buildInventory[year]=result;
            return result;
        }
        internal static bool Resolve(CommandInfo ci, int year, out string dll, out string className)
        {
            dll = ""; className = ci?.ClassName ?? "";
            if(ci == null || !IsExternal(ci.Id) || string.IsNullOrWhiteSpace(className))return false;
            try
            {
                string original = ManifestPaths.Normalize(ci.AssemblyPath, null);
                string baseName = Path.GetFileName(original);
                if(string.IsNullOrWhiteSpace(baseName))return false;
                var local = InstalledAssemblies(year).Where(p =>
                    string.Equals(Path.GetFileName(p), baseName, StringComparison.OrdinalIgnoreCase) &&
                    !OtherYear(p, year)).ToList();
                // Never bind to an arbitrary DLL if duplicate registrations name
                // different files. An exact current-year registered path is safe.
                var exact = local.FirstOrDefault(p => string.Equals(p, original, StringComparison.OrdinalIgnoreCase));
                if(exact != null){dll = exact;return true;}
                if(local.Count == 1){dll = local[0];return true;}
                if(local.Count > 1)
                    throw new InvalidOperationException("Ambiguous assembly registrations: " + string.Join(" | ", local));
                // Preserve the existing dynamic-loader fallback, but reject paths
                // explicitly belonging to another supported year (including Bin/year).
                if(!File.Exists(original) || OtherYear(original,year))return false;
                dll = original;
                return true;
            }
            catch(Exception ex)
            {
                Log.ErrorOnce("Resolve external Revit " + year + " key=" + ci.Key +
                    " id=" + ci.Id + " assembly=" + ManifestPaths.Printable(ci.AssemblyPath), ex);
                return false;
            }
        }
        static bool OtherYear(string path, int year)
        {
            string normalized = path.Replace('/', '\\');
            foreach(int other in Enumerable.Range(2024,4))
                if(other != year && (normalized.IndexOf("\\" + other + "\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.IndexOf("\\Revit " + other + "\\", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace EKRibbon
{
    // No Revit/WPF dependencies: the exact production parser is regression tested.
    internal static class ManifestPaths
    {
        internal static string Printable(string value)
        {
            return string.Concat((value ?? "").Select(c => char.IsControl(c)
                ? "\\u" + ((int)c).ToString("X4") : c.ToString()));
        }

        internal static string Normalize(string raw, string baseDirectory)
        {
            string path = (raw ?? "").Trim();
            if (path.Length == 0) throw new ArgumentException("Assembly path is empty.");
            // Validate consistently on Framework 4.8 and modern .NET. Do not
            // guess at a repaired path or execute a differently named assembly.
            if (path.Any(c => c < 32 || c == '"' || c == '<' || c == '>' || c == '|'))
                throw new ArgumentException("Assembly path contains an invalid Windows path character: " + Printable(raw));
            if (path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0)
                throw new ArgumentException("Wildcards are not valid in an assembly path: " + Printable(raw));
            // Drive-relative paths depend on the process's current directory.
            if (path.Length >= 2 && path[1] == ':' &&
                (path.Length < 3 || (path[2] != '\\' && path[2] != '/')))
                throw new ArgumentException("Drive-relative assembly path: " + Printable(raw));
            // This call MUST be inside the caller's per-entry exception boundary.
            if (!Path.IsPathRooted(path))
            {
                if (string.IsNullOrWhiteSpace(baseDirectory))
                    throw new ArgumentException("Saved assembly path is not absolute: " + Printable(raw));
                path = Path.Combine(baseDirectory, path);
            }
            return Path.GetFullPath(path);
        }

        internal static List<string> Read(IEnumerable<string> manifests, Action<string, Exception> warning)
        {
            var result = new List<string>();
            foreach (string manifest in manifests)
            {
                XmlDocument doc;
                try
                {
                    doc = new XmlDocument { XmlResolver = null };
                    using (var reader = XmlReader.Create(manifest, new XmlReaderSettings {
                        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
                }
                catch (Exception ex) { warning("Read add-in manifest " + manifest, ex); continue; }
                int entry = 0;
                foreach (XmlNode node in doc.SelectNodes("/RevitAddIns/AddIn/Assembly"))
                {
                    entry++;
                    string raw = node.InnerText ?? "";
                    try
                    {
                        string path = Normalize(raw, Path.GetDirectoryName(manifest));
                        if (File.Exists(path)) result.Add(path);
                        else warning("Missing add-in assembly " + manifest + " entry=" + entry +
                            " raw=" + Printable(raw), new FileNotFoundException("Assembly file not found.", path));
                    }
                    catch (Exception ex)
                    {
                        // Includes IsPathRooted, Combine and GetFullPath. One bad
                        // entry must not stop later entries in this SAME manifest.
                        warning("Invalid add-in assembly " + manifest + " entry=" + entry +
                            " raw=" + Printable(raw), ex);
                    }
                }
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}

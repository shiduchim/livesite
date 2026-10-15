using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Xml;

namespace EKTriangle.Core
{
    /// <summary>A .addin manifest that loads the experimental EKGlyph / EKTreeGlyphProbe hook.</summary>
    public sealed class LegacyAddin
    {
        public int Year;
        public string Manifest;
        public string AssemblyPath;
        public string Folder;          // add-in folder that may be moved with the manifest (null = leave alone)
        public bool MachineWide;
        public string Note = "";

        public override string ToString() => Manifest + (Folder != null ? " (+ folder " + Folder + ")" : "");
    }

    public sealed class NativeStatus
    {
        public int Year;
        public bool RevitInstalled;
        public bool Installed;            // our manifest present and pointing at an existing loader
        public string Manifest;
        public string Conflict;           // reason native install is impossible, or null
        public List<LegacyAddin> Legacy = new List<LegacyAddin>();
    }

    /// <summary>Installs and removes the per-user Revit add-in manifest, and migrates the legacy test add-in.</summary>
    public sealed class AddinManager
    {
        static readonly HashSet<string> LegacyFolderFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EKGlyph.dll", "EKGlyphHook.dll", "glyph.ini", "EKTreeGlyphProbe2024.owner", "EKGlyph.pdb", "EKGlyphHook.pdb",
        };

        readonly Env _env;
        readonly Log _log;

        public AddinManager(Env env, Log log) { _env = env; _log = log; }

        public string ManifestPath(int year) => Path.Combine(_env.UserAddinsDir(year), Product.ManifestFileName);

        // ------------------------------------------------------------ status --

        public NativeStatus Inspect(int year)
        {
            var s = new NativeStatus { Year = year, Manifest = ManifestPath(year) };
            s.RevitInstalled = _env.FindRevitDir(year) != null;
            if (File.Exists(s.Manifest))
            {
                var info = ReadManifest(s.Manifest);
                if (info == null || !info.IsOurs || info.HasOtherAddins || info.IsLegacy)
                    s.Conflict = "a different add-in already uses the file name " + s.Manifest;
                else
                    s.Installed = info.AssemblyPath != null && File.Exists(info.AssemblyPath) &&
                                  File.Exists(Path.Combine(Path.GetDirectoryName(info.AssemblyPath), Product.NativeFileName));
            }
            foreach (string dir in new[] { _env.UserAddinsDir(year), _env.MachineAddinsDir(year) })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string manifest in Directory.GetFiles(dir, "*.addin"))
                {
                    var info = ReadManifest(manifest);
                    if (info == null || !info.IsOurs) continue;
                    if (dir == _env.MachineAddinsDir(year))
                        s.Conflict = "EK Triangle is installed for all users at " + manifest + "; ask the administrator to remove that registration first";
                    else if (info.HasOtherAddins || info.IsLegacy)
                        s.Conflict = "mixed add-in manifest requires review; left unchanged: " + manifest;
                    else if (info.AssemblyPath != null && File.Exists(info.AssemblyPath) &&
                             File.Exists(Path.Combine(Path.GetDirectoryName(info.AssemblyPath), Product.NativeFileName)))
                        s.Installed = true;
                }
            }
            s.Legacy.AddRange(FindLegacy(year, _env.UserAddinsDir(year), false));
            s.Legacy.AddRange(FindLegacy(year, _env.MachineAddinsDir(year), true));
            foreach (var l in s.Legacy)
                if (l.MachineWide && s.Conflict == null)
                    s.Conflict = l.Note == "mixed manifest requires administrator review"
                        ? "mixed legacy manifest requires review; unrelated add-ins left unchanged: " + l.Manifest
                        : "the experimental EKGlyph add-in is installed for all users at " + l.Manifest +
                          " - remove it (administrator), then run EK Triangle Setup again";
            return s;
        }

        sealed class ManifestInfo
        {
            public bool IsOurs, IsLegacy, HasOtherAddins;
            public string AssemblyPath;
        }

        static ManifestInfo ReadManifest(string path)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { return null; }
            var info = new ManifestInfo();
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var sr = new StringReader(text))
                using (var xr = XmlReader.Create(sr, settings))
                    doc.Load(xr);
                foreach (XmlNode addin in doc.SelectNodes("/RevitAddIns/AddIn"))
                {
                    string id = (addin.SelectSingleNode("AddInId")?.InnerText ?? addin.SelectSingleNode("ClientId")?.InnerText ?? "").Trim();
                    string cls = (addin.SelectSingleNode("FullClassName")?.InnerText ?? "").Trim();
                    string asm = (addin.SelectSingleNode("Assembly")?.InnerText ?? "").Trim();
                    bool ours = string.Equals(id, Product.AddInId, StringComparison.OrdinalIgnoreCase);
                    bool legacy = string.Equals(id, Product.LegacyAddInId, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(cls, Product.LegacyClass, StringComparison.Ordinal);
                    if (ours) info.IsOurs = true;
                    if (!ours && !legacy) info.HasOtherAddins = true;
                    if (string.Equals(id, Product.LegacyAddInId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(cls, Product.LegacyClass, StringComparison.Ordinal))
                        info.IsLegacy = true;
                    if ((info.IsOurs || info.IsLegacy) && asm.Length > 0 && info.AssemblyPath == null)
                    {
                        try
                        {
                            info.AssemblyPath = Path.IsPathRooted(asm) ? asm : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), asm));
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // A GUID in a comment or corrupt XML is not proof of ownership.
                return null;
            }
            return info;
        }

        List<LegacyAddin> FindLegacy(int year, string dir, bool machine)
        {
            var list = new List<LegacyAddin>();
            if (!Directory.Exists(dir)) return list;
            foreach (var m in Directory.GetFiles(dir, "*.addin"))
            {
                var info = ReadManifest(m);
                if (info == null || !info.IsLegacy || info.IsOurs) continue;
                if (info.HasOtherAddins)
                {
                    // Never move a manifest that also registers an unrelated app.
                    list.Add(new LegacyAddin { Year = year, Manifest = m, MachineWide = true,
                        Note = "mixed manifest requires administrator review" });
                    continue;
                }
                var l = new LegacyAddin { Year = year, Manifest = m, AssemblyPath = info.AssemblyPath, MachineWide = machine };
                if (info.AssemblyPath != null)
                {
                    string folder = Path.GetDirectoryName(info.AssemblyPath);
                    // Only a sub-folder directly inside this Addins folder that holds nothing but the known test files.
                    if (folder != null && Directory.Exists(folder) &&
                        string.Equals(Path.GetFullPath(Path.GetDirectoryName(folder)).TrimEnd('\\', '/'),
                                      Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        bool onlyKnown = Directory.GetDirectories(folder).Length == 0;
                        foreach (var f in Directory.GetFiles(folder))
                            if (!LegacyFolderFiles.Contains(Path.GetFileName(f))) onlyKnown = false;
                        if (onlyKnown) l.Folder = folder;
                        else l.Note = "folder " + folder + " contains other files and was left in place (inactive without its manifest)";
                    }
                }
                list.Add(l);
            }
            return list;
        }

        // ----------------------------------------------------------- install --

        /// <summary>
        /// Installs the manifest for one year. Moves (never deletes) legacy test
        /// manifests first and moves them back if the install does not complete.
        /// </summary>
        public ItemResult Install(int year, string loaderPath)
        {
            var s = Inspect(year);
            if (!s.RevitInstalled) return new ItemResult(ItemOutcome.NotApplicable, "Revit " + year + " not installed");
            if (s.Conflict != null) return new ItemResult(ItemOutcome.Skipped, "dialog triangles not installed: " + s.Conflict);
            if (!File.Exists(loaderPath)) return new ItemResult(ItemOutcome.Failed, "loader missing from setup: " + loaderPath);
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(loaderPath), Product.NativeFileName)))
                return new ItemResult(ItemOutcome.Failed, "native drawing DLL missing next to " + loaderPath);
            var running = _env.RevitBlocking(new[] { year });
            if (running.Count > 0) return new ItemResult(ItemOutcome.Blocked, "close " + Env.DescribeRunning(running));

            byte[] previousManifest = File.Exists(s.Manifest) ? File.ReadAllBytes(s.Manifest) : null;
            var moved = new List<KeyValuePair<string, string>>();
            string backupDir = null;
            try
            {
                if (s.Legacy.Count > 0)
                {
                    backupDir = Path.Combine(_env.LegacyBackupRoot, year + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + FileUtil.Nonce().Substring(0, 4));
                    Directory.CreateDirectory(backupDir);
                    var record = new StringBuilder();
                    record.Append("# Experimental EKGlyph add-in moved aside by EK Triangle ").Append(Product.Version).Append("\r\n");
                    record.Append("# To roll back: uninstall EK Triangle, then move these items back to the paths below.\r\n");
                    int i = 0;
                    foreach (var l in s.Legacy)
                    {
                        string mDest = Path.Combine(backupDir, "manifest" + i + "-" + Path.GetFileName(l.Manifest));
                        File.Move(l.Manifest, mDest);   // deactivates the old hook
                        moved.Add(new KeyValuePair<string, string>(mDest, l.Manifest));
                        record.Append("Manifest").Append(i).Append('=').Append(l.Manifest).Append("\r\n");
                        if (l.Folder != null)
                        {
                            string fDest = Path.Combine(backupDir, "folder" + i + "-" + Path.GetFileName(l.Folder));
                            Directory.Move(l.Folder, fDest);
                            moved.Add(new KeyValuePair<string, string>(fDest, l.Folder));
                            record.Append("Folder").Append(i).Append('=').Append(l.Folder).Append("\r\n");
                        }
                        if (l.Note.Length > 0) _log?.Warn("Revit " + year + ": " + l.Note);
                        i++;
                    }
                    File.WriteAllText(Path.Combine(backupDir, "moved-items.txt"), record.ToString(), Encoding.UTF8);
                    _log?.Info("Revit " + year + ": experimental EKGlyph add-in moved to " + backupDir);
                }

                // Deactivate renamed/duplicated owned manifests transactionally.
                // Keep a recoverable copy; never touch a mixed or foreign file.
                string userDir = _env.UserAddinsDir(year);
                if (Directory.Exists(userDir))
                foreach (string manifest in Directory.GetFiles(userDir, "*.addin"))
                {
                    if (string.Equals(manifest, ManifestPath(year), StringComparison.OrdinalIgnoreCase)) continue;
                    var info = ReadManifest(manifest);
                    if (info == null || !info.IsOurs || info.HasOtherAddins || info.IsLegacy) continue;
                    if (backupDir == null)
                    {
                        backupDir = Path.Combine(_env.LegacyBackupRoot, year + "-duplicates-" + FileUtil.Nonce());
                        Directory.CreateDirectory(backupDir);
                    }
                    string dest = Path.Combine(backupDir, FileUtil.Nonce() + "-" + Path.GetFileName(manifest));
                    File.Move(manifest, dest);
                    moved.Add(new KeyValuePair<string, string>(dest, manifest));
                    File.AppendAllText(Path.Combine(backupDir, "duplicate-paths.txt"), dest + " => " + manifest + "\r\n");
                }
                WriteManifest(year, loaderPath);
                var after = Inspect(year);
                if (!after.Installed || after.Legacy.Count > 0)
                    throw new IOException("manifest did not verify after writing");
            }
            catch (Exception ex)
            {
                // Put everything back the way it was.
                RemoveOurManifest(year);
                if (previousManifest != null)
                {
                    try
                    {
                        if (!File.Exists(s.Manifest)) FileUtil.WriteNewVerified(s.Manifest, previousManifest, FileUtil.Sha256(previousManifest));
                    }
                    catch (Exception rex) { _log?.Error("could not restore previous manifest: " + rex.Message); }
                }
                for (int i = moved.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (Directory.Exists(moved[i].Key)) Directory.Move(moved[i].Key, moved[i].Value);
                        else File.Move(moved[i].Key, moved[i].Value);
                    }
                    catch (Exception rex) { _log?.Error("Revit " + year + ": could not move back " + moved[i].Key + ": " + rex.Message); }
                }
                return new ItemResult(ItemOutcome.Failed, "dialog triangles not installed (" + ex.Message + "); previous add-ins left as they were");
            }
            string note = s.Legacy.Count > 0 ? " (experimental EKGlyph test add-in deactivated; kept in " + backupDir + ")" : "";
            return new ItemResult(s.Installed ? ItemOutcome.AlreadyDone : ItemOutcome.Done,
                (s.Installed ? "dialog triangles already installed (refreshed)" : "dialog triangles installed") + note);
        }

        void WriteManifest(int year, string loaderPath)
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<!-- Installed by EK Triangle " + Product.Version + ". Remove it with Windows Settings > Apps > EK Triangle > Uninstall. -->\r\n" +
                "<RevitAddIns>\r\n" +
                "  <AddIn Type=\"Application\">\r\n" +
                "    <Name>EK Triangle</Name>\r\n" +
                "    <Assembly>" + SecurityElement.Escape(Path.GetFullPath(loaderPath)) + "</Assembly>\r\n" +
                "    <FullClassName>" + Product.LoaderClass + "</FullClassName>\r\n" +
                "    <AddInId>" + Product.AddInId + "</AddInId>\r\n" +
                "    <VendorId>EKTriangle</VendorId>\r\n" +
                "    <VendorDescription>EK Triangle - triangle expand/collapse symbols</VendorDescription>\r\n" +
                "  </AddIn>\r\n" +
                "</RevitAddIns>\r\n";
            string path = ManifestPath(year);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            FileUtil.WriteTextAtomic(path, xml);
        }

        // --------------------------------------------------------- uninstall --

        bool RemoveOurManifest(int year)
        {
            string path = ManifestPath(year);
            var info = File.Exists(path) ? ReadManifest(path) : null;
            if (info == null || !info.IsOurs || info.HasOtherAddins || info.IsLegacy) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>Removes every manifest carrying our AddInId for that year (current user). Nothing else.</summary>
        public ItemResult Uninstall(int year)
        {
            string machineDir = _env.MachineAddinsDir(year);
            if (Directory.Exists(machineDir))
            foreach (string manifest in Directory.GetFiles(machineDir, "*.addin"))
            {
                var info = ReadManifest(manifest);
                if (info != null && info.IsOurs)
                    return new ItemResult(ItemOutcome.Failed, "all-users EK Triangle registration requires administrator review: " + manifest);
            }
            string dir = _env.UserAddinsDir(year);
            if (!Directory.Exists(dir)) return new ItemResult(ItemOutcome.AlreadyDone, "no add-in folder");
            var ours = new List<string>();
            foreach (var m in Directory.GetFiles(dir, "*.addin"))
            {
                var info = ReadManifest(m);
                if (info != null && info.IsOurs)
                {
                    if (info.HasOtherAddins || info.IsLegacy)
                        return new ItemResult(ItemOutcome.Failed, "mixed manifest NOT removed; unrelated add-ins preserved: " + m);
                    ours.Add(m);
                }
            }
            if (ours.Count == 0) return new ItemResult(ItemOutcome.AlreadyDone, "dialog triangles not installed");
            var running = _env.RevitBlocking(new[] { year });
            if (running.Count > 0) return new ItemResult(ItemOutcome.Blocked, "close " + Env.DescribeRunning(running));
            try
            {
                foreach (var m in ours) File.Delete(m);
            }
            catch (Exception ex)
            {
                return new ItemResult(ItemOutcome.Failed, "could not remove " + string.Join(", ", ours) + ": " + ex.Message);
            }
            _log?.Info("Revit " + year + ": removed " + string.Join(", ", ours));
            return new ItemResult(ItemOutcome.Done, "dialog triangles removed");
        }
    }
}

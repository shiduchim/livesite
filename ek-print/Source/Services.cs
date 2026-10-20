using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace EKPrint
{
    public enum QuickMultipleMode { Combine, Separate }
    public enum PdfNameMode { Item, ProjectItem, CustomNumbered }
    public enum DwgNameMode { Item, ProjectItem, CustomNumbered }

    public sealed class SettingsData
    {
        public string OutputFolder = "";
        public QuickMultipleMode QuickMultipleMode = QuickMultipleMode.Combine;
    }

    public static class SettingsStore
    {
        private static string FolderPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EKPrintCSharp");
            }
        }

        private static string SettingsPath { get { return Path.Combine(FolderPath, "settings.ini"); } }

        public static SettingsData Load()
        {
            SettingsData data = new SettingsData();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string[] lines = File.ReadAllLines(SettingsPath);
                    foreach (string raw in lines)
                    {
                        string line = raw ?? "";
                        int idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        string key = line.Substring(0, idx).Trim();
                        string value = line.Substring(idx + 1).Trim();
                        if (key.Equals("OutputFolder", StringComparison.OrdinalIgnoreCase))
                            data.OutputFolder = value;
                        else if (key.Equals("QuickMultipleMode", StringComparison.OrdinalIgnoreCase))
                            data.QuickMultipleMode = value.Equals("Separate", StringComparison.OrdinalIgnoreCase)
                                ? QuickMultipleMode.Separate : QuickMultipleMode.Combine;
                    }
                }
                else
                {
                    // Migrate the user's existing pyRevit EK Print settings on first use.
                    string oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EKTools", "QuickPrint");
                    string oldFolder = Path.Combine(oldDir, "settings.txt");
                    string oldMode = Path.Combine(oldDir, "quick_pdf_mode.txt");
                    if (File.Exists(oldFolder)) data.OutputFolder = File.ReadAllText(oldFolder).Trim();
                    if (File.Exists(oldMode))
                    {
                        string mode = File.ReadAllText(oldMode).Trim();
                        if (mode.Equals("separate", StringComparison.OrdinalIgnoreCase))
                            data.QuickMultipleMode = QuickMultipleMode.Separate;
                    }
                    Save(data);
                }
            }
            catch { }
            return data;
        }

        public static void Save(SettingsData data)
        {
            if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("OutputFolder=" + (data.OutputFolder ?? ""));
            sb.AppendLine("QuickMultipleMode=" + data.QuickMultipleMode.ToString());
            File.WriteAllText(SettingsPath, sb.ToString(), Encoding.UTF8);
        }

        public static bool EnsureOutputFolder(SettingsData data, System.Windows.Forms.IWin32Window owner)
        {
            if (!string.IsNullOrWhiteSpace(data.OutputFolder) && Directory.Exists(data.OutputFolder))
                return true;

            using (System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Choose the folder for EK PDF and DWG exports";
                dlg.ShowNewFolderButton = true;
                System.Windows.Forms.DialogResult result = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
                if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    return false;
                data.OutputFolder = dlg.SelectedPath;
                Save(data);
                return true;
            }
        }
    }

    public static class Dialogs
    {
        public static void Warn(string title, string message)
        {
            System.Windows.Forms.MessageBox.Show(message, title,
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
        }

        public static void Error(string title, string message)
        {
            System.Windows.Forms.MessageBox.Show(message, title,
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        }
    }

    public static class Shell
    {
        public static void OpenFolder(string folder)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch { }
        }
    }

    public static class FileNames
    {
        private static readonly Regex Invalid = new Regex(@"[<>:""/\\|?*]", RegexOptions.Compiled);

        public static string Safe(string text)
        {
            string value = string.IsNullOrWhiteSpace(text) ? "Revit" : text;
            value = Invalid.Replace(value, "_").Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(value) ? "Revit" : value;
        }

        public static string ProjectFileName(Document doc)
        {
            string title = doc == null ? "Revit" : (doc.Title ?? "Revit");
            if (title.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(0, title.Length - 4);
            return Safe(title);
        }

        public static string ItemName(View view)
        {
            ViewSheet sheet = view as ViewSheet;
            if (sheet != null)
                return Safe((sheet.SheetNumber ?? "Sheet") + " - " + (sheet.Name ?? "Sheet"));
            return Safe(view == null ? "View" : view.Name);
        }

        public static string ProjectItemName(Document doc, View view)
        {
            return Safe(ProjectFileName(doc) + " - " + ItemName(view));
        }
    }

    public static class RevitSelection
    {
        public static bool IsPrintable(View view)
        {
            if (view == null) return false;
            try { return !view.IsTemplate && view.CanBePrinted; }
            catch { return false; }
        }

        public static List<View> GetSelectedPrintableViews(UIDocument uidoc)
        {
            List<View> result = new List<View>();
            if (uidoc == null) return result;
            Document doc = uidoc.Document;
            HashSet<string> seen = new HashSet<string>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                View view = doc.GetElement(id) as View;
                if (!IsPrintable(view)) continue;
                string key = view.Id.ToString();
                if (seen.Add(key)) result.Add(view);
            }
            return SortViews(result);
        }

        public static List<View> GetAllPrintableViews(Document doc)
        {
            List<View> result = new List<View>();
            if (doc == null) return result;
            foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
            {
                if (IsPrintable(view)) result.Add(view);
            }
            return SortViews(result);
        }

        public static List<View> SortViews(IEnumerable<View> input)
        {
            return input.OrderBy(v => v is ViewSheet ? 0 : 1)
                .ThenBy(v => v is ViewSheet ? ((ViewSheet)v).SheetNumber : v.Name, NaturalStringComparer.Instance)
                .ThenBy(v => v.Name, NaturalStringComparer.Instance)
                .ToList();
        }
    }

    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();
        private static readonly Regex Parts = new Regex("(\\d+)", RegexOptions.Compiled);

        public int Compare(string x, string y)
        {
            string[] ax = Parts.Split((x ?? "").ToLowerInvariant());
            string[] ay = Parts.Split((y ?? "").ToLowerInvariant());
            int count = Math.Min(ax.Length, ay.Length);
            for (int i = 0; i < count; i++)
            {
                long nx, ny;
                bool ix = long.TryParse(ax[i], out nx);
                bool iy = long.TryParse(ay[i], out ny);
                int c;
                if (ix && iy) c = nx.CompareTo(ny);
                else c = string.Compare(ax[i], ay[i], StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
            return ax.Length.CompareTo(ay.Length);
        }
    }

    public sealed class SheetLocateResult
    {
        public List<ViewSheet> Sheets = new List<ViewSheet>();
        public List<string> UnplacedNames = new List<string>();
    }

    public static class SheetLocator
    {
        public static List<View> GetSelectedBrowserViews(UIDocument uidoc)
        {
            List<View> result = new List<View>();
            if (uidoc == null) return result;
            Document doc = uidoc.Document;
            HashSet<string> seen = new HashSet<string>();
            ICollection<ElementId> ids = uidoc.Selection.GetElementIds();
            if (ids == null) return result;

            foreach (ElementId id in ids)
            {
                try
                {
                    View view = doc.GetElement(id) as View;
                    if (view == null || view.IsTemplate) continue;
                    if (seen.Add(view.Id.ToString())) result.Add(view);
                }
                catch { }
            }
            return result;
        }

        public static SheetLocateResult FindHostSheets(Document doc, IList<View> selectedViews)
        {
            SheetLocateResult result = new SheetLocateResult();
            if (doc == null || selectedViews == null || selectedViews.Count == 0) return result;

            Dictionary<string, View> selectedById = new Dictionary<string, View>();
            HashSet<string> matchedIds = new HashSet<string>();
            HashSet<string> directSheetIds = new HashSet<string>();
            foreach (View view in selectedViews)
            {
                if (view == null) continue;
                string key = view.Id.ToString();
                if (!selectedById.ContainsKey(key)) selectedById.Add(key, view);
                ViewSheet direct = view as ViewSheet;
                if (direct != null)
                    directSheetIds.Add(key);
            }

            List<ViewSheet> printableSheets = new List<ViewSheet>();
            foreach (ViewSheet sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                if (RevitSelection.IsPrintable(sheet)) printableSheets.Add(sheet);
            }

            HashSet<string> selectedScheduleIds = new HashSet<string>();
            foreach (KeyValuePair<string, View> pair in selectedById)
            {
                if (pair.Value is ViewSchedule) selectedScheduleIds.Add(pair.Key);
            }

            Dictionary<string, HashSet<string>> schedulesBySheet = new Dictionary<string, HashSet<string>>();
            if (selectedScheduleIds.Count > 0)
            {
                foreach (ScheduleSheetInstance instance in new FilteredElementCollector(doc)
                    .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                {
                    try
                    {
                        string scheduleKey = instance.ScheduleId.ToString();
                        if (!selectedScheduleIds.Contains(scheduleKey)) continue;
                        string sheetKey = instance.OwnerViewId.ToString();
                        HashSet<string> set;
                        if (!schedulesBySheet.TryGetValue(sheetKey, out set))
                        {
                            set = new HashSet<string>();
                            schedulesBySheet[sheetKey] = set;
                        }
                        set.Add(scheduleKey);
                    }
                    catch { }
                }
            }

            HashSet<string> matchedSheetIds = new HashSet<string>();
            foreach (ViewSheet sheet in printableSheets)
            {
                string sheetKey = sheet.Id.ToString();
                HashSet<string> hits = new HashSet<string>();

                if (directSheetIds.Contains(sheetKey)) hits.Add(sheetKey);

                try
                {
                    foreach (ElementId placedId in sheet.GetAllPlacedViews())
                    {
                        string key = placedId.ToString();
                        if (selectedById.ContainsKey(key)) hits.Add(key);
                    }
                }
                catch { }

                HashSet<string> scheduleHits;
                if (schedulesBySheet.TryGetValue(sheetKey, out scheduleHits))
                {
                    foreach (string key in scheduleHits) hits.Add(key);
                }

                if (hits.Count > 0)
                {
                    foreach (string key in hits) matchedIds.Add(key);
                    matchedSheetIds.Add(sheetKey);
                }
            }

            result.Sheets = printableSheets
                .Where(sht => matchedSheetIds.Contains(sht.Id.ToString()))
                .OrderBy(sht => sht.SheetNumber, NaturalStringComparer.Instance)
                .ThenBy(sht => sht.Name, NaturalStringComparer.Instance)
                .ToList();

            result.UnplacedNames = selectedViews
                .Where(v => v != null && !matchedIds.Contains(v.Id.ToString()))
                .Select(v => v.Name ?? "Unnamed view")
                .OrderBy(n => n, NaturalStringComparer.Instance)
                .ToList();

            return result;
        }

        // Retained for compatibility with earlier EK Print C# builds.
        public static List<ViewSheet> FindSheetsFromCurrentSelection(UIDocument uidoc)
        {
            if (uidoc == null) return new List<ViewSheet>();
            return FindHostSheets(uidoc.Document, GetSelectedBrowserViews(uidoc)).Sheets;
        }
    }

    public sealed class PdfBatchResult
    {
        public int Printed;
        public bool Cancelled;
        public bool Combined;
        public List<string> Failures = new List<string>();
        public List<string> Notes = new List<string>();
    }

    public static class PdfExportService
    {
        public static PDFExportOptions CreateOptions(bool combine, string fileName)
        {
            PDFExportOptions options = new PDFExportOptions();
            options.Combine = combine;
            if (combine) options.FileName = FileNames.Safe(fileName);
            options.PaperFormat = ExportPaperFormat.Default;
            options.ViewLinksInBlue = false;
            options.HideReferencePlane = true;
            options.HideUnreferencedViewTags = true;
            options.MaskCoincidentLines = false;
            options.HideScopeBoxes = true;
            options.HideCropBoundaries = true;
            options.ReplaceHalftoneWithThinLines = false;
            options.StopOnError = true;

            try
            {
                MethodInfo method = options.GetType().GetMethod("SetExportInBackground", new Type[] { typeof(bool) });
                if (method != null) method.Invoke(options, new object[] { false });
            }
            catch { }
            return options;
        }

        public static string NameFor(Document doc, View view, PdfNameMode mode, string customBase, int index)
        {
            if (mode == PdfNameMode.Item) return FileNames.ItemName(view);
            if (mode == PdfNameMode.ProjectItem) return FileNames.ProjectItemName(doc, view);
            return FileNames.Safe((customBase ?? FileNames.ProjectFileName(doc)) + " " + index.ToString());
        }

        public static bool ExportSingle(Document doc, View view, string folder, bool includeProject)
        {
            string name = includeProject ? FileNames.ProjectItemName(doc, view) : FileNames.ItemName(view);
            return ExportCombined(doc, new List<View> { view }, folder, name);
        }

        public static bool ExportSingleNamed(Document doc, View view, string folder, string name)
        {
            return ExportCombined(doc, new List<View> { view }, folder, name);
        }

        // Fast native path. Used for single-item exports. Multi-item jobs use ExportCancellable.
        public static bool ExportCombined(Document doc, IList<View> views, string folder, string fileName)
        {
            if (views == null || views.Count == 0) return false;
            Directory.CreateDirectory(folder);
            string safe = FileNames.Safe(fileName);
            string target = Path.Combine(folder, safe + ".pdf");
            try
            {
                if (File.Exists(target)) File.Delete(target);
            }
            catch
            {
                Dialogs.Error("EK Print", "Could not replace the existing PDF.\n\nClose it if it is open, then try again:\n\n" + target);
                return false;
            }

            List<ElementId> ids = views.Select(v => v.Id).ToList();
            using (PDFExportOptions options = CreateOptions(true, safe))
            {
                return doc.Export(folder, ids, options);
            }
        }

        /// <summary>
        /// Multi-item PDF export.
        /// Combine: Revit itself builds the combined PDF in one export (no third-party code).
        ///          The file is exported to a temporary folder first, so the previous combined PDF
        ///          is only replaced after Revit has finished successfully.
        /// Separate: one item at a time with the progress window, so Cancel Print is honored
        ///          between sheets/views and completed PDFs are kept.
        /// </summary>
        public static PdfBatchResult ExportCancellable(Document doc, IList<View> views, string folder,
            PdfNameMode mode, string customBase, string combineName)
        {
            if (!string.IsNullOrWhiteSpace(combineName))
                return ExportCombinedBatch(doc, views, folder, combineName);

            PdfBatchResult result = new PdfBatchResult();
            if (views == null || views.Count == 0) return result;
            Directory.CreateDirectory(folder);

            List<string> names = BuildNames(doc, views, mode, customBase);
            if (views.Count != names.Count) throw new InvalidOperationException("PDF naming failed.");

            string work = Path.Combine(folder, ".EKPrint_work_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            List<KeyValuePair<string, string>> exported = new List<KeyValuePair<string, string>>();
            PdfProgressForm progress = null;

            try
            {
                if (views.Count > 1)
                {
                    progress = new PdfProgressForm(views.Count);
                    progress.Show();
                    progress.ShowStep(0, views.Count, "starting");
                }

                for (int i = 0; i < views.Count; i++)
                {
                    if (progress != null)
                    {
                        progress.ShowStep(i, views.Count, "printing " + names[i]);
                        if (progress.CancelRequested)
                        {
                            result.Cancelled = true;
                            break;
                        }
                    }

                    string stageBase = "EK_" + (i + 1).ToString("0000");
                    try
                    {
                        string stage = ExportOneRaw(doc, views[i], work, stageBase);
                        exported.Add(new KeyValuePair<string, string>(stage, names[i]));
                        result.Printed++;
                    }
                    catch (Exception ex)
                    {
                        result.Failures.Add(names[i] + ": " + ex.Message);
                    }

                    if (progress != null)
                    {
                        progress.ShowStep(i + 1, views.Count, names[i]);
                        if (progress.CancelRequested)
                        {
                            result.Cancelled = true;
                            break;
                        }
                    }
                }

                foreach (KeyValuePair<string, string> item in exported)
                {
                    try
                    {
                        Publish(item.Key, Path.Combine(folder, FileNames.Safe(item.Value) + ".pdf"));
                    }
                    catch (Exception ex)
                    {
                        result.Failures.Add("Could not save " + item.Value + ": " + ex.Message);
                        if (!result.Notes.Any(n => n.IndexOf("Recovery PDFs remain", StringComparison.OrdinalIgnoreCase) >= 0))
                            result.Notes.Add("Recovery PDFs remain in: " + work);
                    }
                }
            }
            finally
            {
                if (progress != null)
                {
                    try { progress.Close(); progress.Dispose(); } catch { }
                }

                try
                {
                    if (Directory.Exists(work))
                    {
                        string[] leftovers = Directory.GetFiles(work, "*.pdf", SearchOption.TopDirectoryOnly);
                        if (leftovers.Length == 0)
                            Directory.Delete(work, true);
                        else if (!result.Notes.Any(n => n.IndexOf("Recovery PDFs remain", StringComparison.OrdinalIgnoreCase) >= 0))
                            result.Notes.Add("Recovery PDFs remain in: " + work);
                    }
                }
                catch (Exception ex)
                {
                    result.Notes.Add("Could not remove temporary PDF files: " + ex.Message);
                }
            }

            return result;
        }

        // Combine mode: Revit's own combined export (PDFExportOptions.Combine = true) in a single call.
        private static PdfBatchResult ExportCombinedBatch(Document doc, IList<View> views, string folder, string combineName)
        {
            PdfBatchResult result = new PdfBatchResult();
            if (views == null || views.Count == 0) return result;
            Directory.CreateDirectory(folder);

            string target = Path.Combine(folder, FileNames.Safe(combineName) + ".pdf");
            string work = Path.Combine(folder, ".EKPrint_work_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                const string stageBase = "EK_combined";
                List<ElementId> ids = views.Select(v => v.Id).ToList();
                bool ok;
                using (PDFExportOptions options = CreateOptions(true, stageBase))
                {
                    ok = doc.Export(work, ids, options);
                }

                string staged = Path.Combine(work, stageBase + ".pdf");
                if (!ok || !File.Exists(staged) || new FileInfo(staged).Length <= 0)
                {
                    result.Failures.Add("Revit did not create the combined PDF. The previous PDF was not changed.");
                    return result;
                }

                try
                {
                    Publish(staged, target);
                    result.Printed = views.Count;
                    result.Combined = true;
                }
                catch
                {
                    // The old PDF is probably open in a viewer. Keep the new one next to it.
                    string alternate = UniquePdfPath(folder, FileNames.Safe(combineName));
                    File.Move(staged, alternate);
                    result.Printed = views.Count;
                    result.Combined = true;
                    result.Notes.Add("The existing PDF is open or locked, so the new PDF was saved as:\n" + Path.GetFileName(alternate));
                }
            }
            catch (Exception ex)
            {
                result.Failures.Add("Combined PDF export failed: " + ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); }
                catch { }
            }
            return result;
        }

        private static List<string> BuildNames(Document doc, IList<View> views, PdfNameMode mode, string customBase)
        {
            List<string> names = new List<string>();
            Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < views.Count; i++)
            {
                string raw = NameFor(doc, views[i], mode, customBase, i + 1);
                string baseName = FileNames.Safe(raw);
                string name = baseName;
                int suffix = 2;
                while (used.ContainsKey(name))
                {
                    name = FileNames.Safe(baseName + " (" + suffix.ToString() + ")");
                    suffix++;
                }
                used[name] = 1;
                names.Add(name);
            }
            return names;
        }

        private static string ExportOneRaw(Document doc, View view, string folder, string basename)
        {
            List<ElementId> ids = new List<ElementId> { view.Id };
            using (PDFExportOptions options = CreateOptions(true, basename))
            {
                bool ok = doc.Export(folder, ids, options);
                string output = Path.Combine(folder, basename + ".pdf");
                if (!ok || !File.Exists(output) || new FileInfo(output).Length <= 0)
                    throw new IOException("Revit did not create a PDF for this item.");
                return output;
            }
        }

        private static void Publish(string staged, string destination)
        {
            if (!File.Exists(staged)) throw new FileNotFoundException("Staged PDF is missing.", staged);
            if (File.Exists(destination))
            {
                File.Replace(staged, destination, null);
            }
            else
            {
                File.Move(staged, destination);
            }
        }

        private static string UniquePdfPath(string folder, string name)
        {
            string safe = FileNames.Safe(name);
            string path = Path.Combine(folder, safe + ".pdf");
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, safe + " (" + suffix.ToString() + ").pdf");
                suffix++;
            }
            return path;
        }
    }

    public static class DwgExportService
    {
        public static DWGExportOptions CreateOptions()
        {
            DWGExportOptions options = new DWGExportOptions();
            options.FileVersion = ACADVersion.R2013;
            options.TargetUnit = ExportUnit.Centimeter;
            options.SharedCoords = true;
            options.Colors = ExportColorMode.TrueColorPerView;
            options.MergedViews = true;
            options.HideReferencePlane = true;
            options.HideScopeBox = true;
            options.HideUnreferenceViewTags = true;
            options.PreserveCoincidentLines = false;
            return options;
        }

        public static string NameFor(Document doc, View view, DwgNameMode mode, string customBase, int index)
        {
            if (mode == DwgNameMode.Item) return FileNames.ItemName(view);
            if (mode == DwgNameMode.ProjectItem) return FileNames.ProjectItemName(doc, view);
            return FileNames.Safe((customBase ?? FileNames.ProjectFileName(doc)) + " " + index.ToString());
        }

        public static bool Export(Document doc, IList<View> views, string folder, DwgNameMode mode, string customBase)
        {
            if (views == null || views.Count == 0) return false;
            Directory.CreateDirectory(folder);
            List<string> failures = new List<string>();
            int index = 1;

            foreach (View view in views)
            {
                string name = NameFor(doc, view, mode, customBase, index);
                if (mode == DwgNameMode.CustomNumbered) index++;

                if (!ExportOne(doc, view, folder, name)) failures.Add(name);
                System.Windows.Forms.Application.DoEvents();
            }

            if (failures.Count > 0)
            {
                Dialogs.Warn("EK DWG", "Some DWGs could not be exported:\n\n" + string.Join("\n", failures.Take(12).ToArray()));
                return false;
            }
            return true;
        }

        private static bool ExportOne(Document doc, View view, string folder, string desiredName)
        {
            string temp = Path.Combine(Path.GetTempPath(), "EKDWG_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                ICollection<ElementId> ids = new List<ElementId> { view.Id };
                using (DWGExportOptions options = CreateOptions())
                {
                    bool ok = doc.Export(temp, desiredName, ids, options);
                    if (!ok) return false;
                }

                string[] dwgs = Directory.GetFiles(temp, "*.dwg", SearchOption.AllDirectories);
                if (dwgs.Length == 0) return false;

                string primary = dwgs.OrderByDescending(p => new FileInfo(p).Length).First();
                string target = Path.Combine(folder, FileNames.Safe(desiredName) + ".dwg");
                if (File.Exists(target)) File.Delete(target);
                File.Move(primary, target);

                // Keep DWG files only. This normally remains empty because MergedViews=true.
                foreach (string extra in dwgs)
                {
                    if (extra.Equals(primary, StringComparison.OrdinalIgnoreCase)) continue;
                    string extraTarget = Path.Combine(folder, Path.GetFileName(extra));
                    if (File.Exists(extraTarget)) File.Delete(extraTarget);
                    File.Move(extra, extraTarget);
                }
                return true;
            }
            catch (Exception ex)
            {
                Dialogs.Error("EK DWG", desiredName + " failed.\n\n" + ex.Message);
                return false;
            }
            finally
            {
                // Deletes PNG/PCP/PAT and every other sidecar. Only moved DWG files remain.
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace EKPrint
{
    public enum QuickMultipleMode { Combine, Separate }
    public enum PdfNameMode { Item, ProjectItem, CustomNumbered }
    public enum DwgNameMode { Item, ProjectItem, CustomNumbered }
    public enum DwgColorMode { Index, ObjectStyles, View }
    public enum DwgUnit { Foot, Inch, Meter, Centimeter, Millimeter }
    public enum DwgCoordinates { Shared, InternalOrigin }

    public sealed class SettingsData
    {
        public string OutputFolder = "";
        public QuickMultipleMode QuickMultipleMode = QuickMultipleMode.Combine;
        public DwgColorMode DwgColors = DwgColorMode.View;
        public DwgUnit DwgUnit = DwgUnit.Centimeter;
        public DwgCoordinates DwgCoordinates = DwgCoordinates.Shared;

        public SettingsData Clone()
        {
            return (SettingsData)MemberwiseClone();
        }
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
                        else if (key.Equals("DwgColors", StringComparison.OrdinalIgnoreCase))
                            data.DwgColors = ParseEnum(value, data.DwgColors);
                        else if (key.Equals("DwgUnit", StringComparison.OrdinalIgnoreCase))
                            data.DwgUnit = ParseEnum(value, data.DwgUnit);
                        else if (key.Equals("DwgCoordinates", StringComparison.OrdinalIgnoreCase))
                            data.DwgCoordinates = ParseEnum(value, data.DwgCoordinates);
                    }
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
            sb.AppendLine("DwgColors=" + data.DwgColors.ToString());
            sb.AppendLine("DwgUnit=" + data.DwgUnit.ToString());
            sb.AppendLine("DwgCoordinates=" + data.DwgCoordinates.ToString());
            File.WriteAllText(SettingsPath, sb.ToString(), Encoding.UTF8);
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            T parsed;
            if (Enum.TryParse(value, true, out parsed) && Enum.IsDefined(typeof(T), parsed)) return parsed;
            return fallback;
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
    }

    public sealed class PdfBatchResult
    {
        public int Printed;
        public bool Cancelled;
        public bool Combined;
        public List<string> Failures = new List<string>();
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Local working folder (%TEMP%\EKPrint). Revit always writes here first, so a cancelled or failed
    /// export never touches the save folder, and network / OneDrive folders only receive finished files.
    /// </summary>
    public static class Staging
    {
        public static string Root { get { return Path.Combine(Path.GetTempPath(), "EKPrint"); } }

        public static string NewFolder(string prefix)
        {
            Sweep();
            string dir = Path.Combine(Root, prefix + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void Delete(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        // Removes folders left behind if Revit closed in the middle of an export.
        private static void Sweep()
        {
            try
            {
                if (!Directory.Exists(Root)) return;
                DateTime cutoff = DateTime.UtcNow.AddHours(-12);
                foreach (string dir in Directory.GetDirectories(Root))
                {
                    try { if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, true); }
                    catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>Paper sizes offered by Select PDF. "Use sheet size" is Revit's ExportPaperFormat.Default.</summary>
    public sealed class PaperChoice
    {
        public readonly string Label;
        public readonly ExportPaperFormat Format;

        private PaperChoice(string label, ExportPaperFormat format)
        {
            Label = label;
            Format = format;
        }

        public bool UsesSheetSize { get { return Format == ExportPaperFormat.Default; } }
        public override string ToString() { return Label; }

        public static readonly PaperChoice SheetSize = new PaperChoice("Use sheet size", ExportPaperFormat.Default);

        public static readonly PaperChoice[] All = new PaperChoice[]
        {
            SheetSize,
            new PaperChoice("A4  (210 \u00D7 297 mm)", ExportPaperFormat.ISO_A4),
            new PaperChoice("A3  (297 \u00D7 420 mm)", ExportPaperFormat.ISO_A3),
            new PaperChoice("A2  (420 \u00D7 594 mm)", ExportPaperFormat.ISO_A2),
            new PaperChoice("A1  (594 \u00D7 841 mm)", ExportPaperFormat.ISO_A1),
            new PaperChoice("A0  (841 \u00D7 1189 mm)", ExportPaperFormat.ISO_A0),
            new PaperChoice("B4  (250 \u00D7 353 mm)", ExportPaperFormat.ISO_B4),
            new PaperChoice("B3  (353 \u00D7 500 mm)", ExportPaperFormat.ISO_B3),
            new PaperChoice("B2  (500 \u00D7 707 mm)", ExportPaperFormat.ISO_B2),
            new PaperChoice("B1  (707 \u00D7 1000 mm)", ExportPaperFormat.ISO_B1),
            new PaperChoice("ANSI A / Letter  (8.5 \u00D7 11 in)", ExportPaperFormat.ANSI_A),
            new PaperChoice("ANSI B / Tabloid  (11 \u00D7 17 in)", ExportPaperFormat.ANSI_B),
            new PaperChoice("ANSI C  (17 \u00D7 22 in)", ExportPaperFormat.ANSI_C),
            new PaperChoice("ANSI D  (22 \u00D7 34 in)", ExportPaperFormat.ANSI_D),
            new PaperChoice("ANSI E  (34 \u00D7 44 in)", ExportPaperFormat.ANSI_E),
            new PaperChoice("ARCH A  (9 \u00D7 12 in)", ExportPaperFormat.ARCH_A),
            new PaperChoice("ARCH B  (12 \u00D7 18 in)", ExportPaperFormat.ARCH_B),
            new PaperChoice("ARCH C  (18 \u00D7 24 in)", ExportPaperFormat.ARCH_C),
            new PaperChoice("ARCH D  (24 \u00D7 36 in)", ExportPaperFormat.ARCH_D),
            new PaperChoice("ARCH E  (36 \u00D7 48 in)", ExportPaperFormat.ARCH_E),
            new PaperChoice("ARCH E1  (30 \u00D7 42 in)", ExportPaperFormat.ARCH_E1),
            new PaperChoice("ARCH E2  (26 \u00D7 38 in)", ExportPaperFormat.ARCH_E2),
            new PaperChoice("ARCH E3  (27 \u00D7 39 in)", ExportPaperFormat.ARCH_E3)
        };
    }

    /// <summary>
    /// PDF export through Revit's own PDF engine (Document.Export + PDFExportOptions, Revit 2022+).
    /// No printer driver and no third-party code. Every job:
    ///  * exports into a local staging folder and only then copies the finished PDF to the save folder,
    ///  * shows a progress window (multi-item jobs at once, single items only if they take a while),
    ///  * honors Cancel Print while Revit is exporting, through Revit's ProgressChanged event.
    /// Combine: Revit builds the combined PDF itself in a single export (fastest, one file, vector).
    /// Separate: one export per item, each PDF is saved as soon as it is finished.
    /// </summary>
    public static class PdfExportService
    {
        private const int SingleItemWindowDelayMs = 1500;

        internal static PDFExportOptions CreateOptions(string stagedName, ExportPaperFormat paper)
        {
            PDFExportOptions options = new PDFExportOptions();
            // Each Export call writes exactly one file; it gets its real name when it is published.
            options.Combine = true;
            options.FileName = stagedName;
            options.PaperFormat = paper;
            if (paper != ExportPaperFormat.Default)
            {
                // A fixed paper size: the sheet is scaled to fit the page, centered, rotated as needed.
                // (With the default "use sheet size" Revit ignores the zoom settings.)
                options.ZoomType = ZoomType.FitToPage;
                options.PaperPlacement = PaperPlacementType.Center;
                options.PaperOrientation = PageOrientationType.Auto;
            }
            // Same output as Revit's Export > PDF: vector graphics, full color, high raster quality.
            options.AlwaysUseRaster = false;
            options.ColorDepth = ColorDepthType.Color;
            options.RasterQuality = RasterQualityType.High;
            options.ViewLinksInBlue = false;
            options.HideReferencePlane = true;
            options.HideUnreferencedViewTags = true;
            options.MaskCoincidentLines = false;
            options.HideScopeBoxes = true;
            options.HideCropBoundaries = true;
            options.ReplaceHalftoneWithThinLines = false;
            options.StopOnError = true;

            // Revit 2025+: keep the export in the foreground, so the PDF exists when Export returns
            // and Cancel Print can reach it.
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

        /// <summary>One PDF holding every view, in the given order. Also used for single-item exports.</summary>
        public static PdfBatchResult ExportCombined(Document doc, IList<View> views, string folder, string fileName)
        {
            return ExportCombined(doc, views, folder, fileName, ExportPaperFormat.Default);
        }

        public static PdfBatchResult ExportCombined(Document doc, IList<View> views, string folder, string fileName,
            ExportPaperFormat paper)
        {
            PdfBatchResult result = new PdfBatchResult();
            if (views == null || views.Count == 0) return result;
            string name = FileNames.Safe(fileName);
            int total = views.Count;
            bool existed = File.Exists(Path.Combine(folder, name + ".pdf"));

            string title = total > 1 ? "Exporting combined PDF" : "Exporting PDF";
            string note = existed ? "Cancel stops the export. The existing PDF is not changed."
                                  : "Cancel stops the export. No PDF is saved.";
            using (PdfJob job = new PdfJob(doc, paper, title, note, total > 1 ? 0 : SingleItemWindowDelayMs))
            {
                job.SetStep(0, 1, total > 1 ? total.ToString() + " pages" : "", name + ".pdf");

                string staged = null;
                string error = null;
                try { staged = job.Export(views, "EK_combined"); }
                catch (Exception ex) { error = ex.Message; }

                if (job.CancelRequested)
                {
                    result.Cancelled = true;
                    result.Notes.Add("Print cancelled. No PDF was saved" +
                        (existed ? "; the existing " + name + ".pdf was not changed." : "."));
                    return result;
                }
                if (staged == null)
                {
                    result.Failures.Add(name + ".pdf: " + (error ?? "Revit did not create the PDF.") +
                        (existed ? "\nThe existing PDF was not changed." : ""));
                    return result;
                }

                job.SetStep(1, 1, total > 1 ? total.ToString() + " pages" : "", "Saving " + name + ".pdf");
                if (Publish(staged, folder, name, result) != null)
                {
                    result.Printed = total;
                    result.Combined = total > 1;
                }
            }
            return result;
        }

        /// <summary>One PDF per view. Cancel keeps the PDFs that are already finished.</summary>
        public static PdfBatchResult ExportSeparate(Document doc, IList<View> views, string folder,
            PdfNameMode mode, string customBase)
        {
            return ExportSeparate(doc, views, folder, mode, customBase, ExportPaperFormat.Default);
        }

        public static PdfBatchResult ExportSeparate(Document doc, IList<View> views, string folder,
            PdfNameMode mode, string customBase, ExportPaperFormat paper)
        {
            PdfBatchResult result = new PdfBatchResult();
            if (views == null || views.Count == 0) return result;
            List<string> names = BuildNames(doc, views, mode, customBase);
            int total = views.Count;

            using (PdfJob job = new PdfJob(doc, paper, total > 1 ? "Exporting PDFs" : "Exporting PDF",
                "Cancel stops the export. PDFs already finished are kept.", total > 1 ? 0 : SingleItemWindowDelayMs))
            {
                for (int i = 0; i < total; i++)
                {
                    if (job.CancelRequested) break;
                    job.SetStep(i, total, (i + 1).ToString() + " of " + total.ToString(), names[i] + ".pdf");

                    string staged = null;
                    string error = null;
                    try { staged = job.Export(new List<View> { views[i] }, "EK_" + (i + 1).ToString("0000")); }
                    catch (Exception ex) { error = ex.Message; }

                    if (staged == null)
                    {
                        if (job.CancelRequested) break;   // stopped in the middle of this item
                        result.Failures.Add(names[i] + ".pdf: " + (error ?? "Revit did not create a PDF for this item."));
                        continue;
                    }

                    // A finished item is kept even if Cancel was clicked while it was exporting.
                    if (Publish(staged, folder, names[i], result) != null) result.Printed++;
                    try { File.Delete(staged); } catch { }
                }

                if (job.CancelRequested)
                {
                    result.Cancelled = true;
                    result.Notes.Insert(0, "Print cancelled. " + result.Printed.ToString() + " of " + total.ToString() +
                        " PDFs were saved; the rest were skipped.");
                }
            }
            return result;
        }

        private static List<string> BuildNames(Document doc, IList<View> views, PdfNameMode mode, string customBase)
        {
            List<string> names = new List<string>();
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < views.Count; i++)
            {
                string baseName = FileNames.Safe(NameFor(doc, views[i], mode, customBase, i + 1));
                string name = baseName;
                int suffix = 2;
                while (used.Contains(name))
                {
                    name = FileNames.Safe(baseName + " (" + suffix.ToString() + ")");
                    suffix++;
                }
                used.Add(name);
                names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// Copies a finished PDF from the staging folder into the save folder as name.pdf.
        /// If name.pdf is open in a viewer, the old file is kept and the new one is saved as "name (2).pdf".
        /// Returns the saved path, or null after recording a failure.
        /// </summary>
        private static string Publish(string staged, string folder, string name, PdfBatchResult result)
        {
            string target = Path.Combine(folder, name + ".pdf");
            string part = Path.Combine(folder, "~EKPrint_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                Directory.CreateDirectory(folder);
                File.Copy(staged, part, true);

                if (File.Exists(target))
                {
                    try { File.Delete(target); }
                    catch
                    {
                        string alternate = UniquePdfPath(folder, name);
                        File.Move(part, alternate);
                        result.Notes.Add(name + ".pdf is open in another program, so the new PDF was saved as:\n" +
                            Path.GetFileName(alternate));
                        return alternate;
                    }
                }
                File.Move(part, target);
                return target;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                result.Failures.Add("Could not save " + name + ".pdf: " + ex.Message);
                return null;
            }
        }

        private static string UniquePdfPath(string folder, string name)
        {
            string path = Path.Combine(folder, name + ".pdf");
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, name + " (" + suffix.ToString() + ").pdf");
                suffix++;
            }
            return path;
        }
    }

    /// <summary>
    /// One PDF job: its staging folder, its progress window, and the ProgressChanged hook that passes
    /// Cancel Print on to Revit while Document.Export is running.
    /// </summary>
    internal sealed class PdfJob : IDisposable
    {
        private readonly Document _doc;
        private readonly Autodesk.Revit.ApplicationServices.Application _app;
        private readonly ExportProgress _progress;
        private readonly string _stage;
        private readonly ExportPaperFormat _paper;

        public PdfJob(Document doc, ExportPaperFormat paper, string title, string note, int windowDelayMs)
        {
            _doc = doc;
            _paper = paper;
            _stage = Staging.NewFolder("pdf");
            _progress = ExportProgress.Start(title, note, windowDelayMs);
            try
            {
                _app = doc.Application;
                _app.ProgressChanged += OnRevitProgress;
            }
            catch { _app = null; }
        }

        public bool CancelRequested { get { return _progress != null && _progress.CancelRequested; } }

        public void SetStep(int done, int total, string counter, string label)
        {
            if (_progress != null) _progress.SetStep(done, total, counter, label);
        }

        /// <summary>Exports the views into one staged PDF. Returns its path, or null if Revit made no file.</summary>
        public string Export(IList<View> views, string baseName)
        {
            string output = Path.Combine(_stage, baseName + ".pdf");
            List<ElementId> ids = views.Select(v => v.Id).ToList();
            bool ok;
            using (PDFExportOptions options = PdfExportService.CreateOptions(baseName, _paper))
                ok = _doc.Export(_stage, ids, options);
            if (!ok || !File.Exists(output) || new FileInfo(output).Length <= 0) return null;
            return output;
        }

        private void OnRevitProgress(object sender, ProgressChangedEventArgs e)
        {
            if (_progress == null) return;
            try
            {
                if (_progress.CancelRequested)
                {
                    // Revit accepts Cancel only in these stages; otherwise ask again on the next event.
                    if (e.Stage == ProgressStage.Unchanged || (e.Stage == ProgressStage.PositionChanged && e.Cancellable))
                        e.Cancel();
                    return;
                }
                if (e.Stage == ProgressStage.PositionChanged && e.UpperRange > 0)
                    _progress.SetItemFraction((double)e.Position / e.UpperRange);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_app != null)
            {
                try { _app.ProgressChanged -= OnRevitProgress; } catch { }
            }
            if (_progress != null) _progress.Close();
            Staging.Delete(_stage);
        }
    }

    public static class DwgExportService
    {
        public static DWGExportOptions CreateOptions(SettingsData settings)
        {
            DWGExportOptions options = new DWGExportOptions();
            options.FileVersion = ACADVersion.R2013;
            options.TargetUnit = ToRevit(settings.DwgUnit);
            options.SharedCoords = settings.DwgCoordinates == DwgCoordinates.Shared;
            options.Colors = ToRevit(settings.DwgColors);
            options.MergedViews = true;
            options.HideReferencePlane = true;
            options.HideScopeBox = true;
            options.HideUnreferenceViewTags = true;
            options.PreserveCoincidentLines = false;
            return options;
        }

        private static ExportUnit ToRevit(DwgUnit unit)
        {
            switch (unit)
            {
                case DwgUnit.Foot: return ExportUnit.Foot;
                case DwgUnit.Inch: return ExportUnit.Inch;
                case DwgUnit.Meter: return ExportUnit.Meter;
                case DwgUnit.Millimeter: return ExportUnit.Millimeter;
                default: return ExportUnit.Centimeter;
            }
        }

        private static ExportColorMode ToRevit(DwgColorMode colors)
        {
            switch (colors)
            {
                case DwgColorMode.Index: return ExportColorMode.IndexColors;
                case DwgColorMode.ObjectStyles: return ExportColorMode.TrueColor;
                default: return ExportColorMode.TrueColorPerView;
            }
        }

        public static string UnitName(DwgUnit unit)
        {
            switch (unit)
            {
                case DwgUnit.Foot: return "Feet";
                case DwgUnit.Inch: return "Inches";
                case DwgUnit.Meter: return "Meters";
                case DwgUnit.Millimeter: return "Millimeters";
                default: return "Centimeters";
            }
        }

        /// <summary>Short description of the DWG settings, e.g. for the Select DWG dialog.</summary>
        public static string Describe(SettingsData settings)
        {
            string colors = settings.DwgColors == DwgColorMode.Index ? "Index colors"
                : (settings.DwgColors == DwgColorMode.ObjectStyles ? "Object style colors" : "View colors");
            string coords = settings.DwgCoordinates == DwgCoordinates.Shared ? "Shared coordinates" : "Internal origin";
            return "AutoCAD 2013  \u00B7  " + UnitName(settings.DwgUnit) + "  \u00B7  " + coords + "  \u00B7  " + colors +
                "  \u00B7  Views on sheets merged";
        }

        public static string NameFor(Document doc, View view, DwgNameMode mode, string customBase, int index)
        {
            if (mode == DwgNameMode.Item) return FileNames.ItemName(view);
            if (mode == DwgNameMode.ProjectItem) return FileNames.ProjectItemName(doc, view);
            return FileNames.Safe((customBase ?? FileNames.ProjectFileName(doc)) + " " + index.ToString());
        }

        public static bool Export(Document doc, IList<View> views, string folder, DwgNameMode mode, string customBase,
            SettingsData settings)
        {
            if (views == null || views.Count == 0) return false;
            Directory.CreateDirectory(folder);
            List<string> failures = new List<string>();
            int index = 1;

            foreach (View view in views)
            {
                string name = NameFor(doc, view, mode, customBase, index);
                if (mode == DwgNameMode.CustomNumbered) index++;

                if (!ExportOne(doc, view, folder, name, settings)) failures.Add(name);
                System.Windows.Forms.Application.DoEvents();
            }

            if (failures.Count > 0)
            {
                Dialogs.Warn("EK DWG", "Some DWGs could not be exported:\n\n" + string.Join("\n", failures.Take(12).ToArray()));
                return false;
            }
            return true;
        }

        private static bool ExportOne(Document doc, View view, string folder, string desiredName, SettingsData settings)
        {
            string temp = Staging.NewFolder("dwg");
            try
            {
                ICollection<ElementId> ids = new List<ElementId> { view.Id };
                using (DWGExportOptions options = CreateOptions(settings))
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

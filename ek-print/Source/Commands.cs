using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace EKPrint
{
    public static class CommandHelpers
    {
        public static Result FinishBatch(string title, PdfBatchResult status, string folder, IList<string> extraMessages)
        {
            // Notes come first: they hold the "Print cancelled" line and renamed-file notices.
            List<string> messages = new List<string>();
            messages.AddRange(status.Notes.Where(m => !string.IsNullOrWhiteSpace(m)));
            if (status.Failures.Count > 0)
            {
                string body = "Some PDFs failed:\n" + string.Join("\n", status.Failures.Take(12).ToArray());
                if (status.Failures.Count > 12)
                    body += "\n...and " + (status.Failures.Count - 12).ToString() + " more.";
                messages.Add(body);
            }
            if (extraMessages != null)
                messages.AddRange(extraMessages.Where(m => !string.IsNullOrWhiteSpace(m)));

            if (messages.Count > 0)
                TaskDialog.Show(title, string.Join("\n\n", messages.ToArray()));

            if (status.Printed > 0)
            {
                Shell.OpenFolder(folder);
                return Result.Succeeded;
            }
            return Result.Cancelled;
        }

        public static string UnplacedMessage(IList<string> names)
        {
            if (names == null || names.Count == 0) return "";
            List<string> shown = names.Take(12).ToList();
            string message = "Selected views not found on printable sheets (" + names.Count.ToString() + "):\n" + string.Join("\n", shown.ToArray());
            if (names.Count > shown.Count)
                message += "\n...and " + (names.Count - shown.Count).ToString() + " more.";
            return message;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class QuickPdfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null) return Result.Cancelled;
                Document doc = uidoc.Document;

                SettingsData settings = SettingsStore.Load();
                if (!SettingsStore.EnsureOutputFolder(settings, null)) return Result.Cancelled;

                List<View> views = RevitSelection.GetSelectedPrintableViews(uidoc);
                if (views.Count == 0)
                {
                    View active = doc.ActiveView;
                    if (!RevitSelection.IsPrintable(active))
                    {
                        Dialogs.Warn("EK Quick PDF", "Select printable sheets/views, or open a printable view or sheet.");
                        return Result.Cancelled;
                    }
                    views.Add(active);
                }

                views = RevitSelection.SortViews(views);
                PdfBatchResult status;
                if (views.Count == 1)
                    status = PdfExportService.ExportCombined(doc, views, settings.OutputFolder, FileNames.ProjectItemName(doc, views[0]));
                else if (settings.QuickMultipleMode == QuickMultipleMode.Combine)
                    status = PdfExportService.ExportCombined(doc, views, settings.OutputFolder, FileNames.ProjectFileName(doc));
                else
                    status = PdfExportService.ExportSeparate(doc, views, settings.OutputFolder, PdfNameMode.ProjectItem, null);
                return CommandHelpers.FinishBatch("EK Quick PDF", status, settings.OutputFolder, null);
            }
            catch (Exception ex)
            {
                Dialogs.Error("EK Quick PDF", "Quick PDF failed.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SelectPdfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null) return Result.Cancelled;
                Document doc = uidoc.Document;

                SettingsData settings = SettingsStore.Load();
                if (!SettingsStore.EnsureOutputFolder(settings, null)) return Result.Cancelled;

                List<View> all = RevitSelection.GetAllPrintableViews(doc);
                if (all.Count == 0)
                {
                    Dialogs.Warn("EK Select PDF", "No printable sheets or views were found in this project.");
                    return Result.Cancelled;
                }

                using (PdfPickerForm form = new PdfPickerForm(all, doc.ActiveView, FileNames.ProjectFileName(doc), settings))
                {
                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // The save folder may have been changed through the dialog's Settings link.
                    settings = SettingsStore.Load();
                    if (!SettingsStore.EnsureOutputFolder(settings, null)) return Result.Cancelled;

                    PdfBatchResult status;
                    if (form.Combine)
                        status = PdfExportService.ExportCombined(doc, form.SelectedViews, settings.OutputFolder,
                            form.PdfName, form.Paper);
                    else if (form.SelectedViews.Count == 1)
                        status = PdfExportService.ExportCombined(doc, form.SelectedViews, settings.OutputFolder,
                            PdfExportService.NameFor(doc, form.SelectedViews[0], form.NameMode, form.PdfName, 1), form.Paper);
                    else
                        status = PdfExportService.ExportSeparate(doc, form.SelectedViews, settings.OutputFolder,
                            form.NameMode, form.PdfName, form.Paper);
                    return CommandHelpers.FinishBatch("EK Select PDF", status, settings.OutputFolder, null);
                }
            }
            catch (Exception ex)
            {
                Dialogs.Error("EK Select PDF", "PDF export failed.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SheetPrintCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null) return Result.Cancelled;
                Document doc = uidoc.Document;

                List<View> selectedViews = SheetLocator.GetSelectedBrowserViews(uidoc);
                if (selectedViews.Count == 0)
                {
                    ICollection<ElementId> rawSelection = uidoc.Selection.GetElementIds();
                    if (rawSelection != null && rawSelection.Count > 0)
                    {
                        Dialogs.Warn("EK Sheet Print",
                            "No selected Revit views were found.\n\nSelect view names (not model objects) in the Project Browser, then click Sheet Print.");
                        return Result.Cancelled;
                    }

                    View active = doc.ActiveView;
                    if (active == null || active.IsTemplate)
                    {
                        Dialogs.Warn("EK Sheet Print", "No printable views are selected or active.\n\nSelect views in the Project Browser.");
                        return Result.Cancelled;
                    }
                    selectedViews.Add(active);
                }

                SheetLocateResult located = SheetLocator.FindHostSheets(doc, selectedViews);
                if (located.Sheets.Count == 0)
                {
                    string msg = "No printable sheets contain the selected/current view.";
                    string unplaced = CommandHelpers.UnplacedMessage(located.UnplacedNames);
                    if (!string.IsNullOrWhiteSpace(unplaced)) msg += "\n\n" + unplaced;
                    Dialogs.Warn("EK Sheet Print", msg);
                    return Result.Cancelled;
                }

                SettingsData settings = SettingsStore.Load();
                if (!SettingsStore.EnsureOutputFolder(settings, null)) return Result.Cancelled;

                List<string> extras = new List<string>();
                string unplacedMessage = CommandHelpers.UnplacedMessage(located.UnplacedNames);
                if (!string.IsNullOrWhiteSpace(unplacedMessage)) extras.Add(unplacedMessage);

                List<View> asViews = located.Sheets.Cast<View>().ToList();
                PdfBatchResult status;
                if (asViews.Count == 1)
                    status = PdfExportService.ExportCombined(doc, asViews, settings.OutputFolder, FileNames.ProjectItemName(doc, asViews[0]));
                else if (settings.QuickMultipleMode == QuickMultipleMode.Combine)
                    status = PdfExportService.ExportCombined(doc, asViews, settings.OutputFolder,
                        FileNames.Safe(FileNames.ProjectFileName(doc) + " - Selected Sheets"));
                else
                    status = PdfExportService.ExportSeparate(doc, asViews, settings.OutputFolder, PdfNameMode.ProjectItem, null);
                return CommandHelpers.FinishBatch("EK Sheet Print", status, settings.OutputFolder, extras);
            }
            catch (Exception ex)
            {
                Dialogs.Error("EK Sheet Print", "Sheet Print failed.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class QuickDwgCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null) return Result.Cancelled;
                Document doc = uidoc.Document;

                SettingsData settings = SettingsStore.Load();
                if (!SettingsStore.EnsureOutputFolder(settings, null)) return Result.Cancelled;

                List<View> views = RevitSelection.GetSelectedPrintableViews(uidoc);
                if (views.Count == 0)
                {
                    View active = doc.ActiveView;
                    if (!RevitSelection.IsPrintable(active))
                    {
                        Dialogs.Warn("EK Quick DWG", "Select printable sheets/views, or open a printable view or sheet.");
                        return Result.Cancelled;
                    }
                    views.Add(active);
                }

                views = RevitSelection.SortViews(views);
                bool ok = DwgExportService.Export(doc, views, settings.OutputFolder, DwgNameMode.ProjectItem, null, settings);
                if (ok)
                {
                    Shell.OpenFolder(settings.OutputFolder);
                    return Result.Succeeded;
                }
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Dialogs.Error("EK Quick DWG", "Quick DWG failed.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SelectDwgCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null) return Result.Cancelled;
                Document doc = uidoc.Document;

                SettingsData settings = SettingsStore.Load();
                if (!SettingsStore.EnsureOutputFolder(settings, null)) return Result.Cancelled;

                List<View> all = RevitSelection.GetAllPrintableViews(doc);
                if (all.Count == 0)
                {
                    Dialogs.Warn("EK Select DWG", "No exportable sheets or views were found in this project.");
                    return Result.Cancelled;
                }

                using (DwgPickerForm form = new DwgPickerForm(all, doc.ActiveView, FileNames.ProjectFileName(doc), settings))
                {
                    if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // Folder and DWG options may have been changed through the dialog's Settings link.
                    settings = SettingsStore.Load();
                    if (!SettingsStore.EnsureOutputFolder(settings, null)) return Result.Cancelled;

                    bool ok = DwgExportService.Export(doc, form.SelectedViews, settings.OutputFolder, form.NameMode,
                        form.DwgName, settings);
                    if (ok)
                    {
                        Shell.OpenFolder(settings.OutputFolder);
                        return Result.Succeeded;
                    }
                    return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                Dialogs.Error("EK Select DWG", "DWG export failed.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                SettingsData data = SettingsStore.Load();
                using (SettingsForm form = new SettingsForm(data))
                {
                    if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        SettingsStore.Save(form.Settings);
                        return Result.Succeeded;
                    }
                }
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Dialogs.Error("EK Print Settings", "Could not open settings.\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Offline3D.RevitExporter;

/// <summary>
/// Export 3D PDF. Steps: resolve the view/sheet, read crop regions, show the
/// options (with optional preview), collect cropped GLB geometry from a
/// temporary 3D view, roll every temporary change back, export the clean and
/// marked PDF pages, write the draft and start the viewer helper, which
/// registers the drawing and embeds the model (unchanged contract).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ExportCommand : IExternalCommand
{
    internal const string ExporterVersion = "0.5.3";
    /// <summary>Margin added to the native section box only. The exact crop is
    /// still applied per triangle; the margin keeps any Revit section-box cut
    /// (and its cap faces) outside the crop, so boundary triangles stay whole.</summary>
    private const double SectionBoxMarginFeet = 1.0;

    private readonly Dictionary<ElementId, List<ElementId>> hiddenCache = new Dictionary<ElementId, List<ElementId>>();

    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        string? folder = null; ExportTrace? trace = null; ExportProgressDialog? progress = null;
        bool cancelled = false;
        var warnings = new List<string>();
        void Warn(string warning) { if (warnings.Contains(warning)) return; warnings.Add(warning); trace?.Line("Warning: " + warning); }
        try
        {
            hiddenCache.Clear();
            Contract.Encode(Array.Empty<string>()); // load the writer before any Revit transaction
            var ui = data.Application.ActiveUIDocument; var doc = ui.Document;
            var owner = new RevitWindow(data.Application.MainWindowHandle);
            using var target = ExportTarget.Create(ui, owner.Handle);
            if (target == null) return Result.Cancelled;

            string work = folder = Path.Combine(Path.GetTempPath(), "Offline3DExport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var log = trace = new ExportTrace(work);
            log.Line("Offline3D exporter " + ExporterVersion + " | " + Assembly.GetExecutingAssembly().FullName + " | " + data.Application.Application.VersionName + " " + data.Application.Application.VersionBuild);
            log.Line("Document: " + ExportText.Clean(doc.Title, 200) + " | view: " + ExportText.Clean(target.SourcePlan.Name, 200) + " (" + target.SourcePlan.ViewType + ") | from an unplaced view: " + target.FromPlan + " | viewports: " + target.Viewports.Length);
            var bar = progress = new ExportProgressDialog();

            // ---- regions and registration draft (unchanged mapping math) ----
            log.Stage("Reading view regions");
            var anchor = target.Plan.CropBox;
            var anchorCenter = anchor.Transform.OfPoint((anchor.Min + anchor.Max) / 2);
            var graphics = Matrix4.Graphics(Adapter.Point(anchorCenter));
            var regions = target.Viewports.Select(v => ExportRegion.Create(doc, (Autodesk.Revit.DB.View)doc.GetElement(v.ViewId), v, graphics)).ToArray();
            var main = regions[0].Mapping;
            if (target.FromPlan) { main.ViewId = target.SourcePlan.UniqueId; main.Name = target.SourcePlan.Name; }
            var reg = new Registration
            {
                DocumentId = doc.ProjectInformation.UniqueId, ViewId = main.ViewId, LevelId = target.SourcePlan is ViewPlan vp ? vp.GenLevel.UniqueId : main.ViewId,
                LevelProjectElevationFeet = main.LevelFeet, ModelToSheet = main.ModelToSheet, ModelToGraphics = graphics, CropSheet = main.CropSheet,
                ClickPlaneOrigin = main.PlaneOrigin, ClickPlaneNormal = main.PlaneNormal, ViewRegions = regions.Select(r => r.Mapping).ToArray()
            };
            foreach (var r in regions) log.Line("Region: " + Describe(r));

            int approximateCount = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
            Element[] candidates = Array.Empty<Element>(); bool scanned = false;
            void EnsureCandidates(Action<string> report)
            {
                if (scanned) return;
                report("Scanning model elements…");
                var model = new List<Element>(); int count = 0;
                foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    if (element.Category?.CategoryType == CategoryType.Model) model.Add(element);
                    if (++count % 2000 == 0) report("Scanning model elements… " + count.ToString("N0", CultureInfo.CurrentCulture));
                }
                candidates = model.ToArray(); scanned = true;
                log.Line("Scanned " + count + " elements; " + candidates.Length + " model elements");
            }

            var previewCache = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var temporary = new TransactionGroup(doc, "Offline3D temporary export view"))
            {
                temporary.Start();
                try
                {
                    View3D view;
                    using (var tx = new Autodesk.Revit.DB.Transaction(doc, "Prepare 3D export view"))
                    {
                        tx.Start();
                        var family = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                        view = View3D.CreateIsometric(doc, family.Id);
                        view.Name = "Offline3D_TEMP_" + Guid.NewGuid().ToString("N"); view.ViewTemplateId = ElementId.InvalidElementId;
                        view.DetailLevel = ViewDetailLevel.Medium; view.DisplayStyle = DisplayStyle.Shading; view.IsSectionBoxActive = false;
                        tx.Commit();
                    }

                    BoundingBoxXYZ? fullBounds = null; bool? fullBoundsCategories = null;
                    void PrepareScope(bool outside, bool allUp, bool allDown, bool allCategories, Action<string> report)
                    {
                        EnsureCandidates(report);
                        view.IsSectionBoxActive = false; view.CropBoxActive = false;
                        var revealed = candidates.Where(e => e.CanBeHidden(view) && e.IsHidden(view) && !e.IsHidden(target.SourcePlan)).Select(e => e.Id).ToList();
                        if (revealed.Count > 0) view.UnhideElements(revealed);
                        ConfigureVisibility(doc, target.SourcePlan, view, allCategories, Warn);
                        if (outside || allUp || allDown)
                        {
                            if (fullBounds == null || fullBoundsCategories != allCategories)
                            {
                                doc.Regenerate();
                                fullBounds = PreviewFraming.ModelBounds(doc, view) ?? throw new ExportSetupException("No model bounds were found for the expanded export scope.");
                                fullBoundsCategories = allCategories;
                            }
                        }
                        foreach (var region in regions) region.ConfigureBounds(outside, allUp, allDown, fullBounds);
                    }

                    string Preview(PreviewRequest request, Action<string> report)
                    {
                        if (previewCache.TryGetValue(request.Key, out var cached) && File.Exists(cached)) return cached;
                        log.Line("Preview requested: " + request.Key + " at " + request.PixelSize.Width + "x" + request.PixelSize.Height);
                        var watch = Stopwatch.StartNew();
                        try
                        {
                            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "Update export scope preview"))
                            {
                                tx.Start();
                                PrepareScope(request.OutsideCrop, request.AllUp, request.AllDown, request.IncludeAllCategories, report);
                                var box = regions.Length == 1 ? regions[0].Box : Union(regions);
                                view.SetSectionBox(box); view.IsSectionBoxActive = true;
                                PreviewFraming.Apply(doc, view, box, request.PixelSize, Warn);
                                tx.Commit();
                            }
                            report("Revit is rendering the preview…");
                            string imageDirectory = Path.Combine(work, "preview-" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(imageDirectory);
                            using (var image = new ImageExportOptions { FilePath = Path.Combine(imageDirectory, "snapshot"), ExportRange = ExportRange.SetOfViews, ZoomType = ZoomFitType.FitToPage, FitDirection = FitDirectionType.Horizontal, PixelSize = 1200, HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG })
                            {
                                image.SetViewsAndSheets(new List<ElementId> { view.Id });
                                doc.ExportImage(image);
                            }
                            string path = PreviewImageFiles.Find(imageDirectory);
                            previewCache[request.Key] = path;
                            File.AppendAllText(Path.Combine(work, "preview-timings.txt"), "Preview " + request.Key + ": " + watch.ElapsedMilliseconds + " ms | " + path + Environment.NewLine);
                            log.Line("Preview ready in " + watch.ElapsedMilliseconds + " ms");
                            return path;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            Warn("Preview could not be generated: " + ex.Message);
                            File.AppendAllText(Path.Combine(work, "preview-errors.txt"), ex + Environment.NewLine);
                            throw;
                        }
                    }

                    // ---- options (the preview runs inside this call) ----
                    log.Stage("Export options");
                    using var options = new ExportOptionsDialog(target.SourcePlan.Name, regions[0].BottomLabel, regions[0].TopLabel, approximateCount, regions.All(r => r.Source is ViewPlan));
                    if (options.RunModal(owner, Preview) != OptionsOutcome.Export) { cancelled = true; log.Line("Cancelled in the options dialog"); return Result.Cancelled; }
                    var detail = options.DetailPreset;
                    log.Line("Options: detail " + detail + " (LOD " + options.TessellationLevel + "), outside crop " + options.OutsideCrop + ", all up " + options.AllUp + ", all down " + options.AllDown + ", hidden categories included " + options.IncludeAllCategories + ", tiny-part filter " + options.MinimumPartFeet.ToString("0.###", CultureInfo.InvariantCulture) + " ft, properties: " + string.Join(", ", options.ParameterNames));
                    if (options.PreviewDiagnostics.Length > 0) File.AppendAllText(Path.Combine(work, "preview-timings.txt"), options.PreviewDiagnostics + Environment.NewLine);

                    string stem = string.Concat(target.FileStem.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                    string destination;
                    using (var chooser = new SaveFileDialog { Title = "Export clickable 3D PDF", Filter = "3D PDF (*.pdf)|*.pdf", FileName = stem + "-3D.pdf", OverwritePrompt = true })
                    {
                        if (chooser.ShowDialog(owner) != DialogResult.OK) { cancelled = true; log.Line("Cancelled at file selection"); return Result.Cancelled; }
                        destination = chooser.FileName;
                    }

                    // ---- export view ----
                    bar.Show(owner);
                    log.Stage("Preparing the export view");
                    BoundingBoxXYZ nativeBox;
                    using (var tx = new Autodesk.Revit.DB.Transaction(doc, "Prepare full element geometry"))
                    {
                        tx.Start();
                        PrepareScope(options.OutsideCrop, options.AllUp, options.AllDown, options.IncludeAllCategories, text => Check(bar, text));
                        view.DetailLevel = detail == ExportDetail.Quick ? ViewDetailLevel.Coarse : detail == ExportDetail.Low || detail == ExportDetail.Medium ? ViewDetailLevel.Medium : ViewDetailLevel.Fine;
                        view.DisplayStyle = detail == ExportDetail.Quick ? DisplayStyle.Shading : DisplayStyle.Realistic;
                        view.CropBoxActive = false;
                        nativeBox = Expand(regions.Length == 1 ? regions[0].Box : Union(regions), SectionBoxMarginFeet);
                        view.SetSectionBox(nativeBox); view.IsSectionBoxActive = true;
                        tx.Commit();
                    }
                    foreach (var r in regions) log.Line("Export scope: " + Describe(r));

                    // ---- geometry ----
                    log.Stage("Collecting model geometry");
                    bool reuse = options.IncludeAllCategories && Environment.GetEnvironmentVariable("OFFLINE3D_DISABLE_INSTANCE_REUSE") != "1";
                    log.Line("Instance reuse: " + (reuse ? "on (Revit SymbolGeometryId, verified per symbol)" : "off" + (options.IncludeAllCategories ? " (OFFLINE3D_DISABLE_INSTANCE_REUSE=1)" : " (source view filters may override graphics per element)")));
                    File.WriteAllText(Path.Combine(work, "export-memory.txt"), "Before geometry: " + ExportTrace.Memory() + Environment.NewLine);
                    using var writer = new SceneWriter(work, graphics, includeFeatureEdges: detail != ExportDetail.Quick);
                    var stream = new GeometryStream(new GeometryStreamOptions
                    {
                        Volumes = regions.Select(r => r.Volume).ToArray(),
                        NativeSectionBox = new ExportVolume(Adapter.Point(nativeBox.Min), Adapter.Point(nativeBox.Max), Adapter.Matrix(nativeBox.Transform)),
                        MinimumPartFeet = options.MinimumPartFeet,
                        ReuseInstances = reuse
                    }, writer, doc);
                    var context = new RevitGeometryContext(stream, bar, trace, options.TessellationLevel, options.ParameterNames);
                    try
                    {
                        foreach (var scope in regions)
                        {
                            Check(bar, "Hiding elements outside " + scope.Source.Name);
                            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "Prepare region geometry"))
                            {
                                tx.Start();
                                ConfigureVisibility(doc, scope.Source, view, options.IncludeAllCategories, Warn);
                                var allowed = new HashSet<ElementId>(scope.Candidates(doc));
                                var outside = candidates.Where(e => !(e is RevitLinkInstance) && e.CanBeHidden(view) && !allowed.Contains(e.Id)).Select(e => e.Id).ToList();
                                var outsideSet = new HashSet<ElementId>(outside);
                                var reveal = candidates.Where(e => e.CanBeHidden(view) && e.IsHidden(view) && !outsideSet.Contains(e.Id) && !e.IsHidden(scope.Source)).Select(e => e.Id).ToList();
                                if (reveal.Count > 0) view.UnhideElements(reveal);
                                if (outside.Count > 0) view.HideElements(outside);
                                tx.Commit();
                                log.Line("Region " + ExportText.Clean(scope.Source.Name, 120) + ": " + allowed.Count + " host elements near the crop, " + outside.Count + " hidden for the traversal");
                            }
                            Check(bar, "Collecting model geometry");
                            using (var exporter = new CustomExporter(doc, context)) { exporter.IncludeGeometricObjects = false; exporter.ShouldStopOnError = true; exporter.Export(view); }
                            context.ThrowIfStopped();
                        }
                        stream.Finish();
                    }
                    catch
                    {
                        log.Line("Geometry collection stopped. Statistics so far:");
                        log.Lines(stream.Stats.Report());
                        log.Line("Writer: placements " + writer.PlacementCount + ", unique meshes " + writer.UniqueMeshCount + ", unique bytes " + writer.UniqueBytes + ", projected parts " + writer.ProjectedNodes);
                        throw;
                    }
                    log.Lines(stream.Stats.Report());
                    if (context.SymbolIdFailures > 0) log.Line("SymbolGeometryId unavailable for " + context.SymbolIdFailures + " instances (traversed normally)");
                    if (context.MaterialFailures > 0) log.Line("Appearance details unavailable for " + context.MaterialFailures + " materials (colour kept)");
                    if (writer.PlacementCount == 0) throw new ExportSetupException("No exportable model geometry was found in the export area. Check the preview, the view's phase and visibility, and that linked models are loaded.");

                    // ---- GLB and identities ----
                    Check(bar, "Writing the 3D model");
                    log.Stage("Writing GLB");
                    reg.ExportBoundsGraphics = writer.BoundsGraphics;
                    var stats = writer.Write(Path.Combine(work, "scene.glb"));
                    File.WriteAllBytes(Path.Combine(work, "identities.json"), Contract.Encode(stream.Identities.ToArray()));
                    var summary = "GLB " + stats.Bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes | parts " + stats.Nodes + " (before merging " + stats.NodesBeforeMerge + ", merged " + stats.MergedPlacements + " into " + stats.MergedGroups + ") | unique meshes " + stats.UniqueMeshes + " | shared placements " + stats.SharedPlacements + " | triangles " + stats.Triangles.ToString("N0", CultureInfo.InvariantCulture) + " placed, " + stats.UniqueTriangles.ToString("N0", CultureInfo.InvariantCulture) + " stored | materials " + stats.Materials + " | elements " + stats.Elements;
                    log.Line(summary);
                    File.WriteAllLines(Path.Combine(work, "geometry-diagnostics.txt"), new[] { summary, "Exporter " + ExporterVersion + "; instance reuse " + (reuse ? "on" : "off") + "; " + ExportTrace.Memory() }.Concat(stream.Stats.Report(50)));
                    File.AppendAllText(Path.Combine(work, "export-memory.txt"), "After packing: " + ExportTrace.Memory() + Environment.NewLine);
                    log.Keep(Path.Combine(work, "geometry-diagnostics.txt"));

                    temporary.RollBack();

                    // ---- PDF pages (unchanged) ----
                    log.Stage("Exporting PDF pages");
                    var pdfOptions = new PDFExportOptions { Combine = true, FileName = "drawing-clean", ColorDepth = ColorDepthType.Color };
                    if (target.FromPlan) { pdfOptions.PaperFormat = ExportPaperFormat.ISO_A3; pdfOptions.PaperOrientation = PageOrientationType.Landscape; pdfOptions.PaperPlacement = PaperPlacementType.Center; pdfOptions.ZoomType = ZoomType.FitToPage; }
                    Check(bar, "Exporting the clean PDF page");
                    if (!doc.Export(work, new List<ElementId> { target.Sheet.Id }, pdfOptions)) throw new IOException("Revit could not export the PDF page.");
                    var markers = AutomaticExport.Markers(reg.CropSheet);
                    using (var marks = new TransactionGroup(doc, "Offline3D temporary mapping markers"))
                    {
                        marks.Start();
                        try
                        {
                            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "Prepare mapping check"))
                            {
                                tx.Start();
                                var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                                foreach (var marker in markers)
                                {
                                    var style = doc.Settings.Categories.NewSubcategory(lines, "Offline3D_" + Guid.NewGuid().ToString("N"));
                                    style.LineColor = new Autodesk.Revit.DB.Color((byte)marker.Red, (byte)marker.Green, (byte)marker.Blue);
                                    style.SetLineWeight(1, GraphicsStyleType.Projection);
                                    var p = new XYZ(marker.SheetX, marker.SheetY, 0);
                                    double tolerance = data.Application.Application.ShortCurveTolerance;
                                    foreach (var end in new[] { p + new XYZ(Math.Max(.012, 3 * tolerance), 0, 0), p + new XYZ(0, Math.Max(.008, 2 * tolerance), 0) })
                                    {
                                        var curve = doc.Create.NewDetailCurve(target.Sheet, Line.CreateBound(p, end));
                                        curve.LineStyle = style.GetGraphicsStyle(GraphicsStyleType.Projection);
                                    }
                                }
                                tx.Commit();
                            }
                            Check(bar, "Exporting the registration check page");
                            pdfOptions.FileName = "drawing-marked";
                            if (!doc.Export(work, new List<ElementId> { target.Sheet.Id }, pdfOptions)) throw new IOException("Revit could not export the registration check page.");
                        }
                        finally { marks.RollBack(); }
                    }

                    // ---- draft for the viewer helper (unchanged files and schema) ----
                    File.WriteAllBytes(Path.Combine(work, "markers.json"), Contract.Encode(markers));
                    File.WriteAllBytes(Path.Combine(work, "registration.draft.json"), Contract.Encode(reg));
                    File.WriteAllLines(Path.Combine(work, "exclusions.txt"), new[]
                    {
                        "Coverage: " + (options.OutsideCrop ? "Outside-crop area explicitly confirmed" : "View crop only") + "; all up: " + options.AllUp + "; all down: " + options.AllDown + ". Only intersecting triangles retained, unchanged; viewer clips section faces.",
                        "No DWF or walk mode. Image textures, procedural assets and native cut hatches are not included.",
                        "Geometry detail " + options.TessellationLevel + " (" + detail + "); elements " + stream.Stats.ElementsExported + "; triangles " + stats.Triangles + "; linked elements " + stream.Stats.LinkedElementsExported + "; GLB bytes " + stats.Bytes
                    });
                    File.WriteAllLines(Path.Combine(work, "export-warnings.txt"), warnings);
                    File.WriteAllLines(Path.Combine(work, "unloaded-links.txt"), new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Where(l => l.GetLinkDocument() == null).Select(l => "Unloaded link: " + ExportText.Clean(l.Name, 200)));
                    File.WriteAllLines(Path.Combine(work, "export-timings.txt"), bar.TimingLog.Concat(log.StageTimes).Concat(new[] { "Sheet: " + ExportText.Clean(target.FileStem, 200) + " | " + summary + " | optional tiny parts skipped: " + stream.Stats.SkippedTiny }));
                    File.WriteAllText(Path.Combine(work, "exporter-version.txt"), Assembly.GetExecutingAssembly().FullName + " | " + data.Application.Application.VersionName);
                    foreach (var name in new[] { "export-timings.txt", "export-warnings.txt", "unloaded-links.txt", "preview-timings.txt", "preview-errors.txt", "export-memory.txt" }) log.Keep(Path.Combine(work, name));

                    target.Restore();
                    log.Stage("Starting the viewer helper");
                    Check(bar, "Checking the PDF mapping in the viewer helper");
                    string install = Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.Parent!.Parent!.FullName;
                    string tool = Path.Combine(install, "Viewer", "Offline3D.FastPDF.Dev.exe");
                    if (!File.Exists(tool)) throw new FileNotFoundException("The viewer helper is missing (" + tool + "). Reinstall Offline 3D PDF.");
                    using (var helper = Process.Start(new ProcessStartInfo(tool, "--auto-export \"" + work + "\" \"" + destination + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(tool)! }))
                        log.Line("Viewer helper started (process " + (helper?.Id.ToString(CultureInfo.InvariantCulture) ?? "?") + "). It reports success or failure in its own message. Destination: " + destination);
                    bar.Complete();
                    return Result.Succeeded;
                }
                finally { if (temporary.GetStatus() == TransactionStatus.Started) temporary.RollBack(); }
            }
        }
        catch (OperationCanceledException) { cancelled = true; trace?.Line("Cancelled"); return Result.Cancelled; }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { cancelled = true; trace?.Line("Cancelled by Revit"); return Result.Cancelled; }
        catch (ExportSetupException ex)
        {
            trace?.Line("Stopped: " + ex.Message);
            progress?.Hide();
            TaskDialog.Show("Export 3D PDF", ex.Message + (trace?.LogDirectory != null ? "\n\nLog: " + trace.LogDirectory : ""));
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            if (progress?.CancelRequested == true) { cancelled = true; trace?.Line("Cancelled (" + ex.GetType().Name + ")"); return Result.Cancelled; }
            trace?.Line("FAILED: " + ex);
            message = "Export 3D PDF " + ExporterVersion + " could not finish.\n\n" + ex.Message;
            try
            {
                string logs = trace?.LogDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Offline3DPdfPrototype", "Logs");
                Directory.CreateDirectory(logs);
                string path = Path.Combine(logs, "Revit-export-error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
                File.WriteAllText(path, Assembly.GetExecutingAssembly().FullName + "\r\n" + Assembly.GetExecutingAssembly().Location + "\r\n" + ex + "\r\n" + string.Join(Environment.NewLine, progress?.TimingLog ?? Array.Empty<string>()) + "\r\n" + string.Join(Environment.NewLine, warnings));
                if (folder != null) foreach (var name in new[] { "preview-errors.txt", "preview-timings.txt", "geometry-diagnostics.txt" }) trace?.Keep(Path.Combine(folder, name));
                message += "\n\nDetails: " + path;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Result.Failed;
        }
        finally
        {
            progress?.Dispose();
            trace?.Dispose();
            // A cancelled export leaves its trace in the log folder; the work folder is not needed.
            if (cancelled && folder != null) try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Check(ExportProgressDialog progress, string phase) { if (!progress.Pump(phase)) throw new OperationCanceledException("Export cancelled"); }

    private static BoundingBoxXYZ Union(ExportRegion[] regions) => new BoundingBoxXYZ { Min = new XYZ(regions.Min(r => r.Min.X), regions.Min(r => r.Min.Y), regions.Min(r => r.Min.Z)), Max = new XYZ(regions.Max(r => r.Max.X), regions.Max(r => r.Max.Y), regions.Max(r => r.Max.Z)) };

    private static BoundingBoxXYZ Expand(BoundingBoxXYZ box, double margin) => new BoundingBoxXYZ { Transform = box.Transform, Min = box.Min - new XYZ(margin, margin, margin), Max = box.Max + new XYZ(margin, margin, margin) };

    private static string Describe(ExportRegion r)
    {
        var size = r.Box.Max - r.Box.Min; var x = r.Box.Transform.BasisX;
        double angle = Math.Atan2(x.Y, x.X) * 180 / Math.PI;
        return ExportText.Clean(r.Source.Name, 120) + " (" + r.Source.ViewType + ") | crop " + size.X.ToString("0.##", CultureInfo.InvariantCulture) + " x " + size.Y.ToString("0.##", CultureInfo.InvariantCulture) + " ft, height " + size.Z.ToString("0.##", CultureInfo.InvariantCulture) + " ft, rotation " + angle.ToString("0.#", CultureInfo.InvariantCulture) + "° | bottom: " + ExportText.Clean(r.BottomLabel, 80) + " | top: " + ExportText.Clean(r.TopLabel, 80);
    }

    private void ConfigureVisibility(Document doc, Autodesk.Revit.DB.View source, View3D target, bool allCategories, Action<string> warn)
    {
        foreach (var id in new[] { BuiltInParameter.VIEW_PHASE, BuiltInParameter.VIEW_PHASE_FILTER, BuiltInParameter.VIEWER_OPTION_VISIBILITY })
        {
            var from = source.get_Parameter(id); var to = target.get_Parameter(id);
            if (from != null && to != null && !to.IsReadOnly && from.StorageType == StorageType.ElementId && to.StorageType == StorageType.ElementId) to.Set(from.AsElementId());
        }
        foreach (Category c in doc.Settings.Categories)
            if (c.CategoryType == CategoryType.Model && source.CanCategoryBeHidden(c.Id) && target.CanCategoryBeHidden(c.Id))
                target.SetCategoryHidden(c.Id, source.GetCategoryHidden(c.Id) && (!allCategories || c.Id.Value == (long)BuiltInCategory.OST_RvtLinks));
        foreach (var filter in target.GetFilters().ToArray()) target.RemoveFilter(filter);
        if (!allCategories)
            foreach (var filter in source.GetFilters()) { target.AddFilter(filter); target.SetFilterVisibility(filter, source.GetFilterVisibility(filter)); target.SetFilterOverrides(filter, source.GetFilterOverrides(filter)); }
        if (doc.IsWorkshared)
            foreach (var workset in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)) target.SetWorksetVisibility(workset.Id, source.GetWorksetVisibility(workset.Id));
        LinkOverrideTransfer.Copy(doc, source, target, warn);
        if (!hiddenCache.TryGetValue(source.Id, out var hidden))
        {
            hidden = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e => e.CanBeHidden(target) && e.IsHidden(source)).Select(e => e.Id).ToList();
            hiddenCache.Add(source.Id, hidden);
        }
        if (hidden.Count > 0) target.HideElements(hidden);
    }
}

internal static class Adapter
{
    public static Point3 Point(XYZ p) => new Point3(p.X, p.Y, p.Z);
    public static Matrix4 Matrix(Transform t) => new Matrix4(t.BasisX.X, t.BasisY.X, t.BasisZ.X, t.Origin.X, t.BasisX.Y, t.BasisY.Y, t.BasisZ.Y, t.Origin.Y, t.BasisX.Z, t.BasisY.Z, t.BasisZ.Z, t.Origin.Z, 0, 0, 0, 1);
}

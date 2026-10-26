using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Forms;
using System.Diagnostics;
using System.Reflection;
using TaskDialog=Autodesk.Revit.UI.TaskDialog;
namespace Offline3D.RevitExporter;

[Transaction(TransactionMode.Manual)]
public sealed class ExportCommand:IExternalCommand
{
    public Result Execute(ExternalCommandData data,ref string message,ElementSet elements){
        string? folder=null;ExportProgressDialog? progress=null;var visibilityWarnings=new HashSet<string>(StringComparer.Ordinal);
        void Warn(string warning){visibilityWarnings.Add(warning);}
        try{
            hiddenCache.Clear();Contract.Encode(Array.Empty<string>());
            var ui=data.Application.ActiveUIDocument;var doc=ui.Document;var owner=new RevitWindow(data.Application.MainWindowHandle);
            using var target=ExportTarget.Create(ui,owner.Handle);if(target==null)return Result.Cancelled;
            folder=Path.Combine(Path.GetTempPath(),"Offline3DExport-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            progress=new ExportProgressDialog();progress.Show(owner);Check(progress,"Reading sheet regions");
            var anchor=target.Plan.CropBox;var anchorCenter=anchor.Transform.OfPoint((anchor.Min+anchor.Max)/2);
            var graphics=Matrix4.Graphics(Adapter.Point(anchorCenter));
            var regions=target.Viewports.Select(v=>ExportRegion.Create(doc,(Autodesk.Revit.DB.View)doc.GetElement(v.ViewId),v,graphics)).ToArray();
            var main=regions[0].Mapping;
            if(target.FromPlan){main.ViewId=target.SourcePlan.UniqueId;main.Name=target.SourcePlan.Name;}
            var reg=new Registration{DocumentId=doc.ProjectInformation.UniqueId,ViewId=main.ViewId,LevelId=target.SourcePlan is ViewPlan vp?vp.GenLevel.UniqueId:main.ViewId,LevelProjectElevationFeet=main.LevelFeet,ModelToSheet=main.ModelToSheet,ModelToGraphics=graphics,CropSheet=main.CropSheet,ClickPlaneOrigin=main.PlaneOrigin,ClickPlaneNormal=main.PlaneNormal,ViewRegions=regions.Select(r=>r.Mapping).ToArray()};
            Element[] candidates=Array.Empty<Element>();bool scannedCandidates=false;
            int approximateCount=new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
            void EnsureCandidates(){if(scannedCandidates)return;Check(progress,"Scanning model elements");var modelElements=new List<Element>();int scanned=0;foreach(var element in new FilteredElementCollector(doc).WhereElementIsNotElementType()){if(element.Category?.CategoryType==CategoryType.Model)modelElements.Add(element);if(++scanned%200==0&&!progress.Pump("Scanning model elements",scanned))throw new OperationCanceledException("Export cancelled");}candidates=modelElements.ToArray();scannedCandidates=true;}
            var previewCache=new Dictionary<string,string>();
            using(var temporary=new TransactionGroup(doc,"Offline3D temporary export preview")){
                temporary.Start();
                try{
                    View3D view;
                    using(var tx=new Autodesk.Revit.DB.Transaction(doc,"Prepare 3D export preview")){
                        tx.Start();var family=new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t=>t.ViewFamily==ViewFamily.ThreeDimensional);
                        view=View3D.CreateIsometric(doc,family.Id);view.Name="Offline3D_TEMP_"+Guid.NewGuid().ToString("N");view.ViewTemplateId=ElementId.InvalidElementId;view.DetailLevel=ViewDetailLevel.Medium;view.DisplayStyle=DisplayStyle.Shading;view.IsSectionBoxActive=false;
                        tx.Commit();
                    }
                    BoundingBoxXYZ? fullBounds=null;bool? fullBoundsCategories=null;
                    void PrepareScope(bool outside,bool allUp,bool allDown,bool categories){
                        EnsureCandidates();view.IsSectionBoxActive=false;view.CropBoxActive=false;
                        var revealed=candidates.Where(e=>e.CanBeHidden(view)&&e.IsHidden(view)&&!e.IsHidden(target.SourcePlan)).Select(e=>e.Id).ToList();if(revealed.Count>0)view.UnhideElements(revealed);ConfigureVisibility(doc,target.SourcePlan,view,categories,Warn);
                        if(outside||allUp||allDown){if(fullBounds==null||fullBoundsCategories!=categories){doc.Regenerate();fullBounds=PreviewFraming.ModelBounds(doc,view)??throw new ExportSetupException("No model bounds were found for the expanded export scope.");fullBoundsCategories=categories;}}
                        foreach(var region in regions)region.ConfigureBounds(outside,allUp,allDown,fullBounds);
                    }
                    Size previewSize=new Size(900,600);
                    string Preview(bool outside,bool allUp,bool allDown,bool categories){
                        EnsureCandidates();Check(progress,"Rendering requested preview");
                        using(var tx=new Autodesk.Revit.DB.Transaction(doc,"Update export scope preview")){
                            tx.Start();PrepareScope(outside,allUp,allDown,categories);var box=regions.Length==1?regions[0].Box:Union(regions);view.SetSectionBox(box);view.IsSectionBoxActive=true;PreviewFraming.Apply(doc,view,box,previewSize,Warn);tx.Commit();
                        }
                        string imageDirectory=Path.Combine(folder,"preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(imageDirectory);
                        var watch=Stopwatch.StartNew();
                        try{using(var image=new ImageExportOptions{FilePath=Path.Combine(imageDirectory,"snapshot"),ExportRange=ExportRange.SetOfViews,ZoomType=ZoomFitType.FitToPage,FitDirection=FitDirectionType.Horizontal,PixelSize=1200,HLRandWFViewsFileType=ImageFileType.PNG,ShadowViewsFileType=ImageFileType.PNG}){image.SetViewsAndSheets(new List<ElementId>{view.Id});File.AppendAllText(Path.Combine(folder,"preview-timings.txt"),"Requested view: "+view.Id.Value+" | expected native name: "+ImageExportOptions.GetFileName(doc,view.Id)+" | folder: "+imageDirectory+Environment.NewLine);doc.ExportImage(image);}var path=PreviewImageFiles.Find(imageDirectory);Check(progress,"Export options");return path;}
                        catch(Exception ex){File.AppendAllText(Path.Combine(folder,"preview-errors.txt"),ex+Environment.NewLine);throw;}
                        finally{watch.Stop();File.AppendAllText(Path.Combine(folder,"preview-timings.txt"),"ExportImage/locate: "+watch.ElapsedMilliseconds+" ms | preview client: "+previewSize.Width+"x"+previewSize.Height+Environment.NewLine);}

                    }
                    Check(progress,"Export options");progress.Hide();
                    using var preview=new ExportPreviewDialog(null,target.SourcePlan.Name,regions[0].BottomLabel,regions[0].TopLabel,approximateCount,regions.All(r=>r.Source is ViewPlan));
                    while(true){
                        var choice=preview.ShowDialog(owner);if(choice==DialogResult.OK)break;if(choice!=DialogResult.Retry||!preview.PreviewRequested)return Result.Cancelled;
                        // Run native graphics outside the options modal message loop.
                        string key=preview.OutsideCrop+":"+preview.AllUp+":"+preview.AllDown+":"+preview.IncludeAllCategories;preview.BeginPreview();progress.Show();
                        try{if(previewCache.TryGetValue(key,out var cached)){preview.LoadPreview(cached);continue;}previewSize=preview.PreviewClientSize;var path=Preview(preview.OutsideCrop,preview.AllUp,preview.AllDown,preview.IncludeAllCategories);preview.LoadPreview(path);previewCache[key]=path;File.AppendAllText(Path.Combine(folder,"preview-timings.txt"),preview.PreviewDiagnostics+Environment.NewLine);}
                        catch(OperationCanceledException){return Result.Cancelled;}
                        catch(Exception ex){string failure="Preview could not be generated: "+ex.Message;Warn(failure);File.AppendAllText(Path.Combine(folder,"preview-errors.txt"),ex+Environment.NewLine);preview.LoadPreview(null,failure+"\r\nYou can still export without a preview.");}
                        finally{progress.Hide();}
                    }
                    progress.Show();
                    string stem=string.Concat(target.FileStem.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
                    using var chooser=new SaveFileDialog{Title="Export clickable 3D PDF (GLB)",Filter="3D PDF (*.pdf)|*.pdf",FileName=stem+"-3D.pdf",OverwritePrompt=true};
                    if(chooser.ShowDialog(owner)!=DialogResult.OK)return Result.Cancelled;
                    string destination=chooser.FileName;
                    EnsureCandidates();
                    using(var tx=new Autodesk.Revit.DB.Transaction(doc,"Prepare full element geometry")){
                        tx.Start();PrepareScope(preview.OutsideCrop,preview.AllUp,preview.AllDown,preview.IncludeAllCategories);view.DetailLevel=preview.DetailPreset==ExportDetail.Quick?ViewDetailLevel.Coarse:preview.DetailPreset==ExportDetail.Low||preview.DetailPreset==ExportDetail.Medium?ViewDetailLevel.Medium:ViewDetailLevel.Fine;view.DisplayStyle=preview.DetailPreset==ExportDetail.Quick?DisplayStyle.Shading:DisplayStyle.Realistic;
                        view.CropBoxActive=false;view.SetSectionBox(regions.Length==1?regions[0].Box:Union(regions));view.IsSectionBoxActive=true;
                        tx.Commit();
                    }
                    Check(progress,"Exporting GLB geometry");File.WriteAllText(Path.Combine(folder,"export-memory.txt"),"Before geometry: private bytes "+Process.GetCurrentProcess().PrivateMemorySize64+" | managed bytes "+GC.GetTotalMemory(false)+Environment.NewLine);
                    using var sceneWriter=new SpoolSceneWriter(folder,graphics,includeFeatureEdges:preview.DetailPreset!=ExportDetail.Quick);
                    var context=new MeshContext(doc,regions,false,preview.TessellationLevel,progress,preview.ParameterNames,sceneWriter,preview.MinimumPartFeet);
                    try{
                    foreach(var scope in regions){
                        using(var tx=new Autodesk.Revit.DB.Transaction(doc,"Prepare region geometry")){tx.Start();ConfigureVisibility(doc,scope.Source,view,preview.IncludeAllCategories,Warn);var allowed=new HashSet<ElementId>(scope.Candidates(doc));var outside=candidates.Where(e=>!(e is RevitLinkInstance)&&e.CanBeHidden(view)&&!allowed.Contains(e.Id)).Select(e=>e.Id).ToList();var outsideSet=new HashSet<ElementId>(outside);var reveal=candidates.Where(e=>e.CanBeHidden(view)&&e.IsHidden(view)&&!outsideSet.Contains(e.Id)&&!e.IsHidden(scope.Source)).Select(e=>e.Id).ToList();if(reveal.Count>0)view.UnhideElements(reveal);if(outside.Count>0)view.HideElements(outside);tx.Commit();}
                        using(var exporter=new CustomExporter(doc,context)){exporter.IncludeGeometricObjects=false;exporter.ShouldStopOnError=true;exporter.Export(view);}
                    }
                    }catch{File.WriteAllLines(Path.Combine(folder,"geometry-diagnostics.txt"),new[]{"Geometry collection failed","Geometry placements: "+sceneWriter.NodeCount,"Unique meshes: "+sceneWriter.UniqueMeshCount,"Shared placements: "+sceneWriter.InstanceCount,"Unique binary bytes: "+sceneWriter.UniqueBytes,"Placed triangles: "+context.TriangleCount,"Unique triangles: "+sceneWriter.UniqueTriangles,"Scene byte limit: "+SceneLimits.MaxSceneBytes});throw;}
                    Check(progress,"Packing indexed geometry and properties");
                    if(context.TriangleCount==0)throw new ExportSetupException("No exportable model geometry was found. Check the preview visibility, phase and loaded links.");
                    reg.ExportBoundsGraphics=sceneWriter.BoundsGraphics;
                    var stats=sceneWriter.Write(Path.Combine(folder,"scene.glb"));
                    File.WriteAllLines(Path.Combine(folder,"geometry-diagnostics.txt"),new[]{"Retained triangles: "+stats.Triangles,"Triangles outside crop skipped: "+context.TrianglesOutsideCrop,"Mesh batches outside crop skipped: "+context.BatchesOutsideCrop,"Elements outside crop skipped: "+context.ElementsOutsideCrop,"Unique stored triangles: "+stats.UniqueTriangles,"Geometry placements: "+stats.Elements,"Shared placements: "+stats.Instances,"Unique binary bytes: "+sceneWriter.UniqueBytes,"Identified elements: "+context.Identities.Count,"Source visibility scans: "+hiddenCache.Count,"Managed bytes: "+GC.GetTotalMemory(false),"Private bytes: "+Process.GetCurrentProcess().PrivateMemorySize64,"Peak working set: "+Process.GetCurrentProcess().PeakWorkingSet64}.Concat(context.CategoryTriangles.OrderByDescending(p=>p.Value).Select(p=>"Category: "+p.Key+" | "+p.Value)).Concat(context.ElementTriangles.OrderByDescending(p=>p.Value).Take(50).Select(p=>"Element: "+p.Key+" | "+p.Value)));
                    File.AppendAllText(Path.Combine(folder,"export-memory.txt"),"After packing: private bytes "+Process.GetCurrentProcess().PrivateMemorySize64+" | managed bytes "+GC.GetTotalMemory(false)+Environment.NewLine);
                    File.WriteAllBytes(Path.Combine(folder,"identities.json"),Contract.Encode(context.Identities.ToArray()));
                    temporary.RollBack();
                    // Export clean/marked native pages after rolling back preview-only views.
                    var pdfOptions=new PDFExportOptions{Combine=true,FileName="drawing-clean",ColorDepth=ColorDepthType.Color};
                    if(target.FromPlan){pdfOptions.PaperFormat=ExportPaperFormat.ISO_A3;pdfOptions.PaperOrientation=PageOrientationType.Landscape;pdfOptions.PaperPlacement=PaperPlacementType.Center;pdfOptions.ZoomType=ZoomType.FitToPage;}
                    Check(progress,"Exporting clean PDF page");if(!doc.Export(folder,new List<ElementId>{target.Sheet.Id},pdfOptions))throw new IOException("Native PDF export failed");
                    var markers=AutomaticExport.Markers(reg.CropSheet);
                    using(var marks=new TransactionGroup(doc,"Offline3D temporary mapping markers")){
                        marks.Start();try{
                            using(var tx=new Autodesk.Revit.DB.Transaction(doc,"Prepare mapping check")){tx.Start();var lines=doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                                foreach(var marker in markers){var style=doc.Settings.Categories.NewSubcategory(lines,"Offline3D_"+Guid.NewGuid().ToString("N"));style.LineColor=new Autodesk.Revit.DB.Color((byte)marker.Red,(byte)marker.Green,(byte)marker.Blue);style.SetLineWeight(1,GraphicsStyleType.Projection);XYZ p=new XYZ(marker.SheetX,marker.SheetY,0);
                                    foreach(var end in new[]{p+new XYZ(Math.Max(.012,3*data.Application.Application.ShortCurveTolerance),0,0),p+new XYZ(0,Math.Max(.008,2*data.Application.Application.ShortCurveTolerance),0)}){var curve=doc.Create.NewDetailCurve(target.Sheet,Line.CreateBound(p,end));curve.LineStyle=style.GetGraphicsStyle(GraphicsStyleType.Projection);}}
                                tx.Commit();}
                            Check(progress,"Exporting registration verification page");pdfOptions.FileName="drawing-marked";if(!doc.Export(folder,new List<ElementId>{target.Sheet.Id},pdfOptions))throw new IOException("Native registration page failed");
                        }finally{marks.RollBack();}
                    }
                    File.WriteAllBytes(Path.Combine(folder,"markers.json"),Contract.Encode(markers));File.WriteAllBytes(Path.Combine(folder,"registration.draft.json"),Contract.Encode(reg));
                    File.WriteAllLines(Path.Combine(folder,"exclusions.txt"),new[]{"Coverage: "+(preview.OutsideCrop?"Outside-crop area explicitly confirmed":"View crop only")+"; all up: "+preview.AllUp+"; all down: "+preview.AllDown+". Only intersecting triangles retained, unchanged; viewer clips section faces.","No DWF or walk mode. Image textures, procedural assets and native cut hatches are not included.","Geometry detail "+preview.TessellationLevel+"; elements "+context.ElementCount+"; triangles "+context.TriangleCount+"; linked element batches "+context.LinkedElementCount+"; GLB bytes "+stats.Bytes});
                    File.WriteAllLines(Path.Combine(folder,"export-warnings.txt"),visibilityWarnings);
                    File.WriteAllLines(Path.Combine(folder,"unloaded-links.txt"),new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Where(l=>l.GetLinkDocument()==null).Select(l=>"Unloaded link: "+l.Name));
                    File.WriteAllLines(Path.Combine(folder,"export-timings.txt"),progress.TimingLog.Concat(new[]{"Sheet: "+target.FileStem+" | exported batches: "+stats.Elements+" | triangles: "+stats.Triangles+" | instances: "+stats.Instances+" | unique meshes: "+stats.UniqueMeshes+" | GLB bytes: "+stats.Bytes+" | optional tiny parts skipped: "+context.TinyPartsSkipped}));File.WriteAllText(Path.Combine(folder,"exporter-version.txt"),Assembly.GetExecutingAssembly().FullName+" | "+data.Application.Application.VersionName);
                    target.Restore();Check(progress,"Validating PDF mapping in the viewer helper");
                    string install=Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.Parent!.Parent!.FullName;string tool=Path.Combine(install,"Viewer","Offline3D.FastPDF.Dev.exe");if(!File.Exists(tool))throw new FileNotFoundException("Viewer helper missing; rerun INSTALL.cmd.");
                    Process.Start(new ProcessStartInfo(tool,"--auto-export \""+folder+"\" \""+destination+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(tool)});progress.Complete();
                    return Result.Succeeded;
                }finally{if(temporary.GetStatus()==TransactionStatus.Started)temporary.RollBack();}
            }
        }catch(OperationCanceledException){return Result.Cancelled;}
        catch(Autodesk.Revit.Exceptions.OperationCanceledException){return Result.Cancelled;}
        catch(ExportSetupException ex){TaskDialog.Show("Export 3D PDF",ex.Message);return Result.Cancelled;}
        catch(Exception ex){if(progress?.CancelRequested==true)return Result.Cancelled;message="Export 3D PDF v0.5.2 could not finish.\n\n"+ex.Message;try{string logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Offline3DPdfPrototype","Logs");Directory.CreateDirectory(logs);string path=Path.Combine(logs,"Revit-export-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");File.WriteAllText(path,Assembly.GetExecutingAssembly().FullName+"\r\n"+Assembly.GetExecutingAssembly().Location+"\r\n"+ex+"\r\n"+string.Join(Environment.NewLine,progress?.TimingLog??Array.Empty<string>())+"\r\n"+string.Join(Environment.NewLine,visibilityWarnings)+(folder==null?"":string.Concat(new[]{"preview-errors.txt","preview-timings.txt","geometry-diagnostics.txt"}.Where(n=>File.Exists(Path.Combine(folder,n))).Select(n=>"\r\n"+n+"\r\n"+File.ReadAllText(Path.Combine(folder,n))))));message+="\n\nDetails: "+path;}catch{}return Result.Failed;}
        finally{progress?.Dispose();}
    }
    private static void Check(ExportProgressDialog progress,string phase){if(!progress.Pump(phase))throw new OperationCanceledException("Export cancelled");}
    private static BoundingBoxXYZ Union(ExportRegion[] regions)=>new BoundingBoxXYZ{Min=new XYZ(regions.Min(r=>r.Min.X),regions.Min(r=>r.Min.Y),regions.Min(r=>r.Min.Z)),Max=new XYZ(regions.Max(r=>r.Max.X),regions.Max(r=>r.Max.Y),regions.Max(r=>r.Max.Z))};
    private readonly Dictionary<ElementId,List<ElementId>> hiddenCache=new Dictionary<ElementId,List<ElementId>>();
    private void ConfigureVisibility(Document doc,Autodesk.Revit.DB.View source,View3D target,bool allCategories,Action<string> warn){
        foreach(var id in new[]{BuiltInParameter.VIEW_PHASE,BuiltInParameter.VIEW_PHASE_FILTER,BuiltInParameter.VIEWER_OPTION_VISIBILITY}){var from=source.get_Parameter(id);var to=target.get_Parameter(id);if(from!=null&&to!=null&&!to.IsReadOnly&&from.StorageType==StorageType.ElementId&&to.StorageType==StorageType.ElementId)to.Set(from.AsElementId());}
        foreach(Category c in doc.Settings.Categories)if(c.CategoryType==CategoryType.Model&&source.CanCategoryBeHidden(c.Id)&&target.CanCategoryBeHidden(c.Id))target.SetCategoryHidden(c.Id,source.GetCategoryHidden(c.Id)&&(!allCategories||c.Id.Value==(long)BuiltInCategory.OST_RvtLinks));
        foreach(var filter in target.GetFilters().ToArray())target.RemoveFilter(filter);
        if(!allCategories)foreach(var filter in source.GetFilters()){target.AddFilter(filter);target.SetFilterVisibility(filter,source.GetFilterVisibility(filter));target.SetFilterOverrides(filter,source.GetFilterOverrides(filter));}
        if(doc.IsWorkshared)foreach(var workset in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))target.SetWorksetVisibility(workset.Id,source.GetWorksetVisibility(workset.Id));
        LinkOverrideTransfer.Copy(doc,source,target,warn);
        if(!hiddenCache.TryGetValue(source.Id,out var hidden)){hidden=new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e=>e.CanBeHidden(target)&&e.IsHidden(source)).Select(e=>e.Id).ToList();hiddenCache.Add(source.Id,hidden);}if(hidden.Count>0)target.HideElements(hidden);
    }
}
internal static class Adapter{
    public static Point3 Point(XYZ p)=>new Point3(p.X,p.Y,p.Z);
    public static Matrix4 Matrix(Transform t)=>new Matrix4(t.BasisX.X,t.BasisY.X,t.BasisZ.X,t.Origin.X,t.BasisX.Y,t.BasisY.Y,t.BasisZ.Y,t.Origin.Y,t.BasisX.Z,t.BasisY.Z,t.BasisZ.Z,t.Origin.Z,0,0,0,1);
}

using Autodesk.Revit.DB;
namespace Offline3D.RevitExporter;

internal sealed class MeshContext:IExportContext
{
    private sealed class Frame {public ElementId Id=ElementId.InvalidElementId;public Element? Element;public string Key="";public bool Suppressed;public bool Exported;public Document Doc=null!;public string Link="";public ElementIdentity? Identity;public bool IdentityAdded;public Dictionary<Matrix4,Dictionary<string,Pending>> Pending=new Dictionary<Matrix4,Dictionary<string,Pending>>();}
    private sealed class Pending {public Matrix4 Transform=null!;public PlainMaterial Material=null!;public string MaterialKey="";public IndexedMeshAccumulator Geometry=new IndexedMeshAccumulator();}
    private readonly Document host;
    private readonly Stack<Matrix4> transforms=new Stack<Matrix4>();
    private readonly Stack<Matrix4> documentFrames=new Stack<Matrix4>();
    private readonly Stack<Document> documents=new Stack<Document>();
    private readonly Stack<string> links=new Stack<string>();
    private readonly Stack<Frame> elements=new Stack<Frame>();
    private readonly ExportProgressDialog progress;
    private readonly int detail;
    private ExportRegion[] regions;
    private readonly HashSet<string> seen=new HashSet<string>();
    public void SetRegions(ExportRegion[] value){regions=value;}
    private readonly bool wholeModel;
    private readonly string[] parameters;
    private readonly double minimumPartFeet;
    public long TrianglesOutsideCrop {get;private set;}
    public int BatchesOutsideCrop {get;private set;}
    public int ElementsOutsideCrop {get;private set;}
    public int TinyPartsSkipped {get;private set;}
    public SpoolSceneWriter Writer {get;}
    public Dictionary<string,long> CategoryTriangles {get;}=new Dictionary<string,long>();
    public Dictionary<string,long> ElementTriangles {get;}=new Dictionary<string,long>();
    public List<ElementIdentity> Identities {get;}=new List<ElementIdentity>();
    public long TriangleCount {get;private set;}
    public int ElementCount {get;private set;}
    public int LinkedElementCount {get;private set;}
    private sealed class CachedMaterial{public PlainMaterial Material=null!;public string Key="";}
    private readonly Dictionary<Document,Dictionary<string,CachedMaterial>> materialCache=new Dictionary<Document,Dictionary<string,CachedMaterial>>();
    private PlainMaterial material=new PlainMaterial();
    private string materialKey="default";
    public MeshContext(Document document,ExportRegion[] regions,bool wholeModel,int detail,ExportProgressDialog progress,string[] parameters,SpoolSceneWriter writer,double minimumPartFeet=0){Writer=writer;this.minimumPartFeet=minimumPartFeet;host=document;this.regions=regions;this.wholeModel=wholeModel;this.detail=detail;this.progress=progress;this.parameters=parameters;transforms.Push(Matrix4.Identity);documents.Push(document);documentFrames.Push(Matrix4.Identity);links.Push("");}
    public bool Start()=>!progress.CancelRequested;
    public void Finish(){}
    public bool IsCanceled()=>progress.CancelRequested;
    public RenderNodeAction OnViewBegin(ViewNode node){node.LevelOfDetail=detail;return RenderNodeAction.Proceed;}
    public void OnViewEnd(ElementId id){}
    public RenderNodeAction OnElementBegin(ElementId id){
        while(elements.Count>0&&elements.Peek().Suppressed)elements.Pop();
        var doc=documents.Peek();var e=doc.GetElement(id);var key=links.Peek()+"::"+(e?.UniqueId??id.ToString());
        bool skip=e?.Category?.CategoryType!=CategoryType.Model||seen.Contains(key);
        if(!skip&&!wholeModel){skip=!regions.Any(r=>r.Intersects(e?.get_BoundingBox(null),documentFrames.Peek()));if(skip)ElementsOutsideCrop++;}
        if(!skip&&minimumPartFeet>0&&!(e is RevitLinkInstance)){var bounds=e?.get_BoundingBox(null);if(bounds!=null&&Math.Max(bounds.Max.X-bounds.Min.X,Math.Max(bounds.Max.Y-bounds.Min.Y,bounds.Max.Z-bounds.Min.Z))<minimumPartFeet){skip=true;TinyPartsSkipped++;}}
        var frame=new Frame{Id=id,Element=e,Key=key,Suppressed=skip,Doc=doc,Link=links.Peek()};elements.Push(frame);
        if(++ElementCount%50==0&&!progress.Pump("Collecting cropped model geometry",ElementCount,TriangleCount,Writer.UniqueTriangles))throw new OperationCanceledException("Export cancelled");
        return skip?RenderNodeAction.Skip:RenderNodeAction.Proceed;
    }
    private void Flush(Frame frame,Matrix4? transform=null){
        var frames=transform==null?frame.Pending.Keys.ToArray():frame.Pending.ContainsKey(transform)?new[]{transform}:Array.Empty<Matrix4>();
        foreach(var key in frames){foreach(var batch in frame.Pending[key].Values){string meshName=frame.Key+"/"+Writer.NodeCount;if(frame.Identity==null){frame.Identity=Identity(frame.Doc,frame.Element,frame.Key,frame.Link,batch.Material);frame.Identity.MeshName=meshName;}var identity=frame.Identity;Writer.Add(meshName,batch.Geometry.Finish(),batch.Transform,batch.Material,identity);if(!frame.IdentityAdded){Identities.Add(identity);frame.IdentityAdded=true;}if(frame.Doc!=host)LinkedElementCount++;}frame.Pending.Remove(key);}
    }
    public void OnElementEnd(ElementId id){while(elements.Count>0&&elements.Peek().Id!=id)elements.Pop();if(elements.Count>0){var frame=elements.Pop();if(frame.Exported){Flush(frame);seen.Add(frame.Key);}}}
    public RenderNodeAction OnInstanceBegin(InstanceNode node){transforms.Push(transforms.Peek()*Adapter.Matrix(node.GetTransform()));return RenderNodeAction.Proceed;}
    public void OnInstanceEnd(InstanceNode node){if(transforms.Count>1){if(elements.Count>0)Flush(elements.Peek(),transforms.Peek());transforms.Pop();}}
    public RenderNodeAction OnLinkBegin(LinkNode node){
        var doc=node.GetDocument();if(doc==null)return RenderNodeAction.Skip;
        var instance=elements.Count>0?elements.Peek().Element as RevitLinkInstance:null;
        links.Push(links.Peek()+"::"+(instance?.UniqueId??node.SymbolId.ToString()));documents.Push(doc);documentFrames.Push(transforms.Peek()*Adapter.Matrix(node.GetTransform()));transforms.Push(transforms.Peek()*Adapter.Matrix(node.GetTransform()));return RenderNodeAction.Proceed;
    }
    public void OnLinkEnd(LinkNode node){if(documents.Count>1&&node.GetDocument()==documents.Peek()){if(elements.Count>0)Flush(elements.Peek(),transforms.Peek());documents.Pop();documentFrames.Pop();links.Pop();if(transforms.Count>1)transforms.Pop();}}
    public RenderNodeAction OnFaceBegin(FaceNode node)=>RenderNodeAction.Proceed;
    public void OnFaceEnd(FaceNode node){}
    public void OnMaterial(MaterialNode node){
        var doc=documents.Peek();var nativeColor=node.Color;string cacheKey=node.MaterialId.Value+":"+node.Transparency.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+(nativeColor.IsValid?nativeColor.Red+","+nativeColor.Green+","+nativeColor.Blue:"invalid");
        if(!materialCache.TryGetValue(doc,out var cache)){cache=new Dictionary<string,CachedMaterial>();materialCache.Add(doc,cache);}if(cache.TryGetValue(cacheKey,out var cached)){material=cached.Material;materialKey=cached.Key;return;}
        var source=doc.GetElement(node.MaterialId) as Material;var color=source?.Color??node.Color;if(!color.IsValid)color=node.Color;
        material=new PlainMaterial{Name=source?.Name??"Default",Red=color.IsValid?color.Red/255d:.7,Green=color.IsValid?color.Green/255d:.7,Blue=color.IsValid?color.Blue/255d:.7,Opacity=Math.Max(0,Math.Min(1,1-node.Transparency)),Roughness=.8};
        if(source!=null&&doc.GetElement(source.AppearanceAssetId) is AppearanceAssetElement appearance){var asset=appearance.GetRenderingAsset();if(asset.FindByName("generic_glossiness") is Autodesk.Revit.DB.Visual.AssetPropertyDouble gloss)material.Roughness=Math.Max(.08,Math.Sqrt(1-Math.Max(0,Math.Min(1,gloss.Value))));if(asset.FindByName("generic_is_metal") is Autodesk.Revit.DB.Visual.AssetPropertyBoolean metal&&metal.Value)material.Metallic=1;}
        if(source!=null&&doc.GetElement(source.CutForegroundPatternId) is FillPatternElement pe){var p=pe.GetFillPattern();material.CutPattern=new CutPatternDefinition{Name=p.Name,Target=p.Target.ToString(),Solid=p.IsSolidFill,Grids=p.GetFillGrids().Select(g=>new CutGridDefinition{Angle=g.Angle,OriginXFeet=g.Origin.U,OriginYFeet=g.Origin.V,OffsetFeet=g.Offset,ShiftFeet=g.Shift,SegmentsFeet=g.GetSegments().ToArray()}).ToArray()};}
        materialKey=Contract.Hash(Contract.Encode(material));cache.Add(cacheKey,new CachedMaterial{Material=material,Key=materialKey});
    }
    public void OnLight(LightNode node){}
    public void OnRPC(RPCNode node){}
    public void OnPolymesh(PolymeshTopology mesh){
        if(elements.Count==0||elements.Peek().Suppressed||progress.CancelRequested)return;
        var frame=elements.Peek();var transform=transforms.Peek();var points=mesh.GetPoints().Select(Adapter.Point).ToArray();
        var selection=wholeModel?null:new CropMeshSelection(points,transform,regions.Select(r=>r.Volume));
        var nativeFacets=mesh.GetFacets();if(nativeFacets.Count==0)return;
        if(selection?.Outside==true){BatchesOutsideCrop++;TrianglesOutsideCrop+=nativeFacets.Count;if(!progress.Pump("Collecting cropped model geometry",ElementCount,TriangleCount,Writer.UniqueTriangles))throw new OperationCanceledException("Export cancelled");return;}
        var indices=new List<int>();var keptFaces=new List<int>();for(int i=0;i<nativeFacets.Count;i++){var f=nativeFacets[i];if(selection==null||selection.Keep(f.V1,f.V2,f.V3)){indices.Add(f.V1);indices.Add(f.V2);indices.Add(f.V3);keptFaces.Add(i);}if(i%4096==0&&!progress.Pump("Collecting cropped model geometry",ElementCount,TriangleCount,Writer.UniqueTriangles))throw new OperationCanceledException("Export cancelled");}
        TrianglesOutsideCrop+=nativeFacets.Count-keptFaces.Count;if(keptFaces.Count==0){BatchesOutsideCrop++;return;}
        var normals=mesh.GetNormals().Select(Adapter.Point).ToArray();var mode=mesh.DistributionOfNormals==DistributionOfNormals.AtEachPoint?MeshNormalMode.PerVertex:mesh.DistributionOfNormals==DistributionOfNormals.OnEachFacet?MeshNormalMode.PerFace:MeshNormalMode.Single;
        if(mode==MeshNormalMode.PerFace)normals=keptFaces.Select(i=>i<normals.Length?normals[i]:new Point3()).ToArray();
        if(!frame.Pending.TryGetValue(transform,out var byMaterial)){byMaterial=new Dictionary<string,Pending>();frame.Pending.Add(transform,byMaterial);}if(!byMaterial.TryGetValue(materialKey,out var batch)){batch=new Pending{Transform=transform,Material=material,MaterialKey=materialKey};byMaterial.Add(materialKey,batch);}
        frame.Exported=true;batch.Geometry.Append(points,normals,indices,mode);
        long count=keptFaces.Count;TriangleCount+=count;string category=frame.Element?.Category?.Name??"Unknown";CategoryTriangles[category]=(CategoryTriangles.TryGetValue(category,out var prior)?prior:0)+count;ElementTriangles[frame.Key]=(ElementTriangles.TryGetValue(frame.Key,out prior)?prior:0)+count;
        if(!progress.Pump("Collecting cropped model geometry",ElementCount,TriangleCount,Writer.UniqueTriangles))throw new OperationCanceledException("Export cancelled");
    }
    private ElementIdentity Identity(Document doc,Element? e,string key,string link,PlainMaterial surface){
        var type=e==null?null:doc.GetElement(e.GetTypeId()) as ElementType;var identity=new ElementIdentity{UniqueId=e?.UniqueId??key,DocumentId=doc.ProjectInformation.UniqueId,LinkInstanceId=string.IsNullOrEmpty(link)?null:link,ElementId=e?.Id.Value.ToString()??"",Category=e?.Category?.Name??"",FamilyName=type?.FamilyName??"",TypeName=type?.Name??"",LevelName=e==null?"":(doc.GetElement(e.LevelId) as Level)?.Name??"",MaterialName=surface.Name,CutPatternName=surface.CutPattern?.Name??""};
        if(e!=null){identity.Mark=e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()??"";foreach(string name in parameters.Take(20)){var p=e.LookupParameter(name)??type?.LookupParameter(name);if(p!=null){string value=p.StorageType==StorageType.String?p.AsString()??"":p.AsValueString()??(p.StorageType==StorageType.Integer?p.AsInteger().ToString():p.StorageType==StorageType.ElementId?p.AsElementId().Value.ToString():p.StorageType==StorageType.Double?p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture):"");identity.Parameters[name]=value.Length>2048?value.Substring(0,2048):value;}}}return identity;
    }
}

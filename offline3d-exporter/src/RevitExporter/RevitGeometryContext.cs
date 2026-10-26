using System.Globalization;
using Autodesk.Revit.DB;

namespace Offline3D.RevitExporter;

/// <summary>
/// Thin CustomExporter adapter. Revit callbacks are translated into
/// <see cref="GeometryStream"/> events; all bookkeeping (link and instance
/// stacks, crop selection, instance reuse, batching) lives there and is tested
/// without Revit. Runs only on Revit's API thread; progress is pumped between
/// callbacks, never from another thread.
/// </summary>
internal sealed class RevitGeometryContext : IExportContext
{
    private readonly GeometryStream stream;
    private readonly ExportProgressDialog progress;
    private readonly ExportTrace trace;
    private readonly int levelOfDetail;
    private readonly string[] parameters;
    private readonly Dictionary<Document, string> documentIds = new Dictionary<Document, string>();
    private readonly Dictionary<string, (PlainMaterial Material, string Key)> materials = new Dictionary<string, (PlainMaterial, string)>(StringComparer.Ordinal);
    private string? pendingLinkKey;
    private long elementCallbacks;

    public int SymbolIdFailures { get; private set; }
    public int MaterialFailures { get; private set; }
    public ExportStatistics Stats => stream.Stats;

    public RevitGeometryContext(GeometryStream stream, ExportProgressDialog progress, ExportTrace trace, int levelOfDetail, string[] parameters)
    {
        this.stream = stream; this.progress = progress; this.trace = trace; this.levelOfDetail = levelOfDetail; this.parameters = parameters;
    }

    private Document CurrentDocument => (Document)stream.CurrentDocument!;

    public bool Start() => !progress.CancelRequested;
    public void Finish() { }
    public bool IsCanceled() => progress.CancelRequested;

    public RenderNodeAction OnViewBegin(ViewNode node) { node.LevelOfDetail = levelOfDetail; return RenderNodeAction.Proceed; }
    public void OnViewEnd(ElementId elementId) { }

    public RenderNodeAction OnElementBegin(ElementId elementId)
    {
        var doc = CurrentDocument;
        var element = doc.GetElement(elementId);
        bool isLink = element is RevitLinkInstance;
        if (isLink) pendingLinkKey = element!.UniqueId;
        string category = ExportText.Clean(element?.Category?.Name ?? "Unknown", 256);
        string key = stream.LinkChain + "::" + (element?.UniqueId ?? elementId.ToString());
        var info = new ElementInfo
        {
            Key = key,
            // RVT link instances are always entered; their own elements are
            // tested against the crop one by one inside the link.
            IsModel = element?.Category?.CategoryType == CategoryType.Model,
            IsLinkInstance = isLink,
            Category = category,
            Label = category + " | " + ExportText.Clean(element?.Name, 120) + " | id " + elementId + (stream.InLink ? " | linked" : ""),
        };
        if (!isLink && element != null)
        {
            var box = element.get_BoundingBox(null);
            if (box != null)
            {
                var corners = ExportRegion.BoxPoints(box).ToArray();
                info.Min = new Point3(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z));
                info.Max = new Point3(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z));
            }
        }
        string linkChain = stream.LinkChain;
        info.Identity = surface => Identity(doc, element, key, linkChain, surface);
        bool proceed = stream.BeginElement(info);
        if (++elementCallbacks % 50 == 0) Pump();
        return proceed ? RenderNodeAction.Proceed : RenderNodeAction.Skip;
    }

    public void OnElementEnd(ElementId elementId) => stream.EndElement();

    public RenderNodeAction OnInstanceBegin(InstanceNode node)
    {
        string? symbol = null;
        try { symbol = node.GetSymbolGeometryId()?.AsUniqueIdentifier(); }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { SymbolIdFailures++; }
        return stream.BeginInstance(Adapter.Matrix(node.GetTransform()), symbol) ? RenderNodeAction.Proceed : RenderNodeAction.Skip;
    }

    public void OnInstanceEnd(InstanceNode node) => stream.EndInstance();

    public RenderNodeAction OnLinkBegin(LinkNode node)
    {
        // Documents are compared by the stream's own push/pop balance, never by
        // reference: Revit can return a new Document wrapper on every call.
        var doc = node.GetDocument();
        string key = pendingLinkKey ?? node.SymbolId.ToString();
        pendingLinkKey = null;
        bool proceed = stream.BeginLink(Adapter.Matrix(node.GetTransform()), doc, key);
        if (proceed) trace.Line("Entering link " + ExportText.Clean(doc?.Title, 200) + " (" + key + ")");
        return proceed ? RenderNodeAction.Proceed : RenderNodeAction.Skip;
    }

    public void OnLinkEnd(LinkNode node) => stream.EndLink();

    public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;
    public void OnFaceEnd(FaceNode node) { }
    public void OnRPC(RPCNode node) { }
    public void OnLight(LightNode node) { }

    public void OnMaterial(MaterialNode node)
    {
        var doc = CurrentDocument;
        var native = node.Color;
        string cacheKey = DocumentId(doc) + "|" + node.MaterialId.Value.ToString(CultureInfo.InvariantCulture) + "|" + node.Transparency.ToString("R", CultureInfo.InvariantCulture) + "|" + (native.IsValid ? native.Red + "," + native.Green + "," + native.Blue : "invalid");
        if (!materials.TryGetValue(cacheKey, out var cached))
        {
            var material = ReadMaterial(doc, node);
            cached = (material, Contract.Hash(Contract.Encode(material)));
            materials.Add(cacheKey, cached);
        }
        stream.SetMaterial(cached.Material, cached.Key);
    }

    private PlainMaterial ReadMaterial(Document doc, MaterialNode node)
    {
        var source = doc.GetElement(node.MaterialId) as Material;
        var color = source?.Color ?? node.Color;
        if (!color.IsValid) color = node.Color;
        var material = new PlainMaterial
        {
            Name = ExportText.Clean(source?.Name ?? "Default", 256),
            Red = color.IsValid ? color.Red / 255d : .7, Green = color.IsValid ? color.Green / 255d : .7, Blue = color.IsValid ? color.Blue / 255d : .7,
            Opacity = Math.Max(0, Math.Min(1, 1 - node.Transparency)), Roughness = .8
        };
        if (source == null) return material;
        try
        {
            if (doc.GetElement(source.AppearanceAssetId) is AppearanceAssetElement appearance)
            {
                var asset = appearance.GetRenderingAsset();
                if (asset.FindByName("generic_glossiness") is Autodesk.Revit.DB.Visual.AssetPropertyDouble gloss) material.Roughness = Math.Max(.08, Math.Sqrt(1 - Math.Max(0, Math.Min(1, gloss.Value))));
                if (asset.FindByName("generic_is_metal") is Autodesk.Revit.DB.Visual.AssetPropertyBoolean metal && metal.Value) material.Metallic = 1;
            }
            if (doc.GetElement(source.CutForegroundPatternId) is FillPatternElement patternElement)
            {
                var pattern = patternElement.GetFillPattern();
                material.CutPattern = new CutPatternDefinition
                {
                    Name = ExportText.Clean(pattern.Name, 256), Target = pattern.Target.ToString(), Solid = pattern.IsSolidFill,
                    Grids = pattern.GetFillGrids().Select(g => new CutGridDefinition { Angle = g.Angle, OriginXFeet = g.Origin.U, OriginYFeet = g.Origin.V, OffsetFeet = g.Offset, ShiftFeet = g.Shift, SegmentsFeet = g.GetSegments().ToArray() }).ToArray()
                };
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { MaterialFailures++; } // appearance details are optional; colour is kept
        return material;
    }

    public void OnPolymesh(PolymeshTopology mesh)
    {
        if (!stream.WantsGeometry || progress.CancelRequested) return;
        var nativePoints = mesh.GetPoints();
        var points = new Point3[nativePoints.Count];
        for (int i = 0; i < points.Length; i++) { var p = nativePoints[i]; points[i] = new Point3(p.X, p.Y, p.Z); }
        var facets = mesh.GetFacets();
        var corners = new int[facets.Count * 3];
        for (int i = 0; i < facets.Count; i++) { var f = facets[i]; corners[i * 3] = f.V1; corners[i * 3 + 1] = f.V2; corners[i * 3 + 2] = f.V3; }
        var nativeNormals = mesh.GetNormals();
        var normals = new Point3[nativeNormals.Count];
        for (int i = 0; i < normals.Length; i++) { var n = nativeNormals[i]; normals[i] = new Point3(n.X, n.Y, n.Z); }
        var layout = mesh.DistributionOfNormals == DistributionOfNormals.AtEachPoint ? NormalLayout.PerVertex : mesh.DistributionOfNormals == DistributionOfNormals.OnEachFacet ? NormalLayout.PerFace : NormalLayout.Single;
        stream.AddMesh(points, corners, normals, layout);
        Pump();
    }

    private void Pump()
    {
        var s = stream.Stats;
        if (!progress.Pump("Collecting model geometry", new ExportProgressCounts { Elements = s.ElementsBegun, PlacedTriangles = s.FacetsKept + s.TrianglesReused, Reused = s.InstancesReused, Current = stream.CurrentElementLabel }))
            throw new OperationCanceledException("Export cancelled");
        trace.Heartbeat(() => "elements " + s.ElementsBegun + " (exported " + s.ElementsExported + ", outside crop " + s.SkippedOutsideCrop + "), facets received " + s.FacetsReceived + ", kept " + s.FacetsKept + ", reused instances " + s.InstancesReused + ", culled " + s.InstancesCulled + ", current: " + (stream.CurrentElementLabel ?? "-"));
    }

    private string DocumentId(Document doc)
    {
        if (!documentIds.TryGetValue(doc, out var id)) { id = ExportText.Clean(doc.ProjectInformation?.UniqueId ?? doc.Title, 256); documentIds.Add(doc, id); }
        return id;
    }

    private ElementIdentity Identity(Document doc, Element? e, string key, string link, PlainMaterial surface)
    {
        var type = e == null ? null : doc.GetElement(e.GetTypeId()) as ElementType;
        var identity = new ElementIdentity
        {
            UniqueId = e?.UniqueId ?? key, DocumentId = DocumentId(doc), LinkInstanceId = string.IsNullOrEmpty(link) ? null : link,
            ElementId = e?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "", Category = ExportText.Clean(e?.Category?.Name, 256),
            FamilyName = ExportText.Clean(type?.FamilyName, 256), TypeName = ExportText.Clean(type?.Name, 256),
            LevelName = e == null ? "" : ExportText.Clean((doc.GetElement(e.LevelId) as Level)?.Name, 256),
            MaterialName = surface.Name, CutPatternName = surface.CutPattern?.Name ?? ""
        };
        if (e == null) return identity;
        identity.Mark = ExportText.Clean(e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), 2048);
        foreach (string name in parameters.Take(20))
        {
            var p = e.LookupParameter(name) ?? type?.LookupParameter(name);
            if (p == null) continue;
            string value = p.StorageType == StorageType.String ? p.AsString() ?? ""
                : p.AsValueString() ?? (p.StorageType == StorageType.Integer ? p.AsInteger().ToString(CultureInfo.InvariantCulture)
                : p.StorageType == StorageType.ElementId ? p.AsElementId().Value.ToString(CultureInfo.InvariantCulture)
                : p.StorageType == StorageType.Double ? p.AsDouble().ToString("R", CultureInfo.InvariantCulture) : "");
            identity.Parameters[ExportText.Clean(name, 256)] = ExportText.Clean(value, 2048);
        }
        return identity;
    }
}

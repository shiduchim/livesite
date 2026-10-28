using System.Diagnostics;

namespace Offline3D.RevitExporter;

/// <summary>What the Revit adapter knows about an element when it begins.</summary>
internal sealed class ElementInfo
{
    /// <summary>Link chain plus UniqueId; also the node-name prefix the reader groups by.</summary>
    public string Key { get; set; } = "";
    public bool IsModel { get; set; }
    public bool IsLinkInstance { get; set; }
    public string Category { get; set; } = "Unknown";
    private string? label;
    /// <summary>Short human description for diagnostics only. Built on first use from
    /// <see cref="LabelFactory"/>: most elements are skipped and never need one.</summary>
    public string Label { get => label ??= LabelFactory?.Invoke() ?? ""; set => label = value; }
    public Func<string>? LabelFactory { get; set; }
    /// <summary>Bounding box in the element's own document coordinates, if known.</summary>
    public Point3? Min { get; set; }
    public Point3? Max { get; set; }
    /// <summary>Builds the identity on first use, given the first stored material.</summary>
    public Func<PlainMaterial, ElementIdentity> Identity { get; set; } = _ => new ElementIdentity();
}

internal sealed class GeometryStreamOptions
{
    /// <summary>Exact crop volumes in host coordinates (feet).</summary>
    public ExportVolume[] Volumes { get; set; } = Array.Empty<ExportVolume>();
    /// <summary>The native section box applied to the view, for cut evidence only.</summary>
    public ExportVolume? NativeSectionBox { get; set; }
    public double MinimumPartFeet { get; set; }
    /// <summary>Reuse geometry of repeated family instances (Revit 2023+ SymbolGeometryId).</summary>
    public bool ReuseInstances { get; set; } = true;
    /// <summary>Traversed instances that must match the first capture before reuse starts.</summary>
    public int ReuseVerifications { get; set; } = 1;
    /// <summary>A pending batch is written as its own placement when it reaches this size.</summary>
    public int MaxBatchVertices { get; set; } = 750000;
}

/// <summary>
/// Turns the nested begin/end callbacks of a Revit custom export into
/// placements. It has no Revit types so its bookkeeping (link and instance
/// stacks, crop selection, instance reuse, batching) can be tested on any
/// machine with synthetic callback sequences.
///
/// Callback contract (Revit API, RenderNodeAction.Skip): an end notification
/// is still sent for a node whose begin returned Skip, so every Begin* here
/// pushes exactly one entry and every End* pops exactly one.
/// </summary>
internal sealed class GeometryStream
{
    private sealed class Space
    {
        public Matrix4 World = Matrix4.Identity;
        public Space? Parent;
        public Capture? Capture;
    }
    private sealed class DocumentFrame
    {
        public object? Document;
        public string LinkChain = "";
        public Matrix4 Frame = Matrix4.Identity;
    }
    private sealed class Batch
    {
        public Space Space = null!;
        public PlainMaterial Material = null!;
        public string MaterialKey = "";
        public MeshBuilder Builder = new MeshBuilder();
    }
    private sealed class ElementFrame
    {
        public ElementInfo Info = null!;
        public bool Suppressed, Exported, IdentityAdded;
        public ElementIdentity? Identity;
        public readonly List<Batch> Pending = new List<Batch>();
        public long Started, Triangles;
    }
    private sealed class Part
    {
        public MeshHandle Handle = null!;
        public Matrix4 Relative = null!;
    }
    private enum ReuseState { Capturing, Captured, Verified, Unsafe }
    private sealed class ReuseRecord
    {
        public ReuseState State = ReuseState.Capturing;
        public List<Part> Parts = new List<Part>();
        public Point3 Min, Max;
        public bool Empty = true;
        public int Attempts, Verifications;
        public long Triangles;
    }
    private sealed class Capture
    {
        public Space Root = null!;
        public Matrix4 InverseWorld = null!;
        public ReuseRecord Record = null!;
        public bool Verifying, Complete = true, Empty = true;
        public List<Part> Parts = new List<Part>();
        public double MinX = double.PositiveInfinity, MinY = double.PositiveInfinity, MinZ = double.PositiveInfinity;
        public double MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity, MaxZ = double.NegativeInfinity;
    }

    private const int MaxCaptureAttempts = 4;
    private readonly GeometryStreamOptions options;
    private readonly SceneWriter writer;
    private readonly Stack<Space> spaces = new Stack<Space>();
    private readonly Stack<DocumentFrame> documents = new Stack<DocumentFrame>();
    private readonly Stack<bool> linkEntered = new Stack<bool>();
    private readonly Stack<ElementFrame> elements = new Stack<ElementFrame>();
    private readonly List<Capture> captures = new List<Capture>();
    private readonly Dictionary<string, ReuseRecord> reuse = new Dictionary<string, ReuseRecord>(StringComparer.Ordinal);
    private readonly HashSet<string> exported = new HashSet<string>(StringComparer.Ordinal);
    private PlainMaterial material = new PlainMaterial();
    private string materialKey = "default";

    public ExportStatistics Stats { get; } = new ExportStatistics();
    public List<ElementIdentity> Identities { get; } = new List<ElementIdentity>();

    public GeometryStream(GeometryStreamOptions options, SceneWriter writer, object? hostDocument)
    {
        this.options = options; this.writer = writer;
        spaces.Push(new Space());
        documents.Push(new DocumentFrame { Document = hostDocument });
    }

    /// <summary>The Revit document whose element ids the next callbacks refer to.</summary>
    public object? CurrentDocument => documents.Peek().Document;
    /// <summary>Prefix for element keys: empty in the host, "::linkUniqueId" per link level.</summary>
    public string LinkChain => documents.Peek().LinkChain;
    public bool InLink => documents.Count > 1;
    public bool WantsGeometry => elements.Count > 0 && !elements.Peek().Suppressed;
    public string? CurrentElementLabel => elements.Count > 0 ? elements.Peek().Info.Label : null;
    public int Depth => spaces.Count;

    // ---- elements ----

    public bool BeginElement(ElementInfo info)
    {
        var frame = new ElementFrame { Info = info, Started = Stopwatch.GetTimestamp() };
        elements.Push(frame);
        Stats.ElementsBegun++;
        if (InLink) Stats.LinkedElementsBegun++;
        // An element (or link) inside a captured instance makes the capture
        // unsafe to replay: identities and visibility are per element.
        MarkCapturesIncomplete();
        if (!info.IsModel && !info.IsLinkInstance) { frame.Suppressed = true; Stats.SkippedNotModel++; return false; }
        if (exported.Contains(info.Key)) { frame.Suppressed = true; Stats.SkippedAlreadyExported++; return false; }
        if (info.Min.HasValue && info.Max.HasValue && options.Volumes.Length > 0 && !IntersectsAny(info.Min.Value, info.Max.Value, documents.Peek().Frame))
        { frame.Suppressed = true; Stats.SkippedOutsideCrop++; return false; }
        if (options.MinimumPartFeet > 0 && !info.IsLinkInstance && info.Min.HasValue && info.Max.HasValue)
        {
            var a = info.Min.Value; var b = info.Max.Value;
            if (Math.Max(b.X - a.X, Math.Max(b.Y - a.Y, b.Z - a.Z)) < options.MinimumPartFeet) { frame.Suppressed = true; Stats.SkippedTiny++; return false; }
        }
        return true;
    }

    public void EndElement()
    {
        if (elements.Count == 0) { Stats.UnbalancedEnds++; return; }
        var frame = elements.Pop();
        if (!frame.Suppressed)
        {
            foreach (var batch in frame.Pending.ToArray()) Flush(frame, batch);
            frame.Pending.Clear();
            if (frame.Exported) { exported.Add(frame.Info.Key); Stats.ElementsExported++; }
        }
        double ms = (Stopwatch.GetTimestamp() - frame.Started) * 1000.0 / Stopwatch.Frequency;
        if (!frame.Info.IsLinkInstance) Stats.RecordElement(frame.Info, ms, frame.Triangles);
    }

    // ---- instances ----

    /// <returns>False when the instance was culled or replayed from an earlier capture.</returns>
    public bool BeginInstance(Matrix4 local, string? symbolKey)
    {
        var space = new Space { World = spaces.Peek().World * local, Parent = spaces.Peek() };
        spaces.Push(space);
        Stats.InstancesBegun++;
        if (!WantsGeometry || !options.ReuseInstances || string.IsNullOrEmpty(symbolKey) || options.Volumes.Length == 0) return true;

        string key = LinkChain + "|" + symbolKey;
        if (!reuse.TryGetValue(key, out var record)) { record = new ReuseRecord(); reuse.Add(key, record); }
        switch (record.State)
        {
            case ReuseState.Unsafe:
                return true;
            case ReuseState.Capturing:
                if (record.Attempts >= MaxCaptureAttempts) return true;
                record.Attempts++;
                StartCapture(space, record, verifying: false);
                return true;
        }
        // Captured or verified: the record's bounds are complete, so this
        // placement can be classified against the crop without tessellating it.
        bool verify = record.State == ReuseState.Captured && record.Verifications < options.ReuseVerifications;
        if (record.Empty)
        {
            if (verify) { StartCapture(space, record, verifying: true); return true; }
            Stats.InstancesReplayedEmpty++; return false;
        }
        bool inside = false, any = false;
        foreach (var volume in options.Volumes)
        {
            if (!volume.IntersectsBounds(record.Min, record.Max, space.World, out bool contained)) continue;
            any = true; if (contained) { inside = true; break; }
        }
        if (!any) { Stats.InstancesCulled++; MarkCapturesIncomplete(); return false; }
        if (!inside) { Stats.InstancesStraddling++; return true; }
        if (verify) { StartCapture(space, record, verifying: true); return true; }
        Replay(record, space);
        return false;
    }

    public void EndInstance()
    {
        if (spaces.Count <= 1) { Stats.UnbalancedEnds++; return; }
        var space = spaces.Peek();
        if (elements.Count > 0) FlushSpace(elements.Peek(), space);
        spaces.Pop();
        if (space.Capture != null) FinishCapture(space.Capture);
    }

    private void StartCapture(Space space, ReuseRecord record, bool verifying)
    {
        Matrix4 inverse;
        try { inverse = space.World.Inverse(); }
        catch (InvalidDataException) { Stats.CapturesIncomplete++; return; } // degenerate instance transform: traverse normally
        var capture = new Capture { Root = space, InverseWorld = inverse, Record = record, Verifying = verifying };
        space.Capture = capture;
        captures.Add(capture);
    }

    private void FinishCapture(Capture capture)
    {
        captures.Remove(capture);
        var record = capture.Record;
        if (!capture.Complete)
        {
            if (!capture.Verifying) Stats.CapturesIncomplete++;
            return;
        }
        if (!capture.Verifying)
        {
            if (record.State != ReuseState.Capturing) return;
            record.Parts = capture.Parts; record.Empty = capture.Empty;
            if (!capture.Empty) { record.Min = new Point3(capture.MinX, capture.MinY, capture.MinZ); record.Max = new Point3(capture.MaxX, capture.MaxY, capture.MaxZ); }
            record.Triangles = capture.Parts.Sum(p => (long)p.Handle.Triangles);
            record.State = options.ReuseVerifications > 0 ? ReuseState.Captured : ReuseState.Verified;
            Stats.Captures++;
            return;
        }
        if (SameParts(record.Parts, capture.Parts))
        {
            if (++record.Verifications >= options.ReuseVerifications) { record.State = ReuseState.Verified; Stats.ReuseVerified++; }
        }
        else { record.State = ReuseState.Unsafe; Stats.ReuseRejected++; }
    }

    private static bool SameParts(List<Part> a, List<Part> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i].Handle; var y = b[i].Handle;
            if (x.SourceHash != y.SourceHash || x.MaterialKey != y.MaterialKey || x.Triangles != y.Triangles) return false;
            var p = a[i].Relative.Values; var q = b[i].Relative.Values;
            for (int k = 0; k < 16; k++) if (Math.Abs(p[k] - q[k]) > 1e-6 * Math.Max(1, Math.Abs(p[k]))) return false;
        }
        return true;
    }

    private void Replay(ReuseRecord record, Space space)
    {
        var frame = elements.Peek();
        foreach (var part in record.Parts)
        {
            var world = space.World * part.Relative;
            string name = frame.Info.Key + "/" + writer.PlacementCount;
            var identity = IdentityFor(frame, name, part.Handle.Material);
            writer.AddReference(name, part.Handle, world, identity);
            AfterPlacement(frame, part.Handle, world);
        }
        frame.Exported = true;
        frame.Triangles += record.Triangles;
        Stats.InstancesReused++;
        Stats.TrianglesReused += record.Triangles;
        Stats.AddCategoryTriangles(frame.Info.Category, record.Triangles);
    }

    // ---- links ----

    /// <returns>False when the link has no loaded document; it is skipped.</returns>
    public bool BeginLink(Matrix4 local, object? document, string linkKey)
    {
        MarkCapturesIncomplete();
        if (document == null) { linkEntered.Push(false); Stats.LinksUnloaded++; return false; }
        var world = spaces.Peek().World * local;
        spaces.Push(new Space { World = world, Parent = spaces.Peek() });
        documents.Push(new DocumentFrame { Document = document, LinkChain = LinkChain + "::" + linkKey, Frame = world });
        linkEntered.Push(true);
        Stats.LinksEntered++;
        return true;
    }

    public void EndLink()
    {
        if (linkEntered.Count == 0) { Stats.UnbalancedEnds++; return; }
        if (!linkEntered.Pop()) return;
        var space = spaces.Peek();
        if (elements.Count > 0) FlushSpace(elements.Peek(), space);
        spaces.Pop(); documents.Pop();
    }

    // ---- materials and meshes ----

    public void SetMaterial(PlainMaterial value, string key) { material = value; materialKey = key; Stats.MaterialChanges++; }

    /// <param name="points">Points in the current instance coordinates (feet).</param>
    /// <param name="corners">Three point indices per facet.</param>
    /// <param name="normals">Normals as given by <paramref name="layout"/> (per facet: one per facet).</param>
    public void AddMesh(Point3[] points, int[] corners, Point3[] normals, NormalLayout layout)
    {
        if (!WantsGeometry) return;
        int facets = corners.Length / 3;
        if (facets == 0 || points.Length == 0) return;
        var frame = elements.Peek(); var space = spaces.Peek();
        Stats.Polymeshes++; Stats.FacetsReceived += facets;
        if (options.NativeSectionBox != null) CountSectionBoxContact(points, space.World);

        var selection = options.Volumes.Length == 0 ? null : new CropMeshSelection(points, space.World, options.Volumes);
        int[] kept = corners; int keptCorners = corners.Length;
        Point3[] keptNormals = normals;
        if (selection != null && !selection.FullyInside)
        {
            if (selection.Outside) { Stats.FacetsOutsideCrop += facets; Stats.PolymeshesOutsideCrop++; MarkCapturesIncomplete(); return; }
            kept = new int[corners.Length]; keptCorners = 0;
            var faceNormals = layout == NormalLayout.PerFace ? new List<Point3>(facets) : null;
            for (int f = 0; f < facets; f++)
            {
                int a = corners[f * 3], b = corners[f * 3 + 1], c = corners[f * 3 + 2];
                if (!selection.Keep(a, b, c)) continue;
                kept[keptCorners++] = a; kept[keptCorners++] = b; kept[keptCorners++] = c;
                faceNormals?.Add(f < normals.Length ? normals[f] : new Point3());
            }
            int dropped = facets - keptCorners / 3;
            if (dropped > 0) { Stats.FacetsOutsideCrop += dropped; MarkCapturesIncomplete(); }
            if (keptCorners == 0) { Stats.PolymeshesOutsideCrop++; return; }
            if (faceNormals != null) keptNormals = faceNormals.ToArray();
        }

        foreach (var capture in captures) Include(capture, points, kept, keptCorners, space.World);
        var batch = BatchFor(frame, space);
        batch.Builder.Append(points, keptNormals, kept, keptCorners, layout);
        frame.Exported = true;
        long count = keptCorners / 3;
        frame.Triangles += count; Stats.FacetsKept += count;
        Stats.AddCategoryTriangles(frame.Info.Category, count);
        if (batch.Builder.VertexCount >= options.MaxBatchVertices)
        {
            Stats.BatchesSplit++;
            Flush(frame, batch);
        }
    }

    // ---- batches and placements ----

    private Batch BatchFor(ElementFrame frame, Space space)
    {
        foreach (var existing in frame.Pending)
            if (existing.Space == space && existing.MaterialKey == materialKey) return existing;
        var batch = new Batch { Space = space, Material = material, MaterialKey = materialKey };
        frame.Pending.Add(batch);
        return batch;
    }

    private void FlushSpace(ElementFrame frame, Space space)
    {
        if (frame.Suppressed || frame.Pending.Count == 0) return;
        foreach (var batch in frame.Pending.Where(b => b.Space == space).ToArray()) Flush(frame, batch);
    }

    private void Flush(ElementFrame frame, Batch batch)
    {
        frame.Pending.Remove(batch);
        if (batch.Builder.TriangleCount == 0) return;
        var geometry = batch.Builder.Finish();
        string name = frame.Info.Key + "/" + writer.PlacementCount;
        var identity = IdentityFor(frame, name, batch.Material);
        var handle = writer.Add(name, geometry, batch.Space.World, batch.Material, batch.MaterialKey, identity);
        AfterPlacement(frame, handle, batch.Space.World);
        Stats.BatchesWritten++;
    }

    private ElementIdentity IdentityFor(ElementFrame frame, string name, PlainMaterial surface)
    {
        if (frame.Identity == null)
        {
            frame.Identity = frame.Info.Identity(surface);
            frame.Identity.MeshName = name;
        }
        if (!frame.IdentityAdded) { Identities.Add(frame.Identity); frame.IdentityAdded = true; if (InLink) Stats.LinkedElementsExported++; }
        return frame.Identity;
    }

    /// <summary>Every placement made while captures are open belongs to them.</summary>
    private void AfterPlacement(ElementFrame frame, MeshHandle handle, Matrix4 world)
    {
        foreach (var capture in captures)
        {
            capture.Parts.Add(new Part { Handle = handle, Relative = capture.InverseWorld * world });
            capture.Empty = false;
        }
    }

    private void Include(Capture capture, Point3[] points, int[] corners, int count, Matrix4 world)
    {
        var m = (capture.InverseWorld * world).Values;
        for (int i = 0; i < count; i++)
        {
            var p = points[corners[i]];
            double x = m[0] * p.X + m[1] * p.Y + m[2] * p.Z + m[3], y = m[4] * p.X + m[5] * p.Y + m[6] * p.Z + m[7], z = m[8] * p.X + m[9] * p.Y + m[10] * p.Z + m[11];
            if (x < capture.MinX) capture.MinX = x; if (y < capture.MinY) capture.MinY = y; if (z < capture.MinZ) capture.MinZ = z;
            if (x > capture.MaxX) capture.MaxX = x; if (y > capture.MaxY) capture.MaxY = y; if (z > capture.MaxZ) capture.MaxZ = z;
        }
    }

    private void MarkCapturesIncomplete() { foreach (var capture in captures) capture.Complete = false; }

    private bool IntersectsAny(Point3 min, Point3 max, Matrix4 toHost)
    {
        foreach (var volume in options.Volumes) if (volume.IntersectsBounds(min, max, toHost, out _)) return true;
        return false;
    }

    /// <summary>Evidence of whether Revit cuts geometry at the section box:
    /// points lying on its faces (to 1/1000 ft) are counted.</summary>
    private void CountSectionBoxContact(Point3[] points, Matrix4 world)
    {
        var box = options.NativeSectionBox!;
        var m = (box.HostToLocal * world).Values; const double eps = 1e-3;
        bool touched = false;
        foreach (var p in points)
        {
            double x = m[0] * p.X + m[1] * p.Y + m[2] * p.Z + m[3], y = m[4] * p.X + m[5] * p.Y + m[6] * p.Z + m[7], z = m[8] * p.X + m[9] * p.Y + m[10] * p.Z + m[11];
            if (x < box.Min.X - eps || y < box.Min.Y - eps || z < box.Min.Z - eps || x > box.Max.X + eps || y > box.Max.Y + eps || z > box.Max.Z + eps) Stats.PointsOutsideSectionBox++;
            else if (Math.Abs(x - box.Min.X) <= eps || Math.Abs(x - box.Max.X) <= eps || Math.Abs(y - box.Min.Y) <= eps || Math.Abs(y - box.Max.Y) <= eps || Math.Abs(z - box.Min.Z) <= eps || Math.Abs(z - box.Max.Z) <= eps)
            { Stats.PointsOnSectionBoxFaces++; touched = true; }
        }
        if (touched) Stats.PolymeshesTouchingSectionBox++;
    }

    /// <summary>Called after the export; any frame still open means unbalanced callbacks.</summary>
    public void Finish()
    {
        while (elements.Count > 0) { Stats.UnbalancedEnds++; EndElement(); }
        Stats.OpenSpacesAtEnd = spaces.Count - 1;
        Stats.ReuseKeys = reuse.Count;
        Stats.ReuseKeysVerified = reuse.Values.Count(r => r.State == ReuseState.Verified);
        Stats.ReuseKeysUnsafe = reuse.Values.Count(r => r.State == ReuseState.Unsafe);
    }
}

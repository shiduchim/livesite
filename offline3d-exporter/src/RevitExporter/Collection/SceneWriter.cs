using System.Globalization;
using System.Text;

namespace Offline3D.RevitExporter;

/// <summary>A stored source mesh that later placements can reference.</summary>
internal sealed class MeshHandle
{
    internal int Entry;            // stored geometry (possibly the mirrored form of the source)
    internal bool EntryMirrored;   // Entry holds the source mirrored in local X
    internal Point3 OriginFeet;
    internal string MaterialKey = "";
    internal PlainMaterial Material = null!;
    internal ulong SourceHash;     // hash of the unmirrored source bytes (verification)
    public int Triangles { get; internal set; }
}

internal sealed class SceneWriteResult
{
    public int Nodes { get; set; }
    public int NodesBeforeMerge { get; set; }
    public int MergedGroups { get; set; }
    public int MergedPlacements { get; set; }
    public int UniqueMeshes { get; set; }
    public int SharedPlacements { get; set; }
    public long Triangles { get; set; }
    public long UniqueTriangles { get; set; }
    public long Bytes { get; set; }
    public int Materials { get; set; }
    public int Elements { get; set; }
}

/// <summary>
/// Exporter-owned GLB writer. Unique geometry is spooled to disk immediately.
/// Identical geometry (same bytes and material) is stored once and placed with
/// node matrices. At the end, geometry used by only one placement is merged per
/// element and material, so large crops stay inside the reader's node and
/// accessor limits. Output stays within the frozen reader contract: one
/// triangle primitive per mesh, float POSITION/NORMAL, uint32 indices, flat
/// positive-determinant node matrices and optional extras.featureEdges.
/// </summary>
internal sealed class SceneWriter : IDisposable
{
    // Reader limits from Contract.ValidateGlb (Core/Contract.cs, unchanged).
    public const int MaxNodes = 100000, MaxMeshes = 100000, MaxAccessors = 300000, MaxMaterials = 100000, MaxElements = 100000;
    public const int MaxVerticesPerMesh = 3000000, MaxIndicesPerAccessor = 16000000;
    public const double MaxTranslationMetres = 1e7;
    private const int MergedVertexLimit = 1000000, MergedIndexLimit = 6000000;

    private sealed class Entry
    {
        public FileStream Source = null!;
        public long Offset;
        public int CoreLength, Vertices, Indices, Edges, Material;
        public Point3 Min, Max;
        public ulong Hash;
        public int Twin = -1;     // mirrored counterpart, created on demand
        public int Uses, FirstElement;
        public long TotalLength => CoreLength + (long)Edges * 4;
    }
    private sealed class Placement
    {
        public string Name = "";
        public int Entry, Element;
        public double[] Matrix = null!; // row-major graphics transform
    }

    private readonly string spoolPath, mergePath;
    private readonly FileStream spool;
    private FileStream? mergeSpool;
    private readonly Matrix4 hostToGraphics;
    private readonly Matrix4 localGraphicsInverse = Matrix4.Graphics(new Point3()).Inverse();
    private readonly bool includeFeatureEdges;
    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<ulong, List<int>> byHash = new Dictionary<ulong, List<int>>();
    private readonly List<Placement> placements = new List<Placement>();
    private readonly List<PlainMaterial> materials = new List<PlainMaterial>();
    private readonly Dictionary<string, int> materialIds = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<ElementIdentity> elements = new List<ElementIdentity>();
    private readonly Dictionary<string, int> elementIds = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly byte[] compareBuffer = new byte[65536];
    private readonly double[] bounds = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
    // Projection of the node count after merging, kept up to date so a scope
    // that cannot fit fails early instead of after the full collection.
    private readonly Dictionary<long, int> singleUseGroups = new Dictionary<long, int>();
    private int sharedPlacements;

    public long PlacedTriangles { get; private set; }
    public long UniqueTriangles { get; private set; }
    public int PlacementCount => placements.Count;
    public int UniqueMeshCount => entries.Count;
    public long UniqueBytes => spool.Length;
    public int ProjectedNodes => sharedPlacements + singleUseGroups.Count;
    public int MaterialCount => materials.Count;
    public int ElementCount => elements.Count;

    public double[] BoundsGraphics => bounds.Select((v, i) => i < 3 ? Math.Min(v, bounds[i + 3] - .0001) : Math.Max(v, bounds[i - 3] + .0001)).ToArray();

    public SceneWriter(string directory, Matrix4 hostToGraphics, bool includeFeatureEdges = true)
    {
        Directory.CreateDirectory(directory);
        this.hostToGraphics = hostToGraphics; this.includeFeatureEdges = includeFeatureEdges;
        string id = Guid.NewGuid().ToString("N");
        spoolPath = Path.Combine(directory, "geometry-" + id + ".spool");
        mergePath = Path.Combine(directory, "merged-" + id + ".spool");
        spool = new FileStream(spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Stores (or finds) the geometry and places it with <paramref name="localToHost"/>.</summary>
    public MeshHandle Add(string name, MeshData geometry, Matrix4 localToHost, PlainMaterial material, string materialKey, ElementIdentity identity)
    {
        if (geometry.Positions.Length == 0 || geometry.Positions.Length != geometry.Normals.Length || geometry.Positions.Length % 3 != 0 || geometry.Indices.Length % 3 != 0)
            throw new InvalidDataException("Invalid indexed geometry");
        int materialIndex = MaterialIndex(material, materialKey);
        var source = Encode(geometry);
        ulong sourceHash = Hash(source, materialIndex);
        var matrix = NodeMatrix(localToHost, geometry.OriginFeet);
        bool mirrored = Determinant(matrix) < 0;
        int entry = mirrored ? Store(Encode(Mirror(geometry)), materialIndex) : Store(source, materialIndex, sourceHash);
        var handle = new MeshHandle { Entry = entry, EntryMirrored = mirrored, OriginFeet = geometry.OriginFeet, MaterialKey = materialKey, Material = material, SourceHash = sourceHash, Triangles = geometry.Indices.Length / 3 };
        Place(name, entry, mirrored ? matrix * MirrorX : matrix, identity);
        return handle;
    }

    /// <summary>Places already stored geometry again (a reused family instance).</summary>
    public void AddReference(string name, MeshHandle handle, Matrix4 localToHost, ElementIdentity identity)
    {
        var matrix = NodeMatrix(localToHost, handle.OriginFeet);
        bool mirrored = Determinant(matrix) < 0;
        int entry = handle.Entry;
        if (mirrored != handle.EntryMirrored) entry = TwinOf(entry);
        Place(name, entry, mirrored ? matrix * MirrorX : matrix, identity);
    }

    private static readonly Matrix4 MirrorX = new Matrix4(-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    private Matrix4 NodeMatrix(Matrix4 localToHost, Point3 origin)
    {
        var translate = new Matrix4(1, 0, 0, origin.X, 0, 1, 0, origin.Y, 0, 0, 1, origin.Z, 0, 0, 0, 1);
        return hostToGraphics * localToHost * translate * localGraphicsInverse;
    }

    private static double Determinant(Matrix4 matrix)
    {
        var m = matrix.Values;
        return m[0] * (m[5] * m[10] - m[6] * m[9]) - m[1] * (m[4] * m[10] - m[6] * m[8]) + m[2] * (m[4] * m[9] - m[5] * m[8]);
    }

    private int MaterialIndex(PlainMaterial material, string key)
    {
        if (materialIds.TryGetValue(key, out int index)) return index;
        material.Validate();
        if (materials.Count >= MaxMaterials) throw new InvalidDataException(Limit("materials", materials.Count + 1, MaxMaterials));
        index = materials.Count; materials.Add(material); materialIds.Add(key, index);
        return index;
    }

    private void Place(string name, int entry, Matrix4 matrix, ElementIdentity identity)
    {
        var values = (double[])matrix.Values.Clone();
        if (Math.Abs(values[3]) > MaxTranslationMetres || Math.Abs(values[7]) > MaxTranslationMetres || Math.Abs(values[11]) > MaxTranslationMetres)
            throw new InvalidDataException("A model part is more than 10,000 km from the view crop. Check linked model positions (shared coordinates).");
        string elementKey = identity.DocumentId + "::" + identity.LinkInstanceId + "::" + identity.UniqueId;
        if (!elementIds.TryGetValue(elementKey, out int element))
        {
            if (elements.Count >= MaxElements) throw new InvalidDataException(Limit("separately selectable elements", elements.Count + 1, MaxElements));
            element = elements.Count; elementIds.Add(elementKey, element); elements.Add(identity);
        }
        var stored = entries[entry];
        TrackUse(entry, element);
        placements.Add(new Placement { Name = name, Entry = entry, Element = element, Matrix = values });
        PlacedTriangles += stored.Indices / 3;
        for (int corner = 0; corner < 8; corner++)
        {
            var p = matrix.Apply(new Point3((corner & 1) == 0 ? stored.Min.X : stored.Max.X, (corner & 2) == 0 ? stored.Min.Y : stored.Max.Y, (corner & 4) == 0 ? stored.Min.Z : stored.Max.Z));
            bounds[0] = Math.Min(bounds[0], p.X); bounds[1] = Math.Min(bounds[1], p.Y); bounds[2] = Math.Min(bounds[2], p.Z);
            bounds[3] = Math.Max(bounds[3], p.X); bounds[4] = Math.Max(bounds[4], p.Y); bounds[5] = Math.Max(bounds[5], p.Z);
        }
        if (ProjectedNodes > MaxNodes) throw new InvalidDataException(Limit("separate model parts", ProjectedNodes, MaxNodes));
    }

    private static long GroupKey(int element, int material) => ((long)element << 32) | (uint)material;

    private void TrackUse(int entry, int element)
    {
        var stored = entries[entry];
        stored.Uses++;
        if (stored.Uses == 1) { stored.FirstElement = element; long key = GroupKey(element, stored.Material); singleUseGroups.TryGetValue(key, out int count); singleUseGroups[key] = count + 1; return; }
        if (stored.Uses == 2)
        {
            // The first placement stops being mergeable; leave its group.
            long key = GroupKey(stored.FirstElement, stored.Material);
            int count = singleUseGroups[key] - 1;
            if (count == 0) singleUseGroups.Remove(key); else singleUseGroups[key] = count;
            sharedPlacements += 2; return;
        }
        sharedPlacements++;
    }

    private static string Limit(string what, long value, long limit) =>
        "This export would contain " + value.ToString("N0", CultureInfo.InvariantCulture) + " " + what + "; the PDF viewer accepts at most " + limit.ToString("N0", CultureInfo.InvariantCulture) +
        ". Use a smaller view crop, turn off All up / All down or Include geometry outside the crop, or choose Quick or Low detail. No parts were dropped silently.";

    // ---- geometry storage ----

    /// <summary>Spool layout per entry (unchanged from v0.5.2): int32 float count,
    /// positions, normals, int32 index count, indices, then feature-edge pairs.</summary>
    private static byte[] Encode(MeshData geometry)
    {
        var bytes = new byte[8 + geometry.Positions.Length * 8 + geometry.Indices.Length * 4];
        int vertexCount = geometry.Positions.Length / 3, offset = 0;
        void Int(int value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4); offset += 4; }
        Int(geometry.Positions.Length);
        foreach (float v in geometry.Positions) if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Non-finite position");
        foreach (float v in geometry.Normals) if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Non-finite normal");
        Buffer.BlockCopy(geometry.Positions, 0, bytes, offset, geometry.Positions.Length * 4); offset += geometry.Positions.Length * 4;
        Buffer.BlockCopy(geometry.Normals, 0, bytes, offset, geometry.Normals.Length * 4); offset += geometry.Normals.Length * 4;
        Int(geometry.Indices.Length);
        foreach (uint v in geometry.Indices) if (v >= vertexCount) throw new InvalidDataException("Invalid geometry index");
        Buffer.BlockCopy(geometry.Indices, 0, bytes, offset, geometry.Indices.Length * 4);
        return bytes;
    }

    private static MeshData Decode(byte[] core)
    {
        int floats = BitConverter.ToInt32(core, 0);
        var positions = new float[floats]; var normals = new float[floats];
        Buffer.BlockCopy(core, 4, positions, 0, floats * 4);
        Buffer.BlockCopy(core, 4 + floats * 4, normals, 0, floats * 4);
        int count = BitConverter.ToInt32(core, 4 + floats * 8);
        var indices = new uint[count];
        Buffer.BlockCopy(core, 8 + floats * 8, indices, 0, count * 4);
        return new MeshData { Positions = positions, Normals = normals, Indices = indices };
    }

    /// <summary>Local X mirror with corrected winding; node matrices stay positive.</summary>
    private static MeshData Mirror(MeshData geometry)
    {
        var positions = (float[])geometry.Positions.Clone(); var normals = (float[])geometry.Normals.Clone(); var indices = (uint[])geometry.Indices.Clone();
        for (int i = 0; i < positions.Length; i += 3) { positions[i] = -positions[i]; normals[i] = -normals[i]; }
        for (int i = 0; i < indices.Length; i += 3) { uint a = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = a; }
        return new MeshData { Positions = positions, Normals = normals, Indices = indices, OriginFeet = geometry.OriginFeet };
    }

    /// <summary>FNV-1a 64 over the bytes, mixed with the material. Hits are
    /// always confirmed byte for byte before geometry is shared.</summary>
    private static ulong Hash(byte[] data, int material)
    {
        ulong hash = 14695981039346656037UL ^ (ulong)(uint)material;
        for (int i = 0; i < data.Length; i++) { hash ^= data[i]; hash *= 1099511628211UL; }
        return hash;
    }

    private int Store(byte[] core, int material, ulong? knownHash = null)
    {
        ulong hash = knownHash ?? Hash(core, material);
        if (byHash.TryGetValue(hash, out var candidates))
            foreach (int candidate in candidates)
                if (entries[candidate].Material == material && SameBytes(entries[candidate], core)) return candidate;
        var geometry = Decode(core);
        int vertexCount = geometry.Positions.Length / 3;
        var edges = includeFeatureEdges ? FeatureEdgeBuilder.Build(geometry.Positions, geometry.Indices) : Array.Empty<uint>();
        long bytes = core.Length + (long)edges.Length * 4;
        if (spool.Length + bytes > SceneLimits.MaxSceneBytes - 1048576)
            throw new InvalidDataException("Unique geometry exceeds the 512 MiB scene budget of the PDF viewer. Repeated items already share geometry. Use a smaller view crop, turn off All up / All down, or choose a lower detail.");
        var entry = new Entry { Source = spool, Offset = spool.Length, CoreLength = core.Length, Vertices = vertexCount, Indices = geometry.Indices.Length, Edges = edges.Length, Material = material, Hash = hash };
        Bounds(geometry.Positions, out entry.Min, out entry.Max);
        spool.Position = spool.Length;
        spool.Write(core, 0, core.Length);
        if (edges.Length > 0) { var edgeBytes = new byte[edges.Length * 4]; Buffer.BlockCopy(edges, 0, edgeBytes, 0, edgeBytes.Length); spool.Write(edgeBytes, 0, edgeBytes.Length); }
        int index = entries.Count; entries.Add(entry);
        if (candidates == null) { candidates = new List<int>(); byHash.Add(hash, candidates); }
        candidates.Add(index);
        UniqueTriangles += geometry.Indices.Length / 3;
        return index;
    }

    private int TwinOf(int entryIndex)
    {
        var entry = entries[entryIndex];
        if (entry.Twin >= 0) return entry.Twin;
        var mirrored = Mirror(Decode(ReadCore(entry)));
        int twin = Store(Encode(mirrored), entry.Material);
        entries[entryIndex].Twin = twin; entries[twin].Twin = entryIndex;
        return twin;
    }

    private static void Bounds(float[] positions, out Point3 min, out Point3 max)
    {
        double x = double.PositiveInfinity, y = x, z = x, X = double.NegativeInfinity, Y = X, Z = X;
        for (int i = 0; i < positions.Length; i += 3)
        {
            x = Math.Min(x, positions[i]); y = Math.Min(y, positions[i + 1]); z = Math.Min(z, positions[i + 2]);
            X = Math.Max(X, positions[i]); Y = Math.Max(Y, positions[i + 1]); Z = Math.Max(Z, positions[i + 2]);
        }
        min = new Point3(x, y, z); max = new Point3(X, Y, Z);
    }

    private bool SameBytes(Entry entry, byte[] data)
    {
        if (entry.CoreLength != data.Length) return false;
        var stream = entry.Source;
        stream.Position = entry.Offset;
        for (int offset = 0; offset < data.Length;)
        {
            int count = Math.Min(compareBuffer.Length, data.Length - offset), read = 0;
            while (read < count) { int n = stream.Read(compareBuffer, read, count - read); if (n == 0) return false; read += n; }
            for (int i = 0; i < count; i++) if (compareBuffer[i] != data[offset + i]) return false;
            offset += count;
        }
        return true;
    }

    private static byte[] ReadExact(FileStream stream, long offset, long length)
    {
        var data = new byte[checked((int)length)];
        stream.Position = offset;
        for (int read = 0; read < data.Length;) { int n = stream.Read(data, read, data.Length - read); if (n == 0) throw new EndOfStreamException("Geometry spool is truncated"); read += n; }
        return data;
    }
    private static byte[] ReadCore(Entry entry) => ReadExact(entry.Source, entry.Offset, entry.CoreLength);
    private static uint[] ReadEdges(Entry entry)
    {
        if (entry.Edges == 0) return Array.Empty<uint>();
        var bytes = ReadExact(entry.Source, entry.Offset + entry.CoreLength, (long)entry.Edges * 4);
        var edges = new uint[entry.Edges]; Buffer.BlockCopy(bytes, 0, edges, 0, bytes.Length);
        return edges;
    }

    // ---- merging single-use geometry ----

    private void MergeSingleUse(SceneWriteResult result)
    {
        var groups = new Dictionary<long, List<int>>();
        var order = new List<long>();
        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            if (entries[p.Entry].Uses != 1) continue;
            long key = GroupKey(p.Element, entries[p.Entry].Material);
            if (!groups.TryGetValue(key, out var list)) { list = new List<int>(); groups.Add(key, list); order.Add(key); }
            list.Add(i);
        }
        var replaced = new HashSet<int>();
        var additions = new List<KeyValuePair<int, Placement>>();
        foreach (long key in order)
        {
            var members = groups[key];
            if (members.Count < 2) continue;
            int start = 0;
            while (start < members.Count)
            {
                // Greedy chunks keep each merged mesh far below the per-accessor limits.
                int end = start, vertices = 0, indices = 0;
                while (end < members.Count)
                {
                    var e = entries[placements[members[end]].Entry];
                    if (end > start && (vertices + e.Vertices > MergedVertexLimit || indices + e.Indices > MergedIndexLimit)) break;
                    vertices += e.Vertices; indices += e.Indices; end++;
                }
                if (end - start >= 2)
                {
                    var chunk = members.GetRange(start, end - start);
                    var merged = MergeChunk(chunk);
                    foreach (int member in chunk) replaced.Add(member);
                    additions.Add(new KeyValuePair<int, Placement>(chunk[0], merged));
                    result.MergedGroups++; result.MergedPlacements += chunk.Count;
                }
                start = end;
            }
        }
        if (replaced.Count == 0) return;
        var firstOf = additions.ToDictionary(a => a.Key, a => a.Value);
        var output = new List<Placement>(placements.Count - replaced.Count + additions.Count);
        for (int i = 0; i < placements.Count; i++)
        {
            if (firstOf.TryGetValue(i, out var merged)) output.Add(merged);
            else if (!replaced.Contains(i)) output.Add(placements[i]);
        }
        placements.Clear(); placements.AddRange(output);
    }

    private Placement MergeChunk(List<int> members)
    {
        var first = placements[members[0]];
        var baseInverse = new Matrix4((double[])first.Matrix.Clone()).Inverse();
        var positions = new List<float>(); var normals = new List<float>(); var indices = new List<uint>(); var edges = new List<uint>();
        foreach (int member in members)
        {
            var placement = placements[member];
            var entry = entries[placement.Entry];
            var geometry = Decode(ReadCore(entry));
            var relative = (baseInverse * new Matrix4((double[])placement.Matrix.Clone())).Values;
            // Normals transform with the inverse transpose of the linear part.
            var linearInverse = new Matrix4(relative[0], relative[1], relative[2], 0, relative[4], relative[5], relative[6], 0, relative[8], relative[9], relative[10], 0, 0, 0, 0, 1).Inverse().Values;
            uint offset = (uint)(positions.Count / 3);
            for (int i = 0; i < geometry.Positions.Length; i += 3)
            {
                double x = geometry.Positions[i], y = geometry.Positions[i + 1], z = geometry.Positions[i + 2];
                positions.Add((float)(relative[0] * x + relative[1] * y + relative[2] * z + relative[3]));
                positions.Add((float)(relative[4] * x + relative[5] * y + relative[6] * z + relative[7]));
                positions.Add((float)(relative[8] * x + relative[9] * y + relative[10] * z + relative[11]));
                double nx = geometry.Normals[i], ny = geometry.Normals[i + 1], nz = geometry.Normals[i + 2];
                double tx = linearInverse[0] * nx + linearInverse[4] * ny + linearInverse[8] * nz;
                double ty = linearInverse[1] * nx + linearInverse[5] * ny + linearInverse[9] * nz;
                double tz = linearInverse[2] * nx + linearInverse[6] * ny + linearInverse[10] * nz;
                double length = Math.Max(Math.Sqrt(tx * tx + ty * ty + tz * tz), 1e-20);
                normals.Add((float)(tx / length)); normals.Add((float)(ty / length)); normals.Add((float)(tz / length));
            }
            foreach (uint corner in geometry.Indices) indices.Add(corner + offset);
            foreach (uint edge in ReadEdges(entry)) edges.Add(edge + offset);
        }
        var mergedGeometry = new MeshData { Positions = positions.ToArray(), Normals = normals.ToArray(), Indices = indices.ToArray() };
        var core = Encode(mergedGeometry);
        mergeSpool ??= new FileStream(mergePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var mergedEntry = new Entry { Source = mergeSpool, Offset = mergeSpool.Length, CoreLength = core.Length, Vertices = positions.Count / 3, Indices = indices.Count, Edges = edges.Count, Material = entries[first.Entry].Material, Uses = 1 };
        Bounds(mergedGeometry.Positions, out mergedEntry.Min, out mergedEntry.Max);
        mergeSpool.Position = mergeSpool.Length;
        mergeSpool.Write(core, 0, core.Length);
        if (edges.Count > 0) { var edgeBytes = new byte[edges.Count * 4]; Buffer.BlockCopy(edges.ToArray(), 0, edgeBytes, 0, edgeBytes.Length); mergeSpool.Write(edgeBytes, 0, edgeBytes.Length); }
        int index = entries.Count; entries.Add(mergedEntry);
        return new Placement { Name = first.Name, Entry = index, Element = first.Element, Matrix = first.Matrix };
    }

    // ---- output ----

    public SceneWriteResult Write(string path, bool merge = true)
    {
        if (placements.Count == 0) throw new InvalidDataException("No render geometry");
        var result = new SceneWriteResult { NodesBeforeMerge = placements.Count, Triangles = PlacedTriangles, UniqueTriangles = UniqueTriangles, Materials = materials.Count, Elements = elements.Count };
        if (merge) MergeSingleUse(result);

        // Only geometry referenced by the final placements is written.
        var used = new List<int>(); var meshIndex = new Dictionary<int, int>();
        foreach (var p in placements) if (!meshIndex.ContainsKey(p.Entry)) { meshIndex.Add(p.Entry, used.Count); used.Add(p.Entry); }
        int accessorCount = used.Sum(e => entries[e].Edges > 0 ? 4 : 3);
        if (placements.Count > MaxNodes) throw new InvalidDataException(Limit("separate model parts", placements.Count, MaxNodes));
        if (used.Count > MaxMeshes) throw new InvalidDataException(Limit("distinct meshes", used.Count, MaxMeshes));
        if (accessorCount > MaxAccessors) throw new InvalidDataException(Limit("geometry accessors", accessorCount, MaxAccessors));
        foreach (int e in used)
        {
            var entry = entries[e];
            if (entry.Vertices > MaxVerticesPerMesh || entry.Indices > MaxIndicesPerAccessor || entry.Edges > MaxIndicesPerAccessor)
                throw new InvalidDataException("One model part has " + entry.Vertices.ToString("N0", CultureInfo.InvariantCulture) + " vertices, more than the PDF viewer accepts in one mesh. Choose a lower detail for this export.");
        }

        var offsets = new long[used.Count]; long binary = 0;
        for (int i = 0; i < used.Count; i++) { offsets[i] = binary; binary += entries[used[i]].TotalLength; }
        var json = Json(used, offsets, binary);
        if (json.Length > SceneLimits.MaxMetadataBytes) throw new InvalidDataException("The scene description is larger than the 64 MiB the PDF viewer accepts. Use a smaller view crop or a lower detail.");
        int padded = (json.Length + 3) & ~3;
        long length = 28L + padded + binary;
        if (length > SceneLimits.MaxSceneBytes) throw new InvalidDataException("Scene geometry and placement metadata exceed the PDF viewer's 512 MiB limit. Use a smaller view crop or a lower detail; no elements were silently removed.");

        string temp = path + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(0x46546c67u); writer.Write(2u); writer.Write((uint)length);
                writer.Write(padded); writer.Write(0x4e4f534au); writer.Write(json);
                for (int i = json.Length; i < padded; i++) writer.Write((byte)32);
                writer.Write((uint)binary); writer.Write(0x004e4942u); writer.Flush();
                var buffer = new byte[1 << 20];
                foreach (int e in used)
                {
                    var entry = entries[e];
                    entry.Source.Position = entry.Offset;
                    for (long remaining = entry.TotalLength; remaining > 0;)
                    {
                        int n = entry.Source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (n == 0) throw new EndOfStreamException("Geometry spool is truncated");
                        output.Write(buffer, 0, n); remaining -= n;
                    }
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }

        result.Nodes = placements.Count; result.UniqueMeshes = used.Count; result.Bytes = length;
        result.SharedPlacements = placements.Count(p => entries[p.Entry].Uses > 1);
        return result;
    }

    private byte[] Json(List<int> used, long[] offsets, long binaryLength)
    {
        var text = new StringBuilder(placements.Count * 320 + used.Count * 420 + 4096);
        string N(double v) { if (double.IsNaN(v) || double.IsInfinity(v)) throw new InvalidDataException("Non-finite GLB value"); return v.ToString("R", CultureInfo.InvariantCulture); }
        string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        void Str(string value)
        {
            text.Append('"');
            for (int k = 0; k < value.Length; k++)
            {
                char c = value[k];
                // Unpaired surrogates become U+FFFD so odd names never break the JSON.
                if (char.IsHighSurrogate(c) && (k + 1 >= value.Length || !char.IsLowSurrogate(value[k + 1]))) c = '\ufffd';
                else if (char.IsLowSurrogate(c) && (k == 0 || !char.IsHighSurrogate(value[k - 1]))) c = '\ufffd';
                if (c == '"' || c == '\\') { text.Append('\\').Append(c); }
                else if (c < ' ' || c > '~' || c == '<' || c == '>' || c == '&' || c == '\'') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            }
            text.Append('"');
        }
        var meshOf = new Dictionary<int, int>(); for (int i = 0; i < used.Count; i++) meshOf.Add(used[i], i);

        text.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Offline3D indexed local mesh spool 0.5.3\"},\"scene\":0,\"scenes\":[{\"nodes\":[");
        for (int i = 0; i < placements.Count; i++) { if (i > 0) text.Append(','); text.Append(I(i)); }
        text.Append("]}],\"nodes\":[");
        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i]; var m = p.Matrix;
            if (i > 0) text.Append(',');
            text.Append("{\"name\":"); Str(p.Name);
            text.Append(",\"mesh\":").Append(I(meshOf[p.Entry])).Append(",\"matrix\":[");
            for (int k = 0; k < 16; k++) { if (k > 0) text.Append(','); text.Append(N(m[(k % 4) * 4 + k / 4])); } // column-major
            text.Append("],\"extras\":{\"element\":").Append(I(p.Element)).Append("}}");
        }
        text.Append("],\"extras\":{\"revitElements\":");
        var head = Encoding.UTF8.GetBytes(text.ToString()); text.Clear();
        var elementsJson = Contract.EncodeExact(elements);

        text.Append("},\"meshes\":[");
        var views = new StringBuilder(); var accessors = new StringBuilder(); int viewCount = 0, accessorCount = 0;
        int View(long offset, long length, int target)
        {
            if (viewCount > 0) views.Append(',');
            views.Append("{\"buffer\":0,\"byteOffset\":").Append(I(offset)).Append(",\"byteLength\":").Append(I(length)).Append(",\"target\":").Append(I(target)).Append('}');
            return viewCount++;
        }
        int Accessor(int view, int componentType, int count, string type, Point3? min = null, Point3? max = null)
        {
            if (accessorCount > 0) accessors.Append(',');
            accessors.Append("{\"bufferView\":").Append(I(view)).Append(",\"componentType\":").Append(I(componentType)).Append(",\"count\":").Append(I(count)).Append(",\"type\":\"").Append(type).Append('"');
            if (min.HasValue && max.HasValue) accessors.Append(",\"min\":[").Append(N(min.Value.X)).Append(',').Append(N(min.Value.Y)).Append(',').Append(N(min.Value.Z)).Append("],\"max\":[").Append(N(max.Value.X)).Append(',').Append(N(max.Value.Y)).Append(',').Append(N(max.Value.Z)).Append(']');
            accessors.Append('}');
            return accessorCount++;
        }
        for (int i = 0; i < used.Count; i++)
        {
            var entry = entries[used[i]]; long o = offsets[i];
            int position = Accessor(View(o + 4, entry.Vertices * 12L, 34962), 5126, entry.Vertices, "VEC3", entry.Min, entry.Max);
            int normal = Accessor(View(o + 4 + entry.Vertices * 12L, entry.Vertices * 12L, 34962), 5126, entry.Vertices, "VEC3");
            int index = Accessor(View(o + 8 + entry.Vertices * 24L, entry.Indices * 4L, 34963), 5125, entry.Indices, "SCALAR");
            if (i > 0) text.Append(',');
            text.Append("{\"extras\":{");
            if (entry.Edges > 0) text.Append("\"featureEdges\":").Append(I(Accessor(View(o + entry.CoreLength, entry.Edges * 4L, 34963), 5125, entry.Edges, "SCALAR")));
            text.Append("},\"primitives\":[{\"attributes\":{\"POSITION\":").Append(I(position)).Append(",\"NORMAL\":").Append(I(normal))
                .Append("},\"indices\":").Append(I(index)).Append(",\"material\":").Append(I(entry.Material)).Append(",\"mode\":4}]}");
        }
        text.Append("],\"materials\":[");
        for (int i = 0; i < materials.Count; i++)
        {
            var material = materials[i];
            if (i > 0) text.Append(',');
            text.Append("{\"name\":"); Str(material.Name);
            text.Append(",\"extras\":{\"cutPattern\":").Append(Encoding.UTF8.GetString(Contract.EncodeExact(material.CutPattern))).Append('}');
            text.Append(",\"doubleSided\":true,\"alphaMode\":\"").Append(material.Opacity < .999 ? "BLEND" : "OPAQUE").Append("\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[")
                .Append(N(PlainMaterial.Linear(material.Red))).Append(',').Append(N(PlainMaterial.Linear(material.Green))).Append(',').Append(N(PlainMaterial.Linear(material.Blue))).Append(',').Append(N(material.Opacity))
                .Append("],\"metallicFactor\":").Append(N(material.Metallic)).Append(",\"roughnessFactor\":").Append(N(material.Roughness)).Append("}}");
        }
        text.Append("],\"buffers\":[{\"byteLength\":").Append(I(binaryLength)).Append("}],\"bufferViews\":[").Append(views).Append("],\"accessors\":[").Append(accessors).Append("]}");
        var tail = Encoding.UTF8.GetBytes(text.ToString());

        var result = new byte[head.Length + elementsJson.Length + tail.Length];
        Buffer.BlockCopy(head, 0, result, 0, head.Length);
        Buffer.BlockCopy(elementsJson, 0, result, head.Length, elementsJson.Length);
        Buffer.BlockCopy(tail, 0, result, head.Length + elementsJson.Length, tail.Length);
        return result;
    }

    public void Dispose()
    {
        spool.Dispose(); mergeSpool?.Dispose();
        try { if (File.Exists(spoolPath)) File.Delete(spoolPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { if (File.Exists(mergePath)) File.Delete(mergePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

namespace Offline3D.RevitExporter;

/// <summary>How Revit supplied the normals of one polymesh.</summary>
internal enum NormalLayout { PerVertex, PerFace, Single }

/// <summary>Indexed triangles in local graphics metres (Y up), relative to
/// <see cref="OriginFeet"/> in the instance's own Revit coordinates.</summary>
internal sealed class MeshData
{
    public float[] Positions { get; set; } = Array.Empty<float>();
    public float[] Normals { get; set; } = Array.Empty<float>();
    public uint[] Indices { get; set; } = Array.Empty<uint>();
    public Point3 OriginFeet { get; set; }
    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// Accumulates Revit polymeshes for one placement and one material.
/// Produces exactly the vertices of the v0.5.2 IndexedMeshAccumulator
/// (same origin, axis conversion, 6-decimal position rounding, 30 degree
/// crease grouping for facet normals), without per-vertex allocations.
/// </summary>
internal sealed class MeshBuilder
{
    private const double CreaseCosine = .8660254037844386; // cos(30 degrees)
    private const double FeetToMetres = .3048;

    private readonly List<float> positions = new List<float>();
    private readonly List<float> normals = new List<float>();
    private readonly List<uint> indices = new List<uint>();
    private readonly Dictionary<VertexKey, uint> vertices = new Dictionary<VertexKey, uint>();
    private Point3 origin;
    private bool initialized;

    public int VertexCount => positions.Count / 3;
    public int TriangleCount => indices.Count / 3;
    public long ByteSize => ((long)positions.Count + normals.Count + indices.Count) * 4;

    private readonly struct VertexKey : IEquatable<VertexKey>
    {
        public readonly float X, Y, Z, NX, NY, NZ;
        public VertexKey(float x, float y, float z, float nx, float ny, float nz) { X = x; Y = y; Z = z; NX = nx; NY = ny; NZ = nz; }
        public bool Equals(VertexKey b) => X == b.X && Y == b.Y && Z == b.Z && NX == b.NX && NY == b.NY && NZ == b.NZ;
        public override bool Equals(object? o) => o is VertexKey b && Equals(b);
        public override int GetHashCode() { unchecked { int h = X.GetHashCode(); h = h * 397 ^ Y.GetHashCode(); h = h * 397 ^ Z.GetHashCode(); h = h * 397 ^ NX.GetHashCode(); h = h * 397 ^ NY.GetHashCode(); h = h * 397 ^ NZ.GetHashCode(); return h; } }
    }

    /// <summary>One crease group of facet normals meeting at a vertex.</summary>
    private sealed class NormalGroup
    {
        public Point3 Reference, Sum;
        public NormalGroup? Next;
    }

    /// <param name="points">Polymesh points in the current instance's coordinates (feet).</param>
    /// <param name="sourceNormals">Per point, per kept facet, or a single normal, as given by <paramref name="layout"/>.</param>
    /// <param name="corners">Three point indices per kept facet.</param>
    public void Append(Point3[] points, Point3[] sourceNormals, int[] corners, int cornerCount, NormalLayout layout)
    {
        if (cornerCount % 3 != 0 || cornerCount > corners.Length) throw new InvalidDataException("Invalid mesh triangle indices");
        if (cornerCount == 0) return;
        if (!initialized) { origin = points[corners[0]]; initialized = true; }

        int faces = cornerCount / 3;
        var faceNormals = new Point3[faces];
        Dictionary<int, NormalGroup>? groups = layout == NormalLayout.PerVertex ? null : new Dictionary<int, NormalGroup>();
        for (int face = 0; face < faces; face++)
        {
            int a = corners[face * 3], b = corners[face * 3 + 1], c = corners[face * 3 + 2];
            if (a < 0 || b < 0 || c < 0 || a >= points.Length || b >= points.Length || c >= points.Length) throw new InvalidDataException("Mesh index outside points");
            var u = points[b] - points[a]; var v = points[c] - points[a];
            var n = new Point3(u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
            int ni = layout == NormalLayout.PerFace ? face : 0;
            if (layout != NormalLayout.PerVertex && ni < sourceNormals.Length) n = sourceNormals[ni];
            if (n.Length < 1e-12) n = new Point3(0, 0, 1);
            n = n * (1 / Math.Max(n.Length, 1e-20));
            faceNormals[face] = n;
            if (groups == null) continue;
            for (int k = 0; k < 3; k++)
            {
                int index = corners[face * 3 + k];
                if (!groups.TryGetValue(index, out var first)) { groups.Add(index, new NormalGroup { Reference = n, Sum = n }); continue; }
                var match = Find(first, n);
                if (match != null) { match.Sum = match.Sum + n; continue; }
                var last = first; while (last.Next != null) last = last.Next;
                last.Next = new NormalGroup { Reference = n, Sum = n };
            }
        }

        for (int i = 0; i < cornerCount; i++)
        {
            int index = corners[i];
            var n = faceNormals[i / 3];
            if (layout == NormalLayout.PerVertex && index < sourceNormals.Length) { if (sourceNormals[index].Length > 1e-12) n = sourceNormals[index]; }
            else if (groups != null) n = Find(groups[index], n)!.Sum;
            var d = points[index] - origin;
            // "+ 0.0" turns -0 into +0 exactly as the old matrix multiply did.
            double px = d.X * FeetToMetres + 0.0, py = d.Z * FeetToMetres + 0.0, pz = -d.Y * FeetToMetres + 0.0;
            double nl = Math.Sqrt(n.X * n.X + n.Z * n.Z + n.Y * n.Y);
            double scale = 1 / Math.Max(nl, 1e-20);
            double nx = n.X * scale, ny = n.Z * scale, nz = -n.Y * scale;
            if (!Finite(px + py + pz) || !Finite(nx + ny + nz)) throw new InvalidDataException("Non-finite geometry");
            var key = new VertexKey((float)Math.Round(px, 6), (float)Math.Round(py, 6), (float)Math.Round(pz, 6), (float)nx, (float)ny, (float)nz);
            if (!vertices.TryGetValue(key, out uint vertex))
            {
                vertex = (uint)(positions.Count / 3); vertices.Add(key, vertex);
                positions.Add(key.X); positions.Add(key.Y); positions.Add(key.Z);
                normals.Add(key.NX); normals.Add(key.NY); normals.Add(key.NZ);
            }
            indices.Add(vertex);
        }
    }

    private static NormalGroup? Find(NormalGroup? group, Point3 n)
    {
        for (; group != null; group = group.Next)
            if (group.Reference.X * n.X + group.Reference.Y * n.Y + group.Reference.Z * n.Z >= CreaseCosine) return group;
        return null;
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public MeshData Finish() => new MeshData { Positions = positions.ToArray(), Normals = normals.ToArray(), Indices = indices.ToArray(), OriginFeet = origin };
}

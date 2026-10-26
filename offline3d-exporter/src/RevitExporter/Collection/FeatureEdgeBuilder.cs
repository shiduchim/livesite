namespace Offline3D.RevitExporter;

/// <summary>
/// Boundary and sharp (over 30 degrees) edges as vertex index pairs. Same
/// output, in the same order, as Core/FeatureEdges.cs, which keyed vertices
/// by round-trip strings; exact bit patterns give the same identity without
/// three string allocations per vertex.
/// </summary>
internal static class FeatureEdgeBuilder
{
    private sealed class Edge { public uint A, B; public double NX, NY, NZ; public int Count; public bool Sharp; }

    private readonly struct PositionKey : IEquatable<PositionKey>
    {
        private readonly long x, y, z;
        public PositionKey(float px, float py, float pz) { x = BitConverter.DoubleToInt64Bits(px); y = BitConverter.DoubleToInt64Bits(py); z = BitConverter.DoubleToInt64Bits(pz); }
        public bool Equals(PositionKey o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object? o) => o is PositionKey k && Equals(k);
        public override int GetHashCode() => PackedKeyComparer.Mix(PackedKeyComparer.Mix(PackedKeyComparer.Mix(0, (ulong)x), (ulong)y), (ulong)z);
    }

    public static uint[] Build(float[] positions, uint[] indices)
    {
        int count = positions.Length / 3;
        var canonical = new Dictionary<PositionKey, uint>(count);
        var ids = new uint[count];
        for (int i = 0; i < count; i++)
        {
            var key = new PositionKey(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            if (!canonical.TryGetValue(key, out ids[i])) { ids[i] = (uint)canonical.Count; canonical.Add(key, ids[i]); }
        }
        var edges = new List<Edge>();
        // Edge keys pack two vertex numbers; see PackedKeyComparer (v0.5.2 was quadratic here).
        var lookup = new Dictionary<ulong, int>(indices.Length, PackedKeyComparer.Instance);
        double cosine = Math.Cos(Math.PI / 6);
        for (int i = 0; i < indices.Length; i += 3)
        {
            int p0 = (int)indices[i] * 3, p1 = (int)indices[i + 1] * 3, p2 = (int)indices[i + 2] * 3;
            double ax = (double)positions[p1] - positions[p0], ay = (double)positions[p1 + 1] - positions[p0 + 1], az = (double)positions[p1 + 2] - positions[p0 + 2];
            double bx = (double)positions[p2] - positions[p0], by = (double)positions[p2 + 1] - positions[p0 + 1], bz = (double)positions[p2 + 2] - positions[p0 + 2];
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            double scale = 1 / Math.Max(Math.Sqrt(nx * nx + ny * ny + nz * nz), 1e-20);
            nx *= scale; ny *= scale; nz *= scale;
            for (int k = 0; k < 3; k++)
            {
                uint va = indices[i + k], vb = indices[i + (k + 1) % 3], ca = ids[va], cb = ids[vb];
                ulong key = ((ulong)Math.Min(ca, cb) << 32) | Math.Max(ca, cb);
                if (lookup.TryGetValue(key, out int index))
                {
                    var e = edges[index]; e.Count++;
                    if (e.NX * nx + e.NY * ny + e.NZ * nz < cosine) e.Sharp = true;
                }
                else { lookup.Add(key, edges.Count); edges.Add(new Edge { A = va, B = vb, NX = nx, NY = ny, NZ = nz, Count = 1 }); }
            }
        }
        var result = new List<uint>();
        foreach (var e in edges) if (e.Count != 2 || e.Sharp) { result.Add(e.A); result.Add(e.B); }
        return result.ToArray();
    }
}

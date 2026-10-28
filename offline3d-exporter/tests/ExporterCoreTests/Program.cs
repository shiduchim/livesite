extern alias exporter;
using System.Diagnostics;
using System.Text.Json;
using Offline3D;
using E = exporter::Offline3D;
using X = exporter::Offline3D.RevitExporter;

// Linux checks for the Revit-independent exporter core (v0.5.3, extended in 0.5.4). Revit's
// callback order is simulated; native Revit behaviour is NOT tested here.
int passed = 0;
var timer = Stopwatch.StartNew();
string root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "offline3d-core-tests"));
Directory.CreateDirectory(root);
void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws<T>(Action action, string contains = "") where T : Exception
{
    try { action(); } catch (T ex) { Assert(ex.Message.Contains(contains), "Unexpected message: " + ex.Message); return; }
    throw new Exception("Expected " + typeof(T).Name);
}
E.Point3 P(double x, double y, double z = 0) => new E.Point3(x, y, z);
E.Matrix4 Move(double x, double y, double z = 0) => new E.Matrix4(1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1);
E.Matrix4 Turn(double degrees, double x = 0, double y = 0, double z = 0, bool mirror = false)
{
    double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), m = mirror ? -1 : 1;
    return new E.Matrix4(c * m, -s, 0, x, s * m, c, 0, y, 0, 0, 1, z, 0, 0, 0, 1);
}
var random = new Random(20261026);

// ---------------------------------------------------------------- mesh builder
(Point3[] points, int[] corners, Point3[] normals) RandomMesh(Random r, int vertices, int faces, X.NormalLayout layout, bool smooth)
{
    var pts = new Point3[vertices];
    for (int i = 0; i < vertices; i++) pts[i] = new Point3(Math.Round(r.NextDouble() * 20 - 10, smooth ? 2 : 6), Math.Round(r.NextDouble() * 20 - 10, smooth ? 2 : 6), Math.Round(r.NextDouble() * 4, smooth ? 2 : 6));
    var c = new int[faces * 3];
    for (int i = 0; i < c.Length; i++) c[i] = r.Next(vertices);
    int nCount = layout == X.NormalLayout.PerVertex ? vertices : layout == X.NormalLayout.PerFace ? faces : 1;
    var n = new Point3[nCount];
    for (int i = 0; i < nCount; i++) n[i] = i % 7 == 3 ? new Point3(0, 0, 0) : new Point3(r.NextDouble() - .5, r.NextDouble() - .5, r.NextDouble() - .5);
    return (pts, c, n);
}
E.Point3[] ToE(Point3[] p) => p.Select(v => new E.Point3(v.X, v.Y, v.Z)).ToArray();
MeshNormalMode Old(X.NormalLayout l) => l == X.NormalLayout.PerVertex ? MeshNormalMode.PerVertex : l == X.NormalLayout.PerFace ? MeshNormalMode.PerFace : MeshNormalMode.Single;

Check("mesh builder output is bit-identical to the v0.5.2 accumulator (all normal layouts, shared corners, zero normals)", () =>
{
    foreach (var layout in new[] { X.NormalLayout.PerVertex, X.NormalLayout.PerFace, X.NormalLayout.Single })
        for (int round = 0; round < 40; round++)
        {
            var oldBuilder = new IndexedMeshAccumulator(); var newBuilder = new X.MeshBuilder();
            for (int part = 0; part < 3; part++)
            {
                var (pts, corners, normals) = RandomMesh(random, 6 + random.Next(40), 1 + random.Next(60), layout, round % 2 == 0);
                oldBuilder.Append(pts, normals, corners, Old(layout));
                newBuilder.Append(ToE(pts), ToE(normals), corners, corners.Length, layout);
            }
            var a = oldBuilder.Finish(); var b = newBuilder.Finish();
            Assert(a.Positions.SequenceEqual(b.Positions) && a.Normals.SequenceEqual(b.Normals) && a.Indices.SequenceEqual(b.Indices), "Builder output differs for " + layout);
            Assert(a.Positions.Select(BitConverter.SingleToInt32Bits).SequenceEqual(b.Positions.Select(BitConverter.SingleToInt32Bits)), "Signed zero differs");
            Assert(a.OriginFeet.X == b.OriginFeet.X && a.OriginFeet.Y == b.OriginFeet.Y && a.OriginFeet.Z == b.OriginFeet.Z);
        }
});

Check("feature edges are identical to Core/FeatureEdges.cs, in the same order", () =>
{
    for (int round = 0; round < 60; round++)
    {
        var builder = new X.MeshBuilder();
        var (pts, corners, normals) = RandomMesh(random, 5 + random.Next(30), 1 + random.Next(80), X.NormalLayout.PerFace, true);
        builder.Append(ToE(pts), ToE(normals), corners, corners.Length, X.NormalLayout.PerFace);
        var mesh = builder.Finish();
        var points = Enumerable.Range(0, mesh.VertexCount).Select(i => new Point3(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2])).ToArray();
        Assert(FeatureEdges.Build(points, mesh.Indices).SequenceEqual(X.FeatureEdgeBuilder.Build(mesh.Positions, mesh.Indices)), "Feature edges differ");
    }
});

Check("feature edges stay fast on large tessellated grids (v0.5.2 hashing was quadratic) and match the original", () =>
{
    // A 60 x 60 shared-vertex grid with a ridge: typical of a tessellated slab or topography patch.
    int n = 60; var pts = new List<E.Point3>(); var corners = new List<int>();
    for (int y = 0; y <= n; y++) for (int x = 0; x <= n; x++) pts.Add(P(x * .5, y * .5, x == n / 2 ? .4 : 0));
    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { int a = y * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1; corners.AddRange(new[] { a, b, d, a, d, c }); }
    var builder = new X.MeshBuilder(); builder.Append(pts.ToArray(), Array.Empty<E.Point3>(), corners.ToArray(), corners.Count, X.NormalLayout.PerFace);
    var mesh = builder.Finish();
    var points = Enumerable.Range(0, mesh.VertexCount).Select(i => new Point3(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2])).ToArray();
    var watch = Stopwatch.StartNew(); var old = FeatureEdges.Build(points, mesh.Indices); long oldMs = watch.ElapsedMilliseconds;
    watch.Restart(); var fresh = X.FeatureEdgeBuilder.Build(mesh.Positions, mesh.Indices); long newMs = watch.ElapsedMilliseconds;
    Assert(old.SequenceEqual(fresh), "Edges differ");
    Console.WriteLine("  feature edges for " + mesh.TriangleCount + " triangles: v0.5.2 " + oldMs + " ms, v0.5.3 " + newMs + " ms");
    // Larger mesh: new builder only (the old one takes minutes here).
    int big = 400; pts.Clear(); corners.Clear();
    for (int y = 0; y <= big; y++) for (int x = 0; x <= big; x++) pts.Add(P(x * .1, y * .1, (x * 7 + y * 3) % 11 == 0 ? .05 : 0));
    for (int y = 0; y < big; y++) for (int x = 0; x < big; x++) { int a = y * (big + 1) + x, b = a + 1, c = a + big + 1, d = c + 1; corners.AddRange(new[] { a, b, d, a, d, c }); }
    builder = new X.MeshBuilder(); builder.Append(pts.ToArray(), Array.Empty<E.Point3>(), corners.ToArray(), corners.Count, X.NormalLayout.PerFace); mesh = builder.Finish();
    watch.Restart(); X.FeatureEdgeBuilder.Build(mesh.Positions, mesh.Indices); long bigMs = watch.ElapsedMilliseconds;
    Console.WriteLine("  feature edges for " + mesh.TriangleCount + " triangles: v0.5.3 " + bigMs + " ms");
    Assert(bigMs < 10000, "Feature edges too slow: " + bigMs + " ms");
});

// ---------------------------------------------------------------- GLB reading helpers
(JsonDocument json, byte[] bin) ReadGlb(string path)
{
    var bytes = File.ReadAllBytes(path);
    Contract.ValidateGlb(bytes); // unchanged reader validator
    int n = BitConverter.ToInt32(bytes, 12);
    var json = JsonDocument.Parse(bytes.AsMemory(20, n));
    int start = 20 + n + 8;
    return (json, bytes.AsSpan(start).ToArray());
}
float[] Floats(JsonElement root, byte[] bin, int accessor)
{
    var a = root.GetProperty("accessors")[accessor]; var v = root.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
    int count = a.GetProperty("count").GetInt32() * 3; var result = new float[count];
    Buffer.BlockCopy(bin, (int)v.GetProperty("byteOffset").GetInt64(), result, 0, count * 4); return result;
}
uint[] Uints(JsonElement root, byte[] bin, int accessor)
{
    var a = root.GetProperty("accessors")[accessor]; var v = root.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
    int count = a.GetProperty("count").GetInt32(); var result = new uint[count];
    Buffer.BlockCopy(bin, (int)v.GetProperty("byteOffset").GetInt64(), result, 0, count * 4); return result;
}
/// World-space triangles: element key, material, 3 positions and 3 normals.
List<(string Key, int Material, double[] V)> WorldTriangles(string path)
{
    var (doc, bin) = ReadGlb(path); var root = doc.RootElement; var result = new List<(string, int, double[])>();
    foreach (var node in root.GetProperty("nodes").EnumerateArray())
    {
        var c = node.GetProperty("matrix").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var m = new Matrix4(Enumerable.Range(0, 16).Select(i => c[(i % 4) * 4 + i / 4]).ToArray());
        var inverse = m.Inverse().Values;
        var mesh = root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()];
        var primitive = mesh.GetProperty("primitives")[0];
        var pos = Floats(root, bin, primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32());
        var nor = Floats(root, bin, primitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32());
        var idx = Uints(root, bin, primitive.GetProperty("indices").GetInt32());
        string key = node.GetProperty("name").GetString()!.Split('/')[0];
        int material = primitive.GetProperty("material").GetInt32();
        for (int i = 0; i < idx.Length; i += 3)
        {
            var v = new double[18];
            for (int k = 0; k < 3; k++)
            {
                int j = (int)idx[i + k] * 3;
                var p = m.Apply(new Point3(pos[j], pos[j + 1], pos[j + 2]));
                double nx = inverse[0] * nor[j] + inverse[4] * nor[j + 1] + inverse[8] * nor[j + 2], ny = inverse[1] * nor[j] + inverse[5] * nor[j + 1] + inverse[9] * nor[j + 2], nz = inverse[2] * nor[j] + inverse[6] * nor[j + 1] + inverse[10] * nor[j + 2];
                double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                v[k * 3] = p.X; v[k * 3 + 1] = p.Y; v[k * 3 + 2] = p.Z; v[9 + k * 3] = nx / l; v[10 + k * 3] = ny / l; v[11 + k * 3] = nz / l;
            }
            // Rotate the corner order so the smallest vertex comes first (winding kept).
            int first = 0; for (int k = 1; k < 3; k++) if (Less(v, k, first)) first = k;
            var r = new double[18];
            for (int k = 0; k < 3; k++) { int s = (first + k) % 3; Array.Copy(v, s * 3, r, k * 3, 3); Array.Copy(v, 9 + s * 3, r, 9 + k * 3, 3); }
            result.Add((key, material, r));
        }
    }
    return result;
    static bool Less(double[] v, int a, int b) { for (int k = 0; k < 3; k++) { if (v[a * 3 + k] < v[b * 3 + k] - 1e-9) return true; if (v[a * 3 + k] > v[b * 3 + k] + 1e-9) return false; } return false; }
}
void SameWorld(string a, string b, string label)
{
    var x = WorldTriangles(a); var y = WorldTriangles(b);
    Assert(x.Count == y.Count, label + ": triangle count " + x.Count + " vs " + y.Count);
    string Sort((string Key, int Material, double[] V) t) => t.Key + "|" + string.Join(",", t.V.Take(9).Select(v => Math.Round(v, 3).ToString("0.000")));
    var xs = x.OrderBy(Sort, StringComparer.Ordinal).ToList(); var ys = y.OrderBy(Sort, StringComparer.Ordinal).ToList();
    for (int i = 0; i < xs.Count; i++)
    {
        Assert(xs[i].Key == ys[i].Key, label + ": element differs at " + i);
        for (int k = 0; k < 9; k++) Assert(Math.Abs(xs[i].V[k] - ys[i].V[k]) < 2e-5, label + ": position differs at triangle " + i);
        for (int k = 9; k < 18; k++) Assert(Math.Abs(xs[i].V[k] - ys[i].V[k]) < 2e-4, label + ": normal differs at triangle " + i);
    }
}
JsonElement Node(string path, int i) => ReadGlb(path).json.RootElement.GetProperty("nodes")[i];

// ---------------------------------------------------------------- scene writer
E.ElementIdentity Id(string unique, string doc = "host", string? link = null) => new E.ElementIdentity { UniqueId = unique, DocumentId = doc, LinkInstanceId = link, MeshName = unique + "/0", Category = "Generic Models" };
X.MeshData Box(double width = 1, double height = 1)
{
    var p = new[] { P(0, 0, 0), P(width, 0, 0), P(width, 1, 0), P(0, 1, 0), P(0, 0, height), P(width, 0, height), P(width, 1, height), P(0, 1, height) };
    int[] ix = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
    var b = new X.MeshBuilder(); b.Append(p, Array.Empty<E.Point3>(), ix, ix.Length, X.NormalLayout.PerFace); return b.Finish();
}
var grey = new E.PlainMaterial { Name = "Grey", Red = .5, Green = .5, Blue = .5 };
var red = new E.PlainMaterial { Name = "Red \"quoted\" ש😀", Red = .9, Green = .1, Blue = .1, Opacity = .5 };

Check("new writer without merging matches the v0.5.2 SpoolSceneWriter output (JSON semantics and binary bytes)", () =>
{
    string a = Path.Combine(root, "old-writer.glb"), b = Path.Combine(root, "new-writer.glb");
    var oldGrey = new PlainMaterial { Name = grey.Name, Red = grey.Red, Green = grey.Green, Blue = grey.Blue };
    var oldRed = new PlainMaterial { Name = red.Name, Red = red.Red, Green = red.Green, Blue = red.Blue, Opacity = red.Opacity };
    var graphics = new E.Matrix4(Matrix4.Graphics(new Point3(12, -7, 3)).Values);
    using (var w = new SpoolSceneWriter(root, Matrix4.Graphics(new Point3(12, -7, 3))))
    using (var n = new X.SceneWriter(root, graphics))
    {
        var placements = new (X.MeshData Mesh, E.Matrix4 At, bool Red, string Unique)[]
        { (Box(), Turn(0, 1, 2), false, "a"), (Box(), Turn(90, 5, 2), false, "b"), (Box(2), Turn(30, 9, 2, 1), true, "c"), (Box(), Turn(45, 1, 9, 0, true), false, "d"), (Box(), Turn(10, 4, 9, 0, true), false, "a") };
        foreach (var p in placements)
        {
            var geometry = new IndexedGeometry { Positions = p.Mesh.Positions, Normals = p.Mesh.Normals, Indices = p.Mesh.Indices, OriginFeet = new Point3(p.Mesh.OriginFeet.X, p.Mesh.OriginFeet.Y, p.Mesh.OriginFeet.Z) };
            var oldId = new ElementIdentity { UniqueId = p.Unique, DocumentId = "host", MeshName = p.Unique + "/0", Category = "Generic Models" };
            w.Add(p.Unique + "/" + w.NodeCount, geometry, new Matrix4(p.At.Values), p.Red ? oldRed : oldGrey, oldId);
            n.Add(p.Unique + "/" + n.PlacementCount, p.Mesh, p.At, p.Red ? red : grey, p.Red ? "red" : "grey", Id(p.Unique));
        }
        w.Write(a); n.Write(b, merge: false);
    }
    var (oj, ob) = ReadGlb(a); var (nj, nb) = ReadGlb(b);
    Assert(ob.SequenceEqual(nb), "Binary chunk differs");
    bool Same(JsonElement x, JsonElement y, string path)
    {
        if (path == "$.asset.generator") return true;
        if (x.ValueKind != y.ValueKind) return false;
        switch (x.ValueKind)
        {
            case JsonValueKind.Object:
                var xp = x.EnumerateObject().ToList(); var yp = y.EnumerateObject().ToList();
                if (!xp.Select(p => p.Name).SequenceEqual(yp.Select(p => p.Name))) return false;
                return xp.Zip(yp).All(p => Same(p.First.Value, p.Second.Value, path + "." + p.First.Name));
            case JsonValueKind.Array:
                return x.GetArrayLength() == y.GetArrayLength() && x.EnumerateArray().Zip(y.EnumerateArray()).Select((p, i) => Same(p.First, p.Second, path + "[" + i + "]")).All(v => v);
            case JsonValueKind.Number: return x.GetDouble() == y.GetDouble();
            case JsonValueKind.String: return x.GetString() == y.GetString();
            default: return x.GetRawText() == y.GetRawText();
        }
    }
    Assert(Same(oj.RootElement, nj.RootElement, "$"), "glTF JSON differs from v0.5.2");
});

Check("repeated geometry is stored once; mirrored placements get corrected geometry and positive matrices", () =>
{
    string path = Path.Combine(root, "shared.glb");
    using var w = new X.SceneWriter(root, E.Matrix4.Graphics(P(0, 0)));
    var h = w.Add("e1/0", Box(), Turn(0), grey, "grey", Id("e1"));
    w.AddReference("e2/1", h, Turn(90, 5), Id("e2"));
    w.AddReference("e3/2", h, Turn(30, 10, 0, 0, mirror: true), Id("e3"));
    w.AddReference("e4/3", h, Turn(60, 15, 0, 0, mirror: true), Id("e4"));
    Assert(w.UniqueMeshCount == 2, "Expected source and one mirrored twin");
    var result = w.Write(path);
    Assert(result.Nodes == 4 && result.UniqueMeshes == 2 && result.SharedPlacements == 4);
    string expected = Path.Combine(root, "shared-expected.glb");
    using (var direct = new X.SceneWriter(root, E.Matrix4.Graphics(P(0, 0))))
    {
        direct.Add("e1/0", Box(), Turn(0), grey, "grey", Id("e1")); direct.Add("e2/1", Box(), Turn(90, 5), grey, "grey", Id("e2"));
        direct.Add("e3/2", Box(), Turn(30, 10, 0, 0, true), grey, "grey", Id("e3")); direct.Add("e4/3", Box(), Turn(60, 15, 0, 0, true), grey, "grey", Id("e4"));
        direct.Write(expected, merge: false);
    }
    SameWorld(path, expected, "references vs direct");
});

Check("single-use parts merge per element and material without moving any triangle or normal", () =>
{
    string merged = Path.Combine(root, "merged.glb"), plain = Path.Combine(root, "unmerged.glb");
    foreach (bool merge in new[] { true, false })
    {
        using var w = new X.SceneWriter(root, E.Matrix4.Graphics(P(100, 50, 10)));
        for (int i = 0; i < 5; i++) w.Add("fixture/" + w.PlacementCount, Box(1 + i * .1, 1 + i * .2), Turn(i * 17, 100 + i, 50, 10 + i, mirror: i == 3), grey, "grey", Id("fixture"));
        w.Add("fixture/" + w.PlacementCount, Box(3), Turn(5, 103, 52, 10), red, "red", Id("fixture"));
        var shared = w.Add("pump/" + w.PlacementCount, Box(.5), Turn(0, 90, 40), grey, "grey", Id("pump"));
        w.AddReference("pump2/" + w.PlacementCount, shared, Turn(90, 95, 40), Id("pump2"));
        var r = w.Write(merge ? merged : plain, merge);
        if (merge) Assert(r.NodesBeforeMerge == 8 && r.Nodes == 4 && r.MergedGroups == 1 && r.MergedPlacements == 5, "Unexpected merge result " + r.Nodes);
    }
    SameWorld(merged, plain, "merge");
    Assert(Node(merged, 0).GetProperty("name").GetString() == "fixture/0", "Merged node keeps the element's first node name");
});

Check("merged meshes keep feature edges of every part", () =>
{
    string merged = Path.Combine(root, "edges-merged.glb");
    using var w = new X.SceneWriter(root, E.Matrix4.Graphics(P(0, 0)));
    w.Add("a/0", Box(), Turn(0), grey, "grey", Id("a")); w.Add("a/1", Box(2), Turn(20, 4), grey, "grey", Id("a"));
    w.Write(merged);
    var (doc, bin) = ReadGlb(merged); var mesh = doc.RootElement.GetProperty("meshes")[0];
    var edges = Uints(doc.RootElement, bin, mesh.GetProperty("extras").GetProperty("featureEdges").GetInt32());
    Assert(edges.Length == 2 * 2 * 12, "Each box has 12 sharp edges");
});

Check("writer refuses scopes over the reader's 100,000-part limit before collecting further", () =>
{
    using var w = new X.SceneWriter(root, E.Matrix4.Graphics(P(0, 0)));
    var h = w.Add("e/0", Box(), Turn(0), grey, "grey", Id("e0"));
    Throws<InvalidDataException>(() => { for (int i = 1; i <= 100001; i++) w.AddReference("e" + i + "/" + i, h, Move(i % 300, i / 300), Id("e" + i)); }, "the PDF viewer accepts at most 100,000");
});

Check("names with quotes, controls, Hebrew, emoji and a lone surrogate produce valid glTF JSON", () =>
{
    string path = Path.Combine(root, "names.glb");
    using var w = new X.SceneWriter(root, E.Matrix4.Graphics(P(0, 0)));
    var odd = new E.PlainMaterial { Name = "a\"b\\c\n\u0001<>&' שלום 😀 \ud800 end" };
    w.Add("::odd\"name/0", Box(), Turn(0), odd, "odd", Id(X.ExportText.Clean("odd\"\ud800")));
    w.Write(path);
    var (doc, _) = ReadGlb(path);
    Assert(doc.RootElement.GetProperty("materials")[0].GetProperty("name").GetString()!.Contains("שלום 😀 � end"));
});

Check("model text cleaning never splits a surrogate pair and replaces unpaired halves", () =>
{
    string emoji = new string('x', 2047) + "😀tail";
    Assert(X.ExportText.Clean(emoji, 2048) == new string('x', 2047), "Cut must drop the whole pair");
    Assert(X.ExportText.Clean("a\ud800b\udc00c😀") == "a�b�c😀");
    Assert(X.ExportText.Clean(null) == "" && X.ExportText.Clean("plain") == "plain");
    E.Contract.Encode(new[] { X.ExportText.Clean(emoji.Substring(0, 2048)) }); // the wire writer accepts it
});

// ---------------------------------------------------------------- simulated Revit traversal
var volume = new E.ExportVolume(P(0, 0, -1), P(100, 100, 20), E.Matrix4.Identity);
SimMesh BoxMesh(double w, double d, double h, E.PlainMaterial material, string key, double ox = 0, double oy = 0, double oz = 0)
{
    var p = new[] { P(ox, oy, oz), P(ox + w, oy, oz), P(ox + w, oy + d, oz), P(ox, oy + d, oz), P(ox, oy, oz + h), P(ox + w, oy, oz + h), P(ox + w, oy + d, oz + h), P(ox, oy + d, oz + h) };
    int[] ix = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
    return new SimMesh { Points = p, Corners = ix, Normals = Enumerable.Range(0, 12).Select(f => P(0, 0, 0)).ToArray(), Material = material, MaterialKey = key };
}
// A "socket": body plus a nested faceplate component in another material.
SimInstance Socket(E.Matrix4 at, string symbol = "socket-geometry", double plate = .2, double body = .3) => new SimInstance
{
    Transform = at, Symbol = symbol,
    Children = { BoxMesh(body, .2, .4, grey, "grey"), new SimInstance { Transform = Move(.05, -.02, .1), Symbol = "plate-" + plate, Children = { BoxMesh(plate, .02, .2, red, "red") } } }
};

(X.GeometryStream Stream, X.SceneWriteResult Result, List<string> Documents) Run(List<SimElement> scene, string path, bool reuse, int verifications = 1, int batchVertices = 750000, bool merge = true, E.ExportVolume[]? volumes = null)
{
    using var w = new X.SceneWriter(root, E.Matrix4.Graphics(P(50, 50, 0)));
    var s = new X.GeometryStream(new X.GeometryStreamOptions { Volumes = volumes ?? new[] { volume }, ReuseInstances = reuse, ReuseVerifications = verifications, MaxBatchVertices = batchVertices }, w, "host");
    var documents = new List<string>();
    foreach (var e in scene) Sim.Element(s, e, documents);
    s.Finish();
    var result = w.Write(path, merge);
    Assert(s.Stats.UnbalancedEnds == 0 && s.Stats.OpenSpacesAtEnd == 0, "Callback stacks unbalanced");
    return (s, result, documents);
}

Check("links: stacks stay balanced, host elements after a link use host coordinates and document (v0.5.2 defect)", () =>
{
    var linked = new SimElement { Unique = "wall-in-link", Children = { BoxMesh(2, 2, 2, grey, "grey") } };
    var scene = new List<SimElement>
    {
        new SimElement { Unique = "host-before", Children = { BoxMesh(1, 1, 1, grey, "grey", 10, 10) } },
        new SimElement { Unique = "link-1", LinkInstance = true, Category = "RVT Links", Children = { new SimLink { Transform = Turn(90, 40, 20), Document = "linked.rvt", Key = "link-1", Elements = { linked } } } },
        new SimElement { Unique = "unloaded", LinkInstance = true, Category = "RVT Links", Children = { new SimLink { Transform = Move(5, 5), Document = null, Key = "unloaded" } } },
        new SimElement { Unique = "host-after", Children = { BoxMesh(1, 1, 1, grey, "grey", 20, 10) } },
    };
    string path = Path.Combine(root, "links.glb");
    var (s, _, documents) = Run(scene, path, reuse: true);
    Assert(documents.SequenceEqual(new[] { "host", "host", "linked.rvt", "host", "host" }), "Document context after a link: " + string.Join(",", documents));
    var tris = WorldTriangles(path);
    Assert(tris.Any(t => t.Key == "::link-1::wall-in-link"), "Linked element key carries its link chain");
    // host-after must be at host X 20..21 ft (graphics x = (20-50)*.3048), not moved by the link transform.
    var after = tris.Where(t => t.Key == "::host-after").ToList();
    Assert(after.Count == 12 && after.All(t => t.V.Where((v, i) => i % 3 == 0 && i < 9).All(x => x >= (20 - 50) * .3048 - 1e-6 && x <= (21 - 50) * .3048 + 1e-6)), "Host element after a link was misplaced");
    var inLink = tris.Where(t => t.Key == "::link-1::wall-in-link").SelectMany(t => new[] { t.V[0], t.V[3], t.V[6] }).ToList();
    Assert(Math.Abs(inLink.Min() - (38 - 50) * .3048) < 1e-6 && Math.Abs(inLink.Max() - (40 - 50) * .3048) < 1e-6, "Linked element transform");
    Assert(s.Stats.LinksEntered == 1 && s.Stats.LinksUnloaded == 1);
});

Check("Skip at element level still balances (outside crop, not model, tiny parts)", () =>
{
    var scene = new List<SimElement>
    {
        new SimElement { Unique = "far-away", Children = { BoxMesh(1, 1, 1, grey, "grey", 500, 500) } },
        new SimElement { Unique = "annotation", Model = false, Children = { BoxMesh(1, 1, 1, grey, "grey", 1, 1) } },
        new SimElement { Unique = "inside", Children = { BoxMesh(1, 1, 1, grey, "grey", 1, 1) } },
    };
    var (s, r, _) = Run(scene, Path.Combine(root, "skips.glb"), reuse: true);
    Assert(s.Stats.SkippedOutsideCrop == 1 && s.Stats.SkippedNotModel == 1 && r.Nodes == 1 && scene[0].Visits == 0);
});

List<SimElement> Sockets(int count, Func<int, E.Matrix4> at, Func<int, string>? symbol = null, Func<int, double>? plate = null, Func<int, double>? body = null) =>
    Enumerable.Range(0, count).Select(i => new SimElement { Unique = "socket-" + i, Category = "Electrical Fixtures", Children = { Socket(at(i), symbol?.Invoke(i) ?? "socket-geometry", plate?.Invoke(i) ?? .2, body?.Invoke(i) ?? .3) } }).ToList();

Check("repeated sockets: reuse skips Revit traversal yet produces the same world geometry as full traversal", () =>
{
    var scene = Sockets(40, i => Turn(i * 37, 5 + (i % 8) * 10, 5 + (i / 8) * 15, 1, mirror: i % 5 == 4));
    string a = Path.Combine(root, "reuse-on.glb"), b = Path.Combine(root, "reuse-off.glb");
    var (s, r, _) = Run(scene, a, reuse: true);
    var (s0, _, _) = Run(Sockets(40, i => Turn(i * 37, 5 + (i % 8) * 10, 5 + (i / 8) * 15, 1, mirror: i % 5 == 4)), b, reuse: false);
    SameWorld(a, b, "reuse");
    Assert(s.Stats.InstancesReused == 38, "Expected 38 replays, got " + s.Stats.InstancesReused);
    Assert(s.Stats.FacetsReceived * 10 < s0.Stats.FacetsReceived, "Reuse should avoid most tessellation callbacks");
    Assert(r.UniqueMeshes <= 4, "Body and plate (plus mirrored forms) are stored once");
});

Check("one element with many instances (CAD import / railing): outside ones culled without traversal, crossing ones traversed and filtered", () =>
{
    // 10 inside, 5 far outside, 5 whose body crosses x = 100 while the nested plate stays inside.
    Func<int, E.Matrix4> at = i => i < 10 ? Move(5 + i * 3, 5, 1) : i < 15 ? Move(300 + i, 5, 1) : Move(99.75, 10 + i, 1);
    List<SimElement> Import() => new List<SimElement> { new SimElement { Unique = "cad-import", Category = "Imports", Children = Enumerable.Range(0, 20).Select(i => (SimNode)Socket(at(i))).ToList() } };
    string a = Path.Combine(root, "cull-on.glb"), b = Path.Combine(root, "cull-off.glb");
    var (s, _, _) = Run(Import(), a, reuse: true);
    var (s0, _, _) = Run(Import(), b, reuse: false);
    SameWorld(a, b, "cull");
    Assert(s.Stats.InstancesCulled == 5 && s.Stats.InstancesStraddling == 5, "culled " + s.Stats.InstancesCulled + ", straddling " + s.Stats.InstancesStraddling);
    Assert(s.Stats.FacetsReceived < s0.Stats.FacetsReceived / 2, "Culled and reused instances must not be tessellated");
});

Check("separate elements far outside the crop are skipped before any traversal", () =>
{
    var (s, _, _) = Run(Sockets(20, i => i < 15 ? Move(5 + i * 3, 5, 1) : Move(300 + i, 5, 1)), Path.Combine(root, "far.glb"), reuse: true);
    Assert(s.Stats.SkippedOutsideCrop == 5);
});

Check("same symbol key with different geometry is detected by verification and never reused", () =>
{
    Func<int, E.Matrix4> at = i => Move(5 + i * 4, 5, 1);
    string a = Path.Combine(root, "verify-on.glb"), b = Path.Combine(root, "verify-off.glb");
    // Instance 1 reports the same symbol key but a wider body: the key must become unsafe.
    var (s, _, _) = Run(Sockets(12, at, body: i => i == 1 ? .35 : .3), a, reuse: true);
    Run(Sockets(12, at, body: i => i == 1 ? .35 : .3), b, reuse: false);
    SameWorld(a, b, "verification");
    Assert(s.Stats.ReuseRejected >= 1 && s.Stats.ReuseKeysUnsafe >= 1, "rejected " + s.Stats.ReuseRejected + ", unsafe keys " + s.Stats.ReuseKeysUnsafe);
});

Check("a first instance crossing the crop does not poison reuse; a later complete capture is used", () =>
{
    Func<int, E.Matrix4> at = i => i == 0 ? Move(99.9, 5, 1) : Move(5 + i * 4, 5, 1);
    string a = Path.Combine(root, "late-capture-on.glb"), b = Path.Combine(root, "late-capture-off.glb");
    var (s, _, _) = Run(Sockets(10, at), a, reuse: true);
    Run(Sockets(10, at), b, reuse: false);
    SameWorld(a, b, "late capture");
    Assert(s.Stats.CapturesIncomplete >= 1 && s.Stats.InstancesReused == 7, "reused " + s.Stats.InstancesReused);
});

Check("rotated crop: reuse classification follows the oriented crop, not its bounding box", () =>
{
    double q = Math.Sqrt(.5);
    var rotated = new E.ExportVolume(P(-20, -20, -1), P(20, 20, 10), new E.Matrix4(q, -q, 0, 50, q, q, 0, 50, 0, 0, 1, 0, 0, 0, 0, 1));
    Func<int, E.Matrix4> at = i => Move(25 + i * 2.5, 25 + i * 2.5, 1); // diagonal through the rotated square and beyond its corners
    string a = Path.Combine(root, "rotated-on.glb"), b = Path.Combine(root, "rotated-off.glb");
    var (s, _, _) = Run(Sockets(24, at), a, reuse: true, volumes: new[] { rotated });
    Run(Sockets(24, at), b, reuse: false, volumes: new[] { rotated });
    SameWorld(a, b, "rotated crop");
    Assert(s.Stats.InstancesReused > 0);
});

Check("huge single elements are written in several placements instead of failing at 64 MiB", () =>
{
    var mesh = new SimMesh { Material = grey, MaterialKey = "grey" };
    var pts = new List<E.Point3>(); var corners = new List<int>();
    for (int i = 0; i < 4000; i++) { int first = pts.Count; double x = (i % 60) * 1.5, y = (i / 60) * 1.4; pts.Add(P(x, y, 1)); pts.Add(P(x + 1, y, 1)); pts.Add(P(x, y + 1, 1.5)); corners.AddRange(new[] { first, first + 1, first + 2 }); }
    mesh.Points = pts.ToArray(); mesh.Corners = corners.ToArray(); mesh.Normals = Enumerable.Range(0, 4000).Select(_ => P(0, 0, 0)).ToArray();
    string a = Path.Combine(root, "chunk-small.glb"), b = Path.Combine(root, "chunk-none.glb");
    var scene = new List<SimElement> { new SimElement { Unique = "topography", Children = { mesh, mesh, mesh } } };
    var (s, r, _) = Run(scene, a, reuse: true, batchVertices: 5000, merge: false);
    Run(scene, b, reuse: true);
    SameWorld(a, b, "batch split");
    Assert(s.Stats.BatchesSplit >= 2 && r.Nodes >= 3, "split " + s.Stats.BatchesSplit);
});

Check("stress: 3,000 sockets in 30 symbol variants, mirrored and rotated, reuse on vs off", () =>
{
    Func<int, E.Matrix4> at = i => Turn(i * 13, 2 + (i % 50) * 1.9, 2 + (i / 50) * 1.6, i % 3, mirror: i % 7 == 0);
    string a = Path.Combine(root, "stress-on.glb"), b = Path.Combine(root, "stress-off.glb");
    var watch = Stopwatch.StartNew();
    var (s, r, _) = Run(Sockets(3000, at, symbol: i => "socket-" + (i % 30), plate: i => .1 + (i % 30) * .01), a, reuse: true);
    long reuseMs = watch.ElapsedMilliseconds; watch.Restart();
    var (s0, r0, _) = Run(Sockets(3000, at, symbol: i => "socket-" + (i % 30), plate: i => .1 + (i % 30) * .01), b, reuse: false);
    long fullMs = watch.ElapsedMilliseconds;
    SameWorld(a, b, "stress");
    Console.WriteLine("  stress: reuse " + reuseMs + " ms (" + s.Stats.FacetsReceived + " facets from Revit), full traversal " + fullMs + " ms (" + s0.Stats.FacetsReceived + " facets); nodes " + r.Nodes + "/" + r0.Nodes + ", unique meshes " + r.UniqueMeshes);
    Assert(s.Stats.InstancesReused > 2800);
});

Check("statistics report is produced for diagnostics", () =>
{
    var (s, _, _) = Run(Sockets(5, i => Move(5 + i * 4, 5, 1)), Path.Combine(root, "report.glb"), reuse: true);
    var lines = s.Stats.Report().ToList();
    Assert(lines.Any(l => l.StartsWith("Instances begun")) && lines.Any(l => l.StartsWith("Callback balance: unmatched ends 0")));
});

Check("helper path: GLB, identities and registration from the new exporter package into a PDF and reopen exactly (PDFium registration excluded)", () =>
{
    string dir = Path.Combine(root, "package"); Directory.CreateDirectory(dir);
    string glbPath = Path.Combine(dir, "scene.glb");
    var (s, _, _) = Run(Sockets(200, i => Turn(i * 11, 3 + (i % 20) * 4.5, 3 + (i / 20) * 9, 1, mirror: i % 9 == 0)), glbPath, reuse: true);
    var glb = File.ReadAllBytes(glbPath);
    var identities = E.Contract.Encode(s.Identities.ToArray());            // exporter wire writer
    var decoded = Contract.Decode<ElementIdentity[]>(identities);          // reader decoder
    Assert(decoded.Length == 200 && decoded.All(x => x.MeshName.Contains('/') && x.DocumentId == "host"), "identities");
    // Every identity's mesh name is a node of the GLB, as the viewer's lookup expects.
    var names = new HashSet<string>(ReadGlb(glbPath).json.RootElement.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()!));
    Assert(decoded.All(x => names.Contains(x.MeshName)), "Identity MeshName must name a GLB node");
    var reg = new Registration { ViewId = "v", LevelId = "l", DocumentId = "host", MediaBox = new[] { 0d, 0, 612, 792 }, PageCropBox = new[] { 0d, 0, 612, 792 }, CropPdf = new[] { 0d, 0, 100, 0, 100, 100, 0, 100 }, PreciseClicks = true, FitPointCount = 3, CheckPointCount = 3 };
    var files = new Dictionary<string, byte[]> { { "registration.json", Contract.Encode(reg) }, { "identities.json", identities }, { "scene.glb", glb } };
    var manifest = PdfPackageService.Describe(false, "View crop only; test", "0.5.3-test", files);
    manifest.RequiredCapabilities = manifest.RequiredCapabilities.Concat(new[] { "glb-affine-instances-v1" }).ToArray(); // as ExportFinalizer adds for matrix nodes
    Contract.Validate(manifest);
    string plain = Path.Combine(dir, "drawing.pdf"), pdf = Path.Combine(dir, "export.pdf");
    using (var d = new PdfSharp.Pdf.PdfDocument()) { var page = d.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(612); page.Height = PdfSharp.Drawing.XUnit.FromPoint(792); d.Save(plain); }
    var service = new PdfPackageService();
    service.Write(plain, pdf, manifest, files);
    var opened = service.Read(pdf);
    Assert(opened.Glb.SequenceEqual(glb) && opened.Elements.Length == 200, "Reopened package differs");
});

Check("over 64 MiB of distinct geometry from the new writer validates, packages and reopens byte-identical", () =>
{
    string dir = Path.Combine(root, "large"); Directory.CreateDirectory(dir);
    string glbPath = Path.Combine(dir, "scene.glb");
    var watch = Stopwatch.StartNew();
    using (var w = new X.SceneWriter(dir, E.Matrix4.Graphics(P(0, 0))))
    {
        for (int mesh = 0; mesh < 90; mesh++)
        {
            const int triangles = 8192;
            var builder = new X.MeshBuilder(); var pts = new E.Point3[triangles * 3]; var corners = new int[triangles * 3];
            var noise = new Random(mesh); // distinct shapes: identical parts would rightly be stored once
            for (int f = 0; f < triangles; f++) { double x = (f % 128) * .5, y = (f / 128) * .5, z = noise.NextDouble() * .2; pts[f * 3] = P(x, y, z); pts[f * 3 + 1] = P(x + .3, y, z + noise.NextDouble() * .1); pts[f * 3 + 2] = P(x, y + .3, z); corners[f * 3] = f * 3; corners[f * 3 + 1] = f * 3 + 1; corners[f * 3 + 2] = f * 3 + 2; }
            builder.Append(pts, Array.Empty<E.Point3>(), corners, corners.Length, X.NormalLayout.PerFace);
            w.Add("part-" + mesh + "/" + w.PlacementCount, builder.Finish(), Move(mesh * 80, 0), grey, "grey", Id("part-" + mesh));
        }
        var r = w.Write(glbPath);
        Assert(r.Bytes > 64L * 1024 * 1024 && r.UniqueMeshes == 90, "bytes " + r.Bytes);
        Console.WriteLine("  large scene: geometry built and GLB written in " + watch.ElapsedMilliseconds + " ms");
    }
    var glb = File.ReadAllBytes(glbPath); Contract.ValidateGlb(glb);
    var files = new Dictionary<string, byte[]> { { "registration.json", Contract.Encode(new Registration { ViewId = "v", LevelId = "l", DocumentId = "host", MediaBox = new[] { 0d, 0, 612, 792 }, PageCropBox = new[] { 0d, 0, 612, 792 }, CropPdf = new[] { 0d, 0, 100, 0, 100, 100, 0, 100 }, PreciseClicks = true, FitPointCount = 3, CheckPointCount = 3 }) }, { "identities.json", Contract.Encode(Array.Empty<ElementIdentity>()) }, { "scene.glb", glb } };
    var manifest = PdfPackageService.Describe(false, "Large distinct geometry", "0.5.3-test", files);
    manifest.RequiredCapabilities = manifest.RequiredCapabilities.Concat(new[] { "glb-affine-instances-v1" }).ToArray();
    Assert(manifest.RequiredCapabilities.Contains(SceneLimits.LargeSceneCapability)); Contract.Validate(manifest);
    string plain = Path.Combine(dir, "drawing.pdf"), pdf = Path.Combine(dir, "large.pdf");
    using (var d = new PdfSharp.Pdf.PdfDocument()) { var page = d.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(612); page.Height = PdfSharp.Drawing.XUnit.FromPoint(792); d.Save(plain); }
    var service = new PdfPackageService(); service.Write(plain, pdf, manifest, files);
    Assert(Contract.Hash(service.Read(pdf).Glb) == Contract.Hash(glb));
    Console.WriteLine("  large scene: " + glb.Length + " bytes written, packaged and reopened in " + watch.ElapsedMilliseconds + " ms");
});

// ---------------------------------------------------------------- overall progress (0.5.4)
Check("overall progress: weighted stages, links, regions, monotonic, finishes at 1", () =>
{
    double now = 0; var m = new X.ExportProgressModel(() => now);
    m.AddRegion(1000, new Dictionary<string, long> { { "linkA", 500 } });
    m.AddRegion(500, new Dictionary<string, long>());
    m.Enter(X.ExportStage.Preparing); now = 3; m.Update(); Assert(m.Fraction > 0 && m.Fraction < .05 && m.SecondsLeft == null, "preparing");
    m.BeginRegion(); double last = m.Fraction;
    for (int i = 0; i < 1000; i++) { m.ElementBegun(false); now += .01; if (i % 50 == 0) { m.Update(); Assert(m.Fraction >= last); last = m.Fraction; } }
    Assert(Math.Abs(m.GeometryFraction - 1000.0 / 2000) < 1e-9, "host share " + m.GeometryFraction);
    m.LinkEntered("linkA"); m.LinkEntered("nested"); for (int i = 0; i < 900; i++) m.ElementBegun(true); m.LinkExited();
    Assert(m.GeometryFraction <= (1000 + 490) / 2000.0 + 1e-9, "a link never counts past its estimate before it ends");
    m.LinkExited(); Assert(Math.Abs(m.GeometryFraction - 1500.0 / 2000) < 1e-9, "finished link adds its estimate");
    m.BeginRegion(); for (int i = 0; i < 250; i++) m.ElementBegun(false);
    Assert(Math.Abs(m.GeometryFraction - 1750.0 / 2000) < 1e-9, "second region continues");
    m.Enter(X.ExportStage.Writing); m.Enter(X.ExportStage.PdfPages); m.PdfPageDone(); m.PdfPageDone(); m.Enter(X.ExportStage.Finishing); m.Update();
    Assert(m.Fraction > .98 && m.Fraction <= 1, "end " + m.Fraction);
});

Check("time left follows the measured rate and counts down smoothly; size is projected from bytes so far", () =>
{
    double now = 0; var m = new X.ExportProgressModel(() => now);
    m.AddRegion(10000, new Dictionary<string, long>()); m.BeginRegion();
    // 100 elements per second: 100 s of geometry.
    double? previous = null;
    for (int second = 1; second <= 60; second++)
    {
        for (int i = 0; i < 100; i++) m.ElementBegun(false);
        now = second; m.SetModelBytes(second * 1_000_000L); m.Update();
        if (second == 1) Assert(m.SecondsLeft == null, "no estimate in the first 3 %");
        if (second >= 10) { Assert(m.SecondsLeft != null); if (previous != null) Assert(m.SecondsLeft < previous + 1, "does not jump up"); previous = m.SecondsLeft; }
    }
    // 40 s of geometry left plus writing (~2.5 s for ~100 MB) and two PDF pages (7 s).
    Assert(m.SecondsLeft > 42 && m.SecondsLeft < 56, "time left " + m.SecondsLeft);
    Assert(m.ProjectedPdfBytes > 55_000_000 && m.ProjectedPdfBytes < 70_000_000, "size " + m.ProjectedPdfBytes);
    Assert(X.ExportProgressModel.FormatTimeLeft(30) == "Less than a minute left" && X.ExportProgressModel.FormatTimeLeft(185) == "About 3 minutes left" && X.ExportProgressModel.FormatTimeLeft(null) == "");
    Assert(X.ExportProgressModel.FormatTimeLeft(3 * 3600 + 20 * 60) == "About 3 h 20 min left");
});

Check("export preferences remember the phase filter (and odd text) across exports; a broken file gives defaults", () =>
{
    string file = Path.Combine(root, "prefs", "export-options.txt"); if (File.Exists(file)) File.Delete(file);
    var first = new X.ExportPreferences(file); Assert(first.Get("phaseFilter") == null);
    first.Set("phaseFilter", "Show Complete"); first.Set("odd", "a=b\\c\nline"); Assert(first.Save());
    var again = new X.ExportPreferences(file); Assert(again.Get("phaseFilter") == "Show Complete" && again.Get("odd") == "a=b\\c\nline", again.Get("odd") ?? "null");
    File.WriteAllBytes(file, new byte[] { 0xff, 0xfe, 0x00 }); var broken = new X.ExportPreferences(file); Assert(broken.Get("phaseFilter") == null);
});

Console.WriteLine(passed + " exporter core checks passed in " + timer.ElapsedMilliseconds + " ms. Linux .NET, simulated Revit callbacks; native Revit execution remains untested.");

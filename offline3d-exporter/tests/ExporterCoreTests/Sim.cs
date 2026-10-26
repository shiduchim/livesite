extern alias exporter;
using E = exporter::Offline3D;
using X = exporter::Offline3D.RevitExporter;

// Minimal Revit-like scene tree and traversal for the exporter core tests.
abstract class SimNode { }
sealed class SimMesh : SimNode { public E.Point3[] Points = null!; public int[] Corners = null!; public E.Point3[] Normals = Array.Empty<E.Point3>(); public X.NormalLayout Layout = X.NormalLayout.PerFace; public E.PlainMaterial Material = null!; public string MaterialKey = ""; }
sealed class SimInstance : SimNode { public E.Matrix4 Transform = E.Matrix4.Identity; public string? Symbol; public List<SimNode> Children = new(); }
sealed class SimLink : SimNode { public E.Matrix4 Transform = E.Matrix4.Identity; public object? Document; public string Key = ""; public List<SimElement> Elements = new(); }
sealed class SimElement : SimNode { public string Unique = ""; public bool Model = true; public bool LinkInstance; public string Category = "Generic Models"; public List<SimNode> Children = new(); public int Visits; }

static class Sim
{
    public static (E.Point3, E.Point3)? Bounds(List<SimNode> nodes, E.Matrix4 world)
    {
        double x = double.PositiveInfinity, y = x, z = x, X = double.NegativeInfinity, Y = X, Z = X; bool any = false;
        void Walk(List<SimNode> list, E.Matrix4 m)
        {
            foreach (var node in list)
            {
                if (node is SimMesh mesh) foreach (var p in mesh.Points) { var q = m.Apply(p); any = true; x = Math.Min(x, q.X); y = Math.Min(y, q.Y); z = Math.Min(z, q.Z); X = Math.Max(X, q.X); Y = Math.Max(Y, q.Y); Z = Math.Max(Z, q.Z); }
                else if (node is SimInstance i) Walk(i.Children, m * i.Transform);
                else if (node is SimLink l) foreach (var e in l.Elements) Walk(e.Children, m * l.Transform);
            }
        }
        Walk(nodes, world);
        return any ? (new E.Point3(x, y, z), new E.Point3(X, Y, Z)) : null;
    }
    /// Revit-like traversal: children only after Proceed, end always sent.
    public static void Element(X.GeometryStream s, SimElement e, List<string> documentsSeen)
    {
        documentsSeen.Add((string)s.CurrentDocument!);
        var bounds = e.LinkInstance ? null : Bounds(e.Children, E.Matrix4.Identity);
        var info = new X.ElementInfo
        {
            Key = s.LinkChain + "::" + e.Unique, IsModel = e.Model, IsLinkInstance = e.LinkInstance, Category = e.Category, Label = e.Unique,
            Min = bounds?.Item1, Max = bounds?.Item2,
            Identity = m => new E.ElementIdentity { UniqueId = e.Unique, DocumentId = (string)s.CurrentDocument!, LinkInstanceId = s.LinkChain.Length == 0 ? null : s.LinkChain, MaterialName = m.Name, Category = e.Category }
        };
        if (s.BeginElement(info)) { e.Visits++; foreach (var child in e.Children) Node(s, child, documentsSeen); }
        s.EndElement();
    }
    static void Node(X.GeometryStream s, SimNode node, List<string> documentsSeen)
    {
        switch (node)
        {
            case SimMesh m: s.SetMaterial(m.Material, m.MaterialKey); s.AddMesh(m.Points, m.Corners, m.Normals, m.Layout); break;
            case SimInstance i: if (s.BeginInstance(i.Transform, i.Symbol)) foreach (var c in i.Children) Node(s, c, documentsSeen); s.EndInstance(); break;
            case SimLink l: if (s.BeginLink(l.Transform, l.Document, l.Key)) foreach (var e in l.Elements) Element(s, e, documentsSeen); s.EndLink(); break;
        }
    }
}


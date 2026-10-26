namespace Offline3D;
internal static class FeatureEdges
{
    private sealed class Edge{public uint A,B;public Point3 Normal;public int Count;public bool Sharp;}
    public static uint[] Build(IReadOnlyList<Point3> points,uint[] indices){
        var canonical=new Dictionary<string,uint>();var ids=new uint[points.Count];
        for(int i=0;i<points.Count;i++){var p=points[i];string key=p.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);if(!canonical.TryGetValue(key,out ids[i])){ids[i]=(uint)canonical.Count;canonical.Add(key,ids[i]);}}
        var edges=new Dictionary<ulong,Edge>();double cosine=Math.Cos(Math.PI/6);
        for(int i=0;i<indices.Length;i+=3){var a=points[(int)indices[i+1]]-points[(int)indices[i]];var b=points[(int)indices[i+2]]-points[(int)indices[i]];var n=new Point3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);n=n*(1/Math.Max(n.Length,1e-20));
            for(int k=0;k<3;k++){uint va=indices[i+k],vb=indices[i+(k+1)%3],ca=ids[va],cb=ids[vb];ulong key=((ulong)Math.Min(ca,cb)<<32)|Math.Max(ca,cb);if(edges.TryGetValue(key,out var e)){e.Count++;if(e.Normal.X*n.X+e.Normal.Y*n.Y+e.Normal.Z*n.Z<cosine)e.Sharp=true;}else edges.Add(key,new Edge{A=va,B=vb,Normal=n,Count=1});}
        }
        return edges.Values.Where(e=>e.Count!=2||e.Sharp).SelectMany(e=>new[]{e.A,e.B}).ToArray();
    }
}

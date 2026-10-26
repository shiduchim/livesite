namespace Offline3D;

public enum MeshNormalMode { PerVertex,PerFace,Single }

/// <summary>Only the current element's indexed local geometry is held in memory.</summary>
public sealed class IndexedGeometry
{
    public float[] Positions{get;set;}=Array.Empty<float>();
    public float[] Normals{get;set;}=Array.Empty<float>();
    public uint[] Indices{get;set;}=Array.Empty<uint>();
    public Point3 OriginFeet{get;set;}
}
public sealed class IndexedMeshAccumulator
{
    private readonly List<float> positions=new List<float>(),normals=new List<float>();
    private readonly List<uint> indices=new List<uint>();
    private readonly Dictionary<Vertex,uint> vertices=new Dictionary<Vertex,uint>();
    private Point3 origin;private bool initialized;
    public int TriangleCount=>indices.Count/3;
    private readonly struct Vertex:IEquatable<Vertex>{
        public readonly float X,Y,Z,NX,NY,NZ;
        public Vertex(Point3 p,Point3 n){X=(float)Math.Round(p.X,6);Y=(float)Math.Round(p.Y,6);Z=(float)Math.Round(p.Z,6);NX=(float)n.X;NY=(float)n.Y;NZ=(float)n.Z;}
        public bool Equals(Vertex b)=>X==b.X&&Y==b.Y&&Z==b.Z&&NX==b.NX&&NY==b.NY&&NZ==b.NZ;
        public override bool Equals(object? o)=>o is Vertex b&&Equals(b);
        public override int GetHashCode(){unchecked{int h=X.GetHashCode();h=h*397^Y.GetHashCode();h=h*397^Z.GetHashCode();h=h*397^NX.GetHashCode();h=h*397^NY.GetHashCode();h=h*397^NZ.GetHashCode();return h;}}
    }
    private sealed class NormalGroup{public Point3 Reference,Sum;}
    public void Append(IReadOnlyList<Point3> points,IReadOnlyList<Point3> sourceNormals,IReadOnlyList<int> facets,MeshNormalMode mode){
        if(facets.Count%3!=0)throw new InvalidDataException("Invalid mesh triangle indices");if(facets.Count==0)return;
        if(!initialized){origin=points[facets[0]];initialized=true;}
        var graphics=Matrix4.Graphics(new Point3());
        Point3 Normal(Point3 n){var p=new Point3(n.X,n.Z,-n.Y);return p*(1/Math.Max(p.Length,1e-20));}
        var faceNormals=new Point3[facets.Count/3];
        var groups=mode==MeshNormalMode.PerVertex?null:new Dictionary<int,List<NormalGroup>>();
        for(int face=0;face<faceNormals.Length;face++){
            int a=facets[face*3],b=facets[face*3+1],c=facets[face*3+2];if(a<0||b<0||c<0||a>=points.Count||b>=points.Count||c>=points.Count)throw new InvalidDataException("Mesh index outside points");
            var u=points[b]-points[a];var v=points[c]-points[a];var n=new Point3(u.Y*v.Z-u.Z*v.Y,u.Z*v.X-u.X*v.Z,u.X*v.Y-u.Y*v.X);
            int ni=mode==MeshNormalMode.PerFace?face:0;if(mode!=MeshNormalMode.PerVertex&&ni<sourceNormals.Count)n=sourceNormals[ni];if(n.Length<1e-12)n=new Point3(0,0,1);n=n*(1/Math.Max(n.Length,1e-20));faceNormals[face]=n;
            if(groups!=null)for(int k=0;k<3;k++){int index=facets[face*3+k];if(!groups.TryGetValue(index,out var list)){list=new List<NormalGroup>();groups.Add(index,list);}var g=list.FirstOrDefault(x=>Dot(x.Reference,n)>=.8660254037844386);if(g==null)list.Add(new NormalGroup{Reference=n,Sum=n});else g.Sum=g.Sum+n;}
        }
        for(int i=0;i<facets.Count;i++){
            int index=facets[i];var n=faceNormals[i/3];if(mode==MeshNormalMode.PerVertex&&index<sourceNormals.Count)n=sourceNormals[index].Length>1e-12?sourceNormals[index]:n;else if(groups!=null){var g=groups[index].First(x=>Dot(x.Reference,n)>=.8660254037844386);n=g.Sum;}
            var p=graphics.Apply(points[index]-origin);n=Normal(n);if(!Finite(p)||!Finite(n))throw new InvalidDataException("Non-finite geometry");var key=new Vertex(p,n);
            if(!vertices.TryGetValue(key,out uint vertex)){vertex=(uint)(positions.Count/3);vertices.Add(key,vertex);positions.Add(key.X);positions.Add(key.Y);positions.Add(key.Z);normals.Add(key.NX);normals.Add(key.NY);normals.Add(key.NZ);}
            indices.Add(vertex);
            if(((long)positions.Count+normals.Count+indices.Count)*4>Contract.MaxResource)throw new InvalidDataException("One element's indexed geometry exceeds 64 MiB. Select a smaller export scope or lower detail; no parts were omitted.");
        }
    }
    private static double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static bool Finite(Point3 p)=>!double.IsNaN(p.X+p.Y+p.Z)&&!double.IsInfinity(p.X+p.Y+p.Z);
    public IndexedGeometry Finish()=>new IndexedGeometry{Positions=positions.ToArray(),Normals=normals.ToArray(),Indices=indices.ToArray(),OriginFeet=origin};
}

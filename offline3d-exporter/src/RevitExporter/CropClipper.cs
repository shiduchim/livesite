namespace Offline3D.RevitExporter;

/// <summary>A plain position/normal pair, in Revit host internal feet.</summary>
internal readonly struct CropVertex
{
    public Point3 Position {get;}
    public Point3 Normal {get;}
    public CropVertex(Point3 position,Point3 normal){Position=position;Normal=normal;}
}

/// <summary>Exact convex plan-crop extrusion. Revit's section box provides the
/// early broad-phase; this class removes triangles outside the true crop before
/// they consume the export budget. It never receives Revit API objects.</summary>
internal sealed class CropClipper
{
    private const double Epsilon=1e-8;
    private readonly Plane[] planes;
    public Point3 Min {get;}
    public Point3 Max {get;}
    public Point3[] Polygon {get;}
    public double MinZ {get;}
    public double MaxZ {get;}

    public CropClipper(IEnumerable<Point3> polygon,double minZ,double maxZ)
    {
        Polygon=polygon.ToArray();
        if(Polygon.Length<3||Polygon.Length>64||Polygon.Any(p=>!Finite(p))||!Finite(minZ)||!Finite(maxZ)||maxZ-minZ<=Epsilon)
            throw new InvalidDataException("A finite convex crop and positive level range are required.");
        double area=0;var origin=Polygon[0];for(int i=0;i<Polygon.Length;i++){var a=Polygon[i]-origin;var b=Polygon[(i+1)%Polygon.Length]-origin;area+=a.X*b.Y-b.X*a.Y;}
        if(Math.Abs(area)<1e-12)throw new InvalidDataException("The plan crop is degenerate.");
        double winding=area>0?1:-1;
        var list=new List<Plane>();
        for(int i=0;i<Polygon.Length;i++){
            var a=Polygon[i];var b=Polygon[(i+1)%Polygon.Length];double dx=b.X-a.X,dy=b.Y-a.Y,length=Math.Sqrt(dx*dx+dy*dy);
            if(length<=Epsilon)throw new InvalidDataException("The plan crop has a zero-length edge.");
            // Inward unit normal: a positive plane value is inside.
            double nx=-dy*winding/length,ny=dx*winding/length;
            var plane=new Plane(nx,ny,0,a);
            if(Polygon.Any(p=>plane.Distance(p)<-Epsilon))throw new InvalidDataException("The plan crop must be convex and ordered.");
            list.Add(plane);
        }
        list.Add(new Plane(0,0,1,new Point3(0,0,minZ)));list.Add(new Plane(0,0,-1,new Point3(0,0,maxZ)));planes=list.ToArray();MinZ=minZ;MaxZ=maxZ;
        Min=new Point3(Polygon.Min(p=>p.X),Polygon.Min(p=>p.Y),minZ);Max=new Point3(Polygon.Max(p=>p.X),Polygon.Max(p=>p.Y),maxZ);
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    private static bool Finite(Point3 p)=>Finite(p.X)&&Finite(p.Y)&&Finite(p.Z);
    public bool Contains(Point3 point){if(!Finite(point))return false;foreach(var plane in planes)if(plane.Distance(point)<-Epsilon)return false;return true;}

    /// <summary>Allocation-free common path when a triangle is fully inside.
    /// Returns the number of output triangles appended to the plain mesh lists.</summary>
    public int AppendTriangle(Point3 a,Point3 b,Point3 c,Point3 na,Point3 nb,Point3 nc,List<Point3> positions,List<Point3> normals)
    {
        if(!Finite(a)||!Finite(b)||!Finite(c)||!Finite(na)||!Finite(nb)||!Finite(nc))throw new InvalidDataException("Non-finite mesh data.");
        if(Contains(a)&&Contains(b)&&Contains(c)){
            var u=b-a;var v=c-a;var cross=new Point3(u.Y*v.Z-u.Z*v.Y,u.Z*v.X-u.X*v.Z,u.X*v.Y-u.Y*v.X);
            if(cross.Length<=1e-12)return 0;
            positions.Add(a);positions.Add(b);positions.Add(c);normals.Add(na);normals.Add(nb);normals.Add(nc);return 1;
        }
        var clipped=ClipTriangle(a,b,c,na,nb,nc);foreach(var vertex in clipped){positions.Add(vertex.Position);normals.Add(vertex.Normal);}return clipped.Count/3;
    }

    public bool IntersectsBounds(Point3 min,Point3 max)
    {
        ValidateBounds(min,max);
        if(max.X<Min.X-Epsilon||min.X>Max.X+Epsilon||max.Y<Min.Y-Epsilon||min.Y>Max.Y+Epsilon||max.Z<MinZ-Epsilon||min.Z>MaxZ+Epsilon)return false;
        // Conservative support-point rejection: never discards intersecting data.
        foreach(var p in planes){var support=new Point3(p.X>=0?max.X:min.X,p.Y>=0?max.Y:min.Y,p.Z>=0?max.Z:min.Z);if(p.Distance(support)<-Epsilon)return false;}
        return true;
    }
    public bool ContainsBounds(Point3 min,Point3 max)
    {
        ValidateBounds(min,max);
        foreach(var p in planes){var support=new Point3(p.X>=0?min.X:max.X,p.Y>=0?min.Y:max.Y,p.Z>=0?min.Z:max.Z);if(p.Distance(support)<-Epsilon)return false;}
        return true;
    }
    private static void ValidateBounds(Point3 min,Point3 max){if(!Finite(min)||!Finite(max)||min.X>max.X||min.Y>max.Y||min.Z>max.Z)throw new InvalidDataException("Invalid element bounds.");}

    /// <returns>A flat triangle list. Adjacent generated vertices form triples.</returns>
    public IReadOnlyList<CropVertex> ClipTriangle(Point3 a,Point3 b,Point3 c,Point3 na,Point3 nb,Point3 nc)
    {
        if(!Finite(a)||!Finite(b)||!Finite(c)||!Finite(na)||!Finite(nb)||!Finite(nc))throw new InvalidDataException("Non-finite mesh data.");
        var min=new Point3(Math.Min(a.X,Math.Min(b.X,c.X)),Math.Min(a.Y,Math.Min(b.Y,c.Y)),Math.Min(a.Z,Math.Min(b.Z,c.Z)));
        var max=new Point3(Math.Max(a.X,Math.Max(b.X,c.X)),Math.Max(a.Y,Math.Max(b.Y,c.Y)),Math.Max(a.Z,Math.Max(b.Z,c.Z)));
        if(!IntersectsBounds(min,max))return Array.Empty<CropVertex>();
        var source=new List<CropVertex>{new CropVertex(a,na),new CropVertex(b,nb),new CropVertex(c,nc)};
        foreach(var plane in planes){
            bool allInside=true;for(int i=0;i<source.Count;i++)if(plane.Distance(source[i].Position)<-Epsilon){allInside=false;break;}
            if(allInside)continue;
            var clipped=new List<CropVertex>(source.Count+1);
            for(int i=0;i<source.Count;i++){
                var first=source[i];var second=source[(i+1)%source.Count];double da=plane.Distance(first.Position),db=plane.Distance(second.Position);bool insideA=da>=-Epsilon,insideB=db>=-Epsilon;
                if(insideA)clipped.Add(first);
                if(insideA!=insideB){
                    double t=da/(da-db);t=Math.Max(0,Math.Min(1,t));
                    var position=first.Position+(second.Position-first.Position)*t;
                    var normal=first.Normal+(second.Normal-first.Normal)*t;
                    if(normal.Length>1e-20)normal=normal*(1/normal.Length);
                    clipped.Add(new CropVertex(position,normal));
                }
            }
            source=clipped;if(source.Count<3)return Array.Empty<CropVertex>();
        }
        var result=new List<CropVertex>((source.Count-2)*3);
        for(int i=1;i+1<source.Count;i++){
            var u=source[i].Position-source[0].Position;var v=source[i+1].Position-source[0].Position;
            var cross=new Point3(u.Y*v.Z-u.Z*v.Y,u.Z*v.X-u.X*v.Z,u.X*v.Y-u.Y*v.X);
            if(cross.Length<=1e-12)continue;
            result.Add(source[0]);result.Add(source[i]);result.Add(source[i+1]);
        }
        return result;
    }
    private readonly struct Plane
    {
        public double X{get;}public double Y{get;}public double Z{get;}private Point3 Anchor{get;}
        public Plane(double x,double y,double z,Point3 anchor){X=x;Y=y;Z=z;Anchor=anchor;}
        public double Distance(Point3 p)=>X*(p.X-Anchor.X)+Y*(p.Y-Anchor.Y)+Z*(p.Z-Anchor.Z);
    }
}

internal sealed class LevelDatum
{
    public string Name{get;}
    public double ElevationFeet{get;}
    public LevelDatum(string name,double elevationFeet){Name=name;ElevationFeet=elevationFeet;}
}
internal sealed class AdjacentLevelRange
{
    public double LevelFeet{get;private set;}
    public double MinZ{get;private set;}
    public double MaxZ{get;private set;}
    public string TopLabel{get;private set;}="";
    public string BottomLabel{get;private set;}="";
    public bool TopFallback{get;private set;}
    public bool BottomFallback{get;private set;}
    public static AdjacentLevelRange Select(double currentFeet,IEnumerable<LevelDatum> levels,double fallbackAboveFeet=10,double fallbackBelowFeet=10)
    {
        bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        if(!Finite(currentFeet)||!Finite(fallbackAboveFeet)||!Finite(fallbackBelowFeet)||fallbackAboveFeet<=1e-6||fallbackBelowFeet<=1e-6)throw new InvalidDataException("Fallback level heights must be finite and positive.");
        var all=levels.ToArray();if(all.Any(l=>!Finite(l.ElevationFeet)))throw new InvalidDataException("Non-finite level elevation.");
        var above=all.Where(l=>l.ElevationFeet>currentFeet+1e-6).OrderBy(l=>l.ElevationFeet).FirstOrDefault();
        var below=all.Where(l=>l.ElevationFeet<currentFeet-1e-6).OrderByDescending(l=>l.ElevationFeet).FirstOrDefault();
        string Feet(double value)=>value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" ft";
        return new AdjacentLevelRange{LevelFeet=currentFeet,MinZ=below?.ElevationFeet??currentFeet-fallbackBelowFeet,MaxZ=above?.ElevationFeet??currentFeet+fallbackAboveFeet,
            TopLabel=above?.Name??"Fallback: "+Feet(fallbackAboveFeet)+" above (no higher level)",BottomLabel=below?.Name??"Fallback: "+Feet(fallbackBelowFeet)+" below (no lower level)",TopFallback=above==null,BottomFallback=below==null};
    }
}

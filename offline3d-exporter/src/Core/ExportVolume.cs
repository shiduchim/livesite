namespace Offline3D;

/// <summary>A view crop in its own axes, expressed in host internal feet.</summary>
public sealed class ExportVolume
{
    public Point3 Min {get;} public Point3 Max {get;}
    public Matrix4 HostToLocal {get;}
    public static Point3[] PlanBounds(Point3 min,Point3 max,Point3 modelMin,Point3 modelMax,bool outside,bool allUp,bool allDown)=>new[]{
        new Point3(outside?Math.Min(min.X,modelMin.X):min.X,outside?Math.Min(min.Y,modelMin.Y):min.Y,allDown?Math.Min(min.Z,modelMin.Z):min.Z),
        new Point3(outside?Math.Max(max.X,modelMax.X):max.X,outside?Math.Max(max.Y,modelMax.Y):max.Y,allUp?Math.Max(max.Z,modelMax.Z):max.Z)};
    public ExportVolume(Point3 min,Point3 max,Matrix4 localToHost)
    {
        if(new[]{min.X,min.Y,min.Z,max.X,max.Y,max.Z}.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||max.X<=min.X||max.Y<=min.Y||max.Z<=min.Z)throw new InvalidDataException("Invalid export crop volume");
        Min=min;Max=max;HostToLocal=localToHost.Inverse();
    }
    public bool IntersectsBounds(Point3 min,Point3 max,Matrix4 sourceToHost,out bool contained)
    {
        var transform=HostToLocal*sourceToHost;double x=double.PositiveInfinity,y=x,z=x,X=double.NegativeInfinity,Y=X,Z=X;
        for(int i=0;i<8;i++){var p=transform.Apply(new Point3((i&1)==0?min.X:max.X,(i&2)==0?min.Y:max.Y,(i&4)==0?min.Z:max.Z));x=Math.Min(x,p.X);y=Math.Min(y,p.Y);z=Math.Min(z,p.Z);X=Math.Max(X,p.X);Y=Math.Max(Y,p.Y);Z=Math.Max(Z,p.Z);}
        const double epsilon=1e-8;
        contained=x>=Min.X-epsilon&&y>=Min.Y-epsilon&&z>=Min.Z-epsilon&&X<=Max.X+epsilon&&Y<=Max.Y+epsilon&&Z<=Max.Z+epsilon;
        return X>=Min.X-epsilon&&x<=Max.X+epsilon&&Y>=Min.Y-epsilon&&y<=Max.Y+epsilon&&Z>=Min.Z-epsilon&&z<=Max.Z+epsilon;
    }
    // Triangle/AABB SAT in crop-local coordinates. Retain original boundary
    // triangles; never split them or change their normals/materials.
    public bool IntersectsTriangleLocal(Point3 a,Point3 b,Point3 c)
    {
        var center=(Min+Max)*.5;var half=(Max-Min)*.5;a=a-center;b=b-center;c=c-center;
        var e0=b-a;var e1=c-b;var e2=a-c;
        bool Separated(Point3 axis){double length=axis.Length;if(length<1e-14)return false;double A=Dot(a,axis),B=Dot(b,axis),C=Dot(c,axis),radius=half.X*Math.Abs(axis.X)+half.Y*Math.Abs(axis.Y)+half.Z*Math.Abs(axis.Z),epsilon=1e-8*length;return Math.Min(A,Math.Min(B,C))>radius+epsilon||Math.Max(A,Math.Max(B,C))< -radius-epsilon;}
        if(Separated(new Point3(1,0,0))||Separated(new Point3(0,1,0))||Separated(new Point3(0,0,1))||Separated(Cross(e0,e1)))return false;
        return !Separated(new Point3(0,e0.Z,-e0.Y))&&!Separated(new Point3(-e0.Z,0,e0.X))&&!Separated(new Point3(e0.Y,-e0.X,0))
            &&!Separated(new Point3(0,e1.Z,-e1.Y))&&!Separated(new Point3(-e1.Z,0,e1.X))&&!Separated(new Point3(e1.Y,-e1.X,0))
            &&!Separated(new Point3(0,e2.Z,-e2.Y))&&!Separated(new Point3(-e2.Z,0,e2.X))&&!Separated(new Point3(e2.Y,-e2.X,0));
    }
    private static double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static Point3 Cross(Point3 a,Point3 b)=>new Point3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
}

/// <summary>Filters a mesh batch, including linked geometry without element callbacks.</summary>
public sealed class CropMeshSelection
{
    private readonly Point3[][] localPoints;
    private readonly ExportVolume[] volumes;
    public bool FullyInside {get;}
    public bool Outside=>!FullyInside&&volumes.Length==0;
    public CropMeshSelection(IReadOnlyList<Point3> points,Matrix4 sourceToHost,IEnumerable<ExportVolume> crops)
    {
        if(points.Count==0){volumes=Array.Empty<ExportVolume>();localPoints=Array.Empty<Point3[]>();return;}
        var min=new Point3(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z));var max=new Point3(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z));
        var accepted=new List<ExportVolume>();
        foreach(var volume in crops){if(!volume.IntersectsBounds(min,max,sourceToHost,out bool contained))continue;if(contained){FullyInside=true;volumes=Array.Empty<ExportVolume>();localPoints=Array.Empty<Point3[]>();return;}accepted.Add(volume);}
        volumes=accepted.ToArray();localPoints=new Point3[volumes.Length][];
        for(int i=0;i<volumes.Length;i++){var transform=volumes[i].HostToLocal*sourceToHost;localPoints[i]=points.Select(transform.Apply).ToArray();}
    }
    public bool Keep(int a,int b,int c)
    {
        if(FullyInside)return true;
        for(int i=0;i<volumes.Length;i++){var p=localPoints[i];if(volumes[i].IntersectsTriangleLocal(p[a],p[b],p[c]))return true;}
        return false;
    }
}

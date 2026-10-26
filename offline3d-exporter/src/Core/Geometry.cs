namespace Offline3D;

public readonly struct Point3
{
    public double X { get; } public double Y { get; } public double Z { get; }
    public Point3(double x, double y, double z = 0) { X=x; Y=y; Z=z; }
    public static Point3 operator +(Point3 a, Point3 b)=>new Point3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static Point3 operator -(Point3 a, Point3 b)=>new Point3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static Point3 operator *(Point3 a,double n)=>new Point3(a.X*n,a.Y*n,a.Z*n);
    public double Length=>Math.Sqrt(X*X+Y*Y+Z*Z);
}

// Row-major doubles, column vectors. Composition A*B applies B first.
public sealed class Matrix4
{
    public double[] Values { get; set; } = new double[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    public static Matrix4 Identity=>new Matrix4();
    public Matrix4() { }
    public Matrix4(params double[] v) { Values=v; Validate(); }
    public void Validate() { if(Values==null || Values.Length!=16 || Values.Any(v=>double.IsNaN(v)||double.IsInfinity(v))) throw new InvalidDataException("Invalid matrix"); }
    public Point3 Apply(Point3 p) {
        var m=Values; double w=m[12]*p.X+m[13]*p.Y+m[14]*p.Z+m[15];
        if(Math.Abs(w)<1e-14)throw new InvalidDataException("Point at infinity");
        return new Point3((m[0]*p.X+m[1]*p.Y+m[2]*p.Z+m[3])/w,(m[4]*p.X+m[5]*p.Y+m[6]*p.Z+m[7])/w,(m[8]*p.X+m[9]*p.Y+m[10]*p.Z+m[11])/w);
    }
    public static Matrix4 operator *(Matrix4 a,Matrix4 b) {
        var v=new double[16];for(int r=0;r<4;r++)for(int c=0;c<4;c++)for(int k=0;k<4;k++)v[r*4+c]+=a.Values[r*4+k]*b.Values[k*4+c];return new Matrix4(v);
    }
    public Matrix4 Inverse() {
        Validate();var a=new double[4,8];for(int r=0;r<4;r++)for(int c=0;c<4;c++){a[r,c]=Values[r*4+c];a[r,c+4]=r==c?1:0;}
        for(int c=0;c<4;c++) { int pivot=c; for(int r=c+1;r<4;r++)if(Math.Abs(a[r,c])>Math.Abs(a[pivot,c]))pivot=r;
            if(Math.Abs(a[pivot,c])<1e-14)throw new InvalidDataException("Singular matrix");
            for(int k=0;k<8;k++){double t=a[c,k];a[c,k]=a[pivot,k];a[pivot,k]=t;}
            double d=a[c,c];for(int k=0;k<8;k++)a[c,k]/=d;
            for(int r=0;r<4;r++)if(r!=c){double q=a[r,c];for(int k=0;k<8;k++)a[r,k]-=q*a[c,k];}
        }
        var v=new double[16];for(int r=0;r<4;r++)for(int c=0;c<4;c++)v[r*4+c]=a[r,c+4];return new Matrix4(v);
    }
    public static Matrix4 Graphics(Point3 originFeet)=>new Matrix4(.3048,0,0,-originFeet.X*.3048,0,0,.3048,-originFeet.Z*.3048,0,-.3048,0,originFeet.Y*.3048,0,0,0,1);
}

public static class Coordinates
{
    public static bool Inside(double[] polygon,double x,double y) {
        bool inside=false;int n=polygon.Length/2;
        for(int i=0,j=n-1;i<n;j=i++) {double ax=polygon[j*2],ay=polygon[j*2+1],bx=polygon[i*2],by=polygon[i*2+1];
            double cross=(x-ax)*(by-ay)-(y-ay)*(bx-ax);
            if(Math.Abs(cross)<1e-8 && x>=Math.Min(ax,bx)-1e-8 && x<=Math.Max(ax,bx)+1e-8 && y>=Math.Min(ay,by)-1e-8 && y<=Math.Max(ay,by)+1e-8)return true;
            if((ay>y)!=(by>y) && x<(bx-ax)*(y-ay)/(by-ay)+ax)inside=!inside;
        }return inside;
    }
    public static Point3 PlanClick(Registration r,double x,double y) {
        if(!r.PreciseClicks || !(Inside(r.CropPdf,x,y)||r.ViewRegions.Any(v=>Inside(v.CropPdf,x,y))))throw new InvalidDataException("Point outside calibrated plan coverage");
        var region=r.ViewRegions.FirstOrDefault(v=>Inside(v.CropPdf,x,y));
        return region==null?PlanPointOnLevel(r,x,y):PointOnPlane(region.ModelToPdf,x,y,region.PlaneOrigin,region.PlaneNormal,region.LevelFeet);
    }
    // Resizing can put a box edge outside the crop; unlike selecting its center,
    // projecting an edge does not assert that geometry exists there.
    public static Point3 PlanPointOnLevel(Registration r,double x,double y) {
        if(!r.PreciseClicks||double.IsNaN(x)||double.IsNaN(y)||double.IsInfinity(x)||double.IsInfinity(y))throw new InvalidDataException("A verified finite plan point is required");
        return PointOnPlane(r.ModelToPdf,x,y,r.ClickPlaneOrigin,r.ClickPlaneNormal,r.LevelProjectElevationFeet);
    }
    public static Point3 PointOnPlane(Matrix4 transform,double x,double y,double[] origin,double[] normal,double fallbackLevel){
        var inverse=transform.Inverse();var a=inverse.Apply(new Point3(x,y,0));var b=inverse.Apply(new Point3(x,y,1));var ray=b-a;
        var o=origin.Length==3?new Point3(origin[0],origin[1],origin[2]):new Point3(0,0,fallbackLevel);
        var n=normal.Length==3?new Point3(normal[0],normal[1],normal[2]):new Point3(0,0,1);
        double Dot(Point3 p,Point3 q)=>p.X*q.X+p.Y*q.Y+p.Z*q.Z;
        double denominator=Dot(ray,n);if(Math.Abs(denominator)<1e-12)throw new InvalidDataException("Unsupported view ray");
        return a+ray*(Dot(o-a,n)/denominator);
    }
}

public sealed class ControlPoint
{
    public double SheetX {get;set;} public double SheetY {get;set;} public double PdfX {get;set;} public double PdfY {get;set;}
}
public sealed class CalibrationInput
{
    public ControlPoint[] Fit {get;set;}=Array.Empty<ControlPoint>();
    public ControlPoint[] Check {get;set;}=Array.Empty<ControlPoint>();
}
public sealed class CalibrationResult
{
    public Matrix4 SheetToPdf {get;set;}=Matrix4.Identity;
    public double MaxResidualMetres {get;set;}
}
public static class Calibration
{
    public static CalibrationResult Fit(CalibrationInput input,Matrix4 modelToSheet,double levelFeet,double[]? planeOrigin=null,double[]? planeNormal=null) {
        if(input.Fit.Length<3 || input.Check.Length<3)throw new InvalidDataException("Use at least three fit and three independent check points");
        var all=input.Fit.Concat(input.Check).ToArray();
        if(all.Any(p=>new[]{p.SheetX,p.SheetY,p.PdfX,p.PdfY}.Any(v=>double.IsNaN(v)||double.IsInfinity(v))))throw new InvalidDataException("Non-finite control point");
        if(input.Fit.Any(f=>input.Check.Any(c=>Math.Abs(f.SheetX-c.SheetX)+Math.Abs(f.SheetY-c.SheetY)<1e-10)))throw new InvalidDataException("Check points must be independent");
        double cx=input.Fit.Average(p=>p.SheetX),cy=input.Fit.Average(p=>p.SheetY);
        double xx=0,xy=0,yy=0;var rhsX=new double[3];var rhsY=new double[3];
        foreach(var p in input.Fit){double x=p.SheetX-cx,y=p.SheetY-cy;xx+=x*x;xy+=x*y;yy+=y*y;rhsX[0]+=x*p.PdfX;rhsX[1]+=y*p.PdfX;rhsX[2]+=p.PdfX;rhsY[0]+=x*p.PdfY;rhsY[1]+=y*p.PdfY;rhsY[2]+=p.PdfY;}
        double det=xx*yy-xy*xy;if(det<=1e-12*xx*yy || xx<=0 || yy<=0)throw new InvalidDataException("Degenerate calibration");
        double[] Solve(double[] v){double a=(yy*v[0]-xy*v[1])/det,b=(xx*v[1]-xy*v[0])/det;return new[]{a,b,v[2]/input.Fit.Length-a*cx-b*cy};}
        var u=Solve(rhsX);var v=Solve(rhsY);var transform=new Matrix4(u[0],u[1],0,u[2],v[0],v[1],0,v[2],0,0,1,0,0,0,0,1);
        var r=new Registration{ClickPlaneOrigin=planeOrigin??Array.Empty<double>(),ClickPlaneNormal=planeNormal??Array.Empty<double>(),PreciseClicks=true,ModelToPdf=transform*modelToSheet,LevelProjectElevationFeet=levelFeet,CropPdf=new[]{-1e12,-1e12,1e12,-1e12,1e12,1e12,-1e12,1e12}};
        double residual=all.Max(p=>(Coordinates.PlanClick(r,p.PdfX,p.PdfY)-Coordinates.PlanClick(r,transform.Apply(new Point3(p.SheetX,p.SheetY)).X,transform.Apply(new Point3(p.SheetX,p.SheetY)).Y)).Length*.3048);
        if(residual>.001)throw new InvalidDataException("Registration exceeds 1 mm numeric residual: "+residual.ToString("G6"));
        return new CalibrationResult{SheetToPdf=transform,MaxResidualMetres=residual};
    }
}

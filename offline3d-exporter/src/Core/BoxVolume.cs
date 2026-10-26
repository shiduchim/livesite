namespace Offline3D;

/// <summary>Axis-aligned section bounds in local graphics metres.</summary>
public sealed class BoxVolume
{
    public double MinX{get;set;} public double MaxX{get;set;}
    public double MinY{get;set;} public double MaxY{get;set;}
    public double MinZ{get;set;} public double MaxZ{get;set;}
    public double Level{get;set;}
    public double Width=>MaxX-MinX;
    public double Depth=>MaxZ-MinZ;
    public double Above=>MaxY-Level;
    public double Below=>Level-MinY;
    public void Validate(){
        if(new[]{MinX,MaxX,MinY,MaxY,MinZ,MaxZ,Level}.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||Math.Abs(v)>10000000)||Width<.01||Width>20000000||Depth<.01||Depth>20000000||Above<.01||Above>20000000||Below<0||Below>20000000)
            throw new InvalidDataException("Box bounds must be finite with positive dimensions; below level may be zero.");
    }
    public static BoxVolume Stock(Point3 point)=>Centered(point,3.048,3.048,3.048,.1524);
    public static BoxVolume Centered(Point3 p,double width,double depth,double above,double below){var box=new BoxVolume{MinX=p.X-width/2,MaxX=p.X+width/2,MinY=p.Y-below,MaxY=p.Y+above,MinZ=p.Z-depth/2,MaxZ=p.Z+depth/2,Level=p.Y};box.Validate();return box;}
    public Point3[] PdfFootprint(Registration registration){
        Validate();var toModel=registration.ModelToGraphics.Inverse();
        return new[]{new Point3(MinX,Level,MinZ),new Point3(MaxX,Level,MinZ),new Point3(MaxX,Level,MaxZ),new Point3(MinX,Level,MaxZ)}.Select(p=>registration.ModelToPdf.Apply(toModel.Apply(p))).ToArray();
    }
}

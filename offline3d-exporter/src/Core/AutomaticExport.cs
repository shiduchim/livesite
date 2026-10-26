namespace Offline3D;
public sealed class SheetMarker
{
    public int Id {get;set;}
    public double SheetX {get;set;} public double SheetY {get;set;}
    public int Red {get;set;}=241; public int Green {get;set;} public int Blue {get;set;}=229;
}
public static class AutomaticExport
{
    public static SheetMarker[] Markers(double[] cropSheet) {
        if(cropSheet.Length!=8)throw new InvalidDataException("One rectangular sheet crop required");
        var markers=new List<SheetMarker>();
        for(int y=0;y<5;y++)for(int x=0;x<5;x++){
            double u=.08+.21*x,v=.08+.21*y;int i=y*5+x;
            markers.Add(new SheetMarker{Id=i,Green=7+9*i,SheetX=cropSheet[0]+u*(cropSheet[2]-cropSheet[0])+v*(cropSheet[6]-cropSheet[0]),SheetY=cropSheet[1]+u*(cropSheet[3]-cropSheet[1])+v*(cropSheet[7]-cropSheet[1])});
        }return markers.ToArray();
    }
}

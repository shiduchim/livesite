using Offline3D;
using Offline3D.RevitExporter;
int passed=0;
void Check(string name,Action f){f();passed++;Console.WriteLine("PASS "+name);}
void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
void Equal(double a,double b,double epsilon=1e-8)=>Assert(Math.Abs(a-b)<=epsilon);
void Reject(Action f){try{f();}catch(InvalidDataException){return;}throw new Exception("Expected invalid crop rejection");}
Point3 P(double x,double y,double z=0)=>new Point3(x,y,z);
var normal=P(0,0,1);
var square=new[]{P(0,0),P(10,0),P(10,10),P(0,10)};
var crop=new CropClipper(square,-3,8);
double Area(IReadOnlyList<CropVertex> triangles){double area=0;for(int i=0;i<triangles.Count;i+=3){var a=triangles[i+1].Position-triangles[i].Position;var b=triangles[i+2].Position-triangles[i].Position;area+=new Point3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X).Length/2;}return area;}
Check("nearest distinct levels replace full 60-metre height",()=>{var r=AdjacentLevelRange.Select(10,new[]{new LevelDatum("Roof",50),new LevelDatum("Ground",0),new LevelDatum("L1 duplicate",10),new LevelDatum("L2",22),new LevelDatum("Mezzanine",14)});Equal(r.MinZ,0);Equal(r.MaxZ,14);Assert(r.TopLabel=="Mezzanine"&&r.BottomLabel=="Ground"&&!r.TopFallback&&!r.BottomFallback);});
Check("same-elevation level duplicates are skipped",()=>{var r=AdjacentLevelRange.Select(10,new[]{new LevelDatum("Same",10+1e-7),new LevelDatum("Above",20),new LevelDatum("Below",2)});Equal(r.MaxZ,20);Equal(r.MinZ,2);});
Check("missing upper level uses configurable positive fallback",()=>{var r=AdjacentLevelRange.Select(10,new[]{new LevelDatum("Ground",0)},12,6);Equal(r.MaxZ,22);Equal(r.MinZ,0);Assert(r.TopFallback&&!r.BottomFallback&&r.TopLabel.Contains("12 ft"));});
Check("missing lower level uses configurable fallback",()=>{var r=AdjacentLevelRange.Select(10,new[]{new LevelDatum("Up",20)},12,6);Equal(r.MaxZ,20);Equal(r.MinZ,4);Assert(!r.TopFallback&&r.BottomFallback);});
Check("no adjacent levels use defaults and preserve project elevation",()=>{var r=AdjacentLevelRange.Select(100,Array.Empty<LevelDatum>());Equal(r.MinZ,90);Equal(r.MaxZ,110);Equal(r.LevelFeet,100);});
Check("invalid level ranges are rejected",()=>{Reject(()=>AdjacentLevelRange.Select(double.NaN,Array.Empty<LevelDatum>()));Reject(()=>AdjacentLevelRange.Select(0,Array.Empty<LevelDatum>(),0));Reject(()=>AdjacentLevelRange.Select(0,new[]{new LevelDatum("Bad",double.PositiveInfinity)}));});
Check("inside and boundary points are accepted",()=>{Assert(crop.Contains(P(0,0,-3)));Assert(crop.Contains(P(5,5,0)));Assert(crop.Contains(P(10,10,8)));});
Check("horizontal and vertical outside points are rejected",()=>{Assert(!crop.Contains(P(-.1,5)));Assert(!crop.Contains(P(5,5,-3.1)));Assert(!crop.Contains(P(5,5,8.1)));});
Check("fully inside triangle retains geometry and area",()=>{var t=crop.ClipTriangle(P(1,1),P(9,1),P(1,9),normal,normal,normal);Assert(t.Count==3);Equal(Area(t),32);});
Check("outside triangles are rejected before output budget",()=>{Assert(crop.ClipTriangle(P(20,20),P(21,20),P(20,21),normal,normal,normal).Count==0);});
Check("crossing triangle is clipped to exact area and crop",()=>{var t=crop.ClipTriangle(P(-10,0),P(10,0),P(10,20),normal,normal,normal);Assert(t.Count%3==0&&t.All(v=>crop.Contains(v.Position)));Equal(Area(t),100);});
Check("height clipping returns only level-contained triangles",()=>{var t=crop.ClipTriangle(P(1,1,-10),P(9,1,10),P(1,9,10),normal,normal,normal);Assert(t.Count>0&&t.All(v=>v.Position.Z>=-3-1e-8&&v.Position.Z<=8+1e-8));});
Check("clockwise crop winding clips identically",()=>{var reversed=new CropClipper(square.AsEnumerable().Reverse(),-3,8);var a=crop.ClipTriangle(P(-10,0),P(10,0),P(10,20),normal,normal,normal);var b=reversed.ClipTriangle(P(-10,0),P(10,0),P(10,20),normal,normal,normal);Equal(Area(a),Area(b));});
var rotated=new CropClipper(new[]{P(0,5),P(5,0),P(10,5),P(5,10)},-3,8);
Check("rotated crop rejects its axis-aligned bounding-box corners",()=>{Assert(!rotated.Contains(P(.5,.5)));Assert(rotated.Contains(P(5,5)));Assert(!rotated.IntersectsBounds(P(0,0,0),P(1,1,1)));});
Check("rotated crop triangle clipping follows actual diagonal edges",()=>{var t=rotated.ClipTriangle(P(0,0),P(10,0),P(0,10),normal,normal,normal);Assert(t.Count>0&&t.All(v=>rotated.Contains(v.Position)));Equal(Area(t),25);});
Check("bounding-box containment and conservative overlap are consistent",()=>{Assert(crop.ContainsBounds(P(1,1,-2),P(9,9,7)));Assert(!crop.ContainsBounds(P(-1,1,-2),P(9,9,7)));Assert(crop.IntersectsBounds(P(-1,1,-2),P(9,9,7)));Assert(!crop.IntersectsBounds(P(1,1,9),P(9,9,10)));});
Check("intersection normals are interpolated and normalized",()=>{var t=crop.ClipTriangle(P(-2,2),P(2,2),P(2,8),P(1,0,0),P(0,1,0),P(0,0,1));Assert(t.Count>0);foreach(var v in t.Where(v=>Math.Abs(v.Position.X)<1e-8))Equal(v.Normal.Length,1);});
Check("degenerate triangles emit no geometry",()=>Assert(crop.ClipTriangle(P(1,1),P(2,2),P(3,3),normal,normal,normal).Count==0));
Check("nonfinite mesh data is rejected",()=>Reject(()=>crop.ClipTriangle(P(double.NaN,1),P(2,2),P(3,3),normal,normal,normal)));
Check("invalid concave and self-crossing crop polygons are rejected",()=>{Reject(()=>new CropClipper(new[]{P(0,0),P(10,0),P(2,2),P(0,10)},0,10));Reject(()=>new CropClipper(new[]{P(0,0),P(10,10),P(10,0),P(0,10)},0,10));Reject(()=>new CropClipper(square,10,0));});
Check("large-origin crop uses local distance arithmetic",()=>{double o=1e9;var c=new CropClipper(square.Select(p=>P(p.X+o,p.Y-o)),100,120);Assert(c.Contains(P(o+5,-o+5,110)));Assert(!c.Contains(P(o-1,-o+5,110)));var t=c.ClipTriangle(P(o-10,-o,110),P(o+10,-o,110),P(o+10,-o+20,110),normal,normal,normal);Equal(Area(t),100,1e-5);});
Console.WriteLine(passed+" export scope and exact crop checks passed. Linux plain-data tests; native Revit section-box/export execution remains untested.");

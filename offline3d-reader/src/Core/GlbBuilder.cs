namespace Offline3D;
public sealed class PlainMesh
{
    public string Name {get;set;}="";
    public ElementIdentity? Identity {get;set;}
    public bool SmoothNormals {get;set;}
    public List<Point3> Triangles {get;set;}=new List<Point3>();
    public List<Point3> Normals {get;set;}=new List<Point3>();
    public PlainMaterial Material {get;set;}=new PlainMaterial();
}
public sealed class PlainMaterial
{
    public string Name{get;set;}="Default";
    public double Red{get;set;}= .7;
    public double Green{get;set;}= .7;
    public double Blue{get;set;}= .7;
    public double Opacity{get;set;}=1;
    public double Roughness{get;set;}=.8;
    public double Metallic{get;set;}
    public CutPatternDefinition? CutPattern{get;set;}
    public static double Linear(double srgb)=>srgb<=.04045?srgb/12.92:Math.Pow((srgb+.055)/1.055,2.4);
    public void Validate(){if(new[]{Red,Green,Blue,Opacity,Roughness,Metallic}.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||v<0||v>1))throw new InvalidDataException("Invalid material surface value");}
}
public sealed class GlbBuildStats {public int Elements{get;set;}public int UniqueMeshes{get;set;}public int Instances{get;set;}public long Triangles{get;set;}public long UniqueTriangles{get;set;}public long Bytes{get;set;}}
public static class GlbBuilder
{
    public static byte[] Encode(IEnumerable<PlainMesh> input,Matrix4 hostToGraphics)=>Encode(input,hostToGraphics,out _);
    public static byte[] Encode(IEnumerable<PlainMesh> input,Matrix4 hostToGraphics,out GlbBuildStats stats) {
        var meshes=input.Where(m=>m.Triangles.Count>0).ToArray();if(meshes.Length==0)throw new InvalidDataException("No render geometry");
        var bufferViews=new List<object>();var accessors=new List<object>();var glMeshes=new List<object>();var materials=new List<object>();var materialIndices=new Dictionary<string,int>();
        var normalFrame=hostToGraphics.Inverse().Values;var nodeMeshes=new List<int>();var nodeTranslations=new List<double[]>();var sharedGeometry=new Dictionary<string,int>();
        Point3 Normal(Point3 n){var p=new Point3(normalFrame[0]*n.X+normalFrame[4]*n.Y+normalFrame[8]*n.Z,normalFrame[1]*n.X+normalFrame[5]*n.Y+normalFrame[9]*n.Z,normalFrame[2]*n.X+normalFrame[6]*n.Y+normalFrame[10]*n.Z);return p*(1/Math.Max(p.Length,1e-20));}
        using(var bin=new MemoryStream())using(var writer=new BinaryWriter(bin)){
            foreach(var mesh in meshes){
                if(mesh.Triangles.Count%3!=0)throw new InvalidDataException("Expected triangles");
                if(mesh.Normals.Count!=0&&mesh.Normals.Count!=mesh.Triangles.Count)throw new InvalidDataException("Mismatched vertex normals");
                mesh.Material.Validate();var smooth=mesh.SmoothNormals?CreaseNormals.Compute(mesh.Triangles,mesh.Normals):null;var material=mesh.Material;
                string materialKey=Contract.Hash(Contract.Encode(material));
                if(!materialIndices.TryGetValue(materialKey,out int materialIndex)){materialIndex=materials.Count;materialIndices.Add(materialKey,materialIndex);materials.Add(new{name=material.Name,extras=new{cutPattern=material.CutPattern},doubleSided=true,alphaMode=material.Opacity<.999?"BLEND":"OPAQUE",pbrMetallicRoughness=new{baseColorFactor=new[]{PlainMaterial.Linear(material.Red),PlainMaterial.Linear(material.Green),PlainMaterial.Linear(material.Blue),material.Opacity},metallicFactor=material.Metallic,roughnessFactor=material.Roughness}});}
                // Preserve triangle order but store identical float32 position/normal
                // pairs once. Separate normals remain separate vertices at hard edges.
                var first=hostToGraphics.Apply(mesh.Triangles[0]);nodeTranslations.Add(new[]{first.X,first.Y,first.Z});
                var points=new List<Point3>();var normals=new List<Point3>();
                var indices=new uint[mesh.Triangles.Count];var vertices=new Dictionary<VertexKey,uint>();
                for(int i=0;i<mesh.Triangles.Count;i+=3){
                    var p0=hostToGraphics.Apply(mesh.Triangles[i]);var p1=hostToGraphics.Apply(mesh.Triangles[i+1]);var p2=hostToGraphics.Apply(mesh.Triangles[i+2]);
                    var a=p1-p0;var b=p2-p0;var face=new Point3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);face=face*(1/Math.Max(face.Length,1e-20));
                    for(int j=0;j<3;j++){
                        var point=j==0?p0:j==1?p1:p2;var normal=smooth!=null?Normal(smooth[i+j]):mesh.Normals.Count==0?face:Normal(mesh.Normals[i+j]);
                        var local=point-first;local=new Point3(Math.Round(local.X,6),Math.Round(local.Y,6),Math.Round(local.Z,6));var key=new VertexKey(local,normal);
                        if(!vertices.TryGetValue(key,out uint index)){
                            index=checked((uint)points.Count);vertices.Add(key,index);points.Add(key.Position);normals.Add(key.Normal);
                            if(bin.Length+(long)points.Count*24+(long)indices.Length*4>SceneLimits.MaxSceneBytes-1024*1024)throw new InvalidDataException("Scene exceeds the PDF geometry budget; select a lower export detail or a narrower crop");
                        }
                        indices[i+j]=index;
                    }
                }
                string geometryKey;
                using(var temp=new MemoryStream())using(var hashWriter=new BinaryWriter(temp)){
                    hashWriter.Write(materialKey);foreach(var p in points){hashWriter.Write((float)p.X);hashWriter.Write((float)p.Y);hashWriter.Write((float)p.Z);}foreach(var n in normals){hashWriter.Write((float)n.X);hashWriter.Write((float)n.Y);hashWriter.Write((float)n.Z);}foreach(var index in indices)hashWriter.Write(index);geometryKey=Contract.Hash(temp.ToArray());
                }
                if(sharedGeometry.TryGetValue(geometryKey,out int shared)){nodeMeshes.Add(shared);continue;}
                int meshIndex=glMeshes.Count;sharedGeometry.Add(geometryKey,meshIndex);nodeMeshes.Add(meshIndex);
                int posOffset=checked((int)bin.Position);foreach(var p in points){writer.Write((float)p.X);writer.Write((float)p.Y);writer.Write((float)p.Z);}
                int posView=bufferViews.Count;bufferViews.Add(new{buffer=0,byteOffset=posOffset,byteLength=points.Count*12,target=34962});
                int posAccess=accessors.Count;accessors.Add(new{bufferView=posView,componentType=5126,count=points.Count,type="VEC3",min=new[]{points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z)},max=new[]{points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z)}});
                int normOffset=checked((int)bin.Position);foreach(var n in normals){writer.Write((float)n.X);writer.Write((float)n.Y);writer.Write((float)n.Z);}
                int normView=bufferViews.Count;bufferViews.Add(new{buffer=0,byteOffset=normOffset,byteLength=normals.Count*12,target=34962});
                int normAccess=accessors.Count;accessors.Add(new{bufferView=normView,componentType=5126,count=normals.Count,type="VEC3"});
                int indexOffset=checked((int)bin.Position);foreach(var index in indices)writer.Write(index);
                int indexView=bufferViews.Count;bufferViews.Add(new{buffer=0,byteOffset=indexOffset,byteLength=indices.Length*4,target=34963});
                int indexAccess=accessors.Count;accessors.Add(new{bufferView=indexView,componentType=5125,count=indices.Length,type="SCALAR"});
                var featureEdges=FeatureEdges.Build(points,indices);var extras=new Dictionary<string,object>();
                if(featureEdges.Length>0){int edgeOffset=checked((int)bin.Position);foreach(var index in featureEdges)writer.Write(index);int edgeView=bufferViews.Count;bufferViews.Add(new{buffer=0,byteOffset=edgeOffset,byteLength=featureEdges.Length*4,target=34963});int edgeAccessor=accessors.Count;accessors.Add(new{bufferView=edgeView,componentType=5125,count=featureEdges.Length,type="SCALAR"});extras.Add("featureEdges",edgeAccessor);}
                glMeshes.Add(new{name=mesh.Name,extras,primitives=new[]{new{attributes=new{POSITION=posAccess,NORMAL=normAccess},indices=indexAccess,material=materialIndex,mode=4}}});
                if(bin.Length>SceneLimits.MaxSceneBytes-1024*1024)throw new InvalidDataException("Scene exceeds the PDF's geometry budget; use a narrower plan crop");
            }
            var json=Contract.EncodeExact(new{asset=new{version="2.0",generator="Offline3D prototype"},scene=0,scenes=new[]{new{nodes=Enumerable.Range(0,meshes.Length).ToArray()}},nodes=meshes.Select((m,i)=>new{name=m.Name,mesh=nodeMeshes[i],translation=nodeTranslations[i],extras=new{revit=m.Identity}}).ToArray(),meshes=glMeshes,materials,buffers=new[]{new{byteLength=(int)bin.Length}},bufferViews,accessors});
            if(json.Length+bin.Length+32>SceneLimits.MaxSceneBytes)throw new InvalidDataException("Scene exceeds PDF geometry budget");
            int padded=(json.Length+3)&~3;using(var glb=new MemoryStream())using(var w=new BinaryWriter(glb)){w.Write(0x46546c67u);w.Write(2u);w.Write(checked((uint)(12+8+padded+8+bin.Length)));w.Write((uint)padded);w.Write(0x4e4f534au);w.Write(json);for(int i=json.Length;i<padded;i++)w.Write((byte)32);w.Write((uint)bin.Length);w.Write(0x004e4942u);w.Write(bin.ToArray());var bytes=glb.ToArray();stats=new GlbBuildStats{Elements=meshes.Length,UniqueMeshes=glMeshes.Count,Instances=meshes.Length-glMeshes.Count,Triangles=meshes.Sum(m=>(long)m.Triangles.Count/3),Bytes=bytes.Length};return bytes;}
        }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct FloatBits
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public float Value;
        [System.Runtime.InteropServices.FieldOffset(0)] public int Bits;
    }
    private readonly struct VertexKey:IEquatable<VertexKey>
    {
        private readonly int x,y,z,nx,ny,nz;
        private static int Bits(double value){float f=(float)value;if(f==0)f=0;if(float.IsNaN(f)||float.IsInfinity(f))throw new InvalidDataException("Non-finite float32 geometry");return new FloatBits{Value=f}.Bits;}
        private static double Value(int bits)=>new FloatBits{Bits=bits}.Value;
        public VertexKey(Point3 p,Point3 n){x=Bits(p.X);y=Bits(p.Y);z=Bits(p.Z);nx=Bits(n.X);ny=Bits(n.Y);nz=Bits(n.Z);}
        public Point3 Position=>new Point3(Value(x),Value(y),Value(z));
        public Point3 Normal=>new Point3(Value(nx),Value(ny),Value(nz));
        public bool Equals(VertexKey other)=>x==other.x&&y==other.y&&z==other.z&&nx==other.nx&&ny==other.ny&&nz==other.nz;
        public override bool Equals(object? value)=>value is VertexKey other&&Equals(other);
        public override int GetHashCode(){unchecked{int hash=x;hash=hash*397^y;hash=hash*397^z;hash=hash*397^nx;hash=hash*397^ny;return hash*397^nz;}}
    }
    public static PlainMesh Box(string name,Point3 min,Point3 max) {
        var p=new[]{new Point3(min.X,min.Y,min.Z),new Point3(max.X,min.Y,min.Z),new Point3(max.X,max.Y,min.Z),new Point3(min.X,max.Y,min.Z),new Point3(min.X,min.Y,max.Z),new Point3(max.X,min.Y,max.Z),new Point3(max.X,max.Y,max.Z),new Point3(min.X,max.Y,max.Z)};
        int[] indices={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7};return new PlainMesh{Name=name,Triangles=indices.Select(i=>p[i]).ToList()};
    }
}

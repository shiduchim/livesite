using System.Security.Cryptography;
namespace Offline3D;

/// <summary>Unique local meshes are spooled immediately; placements reference the
/// same mesh. Hash hits are compared against the actual bytes before sharing.</summary>
public sealed class SpoolSceneWriter:IDisposable
{
    private sealed class Mesh {public long Offset;public int Length,Vertices,Indices,Edges;public int Material;public Point3 Min,Max;}
    private readonly FileStream spool;private readonly string spoolPath;
    private readonly List<Mesh> meshes=new List<Mesh>();private readonly List<object> nodes=new List<object>(),materials=new List<object>();
    private readonly List<ElementIdentity> elements=new List<ElementIdentity>();private readonly Dictionary<string,int> elementIds=new Dictionary<string,int>();
    private readonly Dictionary<string,List<int>> hashes=new Dictionary<string,List<int>>();private readonly Dictionary<string,int> materialIds=new Dictionary<string,int>();
    private readonly bool includeFeatureEdges;private readonly Func<byte[],string> hash;private readonly Matrix4 hostToGraphics;private readonly byte[] compareBuffer=new byte[65536];
    private readonly double[] bounds={double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity,double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};
    public long ExpandedTriangles{get;private set;}public long UniqueTriangles{get;private set;}public int InstanceCount=>nodes.Count-meshes.Count;public int NodeCount=>nodes.Count;public int UniqueMeshCount=>meshes.Count;public long UniqueBytes=>spool.Length;
    public double[] BoundsGraphics=>bounds.Select((v,i)=>i<3?Math.Min(v,bounds[i+3]-.0001):Math.Max(v,bounds[i-3]+.0001)).ToArray();
    public SpoolSceneWriter(string directory,Matrix4 hostToGraphics,Func<byte[],string>? hashFunction=null,bool includeFeatureEdges=true){this.includeFeatureEdges=includeFeatureEdges;Directory.CreateDirectory(directory);spoolPath=Path.Combine(directory,"geometry-"+Guid.NewGuid().ToString("N")+".spool");spool=new FileStream(spoolPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);this.hostToGraphics=hostToGraphics;hash=hashFunction??Contract.Hash;}
    public void Add(string name,IndexedGeometry geometry,Matrix4 localToHost,PlainMaterial material,ElementIdentity identity){
        if(nodes.Count>=100000)throw new InvalidDataException("The export exceeds 100,000 geometry placements. Select sheet/view regions; no parts were omitted.");
        if(geometry.Positions.Length==0||geometry.Positions.Length!=geometry.Normals.Length||geometry.Positions.Length%3!=0||geometry.Indices.Length%3!=0)throw new InvalidDataException("Invalid indexed geometry");
        var g=Matrix4.Graphics(new Point3());var origin=geometry.OriginFeet;var t=new Matrix4(1,0,0,origin.X,0,1,0,origin.Y,0,0,1,origin.Z,0,0,0,1);var matrix=hostToGraphics*localToHost*t*g.Inverse();var m=matrix.Values;
        double det=m[0]*(m[5]*m[10]-m[6]*m[9])-m[1]*(m[4]*m[10]-m[6]*m[8])+m[2]*(m[4]*m[9]-m[5]*m[8]);
        if(det<0){geometry=new IndexedGeometry{Positions=(float[])geometry.Positions.Clone(),Normals=(float[])geometry.Normals.Clone(),Indices=(uint[])geometry.Indices.Clone(),OriginFeet=geometry.OriginFeet};for(int i=0;i<geometry.Positions.Length;i+=3){geometry.Positions[i]=-geometry.Positions[i];geometry.Normals[i]=-geometry.Normals[i];}for(int i=0;i<geometry.Indices.Length;i+=3){uint a=geometry.Indices[i+1];geometry.Indices[i+1]=geometry.Indices[i+2];geometry.Indices[i+2]=a;}matrix=matrix*new Matrix4(-1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);}
        material.Validate();string materialKey=Contract.Hash(Contract.Encode(material));if(!materialIds.TryGetValue(materialKey,out int materialIndex)){materialIndex=materials.Count;materialIds.Add(materialKey,materialIndex);materials.Add(new{name=material.Name,extras=new{cutPattern=material.CutPattern},doubleSided=true,alphaMode=material.Opacity<.999?"BLEND":"OPAQUE",pbrMetallicRoughness=new{baseColorFactor=new[]{PlainMaterial.Linear(material.Red),PlainMaterial.Linear(material.Green),PlainMaterial.Linear(material.Blue),material.Opacity},metallicFactor=material.Metallic,roughnessFactor=material.Roughness}});}
        byte[] core;
        using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){writer.Write(geometry.Positions.Length);foreach(float v in geometry.Positions){if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidDataException("Non-finite position");writer.Write(v);}foreach(float v in geometry.Normals){if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidDataException("Non-finite normal");writer.Write(v);}writer.Write(geometry.Indices.Length);foreach(uint v in geometry.Indices){if(v>=geometry.Positions.Length/3)throw new InvalidDataException("Invalid geometry index");writer.Write(v);}core=memory.ToArray();}
        string key=materialKey+":"+hash(core);int meshIndex=-1;
        if(hashes.TryGetValue(key,out var candidates))foreach(int candidate in candidates)if(Equal(meshes[candidate],core)){meshIndex=candidate;break;}
        if(meshIndex<0){
            if(spool.Length+(long)geometry.Positions.Length*8+(long)geometry.Indices.Length*4+8>SceneLimits.MaxSceneBytes-1048576)throw new InvalidDataException("Unique geometry exceeds the 512 MiB scene budget. Repeated items already share geometry; select sheet/view regions or lower detail.");
            var points=new Point3[geometry.Positions.Length/3];for(int i=0;i<points.Length;i++)points[i]=new Point3(geometry.Positions[i*3],geometry.Positions[i*3+1],geometry.Positions[i*3+2]);var edges=includeFeatureEdges?FeatureEdges.Build(points,geometry.Indices):Array.Empty<uint>();
            long bytes=(long)geometry.Positions.Length*8+(long)geometry.Indices.Length*4+(long)edges.Length*4;
            if(spool.Length+bytes>SceneLimits.MaxSceneBytes-1048576)throw new InvalidDataException("Unique geometry exceeds the 512 MiB scene budget. Repeated items already share geometry; select sheet/view regions or lower detail for this model.");
            var mesh=new Mesh{Offset=spool.Length,Length=core.Length,Vertices=points.Length,Indices=geometry.Indices.Length,Edges=edges.Length,Material=materialIndex,Min=new Point3(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z)),Max=new Point3(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z))};
            spool.Position=spool.Length;using(var writer=new BinaryWriter(spool,System.Text.Encoding.UTF8,true)){// Header counts are kept for collision-safe byte comparison; GLB views skip them.
                writer.Write(core);foreach(uint edge in edges)writer.Write(edge);}
            meshIndex=meshes.Count;meshes.Add(mesh);if(candidates==null){candidates=new List<int>();hashes.Add(key,candidates);}candidates.Add(meshIndex);UniqueTriangles+=geometry.Indices.Length/3;
        }
        var stored=meshes[meshIndex];for(int corner=0;corner<8;corner++){var p=matrix.Apply(new Point3((corner&1)==0?stored.Min.X:stored.Max.X,(corner&2)==0?stored.Min.Y:stored.Max.Y,(corner&4)==0?stored.Min.Z:stored.Max.Z));var a=new[]{p.X,p.Y,p.Z};for(int k=0;k<3;k++){bounds[k]=Math.Min(bounds[k],a[k]);bounds[k+3]=Math.Max(bounds[k+3],a[k]);}}
        var values=matrix.Values;var columnMajor=Enumerable.Range(0,16).Select(i=>values[(i%4)*4+i/4]).ToArray();string elementKey=identity.DocumentId+"::"+identity.LinkInstanceId+"::"+identity.UniqueId;if(!elementIds.TryGetValue(elementKey,out int elementIndex)){elementIndex=elements.Count;elementIds.Add(elementKey,elementIndex);elements.Add(identity);}nodes.Add(new{name,mesh=meshIndex,matrix=columnMajor,extras=new{element=elementIndex}});ExpandedTriangles+=geometry.Indices.Length/3;
    }
    private bool Equal(Mesh mesh,byte[] data){if(mesh.Length!=data.Length)return false;spool.Position=mesh.Offset;for(int offset=0;offset<data.Length;){int count=Math.Min(compareBuffer.Length,data.Length-offset),read=0;while(read<count){int n=spool.Read(compareBuffer,read,count-read);if(n==0)return false;read+=n;}for(int i=0;i<count;i++)if(compareBuffer[i]!=data[offset+i])return false;offset+=count;}return true;}
    public GlbBuildStats Write(string path){
        if(nodes.Count==0)throw new InvalidDataException("No render geometry");var views=new List<object>();var accessors=new List<object>();var glMeshes=new List<object>();
        foreach(var mesh in meshes){
            int Vector(long offset,Point3? min=null,Point3? max=null){int view=views.Count;views.Add(new{buffer=0,byteOffset=offset,byteLength=mesh.Vertices*12,target=34962});int index=accessors.Count;if(min.HasValue)accessors.Add(new{bufferView=view,componentType=5126,count=mesh.Vertices,type="VEC3",min=new[]{min.Value.X,min.Value.Y,min.Value.Z},max=new[]{max!.Value.X,max.Value.Y,max.Value.Z}});else accessors.Add(new{bufferView=view,componentType=5126,count=mesh.Vertices,type="VEC3"});return index;}
            int Index(long offset,int count){int view=views.Count;views.Add(new{buffer=0,byteOffset=offset,byteLength=count*4,target=34963});int index=accessors.Count;accessors.Add(new{bufferView=view,componentType=5125,count,type="SCALAR"});return index;}
            int p=Vector(mesh.Offset+4,mesh.Min,mesh.Max),n=Vector(mesh.Offset+4+mesh.Vertices*12),ix=Index(mesh.Offset+8+mesh.Vertices*24,mesh.Indices);var extra=new Dictionary<string,object>();if(mesh.Edges>0)extra.Add("featureEdges",Index(mesh.Offset+mesh.Length,mesh.Edges));glMeshes.Add(new{extras=extra,primitives=new[]{new{attributes=new{POSITION=p,NORMAL=n},indices=ix,material=mesh.Material,mode=4}}});
        }
        var json=Contract.EncodeExact(new{asset=new{version="2.0",generator="Offline3D indexed local mesh spool"},scene=0,scenes=new[]{new{nodes=Enumerable.Range(0,nodes.Count).ToArray()}},nodes,extras=new{revitElements=elements},meshes=glMeshes,materials,buffers=new[]{new{byteLength=spool.Length}},bufferViews=views,accessors});int padded=(json.Length+3)&~3;long length=28L+padded+spool.Length;if(length>SceneLimits.MaxSceneBytes)throw new InvalidDataException("Scene geometry and placement metadata exceed 512 MiB. Select sheet/view regions; no elements were silently removed.");
        string temp=path+".tmp";try{using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write))using(var writer=new BinaryWriter(output)){writer.Write(0x46546c67u);writer.Write(2u);writer.Write((uint)length);writer.Write(padded);writer.Write(0x4e4f534au);writer.Write(json);for(int i=json.Length;i<padded;i++)writer.Write((byte)32);writer.Write((uint)spool.Length);writer.Write(0x004e4942u);writer.Flush();spool.Position=0;spool.CopyTo(output);}if(File.Exists(path))File.Delete(path);File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        return new GlbBuildStats{Elements=nodes.Count,UniqueMeshes=meshes.Count,Instances=InstanceCount,Triangles=ExpandedTriangles,UniqueTriangles=UniqueTriangles,Bytes=length};
    }
    public void Dispose(){spool.Dispose();if(File.Exists(spoolPath))File.Delete(spoolPath);}
}

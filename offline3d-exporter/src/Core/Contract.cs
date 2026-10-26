using System.Security.Cryptography;
using System.Text.Json;
namespace Offline3D;
public static class Contract
{
    public const string ManifestName="offline3d.manifest.json";
    public const int MaxResource=SceneLimits.MaxMetadataBytes,MaxScene=SceneLimits.MaxSceneBytes,MaxTotal=SceneLimits.MaxTotalBytes,MaxPdf=SceneLimits.MaxPdfBytes;
    public static readonly JsonSerializerOptions Json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=false,WriteIndented=true,MaxDepth=32};
    public static byte[] Encode<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Json);
    public static byte[] EncodeExact<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value);
    public static T Decode<T>(byte[] data) {if(data.Length>MaxResource)throw new InvalidDataException("JSON too large");return JsonSerializer.Deserialize<T>(data,Json)??throw new InvalidDataException("Null JSON");}
    public static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    public static void Validate(Manifest m) {
        if(m==null || m.SchemaVersion!=1 || !Guid.TryParse(m.PackageId,out _) || m.CoordinateFrame!="revit-host-internal-feet" || string.IsNullOrWhiteSpace(m.Coverage))throw new InvalidDataException("Unsupported/invalid manifest");
        if(m.RequiredCapabilities==null || m.RequiredCapabilities.Any(c=>c!="plan-ray-v1"&&c!="glb-static-v1"&&c!="glb-affine-instances-v1"&&c!=SceneLimits.LargeSceneCapability))throw new InvalidDataException("Unknown required capability");
        if(m.Resources==null || m.Resources.Length!=3)throw new InvalidDataException("Expected registration, identities and one scene");
        if(m.Resources.Select(r=>r.Id).Distinct().Count()!=3 || m.Resources.Select(r=>r.Name).Distinct().Count()!=3)throw new InvalidDataException("Duplicate resource");
        foreach(var r in m.Resources)if(string.IsNullOrEmpty(r.Id)||r.Id.Length>100 || !System.Text.RegularExpressions.Regex.IsMatch(r.Name??"",@"^[a-zA-Z0-9_-]+\.(json|glb)$") || r.Name==ManifestName || r.Size<1 || r.Size>SceneLimits.ResourceBytes(r.Kind) || !System.Text.RegularExpressions.Regex.IsMatch(r.Sha256??"","^[a-f0-9]{64}$"))throw new InvalidDataException("Invalid resource");
        if(m.Resources.Any(r=>r.Kind=="scene"&&r.Size>MaxResource)&&!m.RequiredCapabilities.Contains(SceneLimits.LargeSceneCapability))throw new InvalidDataException("Large scenes must declare viewer support");
        if(m.Resources.Sum(r=>r.Size)>MaxTotal || !new[]{"registration","identities","scene"}.OrderBy(x=>x).SequenceEqual(m.Resources.Select(r=>r.Kind).OrderBy(x=>x)))throw new InvalidDataException("Invalid resource kinds/total");
    }
    public static void Validate(Registration r) {
        bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        if(r.Page!=1 || string.IsNullOrEmpty(r.ViewId)||string.IsNullOrEmpty(r.LevelId)||string.IsNullOrEmpty(r.DocumentId)||!Finite(r.LevelProjectElevationFeet))throw new InvalidDataException("Invalid view/level");
        foreach(var matrix in new[]{r.ModelToPdf,r.ModelToSheet,r.ModelToGraphics}){matrix.Validate();matrix.Inverse();if(!matrix.Values.Skip(12).SequenceEqual(new[]{0d,0,0,1}))throw new InvalidDataException("Only affine frames supported");}
        foreach(var box in new[]{r.MediaBox,r.PageCropBox})if(box==null||box.Length!=4||box.Any(x=>!Finite(x))||box[2]<=box[0]||box[3]<=box[1])throw new InvalidDataException("Invalid page box");
        if(r.Rotation%90!=0 || r.Rotation<0 ||r.Rotation>270 || !Finite(r.UserUnit)||r.UserUnit<=0||r.UserUnit>75000)throw new InvalidDataException("Invalid page transform");
        if(r.CropPdf==null||r.CropPdf.Length!=8||r.CropPdf.Any(x=>!Finite(x)))throw new InvalidDataException("Only rectangular unsplit crops supported");
        // Convex quadrilateral; crop is in PDF coordinates and may be rotated.
        double sign=0;for(int i=0;i<4;i++){int j=(i+1)%4,k=(i+2)%4;double cross=(r.CropPdf[2*j]-r.CropPdf[2*i])*(r.CropPdf[2*k+1]-r.CropPdf[2*j+1])-(r.CropPdf[2*j+1]-r.CropPdf[2*i+1])*(r.CropPdf[2*k]-r.CropPdf[2*j]);if(Math.Abs(cross)<1e-12 || (i>0&&cross*sign<=0))throw new InvalidDataException("Degenerate crop");sign=cross;}
        if(!r.PreciseClicks || !Finite(r.MaxResidualMetres)||r.MaxResidualMetres<0||r.MaxResidualMetres>.001||r.FitPointCount<3||r.CheckPointCount<3)throw new InvalidDataException("Precise interaction requires verified registration");
        if(r.ViewRegions==null||r.ViewRegions.Length>100)throw new InvalidDataException("Too many sheet regions");
        void Plane(double[] o,double[] n){if(o.Length==0&&n.Length==0)return;if(o.Length!=3||n.Length!=3||o.Concat(n).Any(v=>!Finite(v))||n.Sum(v=>v*v)<.5)throw new InvalidDataException("Invalid click plane");}
        void Bounds(double[] bounds){if(bounds.Length==0)return;if(bounds.Length!=6||bounds.Any(v=>!Finite(v)||Math.Abs(v)>1e9)||Enumerable.Range(0,3).Any(i=>bounds[i+3]<=bounds[i]))throw new InvalidDataException("Invalid scene bounds");}
        Plane(r.ClickPlaneOrigin,r.ClickPlaneNormal);Bounds(r.ExportBoundsGraphics);
        foreach(var region in r.ViewRegions){region.ModelToPdf.Validate();region.ModelToPdf.Inverse();Plane(region.PlaneOrigin,region.PlaneNormal);Bounds(region.BoundsGraphics);if(string.IsNullOrWhiteSpace(region.ViewId)||region.CropPdf.Length!=8||region.CropPdf.Any(v=>!Finite(v)))throw new InvalidDataException("Invalid view region");}
        Coordinates.PlanClick(r,r.CropPdf[0],r.CropPdf[1]);
        // Graphics mapping is the declared invertible feet -> metres, Z-up -> Y-up axis conversion.
        var a=r.ModelToGraphics.Values;var expected=Matrix4.Graphics(new Point3()).Values;
        for(int i=0;i<16;i++)if(i!=3&&i!=7&&i!=11&&Math.Abs(a[i]-expected[i])>1e-10)throw new InvalidDataException("Unexpected graphics axes/scale");
    }
    public static void ValidateGlb(byte[] bytes) {
        if(bytes.Length<28||bytes.Length>MaxScene||BitConverter.ToUInt32(bytes,0)!=0x46546c67||BitConverter.ToUInt32(bytes,4)!=2||BitConverter.ToUInt32(bytes,8)!=bytes.Length)throw new InvalidDataException("Invalid GLB header");
        int n=checked((int)BitConverter.ToUInt32(bytes,12));if(n<2||n>MaxResource||n>bytes.Length-28||n%4!=0||BitConverter.ToUInt32(bytes,16)!=0x4e4f534a)throw new InvalidDataException("Invalid GLB JSON chunk");
        using(var doc=JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes,20,n))){
            void Scan(JsonElement e,bool metadata=false){if(e.ValueKind==JsonValueKind.Number && (!e.TryGetDouble(out var number)||double.IsNaN(number)||double.IsInfinity(number)))throw new InvalidDataException("Non-finite GLB value");if(e.ValueKind==JsonValueKind.Object)foreach(var p in e.EnumerateObject()){if(!metadata&&(p.Name=="uri"||p.Name=="extensions"||p.Name=="extensionsRequired"||p.Name=="extensionsUsed"||p.Name=="images"||p.Name=="animations"||p.Name=="skins"))throw new InvalidDataException("GLB must be static and self-contained, without extensions/images");Scan(p.Value,metadata||p.Name=="extras");}else if(e.ValueKind==JsonValueKind.Array)foreach(var v in e.EnumerateArray())Scan(v,metadata);}
            Scan(doc.RootElement);
            if(doc.RootElement.GetProperty("asset").GetProperty("version").GetString()!="2.0")throw new InvalidDataException("Unsupported glTF");
            var buffers=doc.RootElement.GetProperty("buffers");if(buffers.GetArrayLength()!=1)throw new InvalidDataException("Expected one local binary buffer");
            int offset=20+n; if(offset+8>bytes.Length||BitConverter.ToUInt32(bytes,offset+4)!=0x004e4942||BitConverter.ToUInt32(bytes,offset)!=bytes.Length-offset-8 || buffers[0].GetProperty("byteLength").GetInt32()>bytes.Length-offset-8)throw new InvalidDataException("Invalid GLB BIN chunk");
            int binaryLength=buffers[0].GetProperty("byteLength").GetInt32();
            if(binaryLength<0)throw new InvalidDataException("Invalid binary length");
            var views=doc.RootElement.GetProperty("bufferViews");var accessors=doc.RootElement.GetProperty("accessors");
            if(views.GetArrayLength()>300000||accessors.GetArrayLength()>300000)throw new InvalidDataException("Too many geometry accessors");
            foreach(var view in views.EnumerateArray()){
                long start=view.TryGetProperty("byteOffset",out var off)?off.GetInt64():0,length=view.GetProperty("byteLength").GetInt64();
                if(view.GetProperty("buffer").GetInt32()!=0||start<0||start>binaryLength||length<0||length>binaryLength-start||view.TryGetProperty("byteStride",out _))throw new InvalidDataException("Unsupported/out-of-range buffer view");
            }
            foreach(var accessor in accessors.EnumerateArray()){
                int viewIndex=accessor.GetProperty("bufferView").GetInt32(),count=accessor.GetProperty("count").GetInt32(),component=accessor.GetProperty("componentType").GetInt32();
                string type=accessor.GetProperty("type").GetString()??"";
                bool vector=component==5126&&type=="VEC3",index=component==5125&&type=="SCALAR";
                if(viewIndex<0||viewIndex>=views.GetArrayLength()||count<1||(!vector&&!index)||count>(vector?3000000:16000000)||accessor.TryGetProperty("sparse",out _)||(accessor.TryGetProperty("normalized",out var normalized)&&normalized.GetBoolean()))throw new InvalidDataException("Only bounded float VEC3 or uint32 SCALAR accessors supported");
                long relative=accessor.TryGetProperty("byteOffset",out var off)?off.GetInt64():0;
                var view=views[viewIndex];long viewLength=view.GetProperty("byteLength").GetInt64(),width=vector?12:4;
                if(relative<0||relative%4!=0||relative>viewLength||(long)count*width>viewLength-relative)throw new InvalidDataException("Accessor exceeds buffer");
                int start=checked((int)((view.TryGetProperty("byteOffset",out var vo)?vo.GetInt64():0)+relative));
                if(start%4!=0)throw new InvalidDataException("Misaligned geometry accessor");
                if(vector)for(int k=0;k<count*3;k++){float value=BitConverter.ToSingle(bytes,offset+8+start+k*4);if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Non-finite GLB vertex");}
            }
            int AccessorStart(int index){var a=accessors[index];var v=views[a.GetProperty("bufferView").GetInt32()];return checked((int)((v.TryGetProperty("byteOffset",out var vo)?vo.GetInt64():0)+(a.TryGetProperty("byteOffset",out var ao)?ao.GetInt64():0)));}
            var meshes=doc.RootElement.GetProperty("meshes");
            var hasMaterials=doc.RootElement.TryGetProperty("materials",out var materials);
            if(hasMaterials){
                if(materials.GetArrayLength()>100000)throw new InvalidDataException("Too many materials");
                foreach(var material in materials.EnumerateArray()){
                    if(material.TryGetProperty("alphaMode",out var alpha)&&alpha.GetString()!="OPAQUE"&&alpha.GetString()!="BLEND"&&alpha.GetString()!="MASK")throw new InvalidDataException("Invalid material alpha mode");
                    if(material.TryGetProperty("pbrMetallicRoughness",out var pbr)){
                        if(pbr.TryGetProperty("baseColorFactor",out var color)&&(color.GetArrayLength()!=4||color.EnumerateArray().Any(v=>v.GetDouble()<0||v.GetDouble()>1)))throw new InvalidDataException("Invalid material color");
                        foreach(var key in new[]{"roughnessFactor","metallicFactor"})if(pbr.TryGetProperty(key,out var value)&&(value.GetDouble()<0||value.GetDouble()>1))throw new InvalidDataException("Invalid material surface value");
                    }
                }
            }
            if(meshes.GetArrayLength()<1||meshes.GetArrayLength()>100000)throw new InvalidDataException("Invalid mesh count");
            var meshVertexCounts=new List<long>();long totalMeshVertices=0;
            foreach(var mesh in meshes.EnumerateArray()){
                long meshVertices=0;
                foreach(var primitive in mesh.GetProperty("primitives").EnumerateArray()){
                    if(primitive.GetProperty("mode").GetInt32()!=4)throw new InvalidDataException("Only triangles supported");
                    if(primitive.TryGetProperty("material",out var materialIndex)&&(!hasMaterials||materialIndex.GetInt32()<0||materialIndex.GetInt32()>=materials.GetArrayLength()))throw new InvalidDataException("Invalid material index");
                    var attributes=primitive.GetProperty("attributes");var names=new HashSet<string>();
                    int positionIndex=attributes.GetProperty("POSITION").GetInt32();
                    if(positionIndex<0||positionIndex>=accessors.GetArrayLength())throw new InvalidDataException("Invalid position accessor");
                    int vertexCount=accessors[positionIndex].GetProperty("count").GetInt32();
                    foreach(var attribute in attributes.EnumerateObject()){
                        int ai=attribute.Value.GetInt32();
                        if(!names.Add(attribute.Name)||(attribute.Name!="POSITION"&&attribute.Name!="NORMAL")||ai<0||ai>=accessors.GetArrayLength())throw new InvalidDataException("Unsupported mesh attribute");
                        var accessor=accessors[ai];
                        if(accessor.GetProperty("componentType").GetInt32()!=5126||accessor.GetProperty("type").GetString()!="VEC3"||accessor.GetProperty("count").GetInt32()!=vertexCount)throw new InvalidDataException("Mismatched geometry attributes");
                    }
                    if(primitive.TryGetProperty("indices",out var indexProperty)){
                        int ai=indexProperty.GetInt32();if(ai<0||ai>=accessors.GetArrayLength())throw new InvalidDataException("Invalid index accessor");
                        var accessor=accessors[ai];int count=accessor.GetProperty("count").GetInt32();
                        if(accessor.GetProperty("componentType").GetInt32()!=5125||accessor.GetProperty("type").GetString()!="SCALAR"||count%3!=0)throw new InvalidDataException("Only complete uint32 indexed triangles supported");
                        int start=AccessorStart(ai);for(int k=0;k<count;k++)if(BitConverter.ToUInt32(bytes,offset+8+start+k*4)>=vertexCount)throw new InvalidDataException("Triangle index exceeds vertex count");
                        meshVertices+=count;
                    }else{
                        if(vertexCount%3!=0)throw new InvalidDataException("Incomplete triangles");meshVertices+=vertexCount;
                    }
                    
                }
                if(mesh.TryGetProperty("extras",out var extra)&&extra.TryGetProperty("featureEdges",out var edge)){
                    int ei=edge.GetInt32();if(ei<0||ei>=accessors.GetArrayLength())throw new InvalidDataException("Invalid feature edges");var ea=accessors[ei];int count=ea.GetProperty("count").GetInt32();if(ea.GetProperty("componentType").GetInt32()!=5125||ea.GetProperty("type").GetString()!="SCALAR"||count%2!=0)throw new InvalidDataException("Invalid edge pairs");int vertices=accessors[mesh.GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32(),start=AccessorStart(ei);for(int k=0;k<count;k++)if(BitConverter.ToUInt32(bytes,offset+8+start+k*4)>=vertices)throw new InvalidDataException("Feature edge outside vertices");
                }
                totalMeshVertices+=meshVertices;meshVertexCounts.Add(meshVertices);
            }
            // Flat mesh nodes only: excludes cyclic or arbitrarily deep scene graphs.
            var nodes=doc.RootElement.GetProperty("nodes");if(nodes.GetArrayLength()>100000)throw new InvalidDataException("Too many nodes");
            long instancedVertices=0;int elementCount=0;if(doc.RootElement.TryGetProperty("extras",out var rootExtras)&&rootExtras.TryGetProperty("revitElements",out var elementTable)){if(elementTable.ValueKind!=JsonValueKind.Array||elementTable.GetArrayLength()>100000)throw new InvalidDataException("Invalid element metadata table");elementCount=elementTable.GetArrayLength();}
            foreach(var node in nodes.EnumerateArray()){
                if(node.TryGetProperty("extras",out var nodeExtras)&&nodeExtras.TryGetProperty("element",out var elementRef)&&(elementRef.GetInt32()<0||elementRef.GetInt32()>=elementCount))throw new InvalidDataException("Invalid element metadata reference");
                if(node.TryGetProperty("children",out _)||node.TryGetProperty("rotation",out _)||node.TryGetProperty("scale",out _))throw new InvalidDataException("Only flat, validated mesh placements are supported");
                if(node.TryGetProperty("matrix",out var transform)){
                    if(transform.GetArrayLength()!=16||node.TryGetProperty("translation",out _))throw new InvalidDataException("Invalid placement matrix");
                    var column=transform.EnumerateArray().Select(v=>v.GetDouble()).ToArray();var frame=new Matrix4(Enumerable.Range(0,16).Select(i=>column[(i%4)*4+i/4]).ToArray());frame.Inverse();var f=frame.Values;
                    if(!f.Skip(12).SequenceEqual(new[]{0d,0,0,1})||new[]{f[3],f[7],f[11]}.Any(v=>Math.Abs(v)>1e7))throw new InvalidDataException("Invalid affine placement");
                    var lengths=Enumerable.Range(0,3).Select(i=>Math.Sqrt(f[i]*f[i]+f[4+i]*f[4+i]+f[8+i]*f[8+i])).ToArray();if(lengths.Any(v=>v<1e-6||v>1e6))throw new InvalidDataException("Invalid placement scale");
                    for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)if(Math.Abs(f[i]*f[j]+f[4+i]*f[4+j]+f[8+i]*f[8+j])>1e-5*lengths[i]*lengths[j])throw new InvalidDataException("Sheared placements are unsupported");
                    double determinant=f[0]*(f[5]*f[10]-f[6]*f[9])-f[1]*(f[4]*f[10]-f[6]*f[8])+f[2]*(f[4]*f[9]-f[5]*f[8]);if(determinant<=0)throw new InvalidDataException("Mirrors must be stored in geometry with corrected winding");
                }
                else if(node.TryGetProperty("translation",out var translation)&&(translation.GetArrayLength()!=3||translation.EnumerateArray().Any(v=>Math.Abs(v.GetDouble())>1e7)))throw new InvalidDataException("Invalid node translation");
                int index=node.GetProperty("mesh").GetInt32();if(index<0||index>=meshes.GetArrayLength())throw new InvalidDataException("Invalid mesh index");
                instancedVertices+=meshVertexCounts[index]; // Informational: repeated placements do not consume unique binary storage.
            }
            var scenes=doc.RootElement.GetProperty("scenes");if(scenes.GetArrayLength()!=1||doc.RootElement.GetProperty("scene").GetInt32()!=0)throw new InvalidDataException("One scene required");
            var sceneNodes=new HashSet<int>();foreach(var index in scenes[0].GetProperty("nodes").EnumerateArray())if(index.GetInt32()<0||index.GetInt32()>=nodes.GetArrayLength()||!sceneNodes.Add(index.GetInt32()))throw new InvalidDataException("Invalid or duplicate scene node");

        }
    }
}

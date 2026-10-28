using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using System.IO.Compression;
namespace Offline3D;

public sealed class OpenPackage
{
    public required Manifest Manifest {get;init;}
    public required Registration Registration {get;init;}
    public required ElementIdentity[] Elements {get;init;}
    public required byte[] Glb {get;init;}
}
public interface IPdfPackageService
{
    OpenPackage Read(string path);
    void Write(string sourcePdf,string destination,Manifest manifest,IReadOnlyDictionary<string,byte[]> resources);
}
public sealed class PdfPackageService : IPdfPackageService
{
    private static PdfDictionary? Dict(PdfItem? item)=>item is PdfReference r?r.Value as PdfDictionary:item as PdfDictionary;
    private static void Flatten(PdfDictionary node,SortedDictionary<string,PdfItem> result,int depth=0) {
        if(depth>16)throw new InvalidDataException("Attachment name tree too deep");
        if(node.Elements.GetArray("/Names") is PdfArray names){if(names.Elements.Count%2!=0)throw new InvalidDataException("Invalid name tree");for(int i=0;i<names.Elements.Count;i+=2){string name=names.Elements.GetString(i);if(!result.TryAdd(name,names.Elements[i+1]))throw new InvalidDataException("Duplicate attachment name");if(result.Count>128)throw new InvalidDataException("Too many attachments");}}
        if(node.Elements.GetArray("/Kids") is PdfArray kids)foreach(var kid in kids.Elements)Flatten(Dict(kid)??throw new InvalidDataException("Invalid tree child"),result,depth+1);
    }
    private static SortedDictionary<string,PdfItem> Attachments(PdfDocument pdf) {
        var result=new SortedDictionary<string,PdfItem>(StringComparer.Ordinal);
        var root=pdf.Internals.Catalog.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/EmbeddedFiles");if(root!=null)Flatten(root,result);return result;
    }
    private static byte[] Bytes(PdfItem item,int limit,long? expected=null) {
        var fs=Dict(item)??throw new InvalidDataException("Invalid file spec");
        var ef=fs.Elements.GetDictionary("/EF")??throw new InvalidDataException("Missing embedded file");
        var stream=ef.Elements.GetDictionary("/UF")??ef.Elements.GetDictionary("/F")??throw new InvalidDataException("Missing file stream");
        if(stream.Stream==null)throw new InvalidDataException("Missing stream");
        byte[] raw=stream.Stream.Value;
        if(raw.Length>limit)throw new InvalidDataException("Attachment too large");
        string filter=stream.Elements.GetName("/Filter");if(string.IsNullOrEmpty(filter)){if(expected.HasValue&&raw.LongLength!=expected.Value)throw new InvalidDataException("Attachment size does not match descriptor");return raw;}
        if(filter!="/FlateDecode")throw new InvalidDataException("Unsupported attachment filter");
        using var compressed=new MemoryStream(raw);using var zip=new ZLibStream(compressed,CompressionMode.Decompress);
        // Validated descriptors allow one exact-sized output allocation, rather
        // than growing a second full scene buffer during decompression.
        if(expected.HasValue){if(expected<1||expected>limit)throw new InvalidDataException("Invalid attachment size");var result=new byte[(int)expected.Value];int offset=0;while(offset<result.Length){int read=zip.Read(result,offset,result.Length-offset);if(read==0)throw new InvalidDataException("Truncated attachment");offset+=read;}if(zip.ReadByte()!=-1)throw new InvalidDataException("Attachment exceeds declared size");return result;}
        using var output=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=zip.Read(buffer,0,buffer.Length))>0){if(output.Length+n>limit)throw new InvalidDataException("Decompressed attachment too large");output.Write(buffer,0,n);}return output.ToArray();
    }
    public OpenPackage Read(string path) {
        if(new FileInfo(path).Length>Contract.MaxPdf)throw new InvalidDataException("PDF too large");
        using var pdf=PdfReader.Open(path,PdfDocumentOpenMode.Import);
        if(pdf.PageCount!=1)throw new InvalidDataException("Prototype requires one page");
        var files=Attachments(pdf);
        if(!files.TryGetValue(Contract.ManifestName,out var manifestItem))throw new InvalidDataException("No Offline3D package in PDF");
        var manifestBytes=Bytes(manifestItem,65536);
        var m=Contract.Decode<Manifest>(manifestBytes);Contract.Validate(m);
        byte[] Resource(string kind){var r=m.Resources.Single(r=>r.Kind==kind);if(!files.TryGetValue(r.Name,out var item))throw new InvalidDataException("Missing resource");var bytes=Bytes(item,SceneLimits.ResourceBytes(kind),r.Size);if(bytes.Length!=r.Size||Contract.Hash(bytes)!=r.Sha256)throw new InvalidDataException("Resource integrity failure: "+r.Name);return bytes;}
        var reg=Contract.Decode<Registration>(Resource("registration"));Contract.Validate(reg);ValidatePage(pdf.Pages[0],reg);
        var ids=Contract.Decode<ElementIdentity[]>(Resource("identities"));if(ids.Length>100000 || ids.Any(x=>string.IsNullOrWhiteSpace(x.UniqueId)||string.IsNullOrWhiteSpace(x.DocumentId)||string.IsNullOrWhiteSpace(x.MeshName)))throw new InvalidDataException("Invalid identity table");
        var glb=Resource("scene");Contract.ValidateGlb(glb);
        return new OpenPackage{Manifest=m,Registration=reg,Elements=ids,Glb=glb};
    }
    public static void ValidatePage(PdfPage page,Registration r) {
        var m=page.MediaBox;var c=page.CropBox;if(c.X2<=c.X1||c.Y2<=c.Y1)c=m;var media=new[]{m.X1,m.Y1,m.X2,m.Y2};var crop=new[]{c.X1,c.Y1,c.X2,c.Y2};double unit=page.Elements.ContainsKey("/UserUnit")?page.Elements.GetReal("/UserUnit"):1;
        if(!media.Zip(r.MediaBox).All(x=>Math.Abs(x.First-x.Second)<1e-7)||!crop.Zip(r.PageCropBox).All(x=>Math.Abs(x.First-x.Second)<1e-7)||((page.Rotate%360+360)%360)!=r.Rotation||Math.Abs(unit-r.UserUnit)>1e-10)throw new InvalidDataException("PDF page geometry no longer matches registration");
    }
    public static void SetPageGeometry(string path,Registration r) {
        using var pdf=PdfReader.Open(path,PdfDocumentOpenMode.Import);if(pdf.PageCount!=1)throw new InvalidDataException("One page required");var p=pdf.Pages[0];var b=p.MediaBox;var c=p.CropBox;if(c.X2<=c.X1||c.Y2<=c.Y1)c=b;r.MediaBox=new[]{b.X1,b.Y1,b.X2,b.Y2};r.PageCropBox=new[]{c.X1,c.Y1,c.X2,c.Y2};r.Rotation=(p.Rotate%360+360)%360;r.UserUnit=p.Elements.ContainsKey("/UserUnit")?p.Elements.GetReal("/UserUnit"):1;
    }
    public void Write(string sourcePdf,string destination,Manifest manifest,IReadOnlyDictionary<string,byte[]> resources) {
        Contract.Validate(manifest);
        foreach(var r in manifest.Resources)if(!resources.TryGetValue(r.Name,out var bytes)||bytes.Length!=r.Size||Contract.Hash(bytes)!=r.Sha256)throw new InvalidDataException("Resource does not match descriptor");
        string dest=Path.GetFullPath(destination);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);string temp=Path.Combine(Path.GetDirectoryName(dest)!,".offline3d-"+Guid.NewGuid()+".pdf");
        try {
            if(new FileInfo(sourcePdf).Length>Contract.MaxPdf)throw new InvalidDataException("PDF too large");
            using(var pdf=PdfReader.Open(sourcePdf,PdfDocumentOpenMode.Modify)){
                if(pdf.PageCount!=1)throw new InvalidDataException("One page required");
                var files=Attachments(pdf);
                if(files.ContainsKey(Contract.ManifestName))throw new InvalidDataException("Already packaged; use the original drawing PDF");
                foreach(var name in resources.Keys.Append(Contract.ManifestName))if(files.ContainsKey(name))throw new InvalidDataException("Attachment collision: "+name);
                void Add(string name,byte[] data) {
                    var stream=new PdfDictionary(pdf);stream.Elements.SetName("/Type","/EmbeddedFile");stream.CreateStream(data);pdf.Internals.AddObject(stream);
                    var ef=new PdfDictionary(pdf);ef.Elements["/F"]=stream.Reference!;ef.Elements["/UF"]=stream.Reference!;
                    var spec=new PdfDictionary(pdf);spec.Elements.SetName("/Type","/Filespec");spec.Elements.SetString("/F",name);spec.Elements.SetString("/UF",name);spec.Elements["/EF"]=ef;pdf.Internals.AddObject(spec);files.Add(name,spec.Reference!);
                }
                foreach(var entry in resources)Add(entry.Key,entry.Value);Add(Contract.ManifestName,Contract.Encode(manifest));
                var names=pdf.Internals.Catalog.Elements.GetDictionary("/Names");if(names==null){names=new PdfDictionary(pdf);pdf.Internals.Catalog.Elements["/Names"]=names;}
                var array=new PdfArray(pdf);foreach(var pair in files){array.Elements.Add(new PdfString(pair.Key));array.Elements.Add(pair.Value);}
                var tree=new PdfDictionary(pdf);tree.Elements["/Names"]=array;names.Elements["/EmbeddedFiles"]=tree;
                // This is a full rewrite, closed before reopening for validation.
                pdf.Save(temp);
            }
            Read(temp); // validates hashes, GLB, registration and actual page geometry
            if(File.Exists(dest))File.Replace(temp,dest,null);else File.Move(temp,dest);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static Manifest Describe(bool synthetic,string coverage,string version,Dictionary<string,byte[]> files,string[]? exclusions=null) =>new Manifest{
        Synthetic=synthetic,Coverage=coverage,ExporterVersion=version,Exclusions=exclusions??Array.Empty<string>(),
        RequiredCapabilities=files.Any(x=>x.Key.EndsWith(".glb",StringComparison.Ordinal)&&x.Value.Length>Contract.MaxResource)?new[]{"plan-ray-v1","glb-static-v1",SceneLimits.LargeSceneCapability}:new[]{"plan-ray-v1","glb-static-v1"},
        Resources=files.Select(x=>new Resource{Id=x.Key.EndsWith(".glb")?"scene.root":"metadata."+Path.GetFileNameWithoutExtension(x.Key),Name=x.Key,Kind=x.Key=="registration.json"?"registration":x.Key=="identities.json"?"identities":"scene",Size=x.Value.Length,Sha256=Contract.Hash(x.Value)}).ToArray()};
}

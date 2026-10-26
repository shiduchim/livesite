extern alias wire;
using Offline3D;
using Offline3D.RevitExporter;
using W=wire::Offline3D;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using System.Diagnostics;
using System.IO.Compression;
int checks=0;void Check(string name,Action action){action();checks++;Console.WriteLine("PASS "+name);}void Assert(bool b){if(!b)throw new Exception("Assertion failed");}void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected rejection");}
string directory=Path.GetFullPath(args.Length==0?"/workspace/work/large-resource-fixture":args[0]);Directory.CreateDirectory(directory);var timer=Stopwatch.StartNew();
Check("standalone and reader share the 512 MiB scene / 64 MiB metadata policy",()=>Assert(W.SceneLimits.MaxSceneBytes==SceneLimits.MaxSceneBytes&&W.SceneLimits.MaxMetadataBytes==Contract.MaxResource&&Contract.MaxScene==512*1024*1024&&Contract.MaxPdf==1024*1024*1024));
string scene=Path.Combine(directory,"scene.glb");
Check("standalone exporter spools over 64 MiB of distinct geometry without lowering detail",()=>{
 using var writer=new W.SpoolSceneWriter(directory,W.Matrix4.Graphics(new W.Point3()));
 for(int mesh=0;mesh<90;mesh++){
  const int triangles=8192;var positions=new float[triangles*9];var normals=new float[positions.Length];var indices=new uint[triangles*3];
  for(int face=0;face<triangles;face++){float x=(face%128)*.15f,z=(face/128)*.15f,y=mesh*.0001f;int p=face*9;positions[p]=x;positions[p+1]=y;positions[p+2]=z;positions[p+3]=x+.1f;positions[p+4]=y;positions[p+5]=z;positions[p+6]=x;positions[p+7]=y;positions[p+8]=z+.1f;for(int k=0;k<3;k++){normals[p+k*3+1]=-1;indices[face*3+k]=(uint)(face*3+k);}}
  writer.Add("part-"+mesh+"/0",new W.IndexedGeometry{Positions=positions,Normals=normals,Indices=indices},new W.Matrix4(1,0,0,mesh*80,0,1,0,0,0,0,1,0,0,0,0,1),new W.PlainMaterial{Name="Concrete",Red=.65,Green=.65,Blue=.65},new W.ElementIdentity{UniqueId="part-"+mesh,DocumentId="host",MeshName="part-"+mesh+"/0",ElementId=mesh.ToString(),Category="Generic Models"});
 }
 var stats=writer.Write(scene);Assert(stats.Bytes>64L*1024*1024&&stats.UniqueMeshes==90&&stats.Triangles==90L*8192&&stats.UniqueTriangles==stats.Triangles);Console.WriteLine("Distinct scene bytes: "+stats.Bytes+"; unique triangles: "+stats.UniqueTriangles);
});
var bytes=File.ReadAllBytes(scene);var sha=Contract.Hash(bytes);
Check("strict reader validates actual large unique geometry",()=>Contract.ValidateGlb(bytes));
var reg=new Registration{ViewId="v",LevelId="l",DocumentId="host",MediaBox=new[]{0d,0,612,792},PageCropBox=new[]{0d,0,612,792},CropPdf=new[]{0d,0,100,0,100,100,0,100},PreciseClicks=true,FitPointCount=3,CheckPointCount=3};
var files=new Dictionary<string,byte[]>{{"registration.json",Contract.Encode(reg)},{"identities.json",Contract.Encode(Array.Empty<ElementIdentity>())},{"scene.glb",bytes}};
var manifest=PdfPackageService.Describe(true,"Large distinct geometry regression", "0.5.1-test",files);manifest.RequiredCapabilities=manifest.RequiredCapabilities.Concat(new[]{"glb-affine-instances-v1"}).ToArray();
Check("large scene capability is explicit and accepted",()=>{Assert(manifest.RequiredCapabilities.Contains(SceneLimits.LargeSceneCapability));Contract.Validate(manifest);});
Check("a large scene without capability is rejected",()=>{var m=Contract.Decode<Manifest>(Contract.Encode(manifest));m.RequiredCapabilities=m.RequiredCapabilities.Where(c=>c!=SceneLimits.LargeSceneCapability).ToArray();Reject(()=>Contract.Validate(m));});
Check("metadata and scene limits remain independent",()=>{var m=Contract.Decode<Manifest>(Contract.Encode(manifest));m.Resources.Single(r=>r.Kind=="identities").Size=Contract.MaxResource+1L;Reject(()=>Contract.Validate(m));m=Contract.Decode<Manifest>(Contract.Encode(manifest));m.Resources.Single(r=>r.Kind=="scene").Size=SceneLimits.MaxSceneBytes+1L;Reject(()=>Contract.Validate(m));});
string plain=Path.Combine(directory,"drawing.pdf"),pdf=Path.Combine(directory,"large.pdf");using(var d=new PdfDocument()){var page=d.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromPoint(612);page.Height=PdfSharp.Drawing.XUnit.FromPoint(792);d.Save(plain);}
var service=new PdfPackageService();
Check("large scene packages and reopens with exact bytes and all nodes",()=>{service.Write(plain,pdf,manifest,files);var reopened=service.Read(pdf);Assert(reopened.Glb.Length==bytes.Length&&Contract.Hash(reopened.Glb)==sha);});
string compressed=Path.Combine(directory,"large-compressed.pdf");
Check("compressed large attachment reopens through the exact-size bounded decoder",()=>{
 using(var d=PdfReader.Open(pdf,PdfDocumentOpenMode.Modify)){var names=d.Internals.Catalog.Elements.GetDictionary("/Names")!.Elements.GetDictionary("/EmbeddedFiles")!.Elements.GetArray("/Names")!;for(int i=0;i<names.Elements.Count;i+=2)if(names.Elements.GetString(i)=="scene.glb"){var spec=(PdfDictionary)((PdfReference)names.Elements[i+1]).Value;var stream=spec.Elements.GetDictionary("/EF")!.Elements.GetDictionary("/F")!;using var output=new MemoryStream();using(var zip=new ZLibStream(output,CompressionLevel.Fastest,true))zip.Write(bytes);stream.Stream.Value=output.ToArray();stream.Elements.SetName("/Filter","/FlateDecode");}d.Save(compressed);}
 var reopened=service.Read(compressed);Assert(reopened.Glb.Length>64*1024*1024&&Contract.Hash(reopened.Glb)==sha);
});
Check("large package failure preserves an existing destination",()=>{var before=Contract.Hash(File.ReadAllBytes(pdf));var bad=Contract.Decode<Manifest>(Contract.Encode(manifest));bad.Resources.Single(r=>r.Kind=="scene").Sha256=new string('a',64);Reject(()=>service.Write(plain,pdf,bad,files));Assert(Contract.Hash(File.ReadAllBytes(pdf))==before);});
string images=Path.Combine(directory,"preview-files");Directory.CreateDirectory(images);foreach(var f in Directory.GetFiles(images))File.Delete(f);
Check("missing preview output reports an error instead of returning null",()=>{try{PreviewImageFiles.Find(images);throw new Exception("Expected failure");}catch(IOException ex){Assert(ex.Message.Contains("0 PNG"));}});
Check("native PNG name and upper-case extension are accepted",()=>{string p=Path.Combine(images,"snapshot - 3D View - Offline3D_TEMP.PNG");File.WriteAllBytes(p,new byte[]{1});Assert(PreviewImageFiles.Find(images)==p);});
Check("empty or ambiguous preview output is rejected",()=>{string p=Directory.GetFiles(images).Single();File.WriteAllBytes(p,Array.Empty<byte>());try{PreviewImageFiles.Find(images);throw new Exception("Expected failure");}catch(IOException ex){Assert(ex.Message.Contains("empty"));}File.WriteAllBytes(p,new byte[]{1});File.WriteAllBytes(Path.Combine(images,"other.png"),new byte[]{1});try{PreviewImageFiles.Find(images);throw new Exception("Expected failure");}catch(IOException ex){Assert(ex.Message.Contains("2 PNG"));}});
File.WriteAllText(Path.Combine(directory,"manifest.json"),System.Text.Json.JsonSerializer.Serialize(new{bytes=bytes.Length,sha256=sha,nodes=90,triangles=90*8192}));
Console.WriteLine(checks+" large resource / preview checks passed in "+timer.ElapsedMilliseconds+" ms. Peak working set "+Process.GetCurrentProcess().PeakWorkingSet64+" bytes; Linux synthetic data, not Revit runtime.");

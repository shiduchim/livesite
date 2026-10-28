using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Offline3D;
namespace EKFastPDF.Offline3D;

/// <summary>Constructed only when the user activates 3D. Never renders a PDF page.</summary>
internal sealed class BoxPanel:IDisposable
{
    private const string Origin="https://offline3d-v052.local";
    public WebView2 Control {get;}=new();
    public event Action? HideRequested;
    public event Action? UndockRequested;
    public event Action<Point3[]>? FootprintChanged;
    public event Action<string>? Status;
    private readonly OpenPackage package;
    private static readonly string DefaultsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Offline3DPdfPrototype","section-box-defaults.json");
    private SectionBoxDefaults defaults;
    private Point3? center;
    private bool ready,sent,disposed;
    private double width=3.048,depth=3.048,above=3.048,below=.1524;
    private BoxVolume? volume;
    private ViewRegion? activeRegion;
    private static readonly HashSet<string> Allowed=new(StringComparer.Ordinal){"/three.html","/three.css","/three.mjs","/navigation.mjs","/section-grips.mjs","/precomputed.mjs","/scene-data.mjs","/renderer.mjs","/sections.mjs","/section-worker.mjs","/comparison-base.mjs","/vendor/babylon.js","/vendor/loaders.js","/vendor/earcut.js","/vendor/studio.env","/vendor/studio.ktx","/vendor/three-adapter.mjs","/vendor/glslang.js","/vendor/glslang.wasm","/vendor/twgsl.js","/vendor/twgsl.wasm"};
    public BoxPanel(OpenPackage package){this.package=package;defaults=SectionBoxDefaults.Load(DefaultsPath);width=depth=defaults.SquareSizeMeters;above=defaults.AboveMeters;below=defaults.BelowMeters;}
    public async Task InitializeAsync(){
        var runtime=Path.Combine(AppContext.BaseDirectory,"WebView2Runtime");
        if(!File.Exists(Path.Combine(runtime,"msedgewebview2.exe")))runtime=null;
        try{CoreWebView2Environment.GetAvailableBrowserVersionString(runtime);}catch{throw new FileNotFoundException("WebView2 Runtime is missing. Rerun INSTALL.cmd to set up the viewer dependency.");}
        var environment=await CoreWebView2Environment.CreateAsync(runtime,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Offline3DPrototype","FastPDF-WebCache"),new CoreWebView2EnvironmentOptions{AdditionalBrowserArguments="--force_high_performance_gpu"});
        if(disposed)return;
        await Control.EnsureCoreWebView2Async(environment);if(disposed)return;
        var web=Control.CoreWebView2;web.Settings.AreHostObjectsAllowed=false;web.Settings.AreDevToolsEnabled=false;web.Settings.AreDefaultContextMenusEnabled=false;web.Settings.IsPasswordAutosaveEnabled=false;web.Settings.IsGeneralAutofillEnabled=false;
        web.SetVirtualHostNameToFolderMapping("offline3d-v052.local",Path.Combine(AppContext.BaseDirectory,"offline3d"),CoreWebView2HostResourceAccessKind.DenyCors);
        web.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        web.WebResourceRequested+=(_,e)=>{
            if(Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out var u)&&u.GetLeftPart(UriPartial.Authority)==Origin&&u.Query.Length==0){
                if(u.AbsolutePath=="/scene.glb"&&e.Request.Method=="GET"&&e.ResourceContext==CoreWebView2WebResourceContext.Fetch){e.Response=environment.CreateWebResourceResponse(new MemoryStream(package.Glb,false),200,"OK","Content-Type: model/gltf-binary\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Length: "+package.Glb.Length);return;}
                if(Allowed.Contains(u.AbsolutePath))return;
            }
            e.Response=environment.CreateWebResourceResponse(new MemoryStream(),403,"Blocked","");
        };
        web.NavigationStarting+=(_,e)=>{if(e.Uri!=Origin+"/three.html")e.Cancel=true;};web.NewWindowRequested+=(_,e)=>e.Handled=true;web.DownloadStarting+=(_,e)=>e.Cancel=true;web.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
        web.WebMessageReceived+=Message;web.Navigate(Origin+"/three.html");
    }
    public void SetPoint(double pdfX,double pdfY){activeRegion=package.Registration.ViewRegions.FirstOrDefault(r=>Coordinates.Inside(r.CropPdf,pdfX,pdfY));center=Coordinates.PlanClick(package.Registration,pdfX,pdfY);
        volume=BoxVolume.Centered(package.Registration.ModelToGraphics.Apply(center.Value),width,depth,above,below);SendBox();}
    public void SetDocked(bool value)=>Post(new{type="docked",value});
    public void ResizeEdgeFromPdf(int edge,double pdfX,double pdfY){
        if(disposed||volume==null)return;
        var model=activeRegion==null?Coordinates.PlanPointOnLevel(package.Registration,pdfX,pdfY):Coordinates.PointOnPlane(activeRegion.ModelToPdf,pdfX,pdfY,activeRegion.PlaneOrigin,activeRegion.PlaneNormal,activeRegion.LevelFeet);
        var p=package.Registration.ModelToGraphics.Apply(model);
        var next=new BoxVolume{MinX=volume.MinX,MaxX=volume.MaxX,MinY=volume.MinY,MaxY=volume.MaxY,MinZ=volume.MinZ,MaxZ=volume.MaxZ,Level=volume.Level};
        if(activeRegion!=null&&Math.Abs(activeRegion.PlaneNormal[2])<.99){var matrix=(activeRegion.ModelToPdf*package.Registration.ModelToGraphics.Inverse()).Values;int row=edge==1||edge==3?0:4;int axis=Enumerable.Range(0,3).OrderByDescending(i=>Math.Abs(matrix[row+i])).First();bool maximum=edge==1||edge==2;if(matrix[row+axis]<0)maximum=!maximum;double value=axis==0?p.X:axis==1?p.Y:p.Z;if(axis==0){if(maximum)next.MaxX=value;else next.MinX=value;}else if(axis==1){if(maximum)next.MaxY=value;else next.MinY=value;}else{if(maximum)next.MaxZ=value;else next.MinZ=value;}}
        else switch(edge){case 0:next.MinZ=p.Z;break;case 1:next.MaxX=p.X;break;case 2:next.MaxZ=p.Z;break;case 3:next.MinX=p.X;break;default:return;}
        try{next.Validate();}catch(InvalidDataException){return;}
        volume=next;width=next.Width;depth=next.Depth;above=next.Above;below=next.Below;SendBox();
    }
    private void Post(object message){if(!disposed&&ready)Control.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message,Contract.Json));}
    private void SendBox(bool fitBox=false){if(!center.HasValue||!ready||disposed)return;
        if(!sent){sent=true;Post(new{type="scene",url=Origin+"/scene.glb",size=package.Glb.Length,identities=package.Elements});}
        if(volume==null)return;
        Post(new{type="box",fitBox,x=(volume.MinX+volume.MaxX)/2,y=volume.Level,z=(volume.MinZ+volume.MaxZ)/2,width=volume.Width,depth=volume.Depth,above=volume.Above,below=volume.Below,minX=volume.MinX,maxX=volume.MaxX,minY=volume.MinY,maxY=volume.MaxY,minZ=volume.MinZ,maxZ=volume.MaxZ});
        FootprintChanged?.Invoke(Footprint(volume));
    }
    private Point3[] Footprint(BoxVolume box){
        if(activeRegion==null)return box.PdfFootprint(package.Registration);
        var inverse=package.Registration.ModelToGraphics.Inverse();var transform=activeRegion.ModelToPdf*inverse;
        if(Math.Abs(activeRegion.PlaneNormal[2])>.99)return new[]{new Point3(box.MinX,box.Level,box.MinZ),new Point3(box.MaxX,box.Level,box.MinZ),new Point3(box.MaxX,box.Level,box.MaxZ),new Point3(box.MinX,box.Level,box.MaxZ)}.Select(transform.Apply).ToArray();
        var points=Enumerable.Range(0,8).Select(i=>transform.Apply(new Point3((i&1)==0?box.MinX:box.MaxX,(i&2)==0?box.MinY:box.MaxY,(i&4)==0?box.MinZ:box.MaxZ))).ToArray();double x=points.Min(p=>p.X),y=points.Min(p=>p.Y),X=points.Max(p=>p.X),Y=points.Max(p=>p.Y);return new[]{new Point3(x,y),new Point3(X,y),new Point3(X,Y),new Point3(x,Y)};
    }
    private void PostDefaults()=>Post(new{type="boxDefaults",square=defaults.SquareSizeMeters,above=defaults.AboveMeters,below=defaults.BelowMeters,unit=defaults.Unit});
    private void ResetBounds(){width=depth=defaults.SquareSizeMeters;above=defaults.AboveMeters;below=defaults.BelowMeters;if(!center.HasValue)return;volume=defaults.At(package.Registration.ModelToGraphics.Apply(center.Value));SendBox(true);}
    private void Message(object? sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(disposed||e.Source!=Origin+"/three.html"||e.WebMessageAsJson.Length>4096)return;
        try{using var doc=JsonDocument.Parse(e.WebMessageAsJson);var root=doc.RootElement;switch(root.GetProperty("type").GetString()){
            case "ready":ready=true;PostDefaults();SendBox();break;
            case "hide":HideRequested?.Invoke();break;
            case "undock":UndockRequested?.Invoke();break;
            case "resetBounds":ResetBounds();break;
            case "setBoxDefaults":
                var updated=new SectionBoxDefaults{SquareSizeMeters=root.GetProperty("square").GetDouble(),AboveMeters=root.GetProperty("above").GetDouble(),BelowMeters=root.GetProperty("below").GetDouble(),Unit=root.GetProperty("unit").GetString()??""};
                updated.Save(DefaultsPath);defaults=updated;PostDefaults();ResetBounds();break;
            case "error":Status?.Invoke(root.GetProperty("message").GetString()??"3D error");break;
            case "size":
                double Read(string key,double min){double v=root.GetProperty(key).GetDouble();if(!double.IsFinite(v)||v<min||v>2000000000)throw new InvalidDataException("Invalid 3D box size");return v/100;}
                double w=Read("width",1),d=Read("depth",1),a=Read("above",1),b=Read("below",0);width=w;depth=d;above=a;below=b;
                if(volume!=null)volume=BoxVolume.Centered(new Point3((volume.MinX+volume.MaxX)/2,volume.Level,(volume.MinZ+volume.MaxZ)/2),w,d,a,b);
                SendBox();break;
            case "bounds":
                if(volume==null)break;
                var next=new BoxVolume{MinX=root.GetProperty("minX").GetDouble(),MaxX=root.GetProperty("maxX").GetDouble(),MinY=root.GetProperty("minY").GetDouble(),MaxY=root.GetProperty("maxY").GetDouble(),MinZ=root.GetProperty("minZ").GetDouble(),MaxZ=root.GetProperty("maxZ").GetDouble(),Level=volume.Level};
                next.Validate();volume=next;width=next.Width;depth=next.Depth;above=next.Above;below=next.Below;
                FootprintChanged?.Invoke(Footprint(next));
                if(root.TryGetProperty("final",out var final)&&final.GetBoolean())SendBox();break;
        }}catch(Exception ex){Status?.Invoke(ex.Message);if(ready)Post(new{type="settingsError",message=ex.Message});}
    }
    public void Dispose(){if(disposed)return;disposed=true;Control.Dispose();}
}

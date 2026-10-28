using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Offline3D;
namespace EKFastPDF.Offline3D;

/// <summary>Constructed only when the user activates 3D. Never renders a PDF page.</summary>
internal sealed class BoxPanel:IDisposable
{
    // ".example" is reserved (RFC 6761); WebView2 documents that ".local" host
    // names can delay every navigation.
    private const string Host="offline3d.example",Origin="https://"+Host;
    private const int ChunkBytes=4*1024*1024;
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
    private CoreWebView2Environment? environment;
    // One environment for the whole process: a second CreateAsync on the same profile
    // while the previous browser process is still closing can fail, which made 3D
    // refuse to reopen after it was closed.
    private static Task<CoreWebView2Environment>? sharedEnvironment;
    private static readonly object EnvironmentLock=new();
    private double width=3.048,depth=3.048,above=3.048,below=.1524;
    private BoxVolume? volume;
    private ViewRegion? activeRegion;
    private static readonly HashSet<string> Allowed=new(StringComparer.Ordinal){"/three.html","/three.css","/three.mjs","/measure.mjs","/outlines.mjs","/comparison-worker.mjs","/navigation.mjs","/section-grips.mjs","/precomputed.mjs","/scene-data.mjs","/renderer.mjs","/sections.mjs","/section-worker.mjs","/comparison-base.mjs","/vendor/babylon.js","/vendor/loaders.js","/vendor/earcut.js","/vendor/studio.env","/vendor/studio.ktx","/vendor/three-adapter.mjs","/vendor/glslang.js","/vendor/glslang.wasm","/vendor/twgsl.js","/vendor/twgsl.wasm"};
    public BoxPanel(OpenPackage package){this.package=package;defaults=SectionBoxDefaults.Load(DefaultsPath);width=depth=defaults.SquareSizeMeters;above=defaults.AboveMeters;below=defaults.BelowMeters;}
    public bool IsReady=>ready;
    public async Task InitializeAsync(){
        Task<CoreWebView2Environment> pending;
        lock(EnvironmentLock){pending=sharedEnvironment??=CreateEnvironmentAsync();}
        CoreWebView2Environment environment;
        try{environment=this.environment=await pending;}
        catch{lock(EnvironmentLock){if(ReferenceEquals(sharedEnvironment,pending))sharedEnvironment=null;}throw;}
        if(disposed)return;
        await Control.EnsureCoreWebView2Async(environment);if(disposed)return;
        var web=Control.CoreWebView2;web.Settings.AreHostObjectsAllowed=false;web.Settings.AreDevToolsEnabled=false;web.Settings.AreDefaultContextMenusEnabled=false;web.Settings.IsPasswordAutosaveEnabled=false;web.Settings.IsGeneralAutofillEnabled=false;
        web.SetVirtualHostNameToFolderMapping(Host,Path.Combine(AppContext.BaseDirectory,"offline3d"),CoreWebView2HostResourceAccessKind.DenyCors);
        web.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        web.WebResourceRequested+=(_,e)=>{
            if(Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out var u)&&u.GetLeftPart(UriPartial.Authority)==Origin&&u.Query.Length==0){
                if(Allowed.Contains(u.AbsolutePath))return;
            }
            e.Response=environment.CreateWebResourceResponse(new MemoryStream(),403,"Blocked","");
        };
        web.NavigationStarting+=(_,e)=>{if(e.Uri!=Origin+"/three.html")e.Cancel=true;};web.NewWindowRequested+=(_,e)=>e.Handled=true;web.DownloadStarting+=(_,e)=>e.Cancel=true;web.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
        web.WebMessageReceived+=Message;web.Navigate(Origin+"/three.html");
    }
    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync(){
        var runtime=Path.Combine(AppContext.BaseDirectory,"WebView2Runtime");
        if(!File.Exists(Path.Combine(runtime,"msedgewebview2.exe")))runtime=null;
        try{CoreWebView2Environment.GetAvailableBrowserVersionString(runtime);}catch{throw new FileNotFoundException("WebView2 Runtime is missing. Rerun INSTALL.cmd to set up the viewer dependency.");}
        return await CoreWebView2Environment.CreateAsync(runtime,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Offline3DPrototype","FastPDF-WebCache"),new CoreWebView2EnvironmentOptions{AdditionalBrowserArguments="--force_high_performance_gpu"});
    }
    /// <summary>Where a box would go for a click at this PDF point (the dashed hover preview), or null outside the plan.</summary>
    public Point3[]? PreviewFootprint(double pdfX,double pdfY){
        try{var region=package.Registration.ViewRegions.FirstOrDefault(r=>Coordinates.Inside(r.CropPdf,pdfX,pdfY));
            var point=Coordinates.PlanClick(package.Registration,pdfX,pdfY);
            return Footprint(defaults.At(package.Registration.ModelToGraphics.Apply(point)),region);}
        catch(Exception){return null;}
    }
    /// <summary>
    /// Puts a box at a PDF point. <paramref name="fresh"/> (the first click after 3D opens)
    /// uses the Settings size and fits the view; later clicks keep the current, possibly
    /// stretched, size. A stretch is never saved: closing 3D returns to the Settings size.
    /// </summary>
    public void SetPoint(double pdfX,double pdfY,bool fresh=false){activeRegion=package.Registration.ViewRegions.FirstOrDefault(r=>Coordinates.Inside(r.CropPdf,pdfX,pdfY));center=Coordinates.PlanClick(package.Registration,pdfX,pdfY);
        var at=package.Registration.ModelToGraphics.Apply(center.Value);
        volume=fresh?defaults.At(at):BoxVolume.Centered(at,width,depth,above,below);width=volume.Width;depth=volume.Depth;above=volume.Above;below=volume.Below;SendBox(fresh);}
    public void SetDocked(bool value)=>Post(new{type="docked",value});
    public void ResizeEdgeFromPdf(int edge,double pdfX,double pdfY){
        if(disposed||volume==null)return;
        var model=activeRegion==null?Coordinates.PlanPointOnLevel(package.Registration,pdfX,pdfY):Coordinates.PointOnPlane(activeRegion.ModelToPdf,pdfX,pdfY,activeRegion.PlaneOrigin,activeRegion.PlaneNormal,activeRegion.LevelFeet);
        var p=package.Registration.ModelToGraphics.Apply(model);
        var next=new BoxVolume{MinX=volume.MinX,MaxX=volume.MaxX,MinY=volume.MinY,MaxY=volume.MaxY,MinZ=volume.MinZ,MaxZ=volume.MaxZ,Level=volume.Level};
        if(activeRegion!=null&&Math.Abs(activeRegion.PlaneNormal[2])<.99){var matrix=(activeRegion.ModelToPdf*package.Registration.ModelToGraphics.Inverse()).Values;int row=edge==1||edge==3?0:4;int axis=Enumerable.Range(0,3).OrderByDescending(i=>Math.Abs(matrix[row+i])).First();bool maximum=edge==1||edge==2;if(matrix[row+axis]<0)maximum=!maximum;double value=axis==0?p.X:axis==1?p.Y:p.Z;if(axis==0){if(maximum)next.MaxX=value;else next.MinX=value;}else if(axis==1){if(maximum)next.MaxY=value;else next.MinY=value;}else{if(maximum)next.MaxZ=value;else next.MinZ=value;}}
        else switch(edge){case 0:next.MinZ=p.Z;break;case 1:next.MaxX=p.X;break;case 2:next.MaxZ=p.Z;break;case 3:next.MinX=p.X;break;default:return;}
        try{next.Validate();}catch(InvalidDataException){return;}
        volume=next;width=next.Width;depth=next.Depth;above=next.Above;below=next.Below;SendLive();
    }
    private bool livePosted;
    /// <summary>
    /// A stretch from the plan: the plan outline follows at once; the 3D page gets the latest
    /// box once per idle moment. Before 0.5.4 every mouse move was posted and the page worked
    /// through the queue one by one, so 3D followed in slow motion.
    /// </summary>
    private void SendLive(){
        if(volume==null)return;FootprintChanged?.Invoke(Footprint(volume,activeRegion));
        if(livePosted||!ready||disposed)return;livePosted=true;
        Control.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,()=>{livePosted=false;if(volume==null||!center.HasValue)return;
            Post(new{type="box",live=true,fitBox=false,x=(volume.MinX+volume.MaxX)/2,y=volume.Level,z=(volume.MinZ+volume.MaxZ)/2,width=volume.Width,depth=volume.Depth,above=volume.Above,below=volume.Below,minX=volume.MinX,maxX=volume.MaxX,minY=volume.MinY,maxY=volume.MaxY,minZ=volume.MinZ,maxZ=volume.MaxZ});});
    }
    private void Post(object message){if(!disposed&&ready)Control.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message,Contract.Json));}
    private void SendBox(bool fitBox=false){if(!center.HasValue||!ready||disposed)return;
        if(!sent){sent=true;SendScene();}
        if(volume==null)return;
        Post(new{type="box",fitBox,x=(volume.MinX+volume.MaxX)/2,y=volume.Level,z=(volume.MinZ+volume.MaxZ)/2,width=volume.Width,depth=volume.Depth,above=volume.Above,below=volume.Below,minX=volume.MinX,maxX=volume.MaxX,minY=volume.MinY,maxY=volume.MaxY,minZ=volume.MinZ,maxZ=volume.MaxZ});
        FootprintChanged?.Invoke(Footprint(volume,activeRegion));
    }
    /// <summary>
    /// v0.5.2 served scene.glb by answering a fetch for a URL on the
    /// folder-mapped virtual host from WebResourceRequested. In the installed
    /// viewer that fetch failed ("Failed to fetch") and 3D stayed on "Loading".
    /// The model is now handed over without any network request: as one
    /// read-only shared-memory buffer (WebView2 PostSharedBufferToScript), or,
    /// on a runtime without that API, as base64 chunks over the message channel.
    /// </summary>
    private void SendScene(){
        // The page accepts whichever complete copy arrives: the buffer or all chunks.
        Post(new{type="scene",size=package.Glb.Length,identities=package.Elements});
        try{
            using var buffer=environment!.CreateSharedBuffer((ulong)package.Glb.Length);
            using(var stream=buffer.OpenStream())stream.Write(package.Glb,0,package.Glb.Length);
            Control.CoreWebView2.PostSharedBufferToScript(buffer,CoreWebView2SharedBufferAccess.ReadOnly,JsonSerializer.Serialize(new{type="sceneBuffer",size=package.Glb.Length}));
            return;
        }catch(Exception ex) when (ex is NotImplementedException or InvalidCastException or COMException or ArgumentException or InvalidOperationException){
            Status?.Invoke("3D: shared memory unavailable ("+ex.GetType().Name+"); sending the model in parts.");
        }
        for(int offset=0;offset<package.Glb.Length&&!disposed;offset+=ChunkBytes){
            int count=Math.Min(ChunkBytes,package.Glb.Length-offset);
            Post(new{type="sceneChunk",offset,total=package.Glb.Length,data=Convert.ToBase64String(package.Glb,offset,count)});
        }
    }
    private Point3[] Footprint(BoxVolume box,ViewRegion? region){
        if(region==null)return box.PdfFootprint(package.Registration);
        var inverse=package.Registration.ModelToGraphics.Inverse();var transform=region.ModelToPdf*inverse;
        if(Math.Abs(region.PlaneNormal[2])>.99)return new[]{new Point3(box.MinX,box.Level,box.MinZ),new Point3(box.MaxX,box.Level,box.MinZ),new Point3(box.MaxX,box.Level,box.MaxZ),new Point3(box.MinX,box.Level,box.MaxZ)}.Select(transform.Apply).ToArray();
        var points=Enumerable.Range(0,8).Select(i=>transform.Apply(new Point3((i&1)==0?box.MinX:box.MaxX,(i&2)==0?box.MinY:box.MaxY,(i&4)==0?box.MinZ:box.MaxZ))).ToArray();double x=points.Min(p=>p.X),y=points.Min(p=>p.Y),X=points.Max(p=>p.X),Y=points.Max(p=>p.Y);return new[]{new Point3(x,y),new Point3(X,y),new Point3(X,Y),new Point3(x,Y)};
    }
    private void PostDefaults()=>Post(new{type="boxDefaults",square=defaults.SquareSizeMeters,above=defaults.AboveMeters,below=defaults.BelowMeters,unit=defaults.Unit,isDefault=defaults.IsFactory()&&defaults.Unit=="feet"});
    private void SaveDefaults(SectionBoxDefaults next){next.Save(DefaultsPath);defaults=next;PostDefaults();}
    private void ResetBounds(){width=depth=defaults.SquareSizeMeters;above=defaults.AboveMeters;below=defaults.BelowMeters;if(!center.HasValue)return;volume=defaults.At(package.Registration.ModelToGraphics.Apply(center.Value));SendBox();}
    private void Message(object? sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(disposed||e.Source!=Origin+"/three.html"||e.WebMessageAsJson.Length>4096)return;
        try{using var doc=JsonDocument.Parse(e.WebMessageAsJson);var root=doc.RootElement;switch(root.GetProperty("type").GetString()){
            // Send the model as soon as the page can take it, usually while the
            // user is still choosing a spot, so the panel opens on a ready model.
            case "ready":ready=true;PostDefaults();if(!sent){sent=true;SendScene();}SendBox();break;
            case "hide":HideRequested?.Invoke();break;
            case "undock":UndockRequested?.Invoke();break;
            case "resetBounds":ResetBounds();break;
            // Settings: saved at once and applied to the open box (keeping its spot).
            case "setBoxDefaults":
                SaveDefaults(new SectionBoxDefaults{SquareSizeMeters=root.GetProperty("square").GetDouble(),AboveMeters=root.GetProperty("above").GetDouble(),BelowMeters=root.GetProperty("below").GetDouble(),Unit=defaults.Unit});ResetBounds();break;
            // Reset: 10 ft above, 0.5 ft below, 10 ft square, shown in Feet.
            case "restoreBoxDefault":SaveDefaults(SectionBoxDefaults.Factory());ResetBounds();break;
            case "setUnit":var unit=root.GetProperty("unit").GetString();if(unit is "feet" or "meters"&&unit!=defaults.Unit)SaveDefaults(defaults.WithUnit(unit));break;
            case "error":Status?.Invoke(root.GetProperty("message").GetString()??"3D error");break;
            case "bounds":
                if(volume==null)break;
                var next=new BoxVolume{MinX=root.GetProperty("minX").GetDouble(),MaxX=root.GetProperty("maxX").GetDouble(),MinY=root.GetProperty("minY").GetDouble(),MaxY=root.GetProperty("maxY").GetDouble(),MinZ=root.GetProperty("minZ").GetDouble(),MaxZ=root.GetProperty("maxZ").GetDouble(),Level=volume.Level};
                next.Validate();volume=next;width=next.Width;depth=next.Depth;above=next.Above;below=next.Below;
                FootprintChanged?.Invoke(Footprint(next,activeRegion));
                if(root.TryGetProperty("final",out var final)&&final.GetBoolean())SendBox();break;
        }}catch(Exception ex){Status?.Invoke(ex.Message);if(ready)Post(new{type="settingsError",message=ex.Message});}
    }
    public void Dispose(){if(disposed)return;disposed=true;Control.Dispose();}
}

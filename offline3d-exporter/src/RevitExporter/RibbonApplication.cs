using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;
namespace Offline3D.RevitExporter;
public sealed class RibbonApplication:IExternalApplication
{
    // EK add-in style: one large button; the panel title is blank so the name is not shown twice.
    public Result OnStartup(UIControlledApplication app){
        var panel=app.CreateRibbonPanel("Offline 3D PDF");
        panel.Title=" ";
        var data=new PushButtonData("Offline3D.Export","Export\n3D PDF",Assembly.GetExecutingAssembly().Location,typeof(ExportCommand).FullName);
        var large=TryLoadIcon(32);var small=TryLoadIcon(16);
        if(large!=null)data.LargeImage=large;
        if(small!=null)data.Image=small;else if(large!=null)data.Image=large;
        var button=(PushButton)panel.AddItem(data);
        button.ToolTip="Export the open plan, RCP, section, elevation or orthographic 3D view (or the model views on the open sheet) as a PDF with clickable offline 3D, cropped to the view. Automatic registration is checked before delivery.";
        button.AvailabilityClassName=typeof(ExportAvailability).FullName;
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app)=>Result.Succeeded;

    private static readonly int[] LargeSizes={32,40,48,56,64,72,80,96},SmallSizes={16,20,24,28,32,36,40,48};
    /// <summary>
    /// The smallest embedded PNG that covers the display scale, rebuilt with its exact DPI so
    /// WPF draws it pixel for pixel (no blur at 125-300 %). Any error gives null: the
    /// ribbon never fails because of an icon.
    /// </summary>
    private static System.Windows.Media.ImageSource? TryLoadIcon(int logical){
        try{
            int needed=(int)Math.Ceiling(logical*DisplayScale()-.01);
            var sizes=logical==16?SmallSizes:LargeSizes;
            int size=sizes.FirstOrDefault(s=>s>=needed);if(size==0)size=sizes[sizes.Length-1];
            string name="Icons.Export3DPdf"+(logical==16?(size==16?"_16":"_16_"+size):(size==32?"":"_"+size))+".png";
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name);if(stream==null)return null;
            var decoder=new System.Windows.Media.Imaging.PngBitmapDecoder(stream,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat|System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            System.Windows.Media.Imaging.BitmapSource frame=decoder.Frames[0];
            if(frame.Format!=System.Windows.Media.PixelFormats.Pbgra32)frame=new System.Windows.Media.Imaging.FormatConvertedBitmap(frame,System.Windows.Media.PixelFormats.Pbgra32,null,0);
            int w=frame.PixelWidth,h=frame.PixelHeight,stride=w*4;var pixels=new byte[stride*h];frame.CopyPixels(pixels,stride,0);
            double dpi=96.0*w/logical;
            var exact=System.Windows.Media.Imaging.BitmapSource.Create(w,h,dpi,dpi,System.Windows.Media.PixelFormats.Pbgra32,null,pixels,stride);exact.Freeze();return exact;
        }catch(Exception){return null;}
    }
    private static double DisplayScale(){
        try{using var g=System.Drawing.Graphics.FromHwnd(IntPtr.Zero);double scale=g.DpiX/96.0;return double.IsNaN(scale)||scale<1?1:scale;}
        catch(Exception){return 1;}
    }
}
public sealed class ExportAvailability:IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication app,CategorySet categories){
        var ui=app.ActiveUIDocument;
        return ui!=null&&!ui.Document.IsFamilyDocument&&(ui.ActiveView is ViewSheet||ExportTarget.IsSupportedView(ui.ActiveView));
    }
}

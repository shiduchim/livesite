using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;
namespace Offline3D.RevitExporter;
public sealed class RibbonApplication:IExternalApplication
{
    public Result OnStartup(UIControlledApplication app){
        var panel=app.CreateRibbonPanel("Offline 3D PDF v0.5.2");
        var button=(PushButton)panel.AddItem(new PushButtonData("Offline3D.Export","Export 3D PDF",Assembly.GetExecutingAssembly().Location,typeof(ExportCommand).FullName));
        button.ToolTip="Export the open floor plan, or a plan on the open sheet, as a PDF with clickable offline 3D. No viewport selection is needed for one plan. Automatic registration is checked before delivery.";
        button.AvailabilityClassName=typeof(ExportAvailability).FullName;
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app)=>Result.Succeeded;
}
public sealed class ExportAvailability:IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication app,CategorySet categories){
        var ui=app.ActiveUIDocument;
        return ui!=null&&!ui.Document.IsFamilyDocument&&(ui.ActiveView is ViewSheet||ExportTarget.IsSupportedView(ui.ActiveView));
    }
}

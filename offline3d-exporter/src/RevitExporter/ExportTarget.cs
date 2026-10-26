using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Forms;
using Form=System.Windows.Forms.Form;
using ComboBox=System.Windows.Forms.ComboBox;
using Button=System.Windows.Forms.Button;
using Control=System.Windows.Forms.Control;

namespace Offline3D.RevitExporter;

internal sealed class ExportSetupException:Exception
{
    public ExportSetupException(string message):base(message){}
}

/// <summary>Resolves one plan without requiring an on-canvas selection. All
/// direct-view preparation is temporary and is rolled back on every exit.</summary>
internal sealed class ExportTarget:IDisposable
{
    private TransactionGroup? temporary;
    public ViewSheet Sheet{get;private set;}=null!;
    public Viewport Viewport{get;private set;}=null!;
    public Autodesk.Revit.DB.View Plan{get;private set;}=null!;
    public Autodesk.Revit.DB.View SourcePlan{get;private set;}=null!;
    public Viewport[] Viewports {get;private set;}=Array.Empty<Viewport>();
    public bool FromPlan{get;private set;}
    public string FileStem=>FromPlan?SourcePlan.Name:Sheet.SheetNumber;

    public static bool IsFloorPlan(Autodesk.Revit.DB.View? view)=>view!=null&&!view.IsTemplate&&(view.ViewType==ViewType.FloorPlan||view.ViewType==ViewType.CeilingPlan);
    public static bool IsSupportedView(Autodesk.Revit.DB.View? view)=>view!=null&&!view.IsTemplate&&view.HasViewTransforms()&&(IsFloorPlan(view)||view.ViewType==ViewType.Section||view.ViewType==ViewType.Elevation||(view is View3D v&&!v.IsPerspective));

    public static ExportTarget? Create(UIDocument ui,IntPtr owner)
    {
        var result=new ExportTarget();
        try{
            var doc=ui.Document;
            if(IsSupportedView(doc.ActiveView)){
                result.SourcePlan=doc.ActiveView;
                var duplicate=result.SourcePlan.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing)?ViewDuplicateOption.WithDetailing:ViewDuplicateOption.Duplicate;
                if(!result.SourcePlan.CanViewBeDuplicated(duplicate))
                    throw new ExportSetupException("This view cannot be copied for export. Open an independent supported model view and try again.");
                result.FromPlan=true;
                result.temporary=new TransactionGroup(doc,"Offline3D temporary plan export");
                result.temporary.Start();
                using(var tx=new Autodesk.Revit.DB.Transaction(doc,"Prepare temporary PDF page")){
                    tx.Start();
                    result.Plan=(Autodesk.Revit.DB.View)doc.GetElement(result.SourcePlan.Duplicate(duplicate));
                    result.Plan.Name="Offline3D_TEMP_"+Guid.NewGuid().ToString("N");
                    // Detaching only the temporary copy permits a crop controlled
                    // by a template to be enabled without editing the user's view.
                    result.Plan.ViewTemplateId=ElementId.InvalidElementId;
                    if(!(result.Plan is View3D))result.Plan.CropBoxActive=true;
                    result.Plan.CropBoxVisible=false;
                    result.Sheet=ViewSheet.Create(doc,ElementId.InvalidElementId);
                    result.Sheet.Name="Offline 3D PDF temporary page";
                    if(!Autodesk.Revit.DB.Viewport.CanAddViewToSheet(doc,result.Sheet.Id,result.Plan.Id))
                        throw new ExportSetupException("Revit could not prepare a PDF page for this view. Open an independent supported model view and try again.");
                    result.Viewport=Autodesk.Revit.DB.Viewport.Create(doc,result.Sheet.Id,result.Plan.Id,new XYZ(0,0,0));
                    var viewportType=(ElementType)doc.GetElement(result.Viewport.GetTypeId());
                    var exportType=viewportType.Duplicate("Offline3D_TEMP_"+Guid.NewGuid().ToString("N"));
                    var showLabel=exportType.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL);
                    if(showLabel!=null&&!showLabel.IsReadOnly)showLabel.Set(0);
                    result.Viewport.ChangeTypeId(exportType.Id);
                    tx.Commit();
                }
            }else if(doc.ActiveView is ViewSheet sheet){
                result.Sheet=sheet;
                var all=sheet.GetAllViewports().Select(id=>(Viewport)doc.GetElement(id)).ToArray();
                var plans=all.Where(v=>IsSupportedView(doc.GetElement(v.ViewId) as Autodesk.Revit.DB.View)).ToArray();
                if(plans.Length==0)throw new ExportSetupException("This sheet has no supported model viewport. Open a plan, RCP, section, elevation or orthographic 3D view.");
                int choice=AutomaticPlanChoice.SelectIndex(plans.Select(v=>v.Id).ToArray(),ui.Selection.GetElementIds());
                result.Viewport=plans[Math.Max(0,choice)];
                result.Viewports=new[]{result.Viewport}.Concat(plans.Where(v=>v.Id!=result.Viewport.Id)).ToArray();
                result.Plan=(Autodesk.Revit.DB.View)doc.GetElement(result.Viewport.ViewId);result.SourcePlan=result.Plan;
            }else throw new ExportSetupException("Open a plan, RCP, section, elevation, orthographic 3D view, or a sheet containing one, then click Export 3D PDF.");
            if(result.Viewports.Length==0)result.Viewports=new[]{result.Viewport};
            return result;
        }catch{result.Dispose();throw;}
    }

    private static SheetBounds Bounds(Viewport viewport)
    {
        var box=viewport.GetBoxOutline();var title=viewport.GetLabelOutline();
        if(title.IsEmpty)return new SheetBounds(box.MinimumPoint.X,box.MinimumPoint.Y,box.MaximumPoint.X,box.MaximumPoint.Y);
        return new SheetBounds(Math.Min(box.MinimumPoint.X,title.MinimumPoint.X),Math.Min(box.MinimumPoint.Y,title.MinimumPoint.Y),Math.Max(box.MaximumPoint.X,title.MaximumPoint.X),Math.Max(box.MaximumPoint.Y,title.MaximumPoint.Y));
    }

    public void Restore()
    {
        if(temporary==null)return;
        try{if(temporary.GetStatus()==TransactionStatus.Started)temporary.RollBack();}
        finally{temporary.Dispose();temporary=null;}
    }
    public void Dispose()=>Restore();
}

internal sealed class RevitWindow:IWin32Window
{
    public IntPtr Handle{get;}
    public RevitWindow(IntPtr handle){Handle=handle;}
}

internal sealed class PlanPicker:Form
{
    private readonly ComboBox plans;
    public int SelectedIndex=>plans.SelectedIndex;
    public PlanPicker(string[] names)
    {
        Text="Export 3D PDF";Width=560;Height=205;FormBorderStyle=FormBorderStyle.FixedDialog;
        StartPosition=FormStartPosition.CenterParent;MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=false;
        var label=new Label{Text="Choose the floor plan for clickable 3D on this PDF page:",Left=18,Top=18,Width=500,Height=24};
        plans=new ComboBox{Left=18,Top=50,Width=500,DropDownStyle=ComboBoxStyle.DropDownList};
        plans.Items.AddRange(names);plans.SelectedIndex=0;
        var export=new Button{Text="Continue",Left=308,Top=100,Width=100,DialogResult=DialogResult.OK};
        var cancel=new Button{Text="Cancel",Left=418,Top=100,Width=100,DialogResult=DialogResult.Cancel};
        Controls.AddRange(new Control[]{label,plans,export,cancel});AcceptButton=export;CancelButton=cancel;
    }
}

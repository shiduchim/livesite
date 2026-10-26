using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace Offline3D.Automatic;
public sealed class AutomaticRegistrationResult
{
    public required CalibrationInput Controls {get;init;}
    public required CalibrationResult Calibration {get;init;}
    public required string AppearanceSha256 {get;init;}
    public int RenderWidth {get;init;} public int RenderHeight {get;init;}
    public int MarkerCount {get;init;}
}
public static class AutomaticRegistrationService
{
    private static readonly object Gate=new();
    public static AutomaticRegistrationResult Register(string cleanPdf,string markedPdf,Registration registration,SheetMarker[] markers) {
        if(markers.Length!=25||markers.Select(m=>m.Id).Distinct().Count()!=25||markers.Any(m=>m.Id<0||m.Id>=25||m.Red!=241||m.Blue!=229||m.Green!=7+9*m.Id))throw new InvalidDataException("Expected 25 valid registration markers");
        foreach(string p in new[]{cleanPdf,markedPdf})if(new FileInfo(p).Length>Contract.MaxPdf)throw new InvalidDataException("PDF exceeds bounded export budget");
        // PDFium is process-global and not thread safe; serialized, out of the Revit process.
        lock(Gate){Native.FPDF_InitLibrary();IntPtr clean=IntPtr.Zero,marked=IntPtr.Zero,cleanPage=IntPtr.Zero,markedPage=IntPtr.Zero;
            try{
                clean=Native.FPDF_LoadDocument(cleanPdf,IntPtr.Zero);marked=Native.FPDF_LoadDocument(markedPdf,IntPtr.Zero);
                if(clean==IntPtr.Zero||marked==IntPtr.Zero||Native.FPDF_GetPageCount(clean)!=1||Native.FPDF_GetPageCount(marked)!=1)throw new InvalidDataException("Two valid single-page native exports required");
                cleanPage=Native.FPDF_LoadPage(clean,0);markedPage=Native.FPDF_LoadPage(marked,0);
                if(cleanPage==IntPtr.Zero||markedPage==IntPtr.Zero)throw new InvalidDataException("Cannot load exported page");
                var cleanGeometry=new Registration();var markedGeometry=new Registration();PdfPackageService.SetPageGeometry(cleanPdf,cleanGeometry);PdfPackageService.SetPageGeometry(markedPdf,markedGeometry);
                if(!cleanGeometry.MediaBox.SequenceEqual(markedGeometry.MediaBox)||!cleanGeometry.PageCropBox.SequenceEqual(markedGeometry.PageCropBox)||cleanGeometry.Rotation!=markedGeometry.Rotation||cleanGeometry.UserUnit!=markedGeometry.UserUnit)throw new InvalidDataException("Clean/marked export page geometry differs");
                var detected=markers.ToDictionary(m=>m.Id,_=>new List<Point3>());int objectBudget=0;
                void Visit(IntPtr owner,bool form,Matrix4 parent,int depth,bool remove){
                    if(depth>16)throw new InvalidDataException("PDF object nesting exceeds budget");
                    int count=form?Native.FPDFFormObj_CountObjects(owner):Native.FPDFPage_CountObjects(owner);
                    for(int i=count-1;i>=0;i--){if(++objectBudget>500000)throw new InvalidDataException("PDF object budget exceeded");
                        IntPtr obj=form?Native.FPDFFormObj_GetObject(owner,(uint)i):Native.FPDFPage_GetObject(owner,i);
                        if(Native.FPDFPageObj_GetMatrix(obj,out var local)==0)throw new InvalidDataException("Cannot read PDF object transform");var matrix=parent*local.ToMatrix();int type=Native.FPDFPageObj_GetType(obj);
                        if(type==5){Visit(obj,true,matrix,depth+1,remove);continue;}
                        if(type!=2||Native.FPDFPageObj_GetStrokeColor(obj,out uint r,out uint g,out uint b,out uint a)==0)continue;
                        var matches=markers.Where(m=>Math.Abs((long)r-m.Red)<=1&&Math.Abs((long)g-m.Green)<=1&&Math.Abs((long)b-m.Blue)<=1&&a==255).ToArray();if(matches.Length==0)continue;
                        if(matches.Length!=1)throw new InvalidDataException("Ambiguous marker color");
                        if(!remove)throw new InvalidDataException("Drawing uses reserved calibration colors; change those line colors before export");
                        var points=detected[matches[0].Id];int segments=Native.FPDFPath_CountSegments(obj);if(segments<2||segments>16)throw new InvalidDataException("Unsupported calibration path");
                        for(int k=0;k<segments;k++){if(Native.FPDFPathSegment_GetPoint(Native.FPDFPath_GetPathSegment(obj,k),out float x,out float y)==0)throw new InvalidDataException("Invalid calibration vertex");points.Add(matrix.Apply(new Point3(x,y)));}
                        int removed=form?Native.FPDFFormObj_RemoveObject(owner,obj):Native.FPDFPage_RemoveObject(owner,obj);if(removed==0)throw new InvalidDataException("Cannot isolate calibration marks");Native.FPDFPageObj_Destroy(obj);
                    }
                }
                Visit(cleanPage,false,Matrix4.Identity,0,false);Visit(markedPage,false,Matrix4.Identity,0,true);
                var controls=new List<ControlPoint>();
                foreach(var marker in markers.OrderBy(m=>m.Id)){
                    var unique=new List<Point3>();foreach(var p in detected[marker.Id])if(!unique.Any(q=>(p-q).Length<.0005))unique.Add(p);
                    if(unique.Count!=3)throw new InvalidDataException($"Marker {marker.Id+1}: expected exactly three L vertices, found {unique.Count}");
                    var candidates=unique.Where(p=>{var ends=unique.Where(q=>(q-p).Length>.0005).ToArray();var u=ends[0]-p;var v=ends[1]-p;return Math.Abs(u.X*v.X+u.Y*v.Y)/(u.Length*v.Length)<.01;}).ToArray();
                    if(candidates.Length!=1)throw new InvalidDataException("Cannot identify calibration corner");
                    controls.Add(new ControlPoint{SheetX=marker.SheetX,SheetY=marker.SheetY,PdfX=candidates[0].X,PdfY=candidates[0].Y});
                }
                var input=new CalibrationInput{Fit=controls.Where((_,i)=>i%2==1).ToArray(),Check=controls.Where((_,i)=>i%2==0).ToArray()};
                var calibration=Calibration.Fit(input,registration.ModelToSheet,registration.LevelProjectElevationFeet,registration.ClickPlaneOrigin,registration.ClickPlaneNormal);
                // Compare the marked export after removing only the known markers with the untouched
                // clean native PDF. Deliver the clean PDF, so no content regeneration affects its drawing.
                if(Native.FPDFPage_GenerateContent(markedPage)==0)throw new InvalidDataException("Cannot regenerate comparison page");
                float w=Native.FPDF_GetPageWidthF(cleanPage),h=Native.FPDF_GetPageHeightF(cleanPage);if(w<=0||h<=0||w>100000||h>100000)throw new InvalidDataException("Unsupported page dimensions");
                double scale=Math.Min(2,Math.Sqrt(12000000d/(w*h)));int width=Math.Max(1,(int)Math.Round(w*scale)),height=Math.Max(1,(int)Math.Round(h*scale));
                byte[] original=Render(cleanPage,width,height),comparison=Render(markedPage,width,height);
                if(!original.SequenceEqual(comparison))throw new InvalidDataException("Clean/marked exports do not render identically after marker removal; no precise package was produced");
                return new AutomaticRegistrationResult{Controls=input,Calibration=calibration,AppearanceSha256=Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),RenderWidth=width,RenderHeight=height,MarkerCount=25};
            }finally{if(cleanPage!=IntPtr.Zero)Native.FPDF_ClosePage(cleanPage);if(markedPage!=IntPtr.Zero)Native.FPDF_ClosePage(markedPage);if(clean!=IntPtr.Zero)Native.FPDF_CloseDocument(clean);if(marked!=IntPtr.Zero)Native.FPDF_CloseDocument(marked);Native.FPDF_DestroyLibrary();}
        }
    }
    private static byte[] Render(IntPtr page,int width,int height){
        IntPtr bitmap=Native.FPDFBitmap_Create(width,height,0);if(bitmap==IntPtr.Zero)throw new InvalidDataException("Cannot allocate comparison bitmap");
        try{Native.FPDFBitmap_FillRect(bitmap,0,0,width,height,0xffffffff);Native.FPDF_RenderPageBitmap(bitmap,page,0,0,width,height,0,1);int stride=Native.FPDFBitmap_GetStride(bitmap);var bytes=new byte[checked(width*height*4)];IntPtr source=Native.FPDFBitmap_GetBuffer(bitmap);for(int y=0;y<height;y++)Marshal.Copy(source+y*stride,bytes,y*width*4,width*4);return bytes;}finally{Native.FPDFBitmap_Destroy(bitmap);}
    }
}

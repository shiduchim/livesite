using System.Reflection;
using System.Runtime.InteropServices;
namespace Offline3D.Automatic;
internal static class Native
{
    const string Dll="offline3d_pdfium";
    static Native(){NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,Resolve);}
    private static IntPtr Resolve(string name,Assembly assembly,DllImportSearchPath? path){
        if(name!=Dll)return IntPtr.Zero;string file=OperatingSystem.IsWindows()?"pdfium.dll":"libpdfium.so";
        foreach(string p in new[]{Path.Combine(AppContext.BaseDirectory,file),Path.Combine(AppContext.BaseDirectory,"runtimes",OperatingSystem.IsWindows()?"win-x64":"linux-x64","native",file)})
            if(File.Exists(p))return NativeLibrary.Load(p);
        throw new DllNotFoundException("Packaged PDFium dependency missing: "+file);
    }
    [StructLayout(LayoutKind.Sequential)]internal struct Matrix {public float A,B,C,D,E,F;public Matrix4 ToMatrix()=>new(A,C,0,E,B,D,0,F,0,0,1,0,0,0,0,1);}
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void FPDF_InitLibrary();
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void FPDF_DestroyLibrary();
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)]string file,IntPtr password);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr FPDF_LoadPage(IntPtr doc,int index);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPage_CountObjects(IntPtr page);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr FPDFPage_GetObject(IntPtr page,int index);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPageObj_GetType(IntPtr obj);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPageObj_GetMatrix(IntPtr obj,out Matrix matrix);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFFormObj_CountObjects(IntPtr form);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr FPDFFormObj_GetObject(IntPtr form,uint index);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFFormObj_RemoveObject(IntPtr form,IntPtr obj);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPage_RemoveObject(IntPtr page,IntPtr obj);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void FPDFPageObj_Destroy(IntPtr obj);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPage_GenerateContent(IntPtr page);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPageObj_GetStrokeColor(IntPtr obj,out uint r,out uint g,out uint b,out uint a);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPath_CountSegments(IntPtr obj);
    /// <summary>fillMode: 0 none, 1 alternate, 2 winding; stroke: nonzero when the path is stroked.</summary>
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPath_GetDrawMode(IntPtr path,out int fillMode,out int stroke);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr FPDFPath_GetPathSegment(IntPtr obj,int index);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFPathSegment_GetPoint(IntPtr segment,out float x,out float y);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr FPDFBitmap_Create(int width,int height,int alpha);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFBitmap_FillRect(IntPtr bitmap,int left,int top,int width,int height,uint color);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap,IntPtr page,int x,int y,int w,int h,int rotate,int flags);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int FPDFBitmap_GetStride(IntPtr bitmap);
}

using System.Reflection;
using System.Runtime.InteropServices;

namespace EKFastPDF.Core.Native;

/// <summary>
/// Raw PDFium entry points. PDFium is NOT thread-safe: every call in a process must be
/// serialized. EK FastPDF therefore only calls PDFium from the single job thread of a
/// render worker process (see <see cref="WorkerHost"/>); the UI process never loads PDFium.
/// </summary>
internal static unsafe class Pdfium
{
    private const string Dll = "pdfium";

    public const int FPDF_ANNOT = 0x01;
    public const int FPDF_LCD_TEXT = 0x02;
    public const int FPDF_PRINTING = 0x800;

    public const int FPDF_RENDER_TOBECONTINUED = 1;
    public const int FPDF_RENDER_DONE = 2;
    public const int FPDF_RENDER_FAILED = 3;

    public const int FPDFBitmap_BGRx = 3;

    public const uint FPDF_ERR_FILE = 2;
    public const uint FPDF_ERR_FORMAT = 3;
    public const uint FPDF_ERR_PASSWORD = 4;
    public const uint FPDF_ERR_SECURITY = 5;

    public const uint FPDF_MATCHCASE = 0x1;
    public const uint FPDF_MATCHWHOLEWORD = 0x2;

    private static int _resolverInstalled;

    /// <summary>Loads pdfium from the application directory explicitly, independent of the
    /// current working directory or PATH (launch from Explorer, shortcuts and file associations
    /// all use different working directories).</summary>
    public static void InstallResolver()
    {
        if (Interlocked.Exchange(ref _resolverInstalled, 1) != 0) return;
        NativeLibrary.SetDllImportResolver(typeof(Pdfium).Assembly, Resolve);
    }

    public static string? LoadedFrom { get; private set; }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Dll) return IntPtr.Zero;
        string file = OperatingSystem.IsWindows() ? "pdfium.dll" : OperatingSystem.IsMacOS() ? "libpdfium.dylib" : "libpdfium.so";
        string rid = (OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsMacOS() ? "osx-" : "linux-")
                     + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, file),
                     Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", file),
                 })
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                LoadedFrom = candidate;
                return handle;
            }
        }
        // Fall back to the runtime's normal probing (deps.json native search directories).
        if (NativeLibrary.TryLoad(name, assembly, path, out var fallback))
        {
            LoadedFrom = name + " (runtime probing)";
            return fallback;
        }
        throw new DllNotFoundException($"PDFium ({file}) was not found in {AppContext.BaseDirectory}. Reinstall EK FastPDF.");
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IFSDK_PAUSE
    {
        public int version;
        public delegate* unmanaged[Cdecl]<IFSDK_PAUSE*, int> NeedToPauseNow;
        public void* user;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDF_InitLibrary();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDF_DestroyLibrary();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr FPDF_LoadDocument(byte* utf8Path, byte* password);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDF_CloseDocument(IntPtr document);

    /// <summary>FPDF_FILEACCESS: PDFium reads the file through <see cref="GetBlock"/>.
    /// "unsigned long" is 32-bit on Windows and 64-bit on Linux, hence CULong.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FPDF_FILEACCESS
    {
        public CULong FileLen;
        public delegate* unmanaged[Cdecl]<IntPtr, CULong, byte*, CULong, int> GetBlock;
        public IntPtr Param;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr FPDF_LoadCustomDocument(FPDF_FILEACCESS* access, byte* password);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint FPDF_GetLastError();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDF_GetPageSizeByIndexF(IntPtr document, int index, out FsSizeF size);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDF_DeviceToPage(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int deviceX, int deviceY, out double pageX, out double pageY);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, double pageX, double pageY, out int deviceX, out int deviceY);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, void* firstScan, int stride);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDF_RenderPageBitmap_Start(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags, IFSDK_PAUSE* pause);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDF_RenderPage_Continue(IntPtr page, IFSDK_PAUSE* pause);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDF_RenderPage_Close(IntPtr page);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr FPDFText_FindStart(IntPtr textPage, char* term, uint flags, int startIndex);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDFText_FindNext(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDFText_FindClose(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDFText_GetSchResultIndex(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDFText_GetSchCount(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDFText_CountRects(IntPtr textPage, int start, int count);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int FPDFText_GetRect(IntPtr textPage, int rectIndex, out double left, out double top, out double right, out double bottom);

    // Windows-only (GDI printing). Present in the Windows pdfium.dll; never called elsewhere.
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void FPDF_RenderPage(IntPtr hdc, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct FsSizeF
    {
        public float Width;
        public float Height;
    }
}

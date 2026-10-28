using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Ipc;

namespace EKFastPDF.Core.Native;

/// <summary>
/// Vector printing through GDI: PDFium draws each page straight into the printer device context,
/// so line work prints at the printer's full resolution instead of as a screen-resolution bitmap.
/// Runs inside a dedicated worker process so a slow or misbehaving printer driver never blocks
/// the viewer.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOCINFOW
    {
        public int cbSize;
        public string lpszDocName;
        public string? lpszOutput;
        public string? lpszDatatype;
        public uint fwType;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDCW(string driver, string device, string? output, byte* devMode);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int StartDocW(IntPtr hdc, ref DOCINFOW info);
    [DllImport("gdi32.dll")] private static extern int EndDoc(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int AbortDoc(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int StartPage(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int EndPage(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);

    private const int HORZRES = 8, VERTRES = 10;

    public delegate IntPtr PageOpener(int docId, int pageIndex, out double parseMs);

    public static void Print(MessageReader request, MessageWriter reply, Func<bool> canceled, PageOpener openPage)
    {
        int docId = request.I32();
        string printer = request.Str() ?? throw new ArgumentException("Printer name missing.");
        byte[]? devMode = request.Bytes();
        string docName = request.Str() ?? "EK FastPDF";
        string? outputFile = request.Str();
        int count = request.I32();
        var pages = new int[count];
        for (int i = 0; i < count; i++) pages[i] = request.I32();

        IntPtr hdc;
        fixed (byte* dm = devMode)
            hdc = CreateDCW("WINSPOOL", printer, null, dm);
        if (hdc == IntPtr.Zero) throw new InvalidOperationException($"Could not open printer \"{printer}\" (Win32 error {Marshal.GetLastWin32Error()}).");
        int printed = 0;
        try
        {
            var info = new DOCINFOW { cbSize = Marshal.SizeOf<DOCINFOW>(), lpszDocName = docName, lpszOutput = outputFile };
            if (StartDocW(hdc, ref info) <= 0) throw new InvalidOperationException($"The printer refused the job (Win32 error {Marshal.GetLastWin32Error()}).");
            bool aborted = false;
            foreach (int pageIndex in pages)
            {
                if (canceled()) { AbortDoc(hdc); aborted = true; break; }
                IntPtr page = openPage(docId, pageIndex, out _);
                double pw = Pdfium.FPDF_GetPageWidthF(page), ph = Pdfium.FPDF_GetPageHeightF(page);
                int areaW = GetDeviceCaps(hdc, HORZRES), areaH = GetDeviceCaps(hdc, VERTRES);
                // Turn landscape sheets on portrait paper (and vice versa) to use the whole sheet.
                bool rotate = (pw > ph) != (areaW > areaH) && Math.Abs(pw - ph) > 1;
                double contentW = rotate ? ph : pw, contentH = rotate ? pw : ph;
                double scale = Math.Min(areaW / contentW, areaH / contentH);
                int w = Math.Max(1, (int)Math.Floor(contentW * scale)), h = Math.Max(1, (int)Math.Floor(contentH * scale));
                int x = (areaW - w) / 2, y = (areaH - h) / 2;
                if (StartPage(hdc) <= 0) throw new InvalidOperationException("The printer refused a page.");
                Pdfium.FPDF_RenderPage(hdc, page, x, y, w, h, rotate ? 1 : 0, Pdfium.FPDF_ANNOT | Pdfium.FPDF_PRINTING);
                if (EndPage(hdc) <= 0) throw new InvalidOperationException("The printer failed while finishing a page.");
                printed++;
            }
            if (aborted)
            {
                reply.U8((byte)WorkerStatus.Canceled).Str(null).I32(printed);
                return;
            }
            if (EndDoc(hdc) <= 0) throw new InvalidOperationException("The printer failed to complete the job.");
            Log.Info($"Printed {printed} page(s) to {printer}");
            reply.U8((byte)WorkerStatus.Ok).Str(null).I32(printed);
        }
        finally
        {
            DeleteDC(hdc);
        }
    }
}

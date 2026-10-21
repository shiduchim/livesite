using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EKFastPDF;

/// <summary>
/// PDFium is not safe for simultaneous use from unrelated threads. All calls, including
/// callbacks from PDFium into our random-access file reader, are serialized on Gate.
/// Documents are kept on disk and read on demand; we never read the entire PDF into RAM.
/// </summary>
internal static class PdfEngine
{
    private const string Dll = "pdfium";
    private static readonly object Gate = new();
    private static bool _initialized;
    private static readonly ReadBlockDelegate ReadBlockCallback = ReadBlock;
    private static readonly IntPtr ReadBlockPtr = Marshal.GetFunctionPointerForDelegate(ReadBlockCallback);

    [StructLayout(LayoutKind.Sequential)]
    private struct PdfFileAccess
    {
        public uint Length;
        public IntPtr GetBlock;
        public IntPtr Context;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadBlockDelegate(IntPtr context, uint offset, IntPtr output, uint size);

    // Native PDFium is published as a win-x64 NuGet runtime asset.
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_InitLibrary();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_DestroyLibrary();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDF_LoadCustomDocument(IntPtr fileAccess, IntPtr password);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint FPDF_GetLastError();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern double FPDF_GetPageWidth(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern double FPDF_GetPageHeight(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int pagePixelsWide, int pagePixelsHigh, int rotate, int flags);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFText_FindStart(IntPtr textPage, IntPtr unicodeTerm, uint flags, int startIndex);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFText_FindNext(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFText_FindClose(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFText_GetSchResultIndex(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFText_GetSchCount(IntPtr find);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFText_CountRects(IntPtr textPage, int start, int count);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFText_GetRect(IntPtr textPage, int rectIndex, out double left, out double top, out double right, out double bottom);

    public static PdfDocument Open(string filePath)
    {
        var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        if (file.Length is <= 0 or > uint.MaxValue)
        {
            file.Dispose();
            throw new NotSupportedException("PDF files must be between 1 byte and 4 GB in this version.");
        }

        GCHandle fileHandle = default;
        IntPtr accessPtr = IntPtr.Zero;
        try
        {
            fileHandle = GCHandle.Alloc(file);
            var access = new PdfFileAccess
            {
                Length = (uint)file.Length,
                GetBlock = ReadBlockPtr,
                Context = GCHandle.ToIntPtr(fileHandle)
            };
            accessPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PdfFileAccess>());
            Marshal.StructureToPtr(access, accessPtr, false);
            lock (Gate)
            {
                EnsureInitialized();
                var nativeDoc = FPDF_LoadCustomDocument(accessPtr, IntPtr.Zero);
                if (nativeDoc == IntPtr.Zero)
                {
                    uint error = FPDF_GetLastError();
                    throw new InvalidDataException(error == 4
                        ? "This PDF requires a password. Password support is planned for a later version."
                        : $"PDFium could not open this PDF (error {error}).");
                }
                int count = FPDF_GetPageCount(nativeDoc);
                if (count < 1)
                {
                    FPDF_CloseDocument(nativeDoc);
                    throw new InvalidDataException("This PDF contains no readable pages.");
                }
                return new PdfDocument(filePath, file, fileHandle, accessPtr, nativeDoc, count);
            }
        }
        catch
        {
            if (accessPtr != IntPtr.Zero) Marshal.FreeHGlobal(accessPtr);
            if (fileHandle.IsAllocated) fileHandle.Free();
            file.Dispose();
            throw;
        }
    }

    private static unsafe int ReadBlock(IntPtr context, uint position, IntPtr dest, uint size)
    {
        try
        {
            var stream = (FileStream)GCHandle.FromIntPtr(context).Target!;
            if (size > int.MaxValue || (long)position + size > stream.Length) return 0;
            var span = new Span<byte>(dest.ToPointer(), (int)size);
            int n = 0;
            while (n < span.Length)
            {
                int read = RandomAccess.Read(stream.SafeFileHandle, span[n..], (long)position + n);
                if (read == 0) return 0;
                n += read;
            }
            return 1;
        }
        catch { return 0; }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        FPDF_InitLibrary();
        _initialized = true;
    }

    internal static (double width, double height) PageSize(PdfDocument document, int index)
    {
        lock (Gate)
        {
            document.ThrowIfDisposed();
            var page = FPDF_LoadPage(document.Handle, index);
            if (page == IntPtr.Zero) throw new InvalidDataException("Cannot load PDF page.");
            try { return (FPDF_GetPageWidth(page), FPDF_GetPageHeight(page)); }
            finally { FPDF_ClosePage(page); }
        }
    }

    internal static RenderedTile Render(PdfDocument document, int pageNumber, int fullWidth, int fullHeight,
        int tileX, int tileY, int tileWidth, int tileHeight, CancellationToken token)
    {
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            document.ThrowIfDisposed();
            var page = FPDF_LoadPage(document.Handle, pageNumber);
            if (page == IntPtr.Zero) throw new InvalidDataException("Cannot load PDF page.");
            try
            {
                var bitmap = FPDFBitmap_Create(tileWidth, tileHeight, 1);
                if (bitmap == IntPtr.Zero) throw new OutOfMemoryException("Unable to allocate PDF tile.");
                try
                {
                    FPDFBitmap_FillRect(bitmap, 0, 0, tileWidth, tileHeight, 0xFFFFFFFF);
                    // Negative origins select just the visible region, without creating a huge bitmap.
                    // FPDF_ANNOT (0x01) and FPDF_LCD_TEXT (0x02): better subpixel text antialiasing.
                    FPDF_RenderPageBitmap(bitmap, page, -tileX, -tileY, fullWidth, fullHeight, 0, 0x01 | 0x02);
                    int stride = FPDFBitmap_GetStride(bitmap);
                    var buffer = new byte[checked(stride * tileHeight)];
                    Marshal.Copy(FPDFBitmap_GetBuffer(bitmap), buffer, 0, buffer.Length);
                    return new RenderedTile(tileWidth, tileHeight, stride, buffer);
                }
                finally { FPDFBitmap_Destroy(bitmap); }
            }
            finally { FPDF_ClosePage(page); }
        }
    }

    internal static List<PdfHit> SearchPage(PdfDocument document, int pageIndex, string term, int maxHits, CancellationToken token)
    {
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            document.ThrowIfDisposed();
            var results = new List<PdfHit>();
            var page = FPDF_LoadPage(document.Handle, pageIndex);
            if (page == IntPtr.Zero) return results;
            try
            {
                var textPage = FPDFText_LoadPage(page);
                if (textPage == IntPtr.Zero) return results;
                try
                {
                    IntPtr word = Marshal.StringToHGlobalUni(term);
                    try
                    {
                        var find = FPDFText_FindStart(textPage, word, 0, 0);
                        if (find == IntPtr.Zero) return results;
                        try
                        {
                            while (results.Count < maxHits && FPDFText_FindNext(find) != 0)
                            {
                                token.ThrowIfCancellationRequested();
                                int start = FPDFText_GetSchResultIndex(find);
                                int count = FPDFText_GetSchCount(find);
                                int rectCount = FPDFText_CountRects(textPage, start, count);
                                var rectangles = new List<PdfRect>();
                                for (int r = 0; r < Math.Min(rectCount, 24); r++)
                                {
                                    if (FPDFText_GetRect(textPage, r, out double l, out double t, out double right, out double b) != 0)
                                        rectangles.Add(new PdfRect(l, t, right, b));
                                }
                                results.Add(new PdfHit(pageIndex, rectangles));
                            }
                        }
                        finally { FPDFText_FindClose(find); }
                    }
                    finally { Marshal.FreeHGlobal(word); }
                }
                finally { FPDFText_ClosePage(textPage); }
            }
            finally { FPDF_ClosePage(page); }
            return results;
        }
    }

    internal static void Close(PdfDocument doc)
    {
        lock (Gate)
        {
            if (doc.Handle != IntPtr.Zero) FPDF_CloseDocument(doc.Handle);
            if (doc.NativeAccess != IntPtr.Zero) Marshal.FreeHGlobal(doc.NativeAccess);
            if (doc.FileHandle.IsAllocated) doc.FileHandle.Free();
            doc.Stream.Dispose();
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (!_initialized) return;
            FPDF_DestroyLibrary();
            _initialized = false;
        }
    }
}

internal sealed class PdfDocument : IDisposable
{
    public string FilePath { get; }
    public int PageCount { get; }
    internal FileStream Stream { get; }
    internal GCHandle FileHandle { get; }
    internal IntPtr NativeAccess { get; }
    internal IntPtr Handle { get; }
    private int _disposed;

    internal PdfDocument(string filePath, FileStream stream, GCHandle fileHandle, IntPtr nativeAccess, IntPtr handle, int pageCount)
    {
        FilePath = filePath; Stream = stream; FileHandle = fileHandle;
        NativeAccess = nativeAccess; Handle = handle; PageCount = pageCount;
    }
    public (double width, double height) GetPageSize(int page) => PdfEngine.PageSize(this, page);
    public RenderedTile Render(int page, int width, int height, int x, int y, int tw, int th, CancellationToken token)
        => PdfEngine.Render(this, page, width, height, x, y, tw, th, token);
    public List<PdfHit> Search(int page, string text, int maxHits, CancellationToken token)
        => PdfEngine.SearchPage(this, page, text, maxHits, token);
    internal void ThrowIfDisposed() { if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(PdfDocument)); }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) PdfEngine.Close(this); }
}

internal record RenderedTile(int Width, int Height, int Stride, byte[] Bytes);
internal record PdfRect(double Left, double Top, double Right, double Bottom);
internal record PdfHit(int Page, List<PdfRect> Rectangles);

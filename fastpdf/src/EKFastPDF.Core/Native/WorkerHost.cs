using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Ipc;

namespace EKFastPDF.Core.Native;

/// <summary>
/// Entry point of a render worker process ("EKFastPDF.exe --render-worker ...").
/// One thread, one PDFium instance: this is the only threading model PDFium documents as safe.
/// Parallelism comes from running several workers, each with its own PDFium and its own parsed
/// page objects, so tiles of the same page render simultaneously on different CPU cores.
/// </summary>
public static unsafe class WorkerHost
{
    public const string Argument = "--render-worker";

    private sealed class WorkerDocument
    {
        public IntPtr Handle;
        public readonly List<(int Index, IntPtr Page)> Pages = new(); // most recent last
    }

    private static readonly Dictionary<int, WorkerDocument> Documents = new();
    private static SharedBuffer? _shared;
    private static int _maxLoadedPages = 6;

    // Read by the PDFium pause callback (called on the same thread mid-render).
    private static long _currentRequest;
    private static long* _cancelSlot;

    public static bool IsWorkerInvocation(string[] args) => args.Length > 0 && args[0] == Argument;

    /// <summary>Runs the worker loop. Returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        string? shmName = null;
        long shmSize = 0;
        int index = 0;
        for (int i = 1; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--shm": shmName = args[++i]; break;
                case "--shm-size": shmSize = long.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--index": index = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--max-pages": _maxLoadedPages = Math.Max(1, int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture)); break;
            }
        }

        var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
        Log.Source = "worker " + index;
        Log.Sink = line => { try { stderr.WriteLine(line); } catch (IOException) { } };

        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        var reply = new MessageWriter();

        // Handshake: protocol version + whether PDFium could be loaded.
        string? startupError = null;
        try
        {
            if (shmName is null || shmSize <= Protocol.SharedHeaderBytes) throw new ArgumentException("Missing shared buffer arguments.");
            _shared = SharedBuffer.Open(shmName, shmSize);
            _cancelSlot = _shared.CancelSlot;
            Pdfium.InstallResolver();
            Pdfium.FPDF_InitLibrary();
            if (OperatingSystem.IsWindows()) WindowsPerformance.OptOutOfPowerThrottling();
        }
        catch (Exception e)
        {
            startupError = e.GetType().Name + ": " + e.Message;
            Log.Error("Worker startup failed", e);
        }

        reply.Reset().I32(Protocol.Magic).I32(Protocol.Version).I32(Environment.ProcessId).Str(startupError).Str(Pdfium.LoadedFrom);
        Protocol.WriteFrame(output, reply.Span);
        if (startupError != null) return 2;

        var pause = (Pdfium.IFSDK_PAUSE*)NativeMemory.AllocZeroed((nuint)sizeof(Pdfium.IFSDK_PAUSE));
        pause->version = 1;
        pause->NeedToPauseNow = &NeedToPauseNow;

        try
        {
            while (true)
            {
                byte[]? frame = Protocol.ReadFrame(input);
                if (frame is null) break; // viewer closed the pipe (exit or crash): leave quietly
                var request = new MessageReader(frame);
                var op = (Op)request.U8();
                long id = request.I64();
                reply.Reset().I64(id);
                if (op == Op.Shutdown) break;
                try
                {
                    switch (op)
                    {
                        case Op.OpenDocument: OpenDocument(request, reply); break;
                        case Op.CloseDocument: CloseDocument(request.I32()); reply.U8((byte)WorkerStatus.Ok).Str(null); break;
                        case Op.RenderRegion: RenderRegion(id, request, reply, pause); break;
                        case Op.SearchPage: SearchPage(id, request, reply); break;
                        case Op.PrintDocument:
                            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Printing requires Windows.");
                            WindowsPrinter.Print(request, reply, () => Volatile.Read(ref *_cancelSlot) == id, OpenPage);
                            break;
                        default: throw new InvalidDataException("Unknown request " + op);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Request {op} failed", e);
                    reply.Reset().I64(id).U8((byte)WorkerStatus.Error).Str(e.Message);
                }
                Protocol.WriteFrame(output, reply.Span);
            }
        }
        finally
        {
            foreach (var doc in Documents.Values) CloseNative(doc);
            Documents.Clear();
            NativeMemory.Free(pause);
            try { Pdfium.FPDF_DestroyLibrary(); } catch (DllNotFoundException) { }
            _shared?.Dispose();
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int NeedToPauseNow(Pdfium.IFSDK_PAUSE* self) =>
        Volatile.Read(ref *_cancelSlot) == _currentRequest ? 1 : 0;

    private static void OpenDocument(MessageReader request, MessageWriter reply)
    {
        int docId = request.I32();
        string path = request.Str() ?? "";
        string? password = request.Str();
        bool wantInfo = request.Bool();

        if (!Documents.TryGetValue(docId, out var doc))
        {
            var sw = Stopwatch.StartNew();
            IntPtr handle = LoadDocument(path, password);
            if (handle == IntPtr.Zero)
            {
                uint error = Pdfium.FPDF_GetLastError();
                var status = error switch
                {
                    Pdfium.FPDF_ERR_PASSWORD => WorkerStatus.PasswordRequired,
                    Pdfium.FPDF_ERR_FILE => WorkerStatus.FileNotFound,
                    Pdfium.FPDF_ERR_FORMAT => WorkerStatus.Corrupt,
                    Pdfium.FPDF_ERR_SECURITY => WorkerStatus.UnsupportedSecurity,
                    _ => WorkerStatus.Error,
                };
                reply.U8((byte)status).Str($"PDFium error {error}");
                return;
            }
            doc = new WorkerDocument { Handle = handle };
            Documents[docId] = doc;
            Log.Debug($"Opened document {docId} in {sw.ElapsedMilliseconds} ms");
        }

        int pageCount = Pdfium.FPDF_GetPageCount(doc.Handle);
        reply.U8((byte)WorkerStatus.Ok).Str(null).I32(pageCount);
        if (!wantInfo) return;
        for (int i = 0; i < pageCount; i++)
        {
            // Reads page dictionaries only; page content is not parsed here.
            if (Pdfium.FPDF_GetPageSizeByIndexF(doc.Handle, i, out var size) == 0) size = new Pdfium.FsSizeF { Width = 612, Height = 792 };
            reply.F32(size.Width).F32(size.Height);
        }
    }

    private static IntPtr LoadDocument(string path, string? password)
    {
        // PDFium opens the file itself (64-bit offsets, so no 4 GB limit) and reads on demand.
        string native = path;
        if (OperatingSystem.IsWindows() && native.Length >= 240 && !native.StartsWith(@"\\?\", StringComparison.Ordinal))
            native = native.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + native[2..] : @"\\?\" + native;
        byte[] pathBytes = Encoding.UTF8.GetBytes(native + "\0");
        byte[]? passBytes = password is null ? null : Encoding.UTF8.GetBytes(password + "\0");
        fixed (byte* p = pathBytes)
        fixed (byte* pw = passBytes)
            return Pdfium.FPDF_LoadDocument(p, pw);
    }

    private static void CloseDocument(int docId)
    {
        if (Documents.Remove(docId, out var doc)) CloseNative(doc);
    }

    private static void CloseNative(WorkerDocument doc)
    {
        foreach (var (_, page) in doc.Pages) Pdfium.FPDF_ClosePage(page);
        doc.Pages.Clear();
        if (doc.Handle != IntPtr.Zero) Pdfium.FPDF_CloseDocument(doc.Handle);
        doc.Handle = IntPtr.Zero;
    }

    /// <summary>Returns a parsed page, keeping recently used pages loaded. Parsing a dense
    /// drawing's content stream can take seconds; v1.1 re-parsed the page for every tile.</summary>
    private static IntPtr OpenPage(int docId, int pageIndex, out double parseMs)
    {
        parseMs = 0;
        if (!Documents.TryGetValue(docId, out var doc)) throw new InvalidOperationException("Document is not open in this worker.");
        for (int i = 0; i < doc.Pages.Count; i++)
        {
            if (doc.Pages[i].Index != pageIndex) continue;
            var hit = doc.Pages[i];
            doc.Pages.RemoveAt(i);
            doc.Pages.Add(hit);
            return hit.Page;
        }
        var sw = Stopwatch.StartNew();
        IntPtr page = Pdfium.FPDF_LoadPage(doc.Handle, pageIndex);
        parseMs = sw.Elapsed.TotalMilliseconds;
        if (page == IntPtr.Zero) throw new InvalidDataException($"Page {pageIndex + 1} could not be loaded.");
        doc.Pages.Add((pageIndex, page));
        TrimPages(doc);
        return page;
    }

    private static void TrimPages(WorkerDocument keep)
    {
        int total = Documents.Values.Sum(d => d.Pages.Count);
        while (total > _maxLoadedPages)
        {
            // Evict the oldest page of the document holding the most pages, never the one just used.
            var victim = Documents.Values.Where(d => d.Pages.Count > (d == keep ? 1 : 0)).MaxBy(d => d.Pages.Count);
            if (victim is null) break;
            Pdfium.FPDF_ClosePage(victim.Pages[0].Page);
            victim.Pages.RemoveAt(0);
            total--;
        }
    }

    private static void RenderRegion(long id, MessageReader request, MessageWriter reply, Pdfium.IFSDK_PAUSE* pause)
    {
        int docId = request.I32();
        int pageIndex = request.I32();
        int pageWidth = request.I32();
        int pageHeight = request.I32();
        int x = request.I32();
        int y = request.I32();
        int width = request.I32();
        int height = request.I32();
        int flags = request.I32();
        int margin = request.I32();

        // PDFium anti-aliases paths and positions glyphs slightly differently right at a bitmap
        // edge. Rendering a hidden margin around the requested window and returning only the
        // interior makes adjacent tiles pixel-identical to one large render (no seams).
        int ex0 = Math.Max(0, x - margin), ey0 = Math.Max(0, y - margin);
        int ex1 = Math.Min(pageWidth, x + width + margin), ey1 = Math.Min(pageHeight, y + height + margin);
        int offsetX = x - ex0, offsetY = y - ey0;
        x = ex0; y = ey0; width = ex1 - ex0; height = ey1 - ey0;

        long bytes = (long)width * height * 4;
        if (width <= 0 || height <= 0 || bytes > _shared!.PixelCapacity)
        {
            reply.U8((byte)WorkerStatus.TooLarge).Str($"Region {width}x{height} exceeds the shared buffer.");
            return;
        }

        _currentRequest = id;
        if (Volatile.Read(ref *_cancelSlot) == id)
        {
            reply.U8((byte)WorkerStatus.Canceled).Str(null);
            return;
        }

        IntPtr page = OpenPage(docId, pageIndex, out double parseMs);
        var sw = Stopwatch.StartNew();
        int stride = width * 4;
        IntPtr bitmap = Pdfium.FPDFBitmap_CreateEx(width, height, Pdfium.FPDFBitmap_BGRx, _shared.Pixels, stride);
        if (bitmap == IntPtr.Zero) throw new OutOfMemoryException("PDFium could not create the render bitmap.");
        bool canceled = false;
        try
        {
            Pdfium.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            // Negative origin: render only the requested window of the full-size page, without
            // allocating the full page bitmap. Progressive mode lets the viewer cancel mid-page.
            int status = Pdfium.FPDF_RenderPageBitmap_Start(bitmap, page, -x, -y, pageWidth, pageHeight, 0, flags, pause);
            while (status == Pdfium.FPDF_RENDER_TOBECONTINUED)
            {
                if (Volatile.Read(ref *_cancelSlot) == id) { canceled = true; break; }
                status = Pdfium.FPDF_RenderPage_Continue(page, pause);
            }
            Pdfium.FPDF_RenderPage_Close(page);
            if (!canceled && status == Pdfium.FPDF_RENDER_FAILED) throw new InvalidDataException("PDFium reported a render failure.");
        }
        finally
        {
            Pdfium.FPDFBitmap_Destroy(bitmap);
            _currentRequest = 0;
        }
        if (canceled)
        {
            reply.U8((byte)WorkerStatus.Canceled).Str(null);
            return;
        }
        reply.U8((byte)WorkerStatus.Ok).Str(null).I32(stride).I32(offsetX).I32(offsetY).F64(parseMs).F64(sw.Elapsed.TotalMilliseconds);
    }

    private static void SearchPage(long id, MessageReader request, MessageWriter reply)
    {
        int docId = request.I32();
        int pageIndex = request.I32();
        string term = request.Str() ?? "";
        uint flags = (uint)request.I32();
        int maxHits = request.I32();

        // Text extraction must not evict pages that rendering is using; load a private page.
        if (!Documents.TryGetValue(docId, out var doc)) throw new InvalidOperationException("Document is not open in this worker.");
        IntPtr page = IntPtr.Zero;
        bool ownsPage = true;
        foreach (var p in doc.Pages)
            if (p.Index == pageIndex) { page = p.Page; ownsPage = false; break; }
        if (page == IntPtr.Zero) page = Pdfium.FPDF_LoadPage(doc.Handle, pageIndex);
        if (page == IntPtr.Zero) throw new InvalidDataException($"Page {pageIndex + 1} could not be loaded.");
        try
        {
            IntPtr text = Pdfium.FPDFText_LoadPage(page);
            if (text == IntPtr.Zero)
            {
                reply.U8((byte)WorkerStatus.Ok).Str(null).I32(0).I32(0);
                return;
            }
            try
            {
                int chars = Pdfium.FPDFText_CountChars(text);
                // Display-space conversion (handles /Rotate and crop-box offsets) at 1/64 pt precision.
                const int Precision = 64;
                double pw = Pdfium.FPDF_GetPageWidthF(page), ph = Pdfium.FPDF_GetPageHeightF(page);
                int sx = Math.Max(1, (int)Math.Round(pw * Precision)), sy = Math.Max(1, (int)Math.Round(ph * Precision));
                var hits = new List<float[]>();
                if (chars > 0 && term.Length > 0)
                {
                    fixed (char* t = term + "\0")
                    {
                        IntPtr find = Pdfium.FPDFText_FindStart(text, t, flags, 0);
                        if (find != IntPtr.Zero)
                        {
                            try
                            {
                                while (hits.Count < maxHits && Pdfium.FPDFText_FindNext(find) != 0)
                                {
                                    if (Volatile.Read(ref *_cancelSlot) == id) break;
                                    int start = Pdfium.FPDFText_GetSchResultIndex(find);
                                    int count = Pdfium.FPDFText_GetSchCount(find);
                                    int rects = Math.Min(Pdfium.FPDFText_CountRects(text, start, count), 32);
                                    var list = new float[rects * 4];
                                    int n = 0;
                                    for (int r = 0; r < rects; r++)
                                    {
                                        if (Pdfium.FPDFText_GetRect(text, r, out double l, out double t2, out double rr, out double b) == 0) continue;
                                        Pdfium.FPDF_PageToDevice(page, 0, 0, sx, sy, 0, l, t2, out int x1, out int y1);
                                        Pdfium.FPDF_PageToDevice(page, 0, 0, sx, sy, 0, rr, b, out int x2, out int y2);
                                        list[n++] = Math.Min(x1, x2) / (float)Precision;
                                        list[n++] = Math.Min(y1, y2) / (float)Precision;
                                        list[n++] = Math.Max(x1, x2) / (float)Precision;
                                        list[n++] = Math.Max(y1, y2) / (float)Precision;
                                    }
                                    hits.Add(n == list.Length ? list : list[..n]);
                                }
                            }
                            finally { Pdfium.FPDFText_FindClose(find); }
                        }
                    }
                }
                reply.U8((byte)WorkerStatus.Ok).Str(null).I32(chars).I32(hits.Count);
                foreach (var h in hits)
                {
                    reply.I32(h.Length / 4);
                    foreach (var f in h) reply.F32(f);
                }
            }
            finally { Pdfium.FPDFText_ClosePage(text); }
        }
        finally
        {
            if (ownsPage) Pdfium.FPDF_ClosePage(page);
        }
    }
}

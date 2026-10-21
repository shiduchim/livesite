using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EKFastPDF.Core.Native;

namespace EKFastPDF.Bench;

/// <summary>
/// Faithful re-creation of the EK FastPDF v1.1 rendering path for "before" measurements:
/// one PDFium instance behind a global lock; for every 768 px tile (+2 px bleed) the page is
/// loaded (content stream parsed), rendered with FPDF_RenderPageBitmap, copied to a managed
/// array and closed again; tiles render one after another.
/// </summary>
internal static unsafe class LegacyEngine
{
    private static readonly object Gate = new();
    private static bool _init;

    public const int TileSize = 768;
    public const int TileBleed = 2;

    public static IntPtr Open(string path)
    {
        lock (Gate)
        {
            Pdfium.InstallResolver();
            if (!_init) { Pdfium.FPDF_InitLibrary(); _init = true; }
            var bytes = Encoding.UTF8.GetBytes(path + "\0");
            fixed (byte* p = bytes)
            {
                var doc = Pdfium.FPDF_LoadDocument(p, null);
                if (doc == IntPtr.Zero) throw new InvalidOperationException("open failed " + Pdfium.FPDF_GetLastError());
                return doc;
            }
        }
    }

    public static void Close(IntPtr doc)
    {
        lock (Gate) Pdfium.FPDF_CloseDocument(doc);
    }

    public static (double W, double H) PageSize(IntPtr doc, int index)
    {
        lock (Gate)
        {
            Pdfium.FPDF_GetPageSizeByIndexF(doc, index, out var s);
            return (s.Width, s.Height);
        }
    }

    /// <summary>v1.1 PdfEngine.Render: load page, render tile, copy, close page.</summary>
    public static byte[] RenderTile(IntPtr doc, int page, int fullW, int fullH, int x, int y, int w, int h)
    {
        lock (Gate)
        {
            var p = Pdfium.FPDF_LoadPage(doc, page);
            try
            {
                var buffer = new byte[w * h * 4];
                fixed (byte* b = buffer)
                {
                    var bmp = Pdfium.FPDFBitmap_CreateEx(w, h, 4, b, w * 4);
                    Pdfium.FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                    Pdfium.FPDF_RenderPageBitmap(bmp, p, -x, -y, fullW, fullH, 0, Pdfium.FPDF_ANNOT | Pdfium.FPDF_LCD_TEXT);
                    Pdfium.FPDFBitmap_Destroy(bmp);
                }
                return buffer;
            }
            finally { Pdfium.FPDF_ClosePage(p); }
        }
    }

    /// <summary>Renders every v1.1 tile intersecting the view rectangle (page pixels), centre-first,
    /// sequentially. Returns (time to first tile, time to all visible tiles, tile count).</summary>
    public static (double FirstMs, double AllMs, int Tiles) RenderView(IntPtr doc, int page, int fullW, int fullH,
        double viewX, double viewY, double viewW, double viewH)
    {
        var sw = Stopwatch.StartNew();
        int c0 = Math.Max(0, (int)(viewX / TileSize)), r0 = Math.Max(0, (int)(viewY / TileSize));
        int c1 = Math.Min((fullW - 1) / TileSize, (int)((viewX + viewW) / TileSize));
        int r1 = Math.Min((fullH - 1) / TileSize, (int)((viewY + viewH) / TileSize));
        double cx = viewX + viewW / 2, cy = viewY + viewH / 2;
        var tiles = new List<(int C, int R)>();
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) tiles.Add((c, r));
        tiles.Sort((a, b) => Dist(a).CompareTo(Dist(b)));
        double first = 0;
        foreach (var (c, r) in tiles)
        {
            int x = c * TileSize, y = r * TileSize;
            int w = Math.Min(TileSize + TileBleed, fullW - x), h = Math.Min(TileSize + TileBleed, fullH - y);
            RenderTile(doc, page, fullW, fullH, x, y, w, h);
            if (first == 0) first = sw.Elapsed.TotalMilliseconds;
        }
        return (first, sw.Elapsed.TotalMilliseconds, tiles.Count);

        double Dist((int C, int R) t) => Math.Pow((t.C + .5) * TileSize - cx, 2) + Math.Pow((t.R + .5) * TileSize - cy, 2);
    }
}

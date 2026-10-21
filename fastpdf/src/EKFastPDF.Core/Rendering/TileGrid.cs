namespace EKFastPDF.Core.Rendering;

/// <summary>Identifies one cached raster: a tile of a page at a resolution level.
/// <see cref="Level"/> is the scale (device px per point) in millionths, so the same zoom always
/// maps to the same key and to exactly the same pixel grid. Level 0 denotes the page overview.</summary>
public readonly record struct TileKey(int Doc, int Page, long Level, int Col, int Row);

public static class TileGrid
{
    /// <summary>Overlap rendered around each tile; drawn underneath neighbours so scaled
    /// previews never show hairline seams between tiles.</summary>
    public const int Bleed = 1;
    public const long OverviewLevel = 0;

    public static long LevelOf(double scale) => Math.Max(1, (long)Math.Round(scale * 1_000_000));
    public static double ScaleOf(long level) => level / 1_000_000.0;

    /// <summary>Full-page pixel size of a page rasterised at the level's scale.</summary>
    public static (int Width, int Height) PagePixels(double pageWidthPt, double pageHeightPt, long level)
    {
        double s = ScaleOf(level);
        return ((int)Math.Clamp(Math.Round(pageWidthPt * s), 1, int.MaxValue / 4),
                (int)Math.Clamp(Math.Round(pageHeightPt * s), 1, int.MaxValue / 4));
    }

    /// <summary>Core (non-overlapping) rectangle of a tile in page pixels.</summary>
    public static (int X, int Y, int W, int H) Core(int col, int row, int tileSize, int pageW, int pageH)
    {
        int x = col * tileSize, y = row * tileSize;
        return (x, y, Math.Min(tileSize, pageW - x), Math.Min(tileSize, pageH - y));
    }

    /// <summary>Rendered rectangle: the core plus <see cref="Bleed"/> pixels, clipped to the page.</summary>
    public static (int X, int Y, int W, int H) WithBleed(int col, int row, int tileSize, int pageW, int pageH)
    {
        var (x, y, w, h) = Core(col, row, tileSize, pageW, pageH);
        int x0 = Math.Max(0, x - Bleed), y0 = Math.Max(0, y - Bleed);
        int x1 = Math.Min(pageW, x + w + Bleed), y1 = Math.Min(pageH, y + h + Bleed);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Inclusive tile index range covering page-pixel rectangle [x0,x1) x [y0,y1).</summary>
    public static (int C0, int R0, int C1, int R1) Range(double x0, double y0, double x1, double y1, int tileSize, int pageW, int pageH)
    {
        int cols = (pageW + tileSize - 1) / tileSize, rows = (pageH + tileSize - 1) / tileSize;
        int c0 = Math.Clamp((int)Math.Floor(x0 / tileSize), 0, cols - 1);
        int r0 = Math.Clamp((int)Math.Floor(y0 / tileSize), 0, rows - 1);
        int c1 = Math.Clamp((int)Math.Floor((x1 - 1) / tileSize), 0, cols - 1);
        int r1 = Math.Clamp((int)Math.Floor((y1 - 1) / tileSize), 0, rows - 1);
        if (x1 <= 0 || y1 <= 0 || x0 >= pageW || y0 >= pageH) return (0, 0, -1, -1);
        return (c0, r0, c1, r1);
    }

    /// <summary>Overview resolution for a page: long edge about <paramref name="longEdgePx"/> px.</summary>
    public static double OverviewScale(double pageWidthPt, double pageHeightPt, int longEdgePx) =>
        longEdgePx / Math.Max(pageWidthPt, pageHeightPt);
}

/// <summary>PDFium render flags exposed to the viewer.</summary>
public static class RenderFlags
{
    public const int Annotations = Native.Pdfium.FPDF_ANNOT;
    public const int LcdText = Native.Pdfium.FPDF_LCD_TEXT;
}

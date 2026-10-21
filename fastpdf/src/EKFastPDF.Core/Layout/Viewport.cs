namespace EKFastPDF.Core.Layout;

public enum FitMode { None, Page, Width }

/// <summary>
/// View transform of one document tab: <c>screen = doc * Scale + Offset</c>, in device pixels
/// relative to the viewport's top-left corner. Offsets are kept on whole device pixels so that
/// tiles rendered at exactly <see cref="Scale"/> land 1:1 on the screen's pixel grid (no resampling
/// blur once the view settles).
/// </summary>
public sealed class Viewport
{
    public const double PointsToDips = 96.0 / 72.0;
    public const double MinZoom = 0.02;
    public const double MaxZoom = 64;
    /// <summary>Screen margin (device px) kept around the document when scrolled to an edge.</summary>
    public const double MarginDips = 24;

    public double Zoom { get; private set; } = 1;
    public double DpiScale { get; private set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public FitMode Fit { get; set; } = FitMode.Page;

    /// <summary>Device pixels per PDF point.</summary>
    public double Scale => Zoom * PointsToDips * DpiScale;

    public Viewport Clone() => (Viewport)MemberwiseClone();

    public void SetSize(int width, int height, double dpiScale)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        DpiScale = dpiScale > 0 ? dpiScale : 1;
    }

    public (double X, double Y) DocToScreen(double x, double y) => (x * Scale + OffsetX, y * Scale + OffsetY);
    public (double X, double Y) ScreenToDoc(double x, double y) => ((x - OffsetX) / Scale, (y - OffsetY) / Scale);

    public RectD VisibleDocRect
    {
        get
        {
            var (x, y) = ScreenToDoc(0, 0);
            return new RectD(x, y, Width / Scale, Height / Scale);
        }
    }

    /// <summary>Changes zoom keeping the document point under (screenX, screenY) fixed.</summary>
    public void ZoomAt(double zoom, double screenX, double screenY)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var (dx, dy) = ScreenToDoc(screenX, screenY);
        Zoom = zoom;
        OffsetX = screenX - dx * Scale;
        OffsetY = screenY - dy * Scale;
    }

    public void SetZoomCentered(double zoom) => ZoomAt(zoom, Width / 2.0, Height / 2.0);

    public void Pan(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
    }

    /// <summary>Keeps the document reachable: centred when smaller than the viewport, otherwise
    /// no further than a margin past its edges. Rounds offsets to whole device pixels.</summary>
    public void Clamp(DocumentLayout layout)
    {
        double margin = MarginDips * DpiScale;
        var b = layout.Bounds;
        double contentW = b.Width * Scale, contentH = b.Height * Scale;
        if (contentW + 2 * margin <= Width) OffsetX = (Width - contentW) / 2 - b.X * Scale;
        else OffsetX = Math.Clamp(OffsetX, Width - margin - b.Right * Scale, margin - b.X * Scale);
        if (contentH + 2 * margin <= Height) OffsetY = (Height - contentH) / 2 - b.Y * Scale;
        else OffsetY = Math.Clamp(OffsetY, Height - margin - b.Bottom * Scale, margin - b.Y * Scale);
        OffsetX = Math.Round(OffsetX);
        OffsetY = Math.Round(OffsetY);
    }

    public double FitPageZoom(DocumentLayout layout, int page)
    {
        double margin = 2 * MarginDips * DpiScale;
        var r = layout.PageRect(page);
        double sx = Math.Max(16, Width - margin) / (r.Width * PointsToDips * DpiScale);
        double sy = Math.Max(16, Height - margin) / (r.Height * PointsToDips * DpiScale);
        return Math.Clamp(Math.Min(sx, sy), MinZoom, MaxZoom);
    }

    public double FitWidthZoom(DocumentLayout layout)
    {
        double margin = 2 * MarginDips * DpiScale;
        return Math.Clamp(Math.Max(16, Width - margin) / (layout.MaxWidth * PointsToDips * DpiScale), MinZoom, MaxZoom);
    }

    /// <summary>Applies the fit mode for <paramref name="page"/> and shows that page.</summary>
    public void ApplyFit(DocumentLayout layout, int page)
    {
        if (Fit == FitMode.None) return;
        Zoom = Fit == FitMode.Page ? FitPageZoom(layout, page) : FitWidthZoom(layout);
        ShowPage(layout, page);
    }

    /// <summary>Scrolls so that the top of <paramref name="page"/> is at the top of the view
    /// (or the page is centred when it fits entirely).</summary>
    public void ShowPage(DocumentLayout layout, int page)
    {
        var r = layout.PageRect(page);
        double margin = MarginDips * DpiScale;
        OffsetX = Width / 2.0 - (r.X + r.Width / 2) * Scale;
        if (r.Height * Scale + 2 * margin <= Height) OffsetY = Height / 2.0 - (r.Y + r.Height / 2) * Scale;
        else OffsetY = margin - r.Y * Scale;
        Clamp(layout);
    }

    /// <summary>Centres a document-space point (search results, annotations).</summary>
    public void CenterOn(DocumentLayout layout, double docX, double docY)
    {
        OffsetX = Width / 2.0 - docX * Scale;
        OffsetY = Height / 2.0 - docY * Scale;
        Clamp(layout);
    }

    /// <summary>The page that occupies the viewport's centre line (falls back to nearest).</summary>
    public int CurrentPage(DocumentLayout layout)
    {
        var (_, y) = ScreenToDoc(Width / 2.0, Height / 2.0);
        int p = layout.PageAtY(y);
        // Inside the gap below page p, prefer the next page when it is closer.
        if (p + 1 < layout.PageCount && y > layout.PageRect(p).Bottom && layout.PageRect(p + 1).Y - y < y - layout.PageRect(p).Bottom) p++;
        return p;
    }
}

/// <summary>Discrete zoom ladder. Wheel and toolbar steps land on exact repeatable values
/// (2^(k/6), about 12% apart), so zooming back and forth hits the tile cache instead of
/// producing a new, never-reused raster resolution at every notch.</summary>
public static class ZoomLadder
{
    public const int StepsPerDoubling = 6;
    private static readonly double LogStep = Math.Log(2) / StepsPerDoubling;

    public static double Step(double zoom, int steps)
    {
        if (steps == 0) return zoom;
        double k = Math.Log(zoom) / LogStep;
        double nearest = Math.Round(k);
        // Values already on the ladder (within rounding of the stored zoom) count as that step.
        double baseK = Math.Abs(k - nearest) < 1e-3 ? nearest : steps > 0 ? Math.Floor(k) : Math.Ceiling(k);
        double next = Math.Exp((baseK + steps) * LogStep);
        return Math.Clamp(Math.Round(next, 6), Viewport.MinZoom, Viewport.MaxZoom);
    }
}

namespace EKFastPDF.Core.Layout;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public RectD Intersect(RectD other)
    {
        double x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
        double r = Math.Min(Right, other.Right), b = Math.Min(Bottom, other.Bottom);
        return r <= x || b <= y ? default : new RectD(x, y, r - x, b - y);
    }

    public bool IntersectsWith(RectD o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public RectD Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);
}

/// <summary>
/// Continuous vertical page layout in document space. Units are PDF points (1/72 inch); pages are
/// centred on x = 0 and stacked downward from y = 0 with a fixed gap. Annotation overlays and
/// search highlights use this same space, so everything stays aligned at any zoom.
/// </summary>
public sealed class DocumentLayout
{
    private readonly double[] _tops;

    public DocumentLayout(IReadOnlyList<float> widths, IReadOnlyList<float> heights, double gap = 14)
    {
        if (widths.Count != heights.Count || widths.Count == 0) throw new ArgumentException("Page size lists must be non-empty and equal length.");
        PageCount = widths.Count;
        Widths = new double[PageCount];
        Heights = new double[PageCount];
        _tops = new double[PageCount];
        Gap = gap;
        double y = 0;
        for (int i = 0; i < PageCount; i++)
        {
            Widths[i] = widths[i] > 0.5 ? widths[i] : 612;
            Heights[i] = heights[i] > 0.5 ? heights[i] : 792;
            _tops[i] = y;
            y += Heights[i] + gap;
            MaxWidth = Math.Max(MaxWidth, Widths[i]);
        }
        TotalHeight = y - gap;
    }

    public int PageCount { get; }
    public double[] Widths { get; }
    public double[] Heights { get; }
    public double Gap { get; }
    public double MaxWidth { get; }
    public double TotalHeight { get; }

    public RectD PageRect(int page) => new(-Widths[page] / 2, _tops[page], Widths[page], Heights[page]);

    public RectD Bounds => new(-MaxWidth / 2, 0, MaxWidth, TotalHeight);

    /// <summary>Index of the page at or nearest above document y (binary search).</summary>
    public int PageAtY(double y)
    {
        int lo = 0, hi = PageCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_tops[mid] <= y) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Pages whose rectangles intersect the document-space rectangle.</summary>
    public IEnumerable<int> PagesIn(RectD area)
    {
        if (area.IsEmpty) yield break;
        int first = PageAtY(area.Y);
        for (int i = first; i < PageCount && _tops[i] < area.Bottom; i++)
            if (PageRect(i).IntersectsWith(area)) yield return i;
    }
}

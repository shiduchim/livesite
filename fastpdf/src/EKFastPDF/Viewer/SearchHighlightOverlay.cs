using System.Windows;
using System.Windows.Media;

namespace EKFastPDF.Viewer;

/// <summary>Search highlights, drawn in page coordinates through <see cref="OverlayContext"/>.</summary>
public sealed class SearchHighlightOverlay : IViewportOverlay
{
    private static readonly Brush HitFill = Freeze(new SolidColorBrush(Color.FromArgb(110, 255, 196, 38)));
    private static readonly Brush CurrentFill = Freeze(new SolidColorBrush(Color.FromArgb(90, 60, 104, 181)));
    private static readonly Pen CurrentPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0x68, 0xB5))), 2));

    public void Render(DrawingContext dc, OverlayContext context)
    {
        var search = context.Session.Search;
        if (search.Count == 0) return;
        var current = search.Current;
        int drawn = 0;
        foreach (int page in context.VisiblePages)
        {
            foreach (var hit in search.HitsOnPage(page))
            {
                bool isCurrent = ReferenceEquals(hit, current);
                for (int i = 0; i + 3 < hit.Rects.Length; i += 4)
                {
                    var r = context.PageToScreen(page, hit.Rects[i], hit.Rects[i + 1], hit.Rects[i + 2], hit.Rects[i + 3]);
                    r.Inflate(1.5 * context.DpiScale, 1.5 * context.DpiScale);
                    if (isCurrent) dc.DrawRoundedRectangle(CurrentFill, CurrentPen, r, 2, 2);
                    else dc.DrawRectangle(HitFill, null, r);
                    if (++drawn > 4000) return;
                }
            }
        }
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

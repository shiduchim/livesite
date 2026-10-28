using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EKFastPDF.Core.Layout;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Services;

namespace EKFastPDF.Viewer;

/// <summary>Something drawn on top of the pages in page coordinates (search highlights today;
/// EK HDsnip markups later). Overlays never trigger PDF rendering.</summary>
public interface IViewportOverlay
{
    void Render(DrawingContext dc, OverlayContext context);
}
public interface IInteractiveViewportOverlay:IViewportOverlay
{
    bool TryBeginDrag(Point point,OverlayContext context);
    void DragTo(Point point,OverlayContext context);
    void EndDrag();
}

/// <summary>Maps page display points (top-left origin, page rotation applied: the space used by
/// search results and future annotations) to viewport device pixels for the current frame.</summary>
public sealed class OverlayContext
{
    internal OverlayContext(DocumentSession session, IReadOnlyList<int> visiblePages,Point? pointer=null)
    {
        Session = session;
        VisiblePages = visiblePages;
        Pointer=pointer;
    }

    public DocumentSession Session { get; }
    public IReadOnlyList<int> VisiblePages { get; }
    public Point? Pointer{get;}
    public double Scale => Session.View.Scale;
    public double DpiScale => Session.View.DpiScale;

    public Rect PageToScreen(int page, double left, double top, double right, double bottom)
    {
        var r = Session.Layout!.PageRect(page);
        var (x0, y0) = Session.View.DocToScreen(r.X + left, r.Y + top);
        var (x1, y1) = Session.View.DocToScreen(r.X + right, r.Y + bottom);
        return new Rect(new Point(x0, y0), new Point(x1, y1));
    }

    /// <summary>Inverse mapping for pointer input (future markup tools).</summary>
    public (int Page, double X, double Y)? ScreenToPage(double sx, double sy)
    {
        var (dx, dy) = Session.View.ScreenToDoc(sx, sy);
        var layout = Session.Layout!;
        int p = layout.PageAtY(dy);
        var r = layout.PageRect(p);
        if (dx < r.X || dx > r.Right || dy < r.Y || dy > r.Bottom) return null;
        return (p, dx - r.X, dy - r.Y);
    }
}

/// <summary>
/// The document surface. Presentation is fully retained-mode: every frame is a short list of
/// image draw commands referencing immutable bitmaps that WPF's composition engine keeps as GPU
/// (Direct3D) textures. Zooming and panning only change the coordinates of those commands, so the
/// GPU re-composites existing textures immediately while sharp tiles render in the background.
/// <list type="number">
/// <item>Instant feedback: on zoom, the page overview and any cached level are drawn scaled.</item>
/// <item>Sharp level: tiles rasterised at exactly the current device scale, placed 1:1 on the pixel grid.</item>
/// <item>Atomic reveal: new sharp tiles are held until the whole visible area is ready, then
/// cross-faded in together (no square-by-square assembly).</item>
/// </list>
/// </summary>
public sealed class PdfViewport : FrameworkElement
{
    public const int FadeMs = 110;
    public const int SettleMs = 70;
    public const int RevealHoldMs = 650;
    public const int RevealBatchMs = 180;

    private static readonly int[] OverviewEdges = { 256, 1024, 1600 };
    private static readonly int[] OverviewEdgesLargestFirst = { 1600, 1024, 256 };
    private static readonly Brush Backdrop = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xEB, 0xF3)));
    private static readonly Brush PageBrush = Brushes.White;
    private static readonly Brush ShadowBrush = Frozen(new SolidColorBrush(Color.FromArgb(28, 20, 40, 80)));
    private static readonly Pen PageEdge = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xDF))), 1));

    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _preview = new();
    private readonly DrawingVisual _sharp = new();
    private readonly DrawingVisual _overlay = new();
    private AppServices? _services;
    private DocumentSession? _session;
    private bool _dirty, _subscribed;
    private long _frame;
    private long _viewChangedAt = TileStore.Timestamp, _zoomChangedAt, _lastRevealAt;
    private Point? _cursor;
    private Point _panLast;
    private bool _panning;
    private Point _clickDown;
    private bool _clickMoved;
    private IInteractiveViewportOverlay? _overlayDrag;
    public event Action<int,double,double>? PageClicked;
    private double _panVx, _panVy;
    private double _wheelAccumulator;
    private bool _wasSharp;
    private long _inputAt;

    public PdfViewport()
    {
        _root.Children.Add(_preview);
        _root.Children.Add(_sharp);
        _root.Children.Add(_overlay);
        AddVisualChild(_root);
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.Linear);
        // Sharp tiles are drawn at exactly their pixel size on whole device pixels: no filtering.
        RenderOptions.SetBitmapScalingMode(_sharp, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_sharp, EdgeMode.Aliased);
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Overlays.Add(new SearchHighlightOverlay());
        IsVisibleChanged += (_, _) => Invalidate();
    }

    public List<IViewportOverlay> Overlays { get; } = new();
    public void CancelOverlayInteraction(){_overlayDrag?.EndDrag();_overlayDrag=null;if(IsMouseCaptured)ReleaseMouseCapture();Cursor=_idleCursor;}
    private Cursor? _idleCursor;
    /// <summary>Pointer shown while not panning or dragging (the blue ClickTo3D arrow); null for the default.</summary>
    public Cursor? IdleCursor{get=>_idleCursor;set{_idleCursor=value;if(!_panning&&_overlayDrag==null)Cursor=value;}}
    public ViewerMetrics Metrics { get; } = new();
    public bool ShowDebug { get; set; }

    /// <summary>Raised (at most once per frame) when zoom, page or sharpness changed.</summary>
    public event Action? ViewChanged;
    /// <summary>Raised when the visible area becomes completely sharp after a change.</summary>
    public event Action? BecameSharp;

    public DocumentSession? Session => _session;
    public bool IsSharp => _wasSharp;

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _root;

    public void Attach(AppServices services)
    {
        _services = services;
        services.Tiles.TilesArrived += keys =>
        {
            if (_session != null && keys.Any(k => k.Doc == _session.DocId)) Invalidate();
        };
        services.Tiles.TileFailed += (key, _) => { if (key.Doc == _session?.DocId) Invalidate(); };
    }

    public void SetSession(DocumentSession? session)
    {
        if (ReferenceEquals(session, _session)) { Invalidate(); return; }
        if (_session != null)
        {
            _session.Search.HighlightsChanged -= Invalidate;
            _session.Search.NavigateRequested -= ShowSearchHit;
            _session.IsActive = false;
            DeprioritizeBackground(_session);
        }
        _session = session;
        if (session != null)
        {
            session.IsActive = true;
            session.Search.HighlightsChanged += Invalidate;
            session.Search.NavigateRequested += ShowSearchHit;
        }
        _viewChangedAt = TileStore.Timestamp;
        _wasSharp = false;
        Metrics.MarkViewChange();
        Invalidate();
    }

    /// <summary>An inactive tab must not compete with the active one: withdraw its queued work
    /// (running jobs finish and stay cached for when the user comes back).</summary>
    private void DeprioritizeBackground(DocumentSession s)
    {
        if (_services is null) return;
        foreach (var (key, job) in _services.Tiles.PendingFor(s.DocId).ToList())
            if (!job.IsRunning) _services.Tiles.Cancel(key);
    }

    /// <summary>Marks the time of a (real or simulated) input event for latency measurement.</summary>
    public void NoteInput() => _inputAt = TileStore.Timestamp;

    public void Invalidate()
    {
        _dirty = true;
        if (!_subscribed)
        {
            _subscribed = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }
    /// <summary>Redraw live tools without requesting or recompositing PDF tiles.</summary>
    public void RefreshOverlays(){
        if(ShowDebug){Invalidate();return;}
        if(_session is not {IsReady:true,Layout:not null} session||!session.ViewInitialized)return;
        double dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
        using var drawing=_overlay.RenderOpen();drawing.PushTransform(new ScaleTransform(1/dpi,1/dpi));
        var context=new OverlayContext(session,session.Layout.PagesIn(session.View.VisibleDocRect).ToArray(),_cursor);
        foreach(var overlay in Overlays)overlay.Render(drawing,context);
    }

    private void ViewMoved(bool zoomed)
    {
        _viewChangedAt = TileStore.Timestamp;
        if (zoomed) _zoomChangedAt = _viewChangedAt;
        _wasSharp = false;
        Metrics.MarkViewChange();
        Invalidate();
    }

    // ---------------------------------------------------------------- view commands

    private bool Ready(out DocumentSession s, out DocumentLayout layout)
    {
        s = _session!;
        layout = s?.Layout!;
        return s is { IsReady: true, Layout: not null } && EnsureView(s);
    }

    private bool EnsureView(DocumentSession s)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX), h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        if (w < 8 || h < 8) return false;
        if (!s.ViewInitialized)
        {
            s.View.SetSize(w, h, dpi.DpiScaleX);
            s.View.Fit = FitMode.Page;
            s.View.ApplyFit(s.Layout!, 0);
            s.ViewInitialized = true;
            ViewMoved(zoomed: true);
        }
        else if (s.View.Width != w || s.View.Height != h || Math.Abs(s.View.DpiScale - dpi.DpiScaleX) > 1e-6)
        {
            var v = s.View;
            var (cx, cy) = v.ScreenToDoc(v.Width / 2.0, v.Height / 2.0);
            int page = v.CurrentPage(s.Layout!);
            v.SetSize(w, h, dpi.DpiScaleX);
            if (v.Fit != FitMode.None) v.ApplyFit(s.Layout!, page);
            else v.CenterOn(s.Layout!, cx, cy);
            ViewMoved(zoomed: true);
        }
        return true;
    }

    public void ZoomTo(double zoom, Point? deviceAnchor = null)
    {
        if (!Ready(out var s, out var layout)) return;
        var v = s.View;
        zoom = Math.Clamp(zoom, Viewport.MinZoom, Viewport.MaxZoom);
        if (Math.Abs(zoom - v.Zoom) < 1e-9) return;
        var a = deviceAnchor ?? new Point(v.Width / 2.0, v.Height / 2.0);
        v.ZoomAt(zoom, a.X, a.Y);
        v.Fit = FitMode.None;
        v.Clamp(layout);
        ViewMoved(zoomed: true);
    }

    public void ZoomStep(int steps, Point? deviceAnchor = null)
    {
        if (!Ready(out var s, out var layout)) return;
        // Walk the ladder one notch at a time, stopping at the fit-page zoom when it lies between two
        // notches: zooming back out returns exactly to the (cached) opening view.
        double zoom = s.View.Zoom;
        double fit = s.View.FitPageZoom(layout, s.View.CurrentPage(layout));
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            double next = ZoomLadder.Step(zoom, Math.Sign(steps));
            bool crossesFit = Math.Abs(zoom - fit) > 1e-6 && (fit - zoom) * (next - fit) > 0;
            zoom = crossesFit ? fit : next;
        }
        ZoomTo(zoom, deviceAnchor);
    }

    public void FitPage()
    {
        if (!Ready(out var s, out var layout)) return;
        s.View.Fit = FitMode.Page;
        s.View.ApplyFit(layout, s.View.CurrentPage(layout));
        ViewMoved(zoomed: true);
    }

    public void FitWidth()
    {
        if (!Ready(out var s, out var layout)) return;
        int page = s.View.CurrentPage(layout);
        s.View.Fit = FitMode.Width;
        s.View.ApplyFit(layout, page);
        ViewMoved(zoomed: true);
    }

    public void GoToPage(int page)
    {
        if (!Ready(out var s, out var layout)) return;
        page = Math.Clamp(page, 0, layout.PageCount - 1);
        if (s.View.Fit == FitMode.Page) s.View.ApplyFit(layout, page);
        else s.View.ShowPage(layout, page);
        ViewMoved(zoomed: false);
    }

    public void NextPage() { if (_session is { IsReady: true }) GoToPage(_session.View.CurrentPage(_session.Layout!) + 1); }
    public void PreviousPage() { if (_session is { IsReady: true }) GoToPage(_session.View.CurrentPage(_session.Layout!) - 1); }

    public void ScrollBy(double dxDips, double dyDips)
    {
        if (!Ready(out var s, out var layout)) return;
        s.View.Pan(-dxDips * s.View.DpiScale, -dyDips * s.View.DpiScale);
        s.View.Clamp(layout);
        ViewMoved(zoomed: false);
    }

    public void ShowSearchHit(SearchHit hit)
    {
        if (!Ready(out var s, out var layout) || hit.Rects.Length < 4) return;
        var r = layout.PageRect(hit.Page);
        double x0 = r.X + hit.Rects[0], y0 = r.Y + hit.Rects[1], x1 = r.X + hit.Rects[2], y1 = r.Y + hit.Rects[3];
        var visible = s.View.VisibleDocRect.Inflate(-40 / s.View.Scale, -40 / s.View.Scale);
        if (!(x0 >= visible.X && x1 <= visible.Right && y0 >= visible.Y && y1 <= visible.Bottom))
            s.View.CenterOn(layout, (x0 + x1) / 2, (y0 + y1) / 2);
        ViewMoved(zoomed: false);
    }

    // ---------------------------------------------------------------- input

    private Point DevicePoint(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(p.X * dpi.DpiScaleX, p.Y * dpi.DpiScaleY);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_session is not { IsReady: true }) return;
        _inputAt = TileStore.Timestamp;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool zoom = !ctrl;
        if (zoom)
        {
            // Accumulate high-resolution wheel/touchpad deltas; one notch = one ladder step.
            _wheelAccumulator += e.Delta / 120.0;
            int steps = (int)Math.Truncate(_wheelAccumulator);
            if (steps != 0)
            {
                _wheelAccumulator -= steps;
                ZoomStep(steps, DevicePoint(e));
            }
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ScrollBy(e.Delta * 0.8, 0);
        else ScrollBy(0, e.Delta * 0.8);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if(e.ChangedButton==MouseButton.Left&&Ready(out var overlaySession,out _)){
            var context=new OverlayContext(overlaySession,new[]{0});
            foreach(var overlay in Overlays.OfType<IInteractiveViewportOverlay>().Reverse())if(overlay.TryBeginDrag(DevicePoint(e),context)){_overlayDrag=overlay;CaptureMouse();Cursor=Cursors.SizeAll;e.Handled=true;return;}
        }
        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle && _session is { IsReady: true })
        {
            _panning = true;
            _panLast = DevicePoint(e);
            _clickDown = _panLast;
            _clickMoved = false;
            _panVx = _panVy = 0;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = DevicePoint(e);
        _cursor = p;
        if(_overlayDrag!=null){if(Ready(out var overlaySession,out _))_overlayDrag.DragTo(p,new OverlayContext(overlaySession,new[]{0}));RefreshOverlays();e.Handled=true;return;}
        if(PageClicked!=null)RefreshOverlays();
        if (PageClicked != null && _panning && (p-_clickDown).Length > 3*VisualTreeHelper.GetDpi(this).DpiScaleX) _clickMoved = true;
        if (!_panning || !Ready(out var s, out var layout)) return;
        _inputAt = TileStore.Timestamp;
        double dx = p.X - _panLast.X, dy = p.Y - _panLast.Y;
        _panLast = p;
        if (dx == 0 && dy == 0) return;
        _panVx = _panVx * 0.6 + dx * 0.4;
        _panVy = _panVy * 0.6 + dy * 0.4;
        s.View.Pan(dx, dy);
        s.View.Clamp(layout);
        ViewMoved(zoomed: false);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if(_overlayDrag!=null){CancelOverlayInteraction();RefreshOverlays();e.Handled=true;return;}
        if (!_panning) return;
        _panning = false;
        if (e.ChangedButton == MouseButton.Left && !_clickMoved && PageClicked != null && Ready(out var session,out var pageLayout))
        {
            var pointer=DevicePoint(e);
            var (dx,dy)=session.View.ScreenToDoc(pointer.X,pointer.Y);
            int page=pageLayout.PageAtY(dy);var rect=pageLayout.PageRect(page);
            if(dx>=rect.X && dx<=rect.Right && dy>=rect.Y && dy<=rect.Bottom)
                PageClicked.Invoke(page,(dx-rect.X)/rect.Width,(dy-rect.Y)/rect.Height);
        }
        ReleaseMouseCapture();
        Cursor = _idleCursor;
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _cursor = null;
        if(PageClicked!=null)RefreshOverlays();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Invalidate();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Invalidate();
    }

    // ---------------------------------------------------------------- frame

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_dirty)
        {
            CompositionTarget.Rendering -= OnRendering;
            _subscribed = false;
            return;
        }
        _dirty = false;
        long start = TileStore.Timestamp;
        try
        {
            BuildFrame();
        }
        finally
        {
            Metrics.RecordFrame(TileStore.MsSince(start), _inputAt);
            _inputAt = 0;
        }
    }

    private sealed class PageTiles
    {
        public int Page;
        public double X0, Y0, X1, Y1;   // page rectangle on screen (device px)
        public int Ox, Oy;              // whole-pixel origin of the sharp level
        public readonly List<TileBitmap> Ready = new();
        public readonly List<TileBitmap> Held = new();
        public bool HasOverview;
    }

    private void BuildFrame()
    {
        var store = _services?.Tiles;
        var s = _session;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int vw = Math.Max(1, (int)Math.Round(ActualWidth * dpi)), vh = Math.Max(1, (int)Math.Round(ActualHeight * dpi));
        var unscale = new ScaleTransform(1 / dpi, 1 / dpi);
        unscale.Freeze();

        if (store is null || s is null || !Ready(out s, out var layout) || !IsVisible)
        {
            using var empty = _preview.RenderOpen();
            empty.DrawRectangle(Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
            using (_sharp.RenderOpen()) { }
            using (_overlay.RenderOpen()) { }
            return;
        }

        var v = s.View;
        long now = TileStore.Timestamp;
        store.Frame = ++_frame;
        int tileSize = Math.Clamp(_services!.Settings.TileSize, 128, 2048);
        int flags = RenderFlags.Annotations | (_services.Settings.LcdText ? RenderFlags.LcdText : 0);
        double scale = v.Scale;
        long level = TileGrid.LevelOf(scale);
        bool zoomSettled = TileStore.MsSince(_zoomChangedAt) >= SettleMs;
        double sinceChange = TileStore.MsSince(_viewChangedAt);
        var focus = _cursor ?? new Point(v.Width / 2.0, v.Height / 2.0);
        var wanted = new HashSet<TileKey>();
        int missingVisible = 0;

        var visibleDoc = v.VisibleDocRect;
        var pages = layout.PagesIn(visibleDoc).ToList();
        var pageTiles = new List<PageTiles>(pages.Count);

        using var pre = _preview.RenderOpen();
        pre.PushTransform(unscale);
        pre.DrawRectangle(Backdrop, null, new Rect(0, 0, vw, vh));

        foreach (int page in pages)
        {
            var r = layout.PageRect(page);
            double wPt = layout.Widths[page], hPt = layout.Heights[page];
            var (sx0, sy0) = v.DocToScreen(r.X, r.Y);
            var (sx1, sy1) = v.DocToScreen(r.Right, r.Bottom);
            var pt = new PageTiles { Page = page, X0 = sx0, Y0 = sy0, X1 = sx1, Y1 = sy1, Ox = (int)Math.Round(sx0), Oy = (int)Math.Round(sy0) };
            pageTiles.Add(pt);
            var pageRect = new Rect(pt.Ox, pt.Oy, Math.Max(1, Math.Round(sx1) - pt.Ox), Math.Max(1, Math.Round(sy1) - pt.Oy));
            pre.DrawRectangle(ShadowBrush, null, new Rect(pageRect.X + 1, pageRect.Y + 2, pageRect.Width + 2, pageRect.Height + 2));
            pre.DrawRectangle(PageBrush, PageEdge, new Rect(pageRect.X - 0.5, pageRect.Y - 0.5, pageRect.Width + 1, pageRect.Height + 1));

            var (pw, ph) = TileGrid.PagePixels(wPt, hPt, level);
            bool smallPage = pw <= tileSize && ph <= tileSize;
            // 1) Exact-resolution tiles for the current zoom (drawn later, on the sharp layer).
            int pageMissing = 0;
            var (c0, r0, c1, r1) = TileGrid.Range(-pt.Ox, -pt.Oy, vw - pt.Ox, vh - pt.Oy, tileSize, pw, ph);
            for (int row = r0; row <= r1; row++)
            for (int col = c0; col <= c1; col++)
            {
                var key = new TileKey(s.DocId, page, level, col, row);
                if (store.TryGet(key, out var tile))
                {
                    (tile.RevealedAt != 0 ? pt.Ready : pt.Held).Add(tile);
                    continue;
                }
                if (store.HasFailed(key)) continue;
                missingVisible++;
                pageMissing++;
                wanted.Add(key);
                if (zoomSettled)
                {
                    var (x, y, w, h) = TileGrid.WithBleed(col, row, tileSize, pw, ph);
                    double cx = pt.Ox + x + w / 2.0 - focus.X, cy = pt.Oy + y + h / 2.0 - focus.Y;
                    store.Request(key, pw, ph, x, y, w, h, flags, 1000 + Math.Sqrt(cx * cx + cy * cy) / 10);
                }
            }

            // 2) Overview: whole page in one bitmap, the always-available fallback. Not drawn when
            //    sharp tiles already cover the visible part of the page completely.
            bool covered = pageMissing == 0 && pt.Held.Count == 0 && pt.Ready.Count > 0 &&
                           pt.Ready.All(t => (now - t.RevealedAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= FadeMs);
            double longEdgeOnScreen = Math.Max(sx1 - sx0, sy1 - sy0);
            int wantEdge = OverviewEdges.FirstOrDefault(e => e >= longEdgeOnScreen * 0.9, OverviewEdges[^1]);
            foreach (int edge in OverviewEdgesLargestFirst)
            {
                var ok = OverviewKey(s.DocId, page, wPt, hPt, edge, out _, out _);
                if (!store.Contains(ok)) continue;
                if (covered) { store.Touch(ok); pt.HasOverview = true; continue; } // keep it cached for the next zoom
                if (pt.HasOverview) continue;
                store.TryGet(ok, out var ov);
                pre.DrawImage(ov.Bitmap, pageRect);
                pt.HasOverview = true;
            }
            if (!smallPage)
            {
                var wantKey = OverviewKey(s.DocId, page, wPt, hPt, wantEdge, out int ow, out int oh);
                if (!store.Contains(wantKey))
                {
                    wanted.Add(wantKey);
                    double pageDistance = Math.Abs((sy0 + sy1) / 2 - focus.Y) / Math.Max(1, v.Height);
                    store.Request(wantKey, ow, oh, 0, 0, ow, oh, flags, pageDistance);
                }
            }

            // 3) Cached neighbouring resolution levels, coarse to fine, drawn scaled.
            if (!covered) DrawPreviewLevels(pre, store, s.DocId, page, level, wPt, hPt, pt, vw, vh);

            // 4) Overscan ring, extended in the direction the user is panning.
            if (zoomSettled && sinceChange > 0)
            {
                int ex0 = _panVx > 2 ? 2 : 1, ex1 = _panVx < -2 ? 2 : 1, ey0 = _panVy > 2 ? 2 : 1, ey1 = _panVy < -2 ? 2 : 1;
                int cols = (pw + tileSize - 1) / tileSize, rows = (ph + tileSize - 1) / tileSize;
                if (c1 >= c0 && r1 >= r0)
                {
                    for (int row = Math.Max(0, r0 - ey0); row <= Math.Min(rows - 1, r1 + ey1); row++)
                    for (int col = Math.Max(0, c0 - ex0); col <= Math.Min(cols - 1, c1 + ex1); col++)
                    {
                        if (row >= r0 && row <= r1 && col >= c0 && col <= c1) continue;
                        var key = new TileKey(s.DocId, page, level, col, row);
                        if (store.Contains(key)) { store.Touch(key); continue; }
                        if (store.HasFailed(key)) continue;
                        wanted.Add(key);
                        var (x, y, w, h) = TileGrid.WithBleed(col, row, tileSize, pw, ph);
                        double cx = pt.Ox + x + w / 2.0 - focus.X, cy = pt.Oy + y + h / 2.0 - focus.Y;
                        store.Request(key, pw, ph, x, y, w, h, flags, 100_000 + Math.Sqrt(cx * cx + cy * cy) / 10);
                    }
                }
            }
        }

        // Neighbouring pages: overviews ready before the user scrolls there.
        if (zoomSettled && pages.Count > 0)
        {
            foreach (int page in new[] { pages[0] - 1, pages[^1] + 1, pages[^1] + 2 })
            {
                if (page < 0 || page >= layout.PageCount) continue;
                double longEdge = Math.Max(layout.Widths[page], layout.Heights[page]) * scale;
                int edge = OverviewEdges.FirstOrDefault(e => e >= longEdge * 0.9, OverviewEdges[^1]);
                var key = OverviewKey(s.DocId, page, layout.Widths[page], layout.Heights[page], edge, out int ow, out int oh);
                if (store.Contains(key)) { store.Touch(key); continue; }
                wanted.Add(key);
                store.Request(key, ow, oh, 0, 0, ow, oh, flags, 300_000 + Math.Abs(page - pages[0]));
            }
        }

        // Reveal policy: hold new sharp tiles until every visible one is ready, so the whole view
        // sharpens at once. If that takes long (very complex page), release in batches.
        int held = pageTiles.Sum(p => p.Held.Count);
        bool reveal = held > 0 && (missingVisible == 0
            || (sinceChange > RevealHoldMs && TileStore.MsSince(_lastRevealAt) > RevealBatchMs));
        if (reveal)
        {
            foreach (var p in pageTiles) foreach (var t in p.Held) t.RevealedAt = now;
            foreach (var p in pageTiles) { p.Ready.AddRange(p.Held); p.Held.Clear(); }
            _lastRevealAt = now;
        }

        bool animating = false;
        using (var sharp = _sharp.RenderOpen())
        {
            sharp.PushTransform(unscale);
            foreach (var p in pageTiles)
            {
                foreach (var t in p.Ready)
                {
                    double age = (now - t.RevealedAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double opacity = Math.Clamp(age / FadeMs, 0, 1);
                    if (opacity < 1) { animating = true; sharp.PushOpacity(opacity); }
                    sharp.DrawImage(t.Bitmap, new Rect(p.Ox + t.X, p.Oy + t.Y, t.Width, t.Height));
                    if (opacity < 1) sharp.Pop();
                }
            }
        }

        CancelUnwanted(store, s.DocId, wanted, level);

        // Overlays (search highlights; future markup) and the optional debug readout.
        using (var ov = _overlay.RenderOpen())
        {
            ov.PushTransform(unscale);
            var ctx = new OverlayContext(s, pages,_cursor);
            foreach (var overlay in Overlays) overlay.Render(ov, ctx);
            if (ShowDebug) DrawDebug(ov, s, level, missingVisible, held, dpi);
        }

        int current = v.CurrentPage(layout);
        bool sharpNow = zoomSettled && missingVisible == 0 && held == 0 && !animating && pages.Count > 0;
        if (s.CurrentPage != current) s.CurrentPage = current;
        if (sharpNow && !_wasSharp)
        {
            _wasSharp = true;
            Metrics.MarkSharp(_frame);
            BecameSharp?.Invoke();
        }
        if (!sharpNow) _wasSharp = false;
        if (missingVisible > 0 && pages.Count > 0) Metrics.MarkFirstPixelIfNeeded(pageTiles.Any(p => p.HasOverview || p.Ready.Count > 0));
        else Metrics.MarkFirstPixelIfNeeded(pages.Count > 0);
        ViewChanged?.Invoke();

        // Keep frames coming while fading, while waiting for the zoom to settle, or while held
        // tiles wait for the batch timer.
        if (animating || !zoomSettled || held > 0 && !reveal) _dirty = true;
        store.Trim();
    }

    private static TileKey OverviewKey(int doc, int page, double wPt, double hPt, int edge, out int w, out int h)
    {
        long level = TileGrid.LevelOf(TileGrid.OverviewScale(wPt, hPt, edge));
        (w, h) = TileGrid.PagePixels(wPt, hPt, level);
        return new TileKey(doc, page, -level, 0, 0); // negative level: whole-page overview
    }

    private void DrawPreviewLevels(DrawingContext dc, TileStore store, int doc, int page, long level, double wPt, double hPt, PageTiles pt, int vw, int vh)
    {
        var levels = store.LevelsFor(doc, page).Where(l => l > 0 && l != level).ToList();
        if (levels.Count == 0) return;
        // Up to two coarser levels (nearest last) and one finer level not more than 4x denser.
        var order = levels.Where(l => l < level).OrderByDescending(l => l).Take(2).OrderBy(l => l)
            .Concat(levels.Where(l => l > level && l <= level * 4).OrderBy(l => l).Take(1));
        int tileSize = Math.Clamp(_services!.Settings.TileSize, 128, 2048);
        double screenW = pt.X1 - pt.X0, screenH = pt.Y1 - pt.Y0;
        foreach (long l in order)
        {
            var (pw, ph) = TileGrid.PagePixels(wPt, hPt, l);
            double kx = screenW / pw, ky = screenH / ph;
            // Visible window of the page expressed in that level's pixels.
            double x0 = (0 - pt.X0) / kx, y0 = (0 - pt.Y0) / ky, x1 = (vw - pt.X0) / kx, y1 = (vh - pt.Y0) / ky;
            var (c0, r0, c1, r1) = TileGrid.Range(x0, y0, x1, y1, tileSize, pw, ph);
            if ((c1 - c0 + 1) * (r1 - r0 + 1) > 600) continue; // far finer than useful
            for (int row = r0; row <= r1; row++)
            for (int col = c0; col <= c1; col++)
            {
                if (!store.TryGet(new TileKey(doc, page, l, col, row), out var t)) continue;
                dc.DrawImage(t.Bitmap, new Rect(pt.X0 + t.X * kx, pt.Y0 + t.Y * ky, t.Width * kx, t.Height * ky));
            }
        }
    }

    private void CancelUnwanted(TileStore store, int doc, HashSet<TileKey> wanted, long level)
    {
        foreach (var (key, job) in store.PendingFor(doc).ToList())
        {
            if (wanted.Contains(key)) continue;
            // Same-resolution tiles that just scrolled out stay useful (cache); finish if running.
            if (job.IsRunning && key.Level == level) continue;
            store.Cancel(key);
        }
    }

    private void DrawDebug(DrawingContext dc, DocumentSession s, long level, int missing, int held, double dpi)
    {
        var store = _services!.Tiles;
        var pool = _services.Pool;
        var (p50, p95, _) = pool.Statistics.RenderLatency();
        string text =
            $"zoom {s.View.Zoom * 100:0.#}%  scale {TileGrid.ScaleOf(level):0.###} px/pt  dpi {dpi:0.##}\n" +
            $"missing {missing}  held {held}  pending {store.PendingCount}  queue {pool.QueuedCount}  busy {pool.RunningCount}/{pool.WorkerCount}\n" +
            $"cache {store.Bytes / (1024 * 1024)}/{store.BudgetBytes / (1024 * 1024)} MB  {store.Count} bmp  hit {store.HitRate:P0}\n" +
            $"frame {Metrics.LastFrameMs:0.00} ms  tile p50 {p50:0} p95 {p95:0} ms  sharp {Metrics.LastTimeToSharpMs:0} ms";
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 12 * dpi, Brushes.White, 1.0);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(200, 21, 34, 56)), null, new Rect(10, 10, ft.Width + 20, ft.Height + 16), 6, 6);
        dc.DrawText(ft, new Point(20, 18));
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

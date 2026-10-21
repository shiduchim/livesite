using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EKFastPDF;

/// <summary>
/// On-demand tile-based document viewing. The display canvas can be enormous, but only
/// small visible bitmap tiles are ever allocated. Zoom/page/search don't re-read a PDF.
/// </summary>
public partial class PdfViewerControl : UserControl
{
    // Cache budget tuned for modern Windows desktops, not for minimum-spec machines.
    // Old zoom-level tiles remain available to make zooming back instantaneous.
    private const int TileSize = 768;
    private const int TileBleed = 2;
    private const long CacheBudgetBytes = 512L * 1024 * 1024;
    private const int SearchHitLimit = 3000;
    private const double DipsPerPdfPoint = 96.0 / 72.0;

    private readonly record struct TileKey(int Page, int Width, int Height, int X, int Y);
    private sealed record TileEntry(BitmapSource Bitmap, int Bytes);
    private readonly Dictionary<TileKey, TileEntry> _tileCache = new();
    private readonly LinkedList<TileKey> _cacheOrder = new();
    private readonly Dictionary<TileKey, LinkedListNode<TileKey>> _cacheNodes = new();
    private readonly Dictionary<TileKey, Image> _drawnTiles = new();
    private long _cacheBytes;
    private readonly DispatcherTimer _renderDelay;
    private readonly DispatcherTimer _searchDelay;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _searchCts;
    private int _navigationGeneration;
    private int _pageIndex;
    private int _pixelWidth, _pixelHeight;  // Real raster pixels (monitor DPI aware).
    private double _pageWidthDip, _pageHeightDip;  // WPF device independent pixels.
    private double _pixelsPerDipX = 1, _pixelsPerDipY = 1;
    private double _pointsWidth, _pointsHeight;
    private double _zoom = 1;
    private bool _fitMode = true;
    private bool _panActive;
    private Point _panOrigin;
    private double _panStartX, _panStartY;
    private double _previewOriginalWidth, _previewOriginalHeight;
    private bool _hasPreview;
    private readonly List<PdfHit> _hits = new();
    private int _resultIndex = -1;
    private bool _searchRunning;
    private int _searchProgress;

    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(PdfTab), typeof(PdfViewerControl), new PropertyMetadata(null, OnTabChanged));

    public PdfTab? Tab
    {
        get => (PdfTab?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    public PdfViewerControl()
    {
        InitializeComponent();
        _renderDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Background,
            (_, _) => { _renderDelay.Stop(); _ = PaintVisibleAsync(); }, Dispatcher);
        _renderDelay.Stop();
        _searchDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(320), DispatcherPriority.Background,
            (_, _) => { _searchDelay.Stop(); _ = SearchAsync(); }, Dispatcher);
        _searchDelay.Stop();
    }

    private static void OnTabChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (PdfViewerControl)obj;
        viewer._renderCts?.Cancel();
        viewer._searchCts?.Cancel();
        viewer.ResetVisuals();
        viewer._tileCache.Clear();
        viewer._cacheOrder.Clear();
        viewer._cacheNodes.Clear();
        viewer._cacheBytes = 0;
        viewer._pointsWidth = viewer._pointsHeight = 0;
        if (e.OldValue is PdfTab oldTab) oldTab.PropertyChanged -= viewer.Tab_PropertyChanged;
        if (e.NewValue is PdfTab newTab) newTab.PropertyChanged += viewer.Tab_PropertyChanged;
        viewer.RefreshDocument();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e) => RefreshDocument();
    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _renderDelay.Stop(); _searchDelay.Stop();
        _renderCts?.Cancel(); _searchCts?.Cancel();
        ResetVisuals();
        _tileCache.Clear(); _cacheOrder.Clear(); _cacheNodes.Clear(); _cacheBytes = 0;
    }

    private void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfTab.Document)) RefreshDocument();
        else if (e.PropertyName == nameof(PdfTab.Status) && Tab?.Document == null)
            StateMessage.Text = Tab?.Status ?? "Opening document…";
    }

    private void RefreshDocument()
    {
        if (!IsLoaded || Tab == null) return;
        DocumentCaption.Text = Tab.Title;
        if (Tab.Document == null)
        {
            StateOverlay.Visibility = Visibility.Visible;
            StateMessage.Text = Tab.Status;
            OpenProgress.Visibility = Tab.Status.StartsWith("Unable", StringComparison.Ordinal) ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        StateOverlay.Visibility = Visibility.Collapsed;
        PageCountText.Text = $"/ {Tab.PageCount:N0}";
        _ = DisplayPageAsync(Math.Clamp(Tab.Page, 0, Tab.PageCount - 1));
    }

    private async Task DisplayPageAsync(int requested)
    {
        if (!IsLoaded || Tab is not { Document: { } doc }) return;
        int generation = ++_navigationGeneration;
        _renderCts?.Cancel();
        int nextPage = Math.Clamp(requested, 0, doc.PageCount - 1);
        try
        {
            var dimensions = await Task.Run(() => doc.GetPageSize(nextPage));
            if (!IsLoaded || Tab?.Document != doc || generation != _navigationGeneration) return;
            if (dimensions.width <= 0 || dimensions.height <= 0) throw new InvalidOperationException("Invalid page size.");
            StateOverlay.Visibility = Visibility.Collapsed;
            if (_pageIndex != nextPage) ResetVisuals();
            _pageIndex = nextPage;
            Tab.Page = nextPage;
            _pointsWidth = dimensions.width;
            _pointsHeight = dimensions.height;
            PageBox.Text = (nextPage + 1).ToString();
            if (_fitMode) SetFitZoom();
            ApplyPageDimensions();
            ViewScroll.ScrollToHome();
            DrawHighlights();
            RequestPaint();
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (generation == _navigationGeneration && Tab is { IsClosed: false })
            {
                StateMessage.Text = "Could not display this PDF page: " + ex.Message;
                OpenProgress.Visibility = Visibility.Collapsed;
                StateOverlay.Visibility = Visibility.Visible;
            }
        }
    }

    private void SetFitZoom()
    {
        if (_pointsWidth < 1 || _pointsHeight < 1) return;
        double availableWidth = Math.Max(180, ViewScroll.ViewportWidth > 0 ? ViewScroll.ViewportWidth - 80 : ViewScroll.ActualWidth - 80);
        double availableHeight = Math.Max(150, ViewScroll.ViewportHeight > 0 ? ViewScroll.ViewportHeight - 80 : ViewScroll.ActualHeight - 80);
        _zoom = Math.Clamp(Math.Min(availableWidth / (_pointsWidth * DipsPerPdfPoint),
            availableHeight / (_pointsHeight * DipsPerPdfPoint)), 0.02, 16);
        ZoomText.Text = "Fit";
    }

    private void ApplyPageDimensions()
    {
        if (_pointsWidth < 1 || _pointsHeight < 1) return;
        _pageWidthDip = _pointsWidth * DipsPerPdfPoint * _zoom;
        _pageHeightDip = _pointsHeight * DipsPerPdfPoint * _zoom;

        // FIX: raster resolution follows Windows per-monitor scaling (125%, 150%, 200%...).
        // Original viewer rendered all content as 96-DPI pixels, which WPF enlarged/blurted.
        var dpi = VisualTreeHelper.GetDpi(this);
        _pixelWidth = (int)Math.Clamp(Math.Ceiling(_pageWidthDip * dpi.DpiScaleX), 1, 25_000_000);
        _pixelHeight = (int)Math.Clamp(Math.Ceiling(_pageHeightDip * dpi.DpiScaleY), 1, 25_000_000);
        _pixelsPerDipX = _pixelWidth / _pageWidthDip;
        _pixelsPerDipY = _pixelHeight / _pageHeightDip;

        PageBorder.Width = _pageWidthDip + 2;
        PageBorder.Height = _pageHeightDip + 2;
        TileCanvas.Width = HighlightCanvas.Width = _pageWidthDip;
        TileCanvas.Height = HighlightCanvas.Height = _pageHeightDip;
        if (_hasPreview)
        {
            var scale = (ScaleTransform)PreviewCanvas.RenderTransform;
            scale.ScaleX = _pageWidthDip / _previewOriginalWidth;
            scale.ScaleY = _pageHeightDip / _previewOriginalHeight;
        }
        DrawHighlights();
    }

    private void CapturePreview()
    {
        // FIX: Retain the old raster at a temporary scale while native PDFium renders
        // the new zoom. This keeps the page visible rather than clearing to white.
        // When a preview already exists, keep it until ALL visible new tiles are ready:
        // otherwise rapid wheel gestures replace a full preview with a single tile.
        if (_hasPreview)
        {
            TileCanvas.Children.Clear();
            _drawnTiles.Clear();
            return;
        }
        if (_drawnTiles.Count == 0) return;
        PreviewCanvas.Children.Clear();
        while (TileCanvas.Children.Count > 0)
        {
            var image = TileCanvas.Children[0];
            TileCanvas.Children.RemoveAt(0);
            PreviewCanvas.Children.Add(image);
        }
        _drawnTiles.Clear();
        _previewOriginalWidth = _pageWidthDip;
        _previewOriginalHeight = _pageHeightDip;
        PreviewCanvas.Width = _previewOriginalWidth;
        PreviewCanvas.Height = _previewOriginalHeight;
        PreviewCanvas.RenderTransform = new ScaleTransform(1, 1);
        _hasPreview = true;
    }

    private void ResetVisuals()
    {
        TileCanvas.Children.Clear();
        PreviewCanvas.Children.Clear();
        _drawnTiles.Clear();
        _hasPreview = false;
    }

    private void SetZoom(double next, Point? viewportPoint = null)
    {
        if (Tab?.Document == null || _pointsWidth < 1) return;
        var newZoom = Math.Clamp(next, 0.02, 16);
        if (Math.Abs(_zoom - newZoom) < 0.000001) return;

        // Zoom towards the cursor (Ctrl+Wheel), or towards the viewport center (buttons).
        Point anchor = viewportPoint ?? new Point(ViewScroll.ViewportWidth / 2, ViewScroll.ViewportHeight / 2);
        Point oldPagePoint = ViewScroll.TranslatePoint(anchor, PageBorder);
        double nx = Math.Clamp((oldPagePoint.X - 1) / Math.Max(1, _pageWidthDip), 0, 1);
        double ny = Math.Clamp((oldPagePoint.Y - 1) / Math.Max(1, _pageHeightDip), 0, 1);

        _renderCts?.Cancel();
        CapturePreview();
        _fitMode = false;
        _zoom = newZoom;
        ZoomText.Text = $"{_zoom * 100:0}%";
        ApplyPageDimensions();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded) return;
            ViewScroll.ScrollToHorizontalOffset(nx * _pageWidthDip + 33 - anchor.X);
            ViewScroll.ScrollToVerticalOffset(ny * _pageHeightDip + 33 - anchor.Y);
            RequestPaint();
        }));
    }

    private void RequestPaint()
    {
        if (!IsLoaded || Tab?.Document == null) return;
        _renderCts?.Cancel();
        _renderDelay.Stop();
        PaintCachedTiles();
        _renderDelay.Start();
    }

    private List<TileKey> VisibleTiles(bool prefetch = true)
    {
        var result = new List<TileKey>();
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return result;
        double left = Math.Max(0, (ViewScroll.HorizontalOffset - 33) * _pixelsPerDipX);
        double top = Math.Max(0, (ViewScroll.VerticalOffset - 33) * _pixelsPerDipY);
        int pad = prefetch ? 1 : 0;
        int firstCol = Math.Max(0, (int)(left / TileSize) - pad);
        int firstRow = Math.Max(0, (int)(top / TileSize) - pad);
        int lastCol = Math.Min((_pixelWidth - 1) / TileSize,
            (int)((left + Math.Max(1, ViewScroll.ViewportWidth * _pixelsPerDipX)) / TileSize) + pad);
        int lastRow = Math.Min((_pixelHeight - 1) / TileSize,
            (int)((top + Math.Max(1, ViewScroll.ViewportHeight * _pixelsPerDipY)) / TileSize) + pad);
        for (int row = firstRow; row <= lastRow; row++)
            for (int col = firstCol; col <= lastCol; col++)
                result.Add(new TileKey(_pageIndex, _pixelWidth, _pixelHeight, col, row));
        return result;
    }

    private void PaintCachedTiles()
    {
        var needed = VisibleTiles();
        var visible = needed.ToHashSet();
        // Scrolling does not tear down and recreate images that are still visible.
        foreach (var key in _drawnTiles.Keys.ToArray())
        {
            if (visible.Contains(key)) continue;
            TileCanvas.Children.Remove(_drawnTiles[key]);
            _drawnTiles.Remove(key);
        }
        foreach (var key in needed)
            if (_tileCache.TryGetValue(key, out var entry) && !_drawnTiles.ContainsKey(key))
            {
                TouchTile(key);
                AddTileVisual(key, entry.Bitmap);
            }
        DropPreviewIfCovered();
    }

    private void AddTileVisual(TileKey key, BitmapSource bmp)
    {
        if (_drawnTiles.ContainsKey(key)) return;
        // PDFium draws a two-pixel overlap at every tile boundary. It removes
        // hairline white seams caused by WPF's fractional-pixel coordinates.
        int x = key.X * TileSize;
        int y = key.Y * TileSize;
        var image = new Image
        {
            Source = bmp, Width = bmp.PixelWidth / _pixelsPerDipX,
            Height = bmp.PixelHeight / _pixelsPerDipY,
            Stretch = Stretch.Fill, SnapsToDevicePixels = false,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(image, x / _pixelsPerDipX);
        Canvas.SetTop(image, y / _pixelsPerDipY);
        TileCanvas.Children.Add(image);
        _drawnTiles[key] = image;
    }

    private void DropPreviewIfCovered()
    {
        if (!_hasPreview) return;
        if (VisibleTiles(false).All(key => _drawnTiles.ContainsKey(key)))
        {
            PreviewCanvas.Children.Clear();
            _hasPreview = false;
        }
    }

    private async Task PaintVisibleAsync()
    {
        if (Tab is not { Document: { } doc } || !IsLoaded) return;
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        var token = cts.Token;
        var keys = VisibleTiles();
        int page = _pageIndex;
        int rasterWidth = _pixelWidth, rasterHeight = _pixelHeight;
        double targetX = (ViewScroll.HorizontalOffset + ViewScroll.ViewportWidth / 2 - 33) * _pixelsPerDipX;
        double targetY = (ViewScroll.VerticalOffset + ViewScroll.ViewportHeight / 2 - 33) * _pixelsPerDipY;
        // Render screen-center first; nearby offscreen tiles are warmed afterwards.
        keys.Sort((a, b) =>
        {
            double da = Math.Pow((a.X + .5) * TileSize - targetX, 2) + Math.Pow((a.Y + .5) * TileSize - targetY, 2);
            double db = Math.Pow((b.X + .5) * TileSize - targetX, 2) + Math.Pow((b.Y + .5) * TileSize - targetY, 2);
            return da.CompareTo(db);
        });
        try
        {
            foreach (var key in keys)
            {
                if (token.IsCancellationRequested || Tab?.Document != doc || !IsLoaded) break;
                if (_tileCache.ContainsKey(key)) continue;
                int x = key.X * TileSize;
                int y = key.Y * TileSize;
                int w = Math.Min(TileSize + TileBleed, key.Width - x);
                int h = Math.Min(TileSize + TileBleed, key.Height - y);
                try
                {
                    var tile = await Task.Run(() => doc.Render(key.Page, key.Width, key.Height, x, y, w, h, token), token);
                    // PDFium cannot interrupt a tile that is already rendering. Keep its
                    // result in cache if it still belongs to the current zoom, even if
                    // the mouse has moved and this request was cancelled in the meantime.
                    if (Tab?.Document != doc || !IsLoaded ||
                        page != _pageIndex || rasterWidth != _pixelWidth || rasterHeight != _pixelHeight) break;
                    var bitmap = BitmapSource.Create(tile.Width, tile.Height, 96, 96, PixelFormats.Bgra32,
                        null, tile.Bytes, tile.Stride);
                    bitmap.Freeze();
                    CacheTile(key, bitmap, tile.Bytes.Length);
                    if (VisibleTiles().Contains(key)) AddTileVisual(key, bitmap);
                    DropPreviewIfCovered();
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine("PDF tile rendering: " + ex);
                        StateMessage.Text = "Page rendering failed: " + ex.Message;
                        OpenProgress.Visibility = Visibility.Collapsed;
                        StateOverlay.Visibility = Visibility.Visible;
                    }
                    break;
                }
            }
        }
        finally
        {
            if (_renderCts == cts) _renderCts = null;
            cts.Dispose();
        }
    }

    private void TouchTile(TileKey key)
    {
        if (!_cacheNodes.TryGetValue(key, out var node)) return;
        _cacheOrder.Remove(node);
        _cacheOrder.AddLast(node);
    }

    private void CacheTile(TileKey key, BitmapSource bitmap, int bytes)
    {
        if (_tileCache.ContainsKey(key)) return;
        _tileCache[key] = new TileEntry(bitmap, bytes);
        _cacheNodes[key] = _cacheOrder.AddLast(key);
        _cacheBytes += bytes;
        while (_cacheBytes > CacheBudgetBytes && _cacheOrder.First != null)
        {
            var oldest = _cacheOrder.First.Value;
            _cacheOrder.RemoveFirst();
            _cacheNodes.Remove(oldest);
            if (_tileCache.Remove(oldest, out var previous)) _cacheBytes -= previous.Bytes;
        }
    }

    private void DrawHighlights()
    {
        HighlightCanvas.Children.Clear();
        if (_pointsWidth <= 0 || _pointsHeight <= 0) return;
        // Cap visible overlay rectangles so search for a common term stays responsive.
        int drawn = 0;
        for (int i = 0; i < _hits.Count && drawn < 1000; i++)
        {
            var hit = _hits[i];
            if (hit.Page != _pageIndex) continue;
            foreach (var box in hit.Rectangles)
            {
                if (++drawn > 1000) return;
                bool selected = i == _resultIndex;
                var rectangle = new Rectangle
                {
                    Width = Math.Max(2, (box.Right - box.Left) * _pageWidthDip / _pointsWidth),
                    Height = Math.Max(2, (box.Top - box.Bottom) * _pageHeightDip / _pointsHeight),
                    Fill = selected ? new SolidColorBrush(Color.FromArgb(115, 60, 104, 181)) : new SolidColorBrush(Color.FromArgb(100, 250, 197, 64)),
                    Stroke = selected ? Brushes.RoyalBlue : Brushes.Transparent,
                    StrokeThickness = selected ? 1.5 : 0
                };
                Canvas.SetLeft(rectangle, box.Left * _pageWidthDip / _pointsWidth);
                Canvas.SetTop(rectangle, (_pointsHeight - box.Top) * _pageHeightDip / _pointsHeight);
                HighlightCanvas.Children.Add(rectangle);
            }
        }
    }

    private void ViewScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 || e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.ViewportWidthChange != 0)
            RequestPaint();
    }
    private void ViewScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode && Tab?.Document != null && _pointsWidth > 0)
        {
            double oldZoom = _zoom;
            SetFitZoom();
            if (Math.Abs(oldZoom - _zoom) < 0.00005) return;
            CapturePreview();
            _renderCts?.Cancel();
            ApplyPageDimensions();
            RequestPaint();
        }
    }
    private void ViewScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(_zoom * Math.Pow(1.15, e.Delta / 120.0), e.GetPosition(ViewScroll));
            e.Handled = true;
        }
    }
    private void ViewScroll_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panActive = true;
        _panOrigin = e.GetPosition(ViewScroll);
        _panStartX = ViewScroll.HorizontalOffset;
        _panStartY = ViewScroll.VerticalOffset;
        ViewScroll.CaptureMouse();
        ViewScroll.Cursor = Cursors.Hand;
        e.Handled = true;
    }
    private void ViewScroll_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panActive) return;
        var location = e.GetPosition(ViewScroll);
        ViewScroll.ScrollToHorizontalOffset(_panStartX + _panOrigin.X - location.X);
        ViewScroll.ScrollToVerticalOffset(_panStartY + _panOrigin.Y - location.Y);
        e.Handled = true;
    }
    private void ViewScroll_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panActive || e.ChangedButton != MouseButton.Middle) return;
        _panActive = false;
        ViewScroll.ReleaseMouseCapture();
        ViewScroll.Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    public void NextPage() { if (Tab is { Document: not null }) _ = DisplayPageAsync(_pageIndex + 1); }
    public void PreviousPage() { if (Tab is { Document: not null }) _ = DisplayPageAsync(_pageIndex - 1); }
    public void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) => NextPage();
    private void PreviousPage_Click(object sender, RoutedEventArgs e) => PreviousPage();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        _renderCts?.Cancel();
        CapturePreview();
        _fitMode = true; SetFitZoom(); ApplyPageDimensions(); RequestPaint();
    }
    private void PageBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return) return;
        if (int.TryParse(PageBox.Text, out int desired)) _ = DisplayPageAsync(desired - 1);
        else PageBox.Text = (_pageIndex + 1).ToString();
        ViewScroll.Focus();
        e.Handled = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchDelay?.Stop();
        _hits.Clear();
        _resultIndex = -1;
        _searchProgress = 0;
        _searchRunning = false;
        SearchCounter.Text = "";
        if (HighlightCanvas != null) DrawHighlights();
        if (Tab?.Document != null && !string.IsNullOrWhiteSpace(SearchBox.Text)) _searchDelay.Start();
    }

    private async Task SearchAsync()
    {
        if (Tab is not { Document: { } doc } || !IsLoaded) return;
        string term = SearchBox.Text.Trim();
        if (term.Length == 0) return;
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;
        _hits.Clear();
        _resultIndex = -1;
        _searchRunning = true;
        try
        {
            for (int i = 0; i < doc.PageCount; i++)
            {
                token.ThrowIfCancellationRequested();
                var results = await Task.Run(() => doc.Search(i, term, SearchHitLimit - _hits.Count, token), token);
                if (token.IsCancellationRequested || !IsLoaded || Tab?.Document != doc) return;
                bool first = _hits.Count == 0 && results.Count > 0;
                _hits.AddRange(results);
                _searchProgress = i + 1;
                if (first)
                {
                    _resultIndex = 0;
                    _ = NavigateToResultAsync();
                }
                if (i % 5 == 0 || results.Count > 0) UpdateSearchCounter();
                if (_hits.Count >= SearchHitLimit) break;
            }
        }
        catch (OperationCanceledException) { return; }
        catch (ObjectDisposedException) { return; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("PDF search: " + ex.Message); }
        finally
        {
            if (_searchCts == cts)
            {
                _searchRunning = false;
                UpdateSearchCounter();
                _searchCts = null;
            }
            cts.Dispose();
        }
    }

    private void UpdateSearchCounter()
    {
        if (Tab?.Document == null || string.IsNullOrWhiteSpace(SearchBox.Text)) { SearchCounter.Text = ""; return; }
        if (_hits.Count == 0)
            SearchCounter.Text = _searchRunning ? $"{_searchProgress}/{Tab.PageCount}" : "No matches";
        else SearchCounter.Text = $"{_resultIndex + 1}/{_hits.Count}{(_searchRunning ? "…" : _hits.Count >= SearchHitLimit ? " limit" : "")}";
        DrawHighlights();
    }

    private async Task NavigateToResultAsync()
    {
        if (_resultIndex < 0 || _resultIndex >= _hits.Count) return;
        var hit = _hits[_resultIndex];
        if (_pageIndex != hit.Page)
        {
            await DisplayPageAsync(hit.Page);
            if (_pageIndex != hit.Page) return;
        }
        DrawHighlights();
        if (hit.Rectangles.Count > 0)
        {
            var rect = hit.Rectangles[0];
            double x = rect.Left * _pageWidthDip / _pointsWidth;
            double y = (_pointsHeight - rect.Top) * _pageHeightDip / _pointsHeight;
            ViewScroll.ScrollToHorizontalOffset(x + 32 - ViewScroll.ViewportWidth / 2);
            ViewScroll.ScrollToVerticalOffset(y + 32 - ViewScroll.ViewportHeight / 2);
        }
        UpdateSearchCounter();
    }
    private void NextResult_Click(object sender, RoutedEventArgs e)
    {
        if (_hits.Count == 0) return;
        _resultIndex = (_resultIndex + 1) % _hits.Count;
        _ = NavigateToResultAsync();
    }
    private void PrevResult_Click(object sender, RoutedEventArgs e)
    {
        if (_hits.Count == 0) return;
        _resultIndex = (_resultIndex + _hits.Count - 1) % _hits.Count;
        _ = NavigateToResultAsync();
    }
}

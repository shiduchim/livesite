using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EKFastPDF.Core.Caching;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Rendering;

namespace EKFastPDF.Viewer;

/// <summary>A rendered raster: a tile or a page overview. Immutable bitmap; WPF uploads it to a
/// GPU texture once, the first time it is drawn, and composites it every frame afterwards.</summary>
public sealed class TileBitmap
{
    public required BitmapSource Bitmap { get; init; }
    /// <summary>Position/size in page pixels of its level.</summary>
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int PageWidth { get; init; }
    public required int PageHeight { get; init; }
    /// <summary>When the viewer first showed it (for the reveal cross-fade); 0 = not yet shown.</summary>
    public long RevealedAt { get; set; }
    public double RenderMs { get; init; }
}

/// <summary>
/// Global raster cache (byte-budgeted LRU shared by all tabs) plus the set of in-flight render
/// jobs. Results produced on render-pool threads are converted to frozen WPF bitmaps right there
/// (off the UI thread) and handed to the UI thread in batches.
/// </summary>
public sealed class TileStore
{
    private readonly RenderPool _pool;
    private readonly ByteBudgetCache<TileKey, TileBitmap> _cache;
    private readonly Dictionary<TileKey, RenderJob> _pending = new();
    private readonly Dictionary<(int Doc, int Page), Dictionary<long, int>> _levels = new();
    private readonly HashSet<TileKey> _failed = new();
    private readonly ConcurrentQueue<(TileKey Key, TileBitmap? Tile, RenderResult Result, int Generation)> _arrivals = new();
    private readonly Dispatcher _dispatcher;
    private int _drainScheduled;

    public TileStore(RenderPool pool, long budgetBytes)
    {
        _pool = pool;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _cache = new ByteBudgetCache<TileKey, TileBitmap>(budgetBytes, OnEvicted);
    }

    /// <summary>Raised on the UI thread after one or more rasters arrived.</summary>
    public event Action<IReadOnlyCollection<TileKey>>? TilesArrived;
    public event Action<TileKey, string>? TileFailed;

    public long Bytes => _cache.Bytes;
    public long BudgetBytes => _cache.BudgetBytes;
    public int Count => _cache.Count;
    public double HitRate => _cache.Hits + _cache.Misses == 0 ? 0 : (double)_cache.Hits / (_cache.Hits + _cache.Misses);
    public long Hits => _cache.Hits;
    public long Misses => _cache.Misses;
    public int PendingCount => _pending.Count;
    public long CompletedTiles { get; private set; }
    public double LastRenderMs { get; private set; }

    public long Frame
    {
        get => _cache.CurrentFrame;
        set => _cache.CurrentFrame = value;
    }

    public bool TryGet(TileKey key, out TileBitmap tile) => _cache.TryGet(key, out tile);
    public bool Contains(TileKey key) => _cache.Contains(key);
    /// <summary>Marks a cached raster as recently used (protects prefetched tiles from eviction).</summary>
    public void Touch(TileKey key) => _cache.Touch(key);
    public bool IsPending(TileKey key) => _pending.ContainsKey(key);
    public bool HasFailed(TileKey key) => _failed.Contains(key);

    /// <summary>Resolution levels that have at least one cached raster for the page.</summary>
    public IEnumerable<long> LevelsFor(int doc, int page) =>
        _levels.TryGetValue((doc, page), out var map) ? map.Keys : Array.Empty<long>();

    public IEnumerable<(TileKey Key, RenderJob Job)> PendingFor(int doc) =>
        _pending.Where(p => p.Key.Doc == doc).Select(p => (p.Key, p.Value));

    /// <summary>Requests a raster unless it is cached, already in flight or previously failed.
    /// Updates the priority of an in-flight request.</summary>
    public void Request(TileKey key, int pageWidth, int pageHeight, int x, int y, int w, int h, int flags, double priority)
    {
        if (_cache.Contains(key) || _failed.Contains(key)) return;
        if (_pending.TryGetValue(key, out var existing))
        {
            existing.Priority = priority;
            return;
        }
        var job = new RenderJob(key.Doc, key.Page, pageWidth, pageHeight, x, y, w, h, flags, priority, OnCompleted, key);
        _pending[key] = job;
        _pool.Submit(job);
    }

    public void Cancel(TileKey key)
    {
        if (_pending.TryGetValue(key, out var job)) _pool.Cancel(job);
    }

    /// <summary>Drops everything belonging to a closed document.</summary>
    public void ForgetDocument(int doc)
    {
        foreach (var key in _pending.Keys.Where(k => k.Doc == doc).ToList()) _pending.Remove(key);
        _failed.RemoveWhere(k => k.Doc == doc);
        _cache.RemoveWhere(k => k.Doc == doc);
    }

    public void Trim() => _cache.Trim();

    // Runs on a render-pool thread while the shared buffer still holds the pixels.
    private void OnCompleted(RenderJob job, RenderResult result)
    {
        var key = (TileKey)job.Tag!;
        TileBitmap? tile = null;
        int generation = Services.PageTint.Generation;
        if (result.Outcome == JobOutcome.Completed)
        {
            try
            {
                int bufferSize = (result.Height - 1) * result.Stride + result.Width * 4;
                var look = Services.PageTint.Look;
                BitmapSource bitmap;
                if (look == PageLook.Original)
                    bitmap = BitmapSource.Create(result.Width, result.Height, 96, 96, PixelFormats.Bgr32, null, result.Pixels, bufferSize, result.Stride);
                else
                {
                    // Theme page look (dimmed, warm paper, dark): change a copy, never the shared buffer.
                    int stride = result.Width * 4;
                    var pixels = new byte[stride * result.Height];
                    for (int y = 0; y < result.Height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(result.Pixels + y * result.Stride, pixels, y * stride, stride);
                    Services.PageTint.Apply(pixels, look);
                    bitmap = BitmapSource.Create(result.Width, result.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
                }
                bitmap.Freeze();
                tile = new TileBitmap
                {
                    Bitmap = bitmap, X = job.X, Y = job.Y, Width = job.Width, Height = job.Height,
                    PageWidth = job.PageWidth, PageHeight = job.PageHeight, RenderMs = result.RenderMs + result.ParseMs,
                };
            }
            catch (Exception e) when (e is OutOfMemoryException or ArgumentException or InvalidOperationException)
            {
                Log.Error("Could not create tile bitmap", e);
                result = new RenderResult { Outcome = JobOutcome.Failed, Error = e.Message };
            }
        }
        _arrivals.Enqueue((key, tile, result, generation));
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, Drain);
    }

    private void Drain()
    {
        Volatile.Write(ref _drainScheduled, 0);
        var arrived = new List<TileKey>();
        while (_arrivals.TryDequeue(out var item))
        {
            var (key, tile, result, generation) = item;
            _pending.Remove(key);
            if (!_pool.IsRegistered(key.Doc)) continue; // document closed meanwhile
            if (generation != Services.PageTint.Generation) continue; // drawn with the previous page look
            switch (result.Outcome)
            {
                case JobOutcome.Completed when tile != null:
                    _cache.Add(key, tile, (long)tile.Width * tile.Height * 4);
                    if (!_levels.TryGetValue((key.Doc, key.Page), out var map)) _levels[(key.Doc, key.Page)] = map = new();
                    map[key.Level] = map.GetValueOrDefault(key.Level) + 1;
                    CompletedTiles++;
                    LastRenderMs = tile.RenderMs;
                    arrived.Add(key);
                    break;
                case JobOutcome.Failed:
                    _failed.Add(key);
                    Log.Warn($"Render failed for page {key.Page + 1}: {result.Error}");
                    TileFailed?.Invoke(key, result.Error ?? "Render failed");
                    break;
            }
        }
        if (arrived.Count > 0) TilesArrived?.Invoke(arrived);
    }

    private void OnEvicted(TileKey key, TileBitmap tile)
    {
        if (_levels.TryGetValue((key.Doc, key.Page), out var map) && map.TryGetValue(key.Level, out int n))
        {
            if (n <= 1) map.Remove(key.Level); else map[key.Level] = n - 1;
            if (map.Count == 0) _levels.Remove((key.Doc, key.Page));
        }
    }

    public static long Timestamp => Stopwatch.GetTimestamp();
    public static double MsSince(long ticks) => (Stopwatch.GetTimestamp() - ticks) * 1000.0 / Stopwatch.Frequency;
}

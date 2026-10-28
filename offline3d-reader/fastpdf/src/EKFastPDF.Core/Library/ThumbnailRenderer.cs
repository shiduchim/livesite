using System.Runtime.InteropServices;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Rendering;

namespace EKFastPDF.Core.Library;

/// <summary>A rendered page picture: BGRx pixels, top row first.</summary>
public sealed record Thumbnail(int Width, int Height, int Stride, byte[] Pixels);

/// <summary>
/// Page pictures for My PDFs, rendered by the same worker processes as the viewer but queued
/// behind everything the viewer asks for. Keeps a few documents open (least recently used are
/// closed) and remembers page sizes, so the gallery can lay out pages before they are drawn.
/// </summary>
public sealed class ThumbnailRenderer : IDisposable
{
    /// <summary>Viewer jobs use priorities below 400 000; pictures always wait for them.</summary>
    private const double BasePriority = 400_000;
    private readonly RenderPool _pool;
    private readonly int _maxOpen;
    private readonly SemaphoreSlim _slots;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DocumentInfo?> _infos = new(StringComparer.OrdinalIgnoreCase);
    private long _use, _sequence;
    private bool _disposed;

    private sealed class Entry
    {
        public required int DocId;
        public required Task<DocumentInfo> Info;
        public long LastUse;
        public int Busy;
    }

    public ThumbnailRenderer(RenderPool pool, int maxOpenDocuments = 6, int concurrentRenders = 2)
    {
        _pool = pool;
        _maxOpen = Math.Max(1, maxOpenDocuments);
        _slots = new SemaphoreSlim(Math.Max(1, concurrentRenders));
    }

    /// <summary>Cache key: the same path with a different size or time is a different file.</summary>
    public static string KeyOf(string path)
    {
        try
        {
            var f = new FileInfo(path);
            return f.FullName + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return path; }
    }

    /// <summary>Page count and sizes (points), or null when the PDF cannot be opened
    /// (damaged, password protected, gone).</summary>
    public async Task<DocumentInfo?> InfoAsync(string path, CancellationToken cancel = default)
    {
        string key = KeyOf(path);
        lock (_gate) if (_infos.TryGetValue(key, out var known)) return known;
        var entry = Acquire(path, key);
        try
        {
            var info = await entry.Info.WaitAsync(cancel).ConfigureAwait(false);
            lock (_gate) _infos[key] = info;
            return info;
        }
        catch (Exception e) when (e is PdfOpenException or InvalidOperationException)
        {
            lock (_gate) _infos[key] = null;
            Forget(key);
            return null;
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            Forget(key);
            return null;
        }
        finally { Release(entry); }
    }

    /// <summary>Renders <paramref name="page"/> (0-based) <paramref name="widthPx"/> pixels wide.
    /// Null when the PDF cannot be opened or rendered.</summary>
    public async Task<Thumbnail?> RenderAsync(string path, int page, int widthPx, CancellationToken cancel = default)
    {
        var info = await InfoAsync(path, cancel).ConfigureAwait(false);
        if (info is null || page < 0 || page >= info.PageCount) return null;
        float pw = Math.Max(1, info.PageWidths[page]), ph = Math.Max(1, info.PageHeights[page]);
        int w = Math.Clamp(widthPx, 16, 4096);
        int h = Math.Max(1, (int)Math.Round(w * ph / pw));
        // Stay well inside the worker's shared buffer (seam margin included).
        long limit = Math.Min(_pool.MaxRegionBytes / 2, 48L << 20);
        while ((long)(w + 2 * RenderJob.SeamMargin) * (h + 2 * RenderJob.SeamMargin) * 4 > limit && w > 64)
        {
            w = (int)(w * 0.8);
            h = Math.Max(1, (int)Math.Round(w * ph / pw));
        }

        await _slots.WaitAsync(cancel).ConfigureAwait(false);
        string key = KeyOf(path);
        Entry? entry = null;
        try
        {
            entry = Acquire(path, key);
            await entry.Info.WaitAsync(cancel).ConfigureAwait(false);
            var done = new TaskCompletionSource<Thumbnail?>(TaskCreationOptions.RunContinuationsAsynchronously);
            double priority = BasePriority + Interlocked.Increment(ref _sequence) % 100_000;
            var job = new RenderJob(entry.DocId, page, w, h, 0, 0, w, h, RenderFlags.Annotations, priority, (_, result) =>
            {
                if (result.Outcome != JobOutcome.Completed) { done.TrySetResult(null); return; }
                int stride = result.Width * 4;
                var pixels = new byte[stride * result.Height];
                for (int y = 0; y < result.Height; y++)
                    Marshal.Copy(result.Pixels + y * result.Stride, pixels, y * stride, stride);
                done.TrySetResult(new Thumbnail(result.Width, result.Height, stride, pixels));
            });
            _pool.Submit(job);
            using (cancel.Register(() => { _pool.Cancel(job); done.TrySetCanceled(cancel); }))
                return await done.Task.ConfigureAwait(false);
        }
        catch (Exception e) when (e is PdfOpenException or InvalidOperationException)
        {
            Log.Warn($"Picture of {Path.GetFileName(path)} failed: {e.Message}");
            return null;
        }
        finally
        {
            if (entry != null) Release(entry);
            _slots.Release();
        }
    }

    private Entry Acquire(string path, string key)
    {
        var toClose = new List<int>();
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_open.TryGetValue(key, out entry!))
            {
                int id = _pool.RegisterDocument(path);
                entry = new Entry { DocId = id, Info = _pool.OpenDocumentAsync(id) };
                _open[key] = entry;
                // Close the least recently used documents nobody is rendering.
                while (_open.Count > _maxOpen)
                {
                    var victim = _open.Where(p => p.Value.Busy == 0 && !ReferenceEquals(p.Value, entry)).OrderBy(p => p.Value.LastUse).FirstOrDefault();
                    if (victim.Value is null) break;
                    _open.Remove(victim.Key);
                    toClose.Add(victim.Value.DocId);
                }
            }
            entry.Busy++;
            entry.LastUse = ++_use;
        }
        foreach (int id in toClose) _pool.CloseDocument(id);
        return entry;
    }

    private void Release(Entry entry)
    {
        lock (_gate) entry.Busy--;
    }

    private void Forget(string key)
    {
        int id = 0;
        lock (_gate)
        {
            if (_open.TryGetValue(key, out var e) && e.Busy <= 1) { _open.Remove(key); id = e.DocId; }
        }
        if (id != 0) _pool.CloseDocument(id);
    }

    public void Dispose()
    {
        List<int> ids;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ids = _open.Values.Select(e => e.DocId).ToList();
            _open.Clear();
        }
        foreach (int id in ids) _pool.CloseDocument(id);
    }
}

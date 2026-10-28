using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Library;
using EKFastPDF.Core.Rendering;

namespace EKFastPDF.Viewer;

/// <summary>What My PDFs knows about a file without opening it again.</summary>
public sealed class PdfFacts
{
    public int Pages { get; set; }
    /// <summary>Page sizes in points (width, height) for every page.</summary>
    public float[]? Widths { get; set; }
    public float[]? Heights { get; set; }
    public bool? Has3D { get; set; }
    public bool Unreadable { get; set; }
    public double Aspect(int page) => Widths is { } w && Heights is { } h && page < w.Length && w[page] > 0 ? h[page] / w[page] : 0.647;
}

/// <summary>
/// Page pictures for My PDFs: rendered by <see cref="ThumbnailRenderer"/>, kept in memory and,
/// for small first-page pictures, on disk in %LOCALAPPDATA%\EKPDF\Pictures so the gallery fills
/// instantly next time. Also remembers page counts, page sizes and whether a PDF has 3D.
/// </summary>
public sealed class PageImages : IDisposable
{
    /// <summary>Pictures are rendered in width steps so small slider moves reuse them.</summary>
    public const int Step = 160;
    private const int DiskMaxWidth = 640;
    private const long MemoryBudget = 160L << 20;
    private readonly ThumbnailRenderer _renderer;
    private readonly Dispatcher _ui;
    private readonly string _dir;
    private readonly Dictionary<string, (BitmapSource Image, long Use, long Bytes)> _memory = new();
    private readonly Dictionary<string, Task<BitmapSource?>> _inFlight = new();
    private readonly ConcurrentDictionary<string, PdfFacts> _facts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<PdfFacts>> _factsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private long _bytes, _use;
    private DispatcherTimer? _saveTimer;

    public PageImages(RenderPool pool)
    {
        _renderer = new ThumbnailRenderer(pool, maxOpenDocuments: 6, concurrentRenders: 2);
        _ui = Dispatcher.CurrentDispatcher;
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EKPDF", "Pictures");
        LoadIndex();
    }

    private string IndexPath => Path.Combine(_dir, "index.json");

    /// <summary>Known facts, or null when the file has not been looked at yet.</summary>
    public PdfFacts? Known(string path) => _facts.TryGetValue(ThumbnailRenderer.KeyOf(path), out var f) ? f : null;

    /// <summary>Page count and sizes, and whether it has 3D. UI thread.</summary>
    public Task<PdfFacts> FactsAsync(string path)
    {
        string key = ThumbnailRenderer.KeyOf(path);
        if (_facts.TryGetValue(key, out var known) && known.Has3D.HasValue) return Task.FromResult(known);
        if (_factsInFlight.TryGetValue(key, out var running)) return running;
        var task = LoadFacts(path, key, known);
        _factsInFlight[key] = task;
        task.ContinueWith(_ => _factsInFlight.Remove(key), TaskScheduler.FromCurrentSynchronizationContext());
        return task;
    }

    private async Task<PdfFacts> LoadFacts(string path, string key, PdfFacts? known)
    {
        var facts = known ?? new PdfFacts();
        if (known is null)
        {
            var info = await _renderer.InfoAsync(path).ConfigureAwait(false);
            if (info is null) facts.Unreadable = true;
            else { facts.Pages = info.PageCount; facts.Widths = info.PageWidths; facts.Heights = info.PageHeights; }
        }
        if (!facts.Unreadable && !facts.Has3D.HasValue)
        {
            try { facts.Has3D = await Task.Run(() => global::Offline3D.PdfPackageService.Has3D(path)).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { facts.Has3D = false; }
        }
        _facts[key] = facts;
        _ = _ui.BeginInvoke(ScheduleSave);
        return facts;
    }

    /// <summary>The picture of <paramref name="page"/> at least <paramref name="widthPx"/> wide
    /// (rounded up to <see cref="Step"/>), or the best one already in memory while it renders.</summary>
    public BitmapSource? Cached(string path, int page, int widthPx, out bool exact)
    {
        string baseKey = ThumbnailRenderer.KeyOf(path) + "|" + page + "|";
        int want = Bucket(widthPx);
        exact = false;
        BitmapSource? best = null; int bestWidth = 0;
        foreach (var (key, value) in _memory)
        {
            if (!key.StartsWith(baseKey, StringComparison.Ordinal)) continue;
            int w = int.Parse(key.AsSpan(baseKey.Length));
            if (w == want) { exact = true; _memory[key] = (value.Image, ++_use, value.Bytes); return value.Image; }
            if (w > bestWidth) { best = value.Image; bestWidth = w; }
        }
        return best;
    }

    public static int Bucket(int widthPx) => Math.Clamp((widthPx + Step - 1) / Step * Step, Step, 3200);

    /// <summary>Renders (or loads from disk) the picture. UI thread; completes on the UI thread.</summary>
    public Task<BitmapSource?> GetAsync(string path, int page, int widthPx)
    {
        int width = Bucket(widthPx);
        string fileKey = ThumbnailRenderer.KeyOf(path);
        string key = fileKey + "|" + page + "|" + width;
        if (_memory.TryGetValue(key, out var hit)) { _memory[key] = (hit.Image, ++_use, hit.Bytes); return Task.FromResult<BitmapSource?>(hit.Image); }
        if (_inFlight.TryGetValue(key, out var running)) return running;
        var task = Load(path, fileKey, page, width, key);
        _inFlight[key] = task;
        return task;
    }

    private async Task<BitmapSource?> Load(string path, string fileKey, int page, int width, string key)
    {
        int generation = Services.PageTint.Generation;
        var look = Services.PageTint.Look;
        try
        {
            // The disk keeps the PDF's own colours; the page look (theme) is applied in memory.
            string disk = Path.Combine(_dir, Hash(fileKey + "|" + page + "|" + width) + ".png");
            BitmapSource? image = width <= DiskMaxWidth ? await Task.Run(() => ReadPng(disk, look)) : null;
            if (image is null)
            {
                var t = await _renderer.RenderAsync(path, page, width);
                if (t is null) return null;
                image = await Task.Run(() =>
                {
                    if (width <= DiskMaxWidth)
                    {
                        var plain = BitmapSource.Create(t.Width, t.Height, 96, 96, PixelFormats.Bgr32, null, t.Pixels, t.Stride);
                        plain.Freeze();
                        WritePng(disk, plain);
                    }
                    return Tinted(t.Pixels, t.Width, t.Height, t.Stride, look);
                });
            }
            if (generation == Services.PageTint.Generation) Remember(key, image);
            return image;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException or OperationCanceledException)
        {
            Log.Warn("Page picture failed: " + e.Message);
            return null;
        }
        finally { _inFlight.Remove(key); }
    }

    private static BitmapSource Tinted(byte[] pixels, int width, int height, int stride, PageLook look)
    {
        if (look != PageLook.Original) Services.PageTint.Apply(pixels, look);
        var b = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        b.Freeze();
        return b;
    }

    /// <summary>Forgets pictures in memory (the page look changed); the disk copies stay.</summary>
    public void ClearMemory()
    {
        _memory.Clear();
        _bytes = 0;
    }

    private void Remember(string key, BitmapSource image)
    {
        long bytes = (long)image.PixelWidth * image.PixelHeight * 4;
        _memory[key] = (image, ++_use, bytes);
        _bytes += bytes;
        while (_bytes > MemoryBudget && _memory.Count > 1)
        {
            var oldest = _memory.MinBy(p => p.Value.Use);
            _memory.Remove(oldest.Key);
            _bytes -= oldest.Value.Bytes;
        }
    }

    private static BitmapSource? ReadPng(string file, PageLook look)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var stream = File.OpenRead(file);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = frame.Format == PixelFormats.Bgr32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
            int stride = source.PixelWidth * 4;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);
            return Tinted(pixels, source.PixelWidth, source.PixelHeight, stride, look);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException) { return null; }
    }

    private void WritePng(string file, BitmapSource image)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            string temp = file + ".tmp";
            using (var stream = File.Create(temp)) encoder.Save(stream);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text.ToUpperInvariant())))[..24];

    // ---- index of facts (page counts, sizes, 3D)

    private void LoadIndex()
    {
        try
        {
            if (!File.Exists(IndexPath)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, PdfFacts>>(File.ReadAllText(IndexPath));
            if (data != null) foreach (var (k, v) in data) _facts[k] = v;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { Log.Warn("Picture index unreadable: " + e.Message); }
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => { _saveTimer!.Stop(); SaveIndex(); }, _ui);
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private void SaveIndex()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            // Keep the index small: drop entries for files that no longer exist.
            var keep = _facts.Where(p => File.Exists(p.Key.Split('|')[0])).Take(5000).ToDictionary(p => p.Key, p => p.Value);
            string temp = IndexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(keep));
            File.Move(temp, IndexPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Removes pictures not used for 60 days (called once at start, in the background).</summary>
    public void TrimDiskCache() => Task.Run(() =>
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            foreach (var f in new DirectoryInfo(_dir).EnumerateFiles("*.png"))
                if (f.LastAccessTimeUtc < DateTime.UtcNow.AddDays(-60) && f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-60)) f.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    });

    public void Dispose()
    {
        _saveTimer?.Stop();
        SaveIndex();
        _renderer.Dispose();
    }
}

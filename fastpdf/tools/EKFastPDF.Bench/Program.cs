using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Native;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Core.Samples;

namespace EKFastPDF.Bench;

/// <summary>
/// Engine-level benchmark (no UI): compares the v1.1 rendering path with the v1.2 worker pool on
/// the synthetic document set, and sweeps worker count and tile size. Usage:
///   EKFastPDF.Bench gen &lt;dir&gt;
///   EKFastPDF.Bench engine &lt;dir&gt; [--workers 1,2,4] [--tiles 256,512,1024] [--out results.json] [--quick]
/// </summary>
public static class Program
{
    private const int ViewW = 2560, ViewH = 1440; // QHD monitor, 100% scaling

    public static int Main(string[] args)
    {
        if (WorkerHost.IsWorkerInvocation(args)) return WorkerHost.Run(args);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length >= 2 && args[0] == "gen")
        {
            var sw = Stopwatch.StartNew();
            SampleDocuments.EnsureAll(args[1]);
            Console.WriteLine($"Samples ready in {args[1]} ({sw.ElapsedMilliseconds} ms)");
            return 0;
        }
        if (args.Length >= 2 && args[0] == "engine") return Engine(args);
        Console.Error.WriteLine("usage: EKFastPDF.Bench gen <dir> | engine <dir> [--workers 1,2,4] [--tiles 256,512,1024] [--out file.json] [--quick]");
        return 1;
    }

    private sealed record ViewSpec(string Name, double Zoom, double CenterX, double CenterY);

    private sealed class Row
    {
        public string Document { get; set; } = "";
        public string Engine { get; set; } = "";
        public int Workers { get; set; }
        public int Tile { get; set; }
        public string View { get; set; } = "";
        public double FirstTileMs { get; set; }
        public double SharpMs { get; set; }
        public int Tiles { get; set; }
        public double OverviewMs { get; set; }
    }

    private static int Engine(string[] args)
    {
        string dir = args[1];
        int[] workerSet = Arg(args, "--workers")?.Split(',').Select(int.Parse).ToArray() ?? DefaultWorkers();
        int[] tileSet = Arg(args, "--tiles")?.Split(',').Select(int.Parse).ToArray() ?? new[] { 256, 512, 1024 };
        string? output = Arg(args, "--out");
        bool quick = args.Contains("--quick");
        bool skipLegacy = args.Contains("--no-legacy");
        Log.MinimumLevel = LogLevel.Warn;
        SampleDocuments.EnsureAll(dir, includeExtreme: !quick);

        var hw = HardwareProfile.Detect();
        Console.WriteLine($"# EK FastPDF engine benchmark");
        Console.WriteLine($"Machine: {RuntimeInformation.OSDescription}; {hw}; recommended workers {hw.RecommendedWorkers}");
        Console.WriteLine($"Viewport {ViewW}x{ViewH} device px. Times in ms (median of runs where noted).");
        Console.WriteLine();

        var docs = new List<(SampleDocuments.Sample Sample, int Page)>
        {
            (SampleDocuments.CadDense, 0),
            (SampleDocuments.Images, 0),
            (SampleDocuments.ManyPages, 300),
        };
        if (!quick) docs.Insert(1, (SampleDocuments.CadExtreme, 0));

        var views = new[]
        {
            new ViewSpec("fit", 0, 0.5, 0.5),
            new ViewSpec("zoom400", 4, 0.45, 0.5),
            new ViewSpec("pan", 4, 0.62, 0.5), // ~half a viewport to the right of zoom400
            new ViewSpec("zoom1600", 16, 0.3, 0.6),
        };

        var rows = new List<Row>();
        foreach (var (sample, page) in docs)
        {
            string path = Path.Combine(dir, sample.FileName);
            Console.WriteLine($"## {sample.FileName} — {sample.Description} (page {page + 1})");
            if (!skipLegacy) rows.AddRange(RunLegacy(sample.FileName, path, page, views));
            foreach (int w in workerSet) rows.AddRange(RunPool(sample.FileName, path, page, views, w, 512));
            int maxW = workerSet.Max();
            foreach (int t in tileSet.Where(t => t != 512)) rows.AddRange(RunPool(sample.FileName, path, page, views, maxW, t));
            Console.WriteLine();
        }

        Console.WriteLine("| Document | Engine | Workers | Tile | View | First tile | Viewport sharp | Tiles |");
        Console.WriteLine("|---|---|---:|---:|---|---:|---:|---:|");
        foreach (var r in rows)
            Console.WriteLine($"| {r.Document} | {r.Engine} | {r.Workers} | {r.Tile} | {r.View} | {r.FirstTileMs:0} | {r.SharpMs:0} | {r.Tiles} |");

        if (output != null)
        {
            var payload = new { machine = RuntimeInformation.OSDescription, hardware = hw.ToString(), recommendedWorkers = hw.RecommendedWorkers, viewport = $"{ViewW}x{ViewH}", rows };
            File.WriteAllText(output, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        return 0;
    }

    private static int[] DefaultWorkers()
    {
        int max = Math.Max(1, Math.Min(8, Environment.ProcessorCount));
        var set = new SortedSet<int> { 1, 2, max, Math.Max(1, max - 1), HardwareProfile.Detect().RecommendedWorkers };
        if (max >= 6) set.Add(4);
        return set.ToArray();
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static (double Zoom, double X, double Y, int PageW, int PageH, double Scale) Geometry(ViewSpec v, double wPt, double hPt)
    {
        double dipsPerPt = 96.0 / 72.0;
        double zoom = v.Zoom > 0 ? v.Zoom : Math.Min((ViewW - 48) / (wPt * dipsPerPt), (ViewH - 48) / (hPt * dipsPerPt));
        double scale = TileGrid.ScaleOf(TileGrid.LevelOf(zoom * dipsPerPt));
        int pw = (int)Math.Round(wPt * scale), ph = (int)Math.Round(hPt * scale);
        double x = Math.Clamp(v.CenterX * pw - ViewW / 2.0, 0, Math.Max(0, pw - ViewW));
        double y = Math.Clamp(v.CenterY * ph - ViewH / 2.0, 0, Math.Max(0, ph - ViewH));
        return (zoom, x, y, pw, ph, scale);
    }

    private static List<Row> RunLegacy(string name, string path, int page, ViewSpec[] views)
    {
        var rows = new List<Row>();
        var doc = LegacyEngine.Open(path);
        try
        {
            var (wPt, hPt) = LegacyEngine.PageSize(doc, page);
            foreach (var v in views)
            {
                var g = Geometry(v, wPt, hPt);
                // v1.1 keyed tiles on the exact raster size, and re-parsed the page per tile.
                var (first, all, tiles) = LegacyEngine.RenderView(doc, page, g.PageW, g.PageH, g.X, g.Y, Math.Min(ViewW, g.PageW), Math.Min(ViewH, g.PageH));
                rows.Add(new Row { Document = name, Engine = "v1.1", Workers = 1, Tile = LegacyEngine.TileSize, View = v.Name, FirstTileMs = first, SharpMs = all, Tiles = tiles });
                Console.WriteLine($"  v1.1      {v.Name,-9} first {first,7:0} ms  sharp {all,7:0} ms  ({tiles} tiles)");
            }
        }
        finally { LegacyEngine.Close(doc); }
        return rows;
    }

    private static List<Row> RunPool(string name, string path, int page, ViewSpec[] views, int workers, int tile)
    {
        var rows = new List<Row>();
        using var pool = new RenderPool(new RenderPoolOptions
        {
            WorkerExecutable = Environment.ProcessPath!,
            WorkerCount = workers,
        });
        // Start worker processes up front (the app does this at start-up, before a file is opened).
        int warm = pool.RegisterDocument(path);
        pool.OpenDocumentAsync(warm).GetAwaiter().GetResult();
        pool.CloseDocument(warm);
        pool.Warmup();
        Thread.Sleep(300);

        int doc = pool.RegisterDocument(path);
        var info = pool.OpenDocumentAsync(doc).GetAwaiter().GetResult();
        double wPt = info.PageWidths[page], hPt = info.PageHeights[page];

        // Overview (first meaningful pixel in the app): long edge 1600 px, one job.
        var overviewScale = TileGrid.OverviewScale(wPt, hPt, 1600);
        var (ow, oh) = TileGrid.PagePixels(wPt, hPt, TileGrid.LevelOf(overviewScale));
        var swo = Stopwatch.StartNew();
        RenderAll(pool, doc, page, ow, oh, new List<(int, int, int, int)> { (0, 0, ow, oh) });
        double overviewMs = swo.Elapsed.TotalMilliseconds;

        foreach (var v in views)
        {
            var g = Geometry(v, wPt, hPt);
            var (c0, r0, c1, r1) = TileGrid.Range(g.X, g.Y, g.X + Math.Min(ViewW, g.PageW), g.Y + Math.Min(ViewH, g.PageH), tile, g.PageW, g.PageH);
            var regions = new List<(int X, int Y, int W, int H)>();
            double cx = g.X + ViewW / 2.0, cy = g.Y + ViewH / 2.0;
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    regions.Add(TileGrid.WithBleed(c, r, tile, g.PageW, g.PageH));
            regions.Sort((a, b) => D(a).CompareTo(D(b)));
            var (first, all) = RenderAll(pool, doc, page, g.PageW, g.PageH, regions);
            rows.Add(new Row { Document = name, Engine = "v1.2 pool", Workers = workers, Tile = tile, View = v.Name, FirstTileMs = first, SharpMs = all, Tiles = regions.Count, OverviewMs = overviewMs });
            Console.WriteLine($"  pool w={workers} t={tile,-4} {v.Name,-9} first {first,7:0} ms  sharp {all,7:0} ms  ({regions.Count} tiles; overview {overviewMs:0} ms)");

            double D((int X, int Y, int W, int H) t) => Math.Pow(t.X + t.W / 2.0 - cx, 2) + Math.Pow(t.Y + t.H / 2.0 - cy, 2);
        }
        pool.CloseDocument(doc);
        return rows;
    }

    private static unsafe (double First, double All) RenderAll(RenderPool pool, int doc, int page, int pw, int ph, List<(int X, int Y, int W, int H)> regions)
    {
        var sw = Stopwatch.StartNew();
        double first = 0;
        int remaining = regions.Count;
        using var done = new ManualResetEventSlim(false);
        string? error = null;
        int order = 0;
        foreach (var (x, y, w, h) in regions)
        {
            var job = new RenderJob(doc, page, pw, ph, x, y, w, h, Pdfium.FPDF_ANNOT | Pdfium.FPDF_LCD_TEXT, order++, (j, result) =>
            {
                if (result.Outcome == JobOutcome.Completed)
                {
                    // Same copy the viewer performs when it creates its bitmap.
                    var copy = GC.AllocateUninitializedArray<byte>(result.Width * 4 * result.Height);
                    for (int row = 0; row < result.Height; row++)
                        Marshal.Copy(result.Pixels + row * result.Stride, copy, row * result.Width * 4, result.Width * 4);
                    if (first == 0) first = sw.Elapsed.TotalMilliseconds;
                }
                else error = result.Error ?? result.Outcome.ToString();
                if (Interlocked.Decrement(ref remaining) == 0) done.Set();
            });
            pool.Submit(job);
        }
        done.Wait();
        if (error != null) throw new InvalidOperationException("Render failed: " + error);
        return (first, sw.Elapsed.TotalMilliseconds);
    }
}

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Samples;
using EKFastPDF.Services;
using EKFastPDF.Viewer;

namespace EKFastPDF;

/// <summary>
/// "EKFastPDF.exe --benchmark &lt;output folder&gt; [--quick]": drives the real viewer (same window,
/// same render pool, same GPU presentation) through scripted open / zoom / pan / page-jump /
/// tab-switch / search scenarios on the synthetic document set and writes results.json,
/// results.md and screenshots. Used in CI and intended to be run on the target PC.
/// </summary>
public sealed class BenchmarkRunner
{
    private readonly MainWindow _window;
    private readonly AppServices _services;
    private readonly string _output;
    private readonly bool _quick;
    private readonly List<Dictionary<string, object?>> _results = new();
    private readonly StringBuilder _md = new();

    public BenchmarkRunner(MainWindow window, AppServices services, string[] args)
    {
        _window = window;
        _services = services;
        int i = Array.IndexOf(args, "--benchmark");
        _output = Path.GetFullPath(i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : "benchmark-results");
        _quick = args.Contains("--quick");
    }

    private PdfViewport View => _window.Viewport;

    public async Task RunAsync()
    {
        int exit = 0;
        try
        {
            Directory.CreateDirectory(_output);
            string samples = Path.Combine(_output, "samples");
            _window.WindowState = WindowState.Normal;
            _window.Width = 1600;
            _window.Height = 1000;
            await Task.Run(() => SampleDocuments.EnsureAll(samples, includeExtreme: !_quick));
            await Task.Delay(1500); // let worker processes finish starting, as on a normal launch

            var gpu = _services.Gpu.Primary;
            _md.AppendLine("# EK FastPDF viewer benchmark").AppendLine();
            _md.AppendLine($"- Machine: {Environment.MachineName}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            _md.AppendLine($"- CPU: {_services.Hardware}; render workers: {_services.Pool.WorkerCount}");
            _md.AppendLine($"- GPU: {gpu?.ToString() ?? "none"}; WPF render tier {RenderCapability.Tier >> 16}; mode {RenderOptions.ProcessRenderMode}");
            _md.AppendLine($"- Viewport: {View.ActualWidth:0}x{View.ActualHeight:0} DIP at {VisualTreeHelper.GetDpi(View).DpiScaleX:0.##}x DPI");
            _md.AppendLine();
            _md.AppendLine("| Document | Scenario | Result (ms) | Frame interval p50/p95 | UI frame build p95 | Input→frame p95 |");
            _md.AppendLine("|---|---|---:|---:|---:|---:|");

            var docs = new List<SampleDocuments.Sample> { SampleDocuments.CadDense, SampleDocuments.Images, SampleDocuments.ManyPages, SampleDocuments.CadSet };
            if (!_quick) docs.Insert(1, SampleDocuments.CadExtreme);
            foreach (var sample in docs) await RunDocument(Path.Combine(samples, sample.FileName), sample.FileName);

            await TabSwitching();
            await SearchWhileRendering();
            Memory();
        }
        catch (Exception e)
        {
            Log.Error("Benchmark failed", e);
            _results.Add(new() { ["error"] = e.ToString() });
            _md.AppendLine().AppendLine("**Benchmark failed:** " + e.Message);
            exit = 2;
        }
        File.WriteAllText(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(_output, "results.md"), _md.ToString());
        Log.Info("Benchmark written to " + _output);
        Application.Current.Shutdown(exit);
    }

    private async Task RunDocument(string path, string name)
    {
        // Open: time to first pixels and to a fully sharp first view.
        View.Metrics.ResetFirstPixel();
        var sw = Stopwatch.StartNew();
        _window.OpenFiles(new[] { path });
        var session = _window.ActiveSession!;
        while (session.State == SessionState.Opening) await Task.Delay(2);
        if (!session.IsReady) throw new InvalidOperationException($"{name}: {session.Status}");
        double opened = sw.Elapsed.TotalMilliseconds;
        double sharp = await WaitSharp(sw);
        Record(name, "open: document ready", opened);
        Record(name, "open: first pixels", View.Metrics.FirstPixelMs);
        Record(name, "open: viewport fully sharp", sharp);
        Screenshot(name + "-open");

        var anchor = new Point(View.ActualWidth * 0.55 * Dpi, View.ActualHeight * 0.45 * Dpi);

        // Zoom in with the wheel (10 notches at 60 Hz), then wait for full sharpness.
        await Interact(name, "zoom in 10 notches (cold)", 10, _ => View.ZoomStep(1, anchor));
        Screenshot(name + "-zoomed");
        // Back out over the same ladder steps: served from cache.
        await Interact(name, "zoom out 10 notches (cached levels)", 10, _ => View.ZoomStep(-1, anchor));
        // Rapid reversals.
        await Interact(name, "rapid zoom reversals 5x(3 in, 3 out)", 30, i => View.ZoomStep((i / 3) % 2 == 0 ? 1 : -1, anchor));
        // Zoom deep and pan across the drawing.
        View.ZoomStep(12, anchor);
        await WaitSharp(Stopwatch.StartNew());
        await Interact(name, "drag-pan 90 frames at 1600%-class zoom", 90, i => View.ScrollBy(i < 45 ? -14 : 14, i < 45 ? -5 : 5));
        Screenshot(name + "-panned");

        if (session.PageCount > 20)
        {
            View.FitWidth();
            await WaitSharp(Stopwatch.StartNew());
            var jump = Stopwatch.StartNew();
            View.GoToPage(session.PageCount / 2);
            Record(name, "jump to middle page: sharp", await WaitSharp(jump));
            jump.Restart();
            View.GoToPage(1);
            Record(name, "jump back to page 2: sharp", await WaitSharp(jump));
        }
        View.FitPage();
        await WaitSharp(Stopwatch.StartNew());
    }

    private async Task Interact(string doc, string scenario, int steps, Action<int> step)
    {
        await WaitSharp(Stopwatch.StartNew());
        long hitsBefore = _services.Tiles.Hits, missesBefore = _services.Tiles.Misses;
        View.Metrics.Reset();
        for (int i = 0; i < steps; i++)
        {
            View.NoteInput();
            step(i);
            await Task.Delay(16);
        }
        var interaction = View.Metrics.Snapshot();
        var settle = Stopwatch.StartNew();
        double sharp = await WaitSharp(settle);
        long hits = _services.Tiles.Hits - hitsBefore, misses = _services.Tiles.Misses - missesBefore;
        Record(doc, scenario + ": sharp after last input", sharp, interaction, hits + misses > 0 ? (double)hits / (hits + misses) : null);
    }

    private async Task TabSwitching()
    {
        var sessions = _window.Sessions.ToList();
        if (sessions.Count < 2) return;
        foreach (var s in sessions)
        {
            var sw = Stopwatch.StartNew();
            View.NoteInput();
            _window.ActivateSession(s);
            double sharp = await WaitSharp(sw);
            Record(s.Title, "tab switch: sharp", sharp);
        }
    }

    private async Task SearchWhileRendering()
    {
        var target = _window.Sessions.FirstOrDefault(s => s.PageCount >= 600);
        if (target is null) return;
        _window.ActivateSession(target);
        await WaitSharp(Stopwatch.StartNew());
        View.Metrics.Reset();
        target.Search.Start(SampleDocuments.SearchWord);
        // Keep panning while the whole document is searched in the background.
        int i = 0;
        var sw = Stopwatch.StartNew();
        while (target.Search.IsRunning && sw.Elapsed < TimeSpan.FromMinutes(3))
        {
            View.NoteInput();
            View.ScrollBy(0, (i++ / 40) % 2 == 0 ? -20 : 20);
            await Task.Delay(16);
        }
        var m = View.Metrics.Snapshot();
        Record(target.Title, $"search {target.PageCount} pages while panning: first hit", target.Search.FirstHitMs, m);
        Record(target.Title, $"search {target.PageCount} pages while panning: complete ({target.Search.Count} hits)", target.Search.CompletedMs);
        if (target.Search.Count != target.PageCount)
            throw new InvalidOperationException($"Search found {target.Search.Count} markers, expected {target.PageCount}");
        target.Search.Next();
        await WaitSharp(Stopwatch.StartNew());
        Screenshot("search-highlight");
    }

    private void Memory()
    {
        using var self = Process.GetCurrentProcess();
        long workers = 0;
        int count = 0;
        foreach (var p in Process.GetProcessesByName(self.ProcessName))
        {
            using (p)
            {
                if (p.Id == self.Id) continue;
                try { workers += p.WorkingSet64; count++; } catch (InvalidOperationException) { }
            }
        }
        var vram = GpuInfo.QueryProcessVideoMemory();
        var pool = _services.Pool.Statistics;
        var (p50, p95, n) = pool.RenderLatency();
        var (q50, q95, _) = pool.QueueLatency();
        var entry = new Dictionary<string, object?>
        {
            ["viewerWorkingSetMB"] = self.WorkingSet64 / 1048576.0,
            ["viewerPrivateMB"] = self.PrivateMemorySize64 / 1048576.0,
            ["workerProcesses"] = count,
            ["workersWorkingSetMB"] = workers / 1048576.0,
            ["tileCacheMB"] = _services.Tiles.Bytes / 1048576.0,
            ["tileCacheHitRate"] = _services.Tiles.HitRate,
            ["vramUsageMB"] = vram?.CurrentUsage / 1048576.0,
            ["vramBudgetMB"] = vram?.Budget / 1048576.0,
            ["tileRenderP50"] = p50, ["tileRenderP95"] = p95, ["tiles"] = n,
            ["queueWaitP50"] = q50, ["queueWaitP95"] = q95,
            ["workerCrashes"] = pool.Crashes,
        };
        _results.Add(entry);
        _md.AppendLine().AppendLine("## Resources at end of run").AppendLine();
        foreach (var (k, v) in entry) _md.AppendLine($"- {k}: {Format(v)}");
    }

    private static string Format(object? v) => v switch
    {
        null => "n/a",
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    private double Dpi => VisualTreeHelper.GetDpi(View).DpiScaleX;

    private async Task<double> WaitSharp(Stopwatch since, int timeoutMs = 120_000)
    {
        await Task.Delay(1);
        while (since.ElapsedMilliseconds < timeoutMs)
        {
            if (View.IsSharp) return since.Elapsed.TotalMilliseconds;
            await Task.Delay(4);
        }
        throw new TimeoutException("Viewport did not become sharp within " + timeoutMs + " ms");
    }

    private void Record(string doc, string scenario, double ms, ViewerMetrics.Summary? m = null, double? hitRate = null)
    {
        _results.Add(new()
        {
            ["document"] = doc, ["scenario"] = scenario, ["ms"] = ms,
            ["frameIntervalP50"] = m?.FrameIntervalMs.P50, ["frameIntervalP95"] = m?.FrameIntervalMs.P95,
            ["frameBuildP50"] = m?.FrameBuildMs.P50, ["frameBuildP95"] = m?.FrameBuildMs.P95,
            ["inputLatencyP50"] = m?.InputLatencyMs.P50, ["inputLatencyP95"] = m?.InputLatencyMs.P95,
            ["frames"] = m?.FrameIntervalMs.Count, ["cacheHitRate"] = hitRate,
        });
        string frames = m is null ? "" : $"{m.FrameIntervalMs.P50:0.0} / {m.FrameIntervalMs.P95:0.0}";
        string build = m is null ? "" : $"{m.FrameBuildMs.P95:0.00}";
        string latency = m is null ? "" : $"{m.InputLatencyMs.P95:0.0}";
        _md.AppendLine($"| {doc} | {scenario}{(hitRate is { } h ? $" (cache hits {h:P0})" : "")} | {ms:0} | {frames} | {build} | {latency} |");
        Log.Info($"BENCH {doc} | {scenario} | {ms:0} ms");
    }

    private void Screenshot(string name)
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(_window);
            var root = (FrameworkElement)_window.Content;
            var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bmp.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(Path.Combine(_output, name + ".png"));
            encoder.Save(file);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException)
        {
            Log.Warn("Screenshot failed: " + e.Message);
        }
    }
}

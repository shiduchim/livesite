using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Viewer;

namespace EKFastPDF.Services;

/// <summary>Process-wide services shared by all tabs.</summary>
public sealed class AppServices : IDisposable
{
    public AppServices(AppSettings settings)
    {
        Settings = settings;
        Hardware = HardwareProfile.Detect();
        Gpu = GpuInfo.Query();
        int workers = settings.RenderWorkers > 0 ? Math.Clamp(settings.RenderWorkers, 1, 32) : Hardware.RecommendedWorkers;
        long cache = settings.CacheMegabytes > 0 ? settings.CacheMegabytes * 1024L * 1024 : Hardware.RecommendedCacheBytes;
        Log.Info($"Hardware: {Hardware}; GPU: {Gpu.Primary?.ToString() ?? "none reported"}");
        Log.Info($"Render workers: {workers} ({(settings.RenderWorkers > 0 ? "settings" : "automatic")}); tile cache {cache / (1024 * 1024)} MB; tile {settings.TileSize}px");
        Pool = new RenderPool(new RenderPoolOptions
        {
            WorkerExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unknown."),
            WorkerCount = workers,
            // Large drawings at high zoom keep several parsed pages; text documents need few.
            MaxLoadedPagesPerWorker = 6,
            MaxConcurrentSearches = Math.Max(1, workers / 4),
        });
        Tiles = new TileStore(Pool, cache);
    }

    public AppSettings Settings { get; }
    public HardwareProfile Hardware { get; }
    public GpuInfo Gpu { get; }
    public RenderPool Pool { get; }
    public TileStore Tiles { get; }

    public void Dispose()
    {
        Pool.Dispose();
    }
}

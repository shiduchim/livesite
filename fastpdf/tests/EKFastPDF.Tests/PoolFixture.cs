using EKFastPDF.Core.Rendering;
using EKFastPDF.Core.Samples;

namespace EKFastPDF.Tests;

/// <summary>Shared sample documents + a render pool whose workers are the bench executable.</summary>
public sealed class PoolFixture : IDisposable
{
    public PoolFixture()
    {
        SampleDir = Path.Combine(Path.GetTempPath(), "ekfastpdf-test-samples-v3");
        SampleDocuments.EnsureAll(SampleDir, includeExtreme: true);
        Pool = new RenderPool(new RenderPoolOptions { WorkerExecutable = WorkerExe, WorkerCount = 3 });
    }

    public static string WorkerExe
    {
        get
        {
            string name = OperatingSystem.IsWindows() ? "EKFastPDF.Bench.exe" : "EKFastPDF.Bench";
            string path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path)) throw new FileNotFoundException("Worker executable missing next to the tests", path);
            return path;
        }
    }

    public string SampleDir { get; }
    public RenderPool Pool { get; }
    public string PathOf(SampleDocuments.Sample s) => Path.Combine(SampleDir, s.FileName);

    public void Dispose() => Pool.Dispose();
}

[CollectionDefinition("pool")]
public sealed class PoolCollection : ICollectionFixture<PoolFixture> { }

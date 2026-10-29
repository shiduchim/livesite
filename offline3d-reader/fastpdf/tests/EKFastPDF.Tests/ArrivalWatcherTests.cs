using System.Collections.Concurrent;
using EKFastPDF.Core.Library;

namespace EKFastPDF.Tests;

/// <summary>Real files dropped into a watched folder (Linux inotify here; Windows notifications in the app).</summary>
public sealed class ArrivalWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ekpdf-arrive-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<(string Path, DateTime At)> _arrived = new();
    private readonly PdfArrivalWatcher _watcher;
    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% test file\n%%EOF\n");

    public ArrivalWatcherTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "already-here.pdf"), Pdf);
        _watcher = new PdfArrivalWatcher(a => a());
        _watcher.Arrived += (_, path) => _arrived.Enqueue((path, DateTime.UtcNow));
        _watcher.Watch(new[] { _dir });
    }

    public void Dispose() { _watcher.Dispose(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private async Task<bool> WaitFor(Func<bool> condition, int ms = 3000)
    {
        for (int t = 0; t < ms; t += 25) { if (condition()) return true; await Task.Delay(25); }
        return condition();
    }

    private int Count(string name) => _arrived.Count(a => Path.GetFileName(a.Path) == name);

    [Fact]
    public async Task CopiedIn_OpensWithinHalfASecond_Once()
    {
        var start = DateTime.UtcNow;
        File.WriteAllBytes(Path.Combine(_dir, "sheet.pdf"), Pdf);
        Assert.True(await WaitFor(() => Count("sheet.pdf") == 1, 2000), "arrived");
        Assert.True((_arrived.First().At - start).TotalMilliseconds < 600, "fast: " + (_arrived.First().At - start).TotalMilliseconds + " ms");
        await Task.Delay(2500); // late notifications and the 1 s listing must not report it again
        Assert.Equal(1, Count("sheet.pdf"));
        Assert.Equal(0, Count("already-here.pdf"));
    }

    [Fact]
    public async Task SlowWrite_ReportedOnlyAfterTheWriterIsDone()
    {
        string path = Path.Combine(_dir, "export.pdf");
        DateTime closed;
        using (var s = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            for (int i = 0; i < 20; i++) { s.Write(Pdf); s.Flush(); await Task.Delay(50); }
            closed = DateTime.UtcNow;
        }
        Assert.True(await WaitFor(() => Count("export.pdf") == 1), "arrived");
        Assert.True(_arrived.First(a => a.Path == path).At >= closed.AddMilliseconds(-60), "not before the writer finished");
    }

    [Fact]
    public async Task DownloadRenamedIntoPlace_IsReported()
    {
        string temp = Path.Combine(_dir, "Unconfirmed 1234.crdownload");
        File.WriteAllBytes(temp, Pdf);
        File.Move(temp, Path.Combine(_dir, "download.pdf"));
        Assert.True(await WaitFor(() => Count("download.pdf") == 1), "arrived");
    }

    [Fact]
    public async Task WrittenOverAnExistingFile_IsReportedAgain()
    {
        string path = Path.Combine(_dir, "level1.pdf");
        File.WriteAllBytes(path, Pdf);
        Assert.True(await WaitFor(() => Count("level1.pdf") == 1), "first");
        await Task.Delay(300);
        File.WriteAllBytes(path, Pdf.Concat(Pdf).ToArray()); // re-export with the same name
        Assert.True(await WaitFor(() => Count("level1.pdf") == 2), "second");
    }

    [Fact]
    public async Task FolderCreatedAfterWatching_IsFoundByTheListing()
    {
        string later = Path.Combine(_dir, "later");
        _watcher.Watch(new[] { _dir, later });
        Directory.CreateDirectory(later);
        File.WriteAllBytes(Path.Combine(later, "late.pdf"), Pdf);
        Assert.True(await WaitFor(() => Count("late.pdf") == 1, 4000), "found by listing");
    }
}

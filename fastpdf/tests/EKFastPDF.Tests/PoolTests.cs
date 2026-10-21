using System.Diagnostics;
using System.Runtime.InteropServices;
using EKFastPDF.Core.Native;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Core.Samples;

namespace EKFastPDF.Tests;

[Collection("pool")]
public class PoolTests
{
    private readonly PoolFixture _f;
    public PoolTests(PoolFixture f) { _f = f; }

    private async Task<(int Doc, DocumentInfo Info)> Open(SampleDocuments.Sample s, string? password = null)
    {
        int doc = _f.Pool.RegisterDocument(_f.PathOf(s), password);
        return (doc, await _f.Pool.OpenDocumentAsync(doc));
    }

    private static Task<(RenderResult Result, byte[] Pixels)> Render(RenderPool pool, int doc, int page, int pw, int ph, int x, int y, int w, int h)
    {
        var tcs = new TaskCompletionSource<(RenderResult, byte[])>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.Submit(new RenderJob(doc, page, pw, ph, x, y, w, h, Pdfium.FPDF_ANNOT, 0, (_, r) =>
        {
            byte[] px = Array.Empty<byte>();
            if (r.Outcome == JobOutcome.Completed)
            {
                px = new byte[r.Width * 4 * r.Height];
                for (int row = 0; row < r.Height; row++)
                    Marshal.Copy(r.Pixels + row * r.Stride, px, row * r.Width * 4, r.Width * 4);
            }
            tcs.SetResult((r, px));
        }));
        return tcs.Task;
    }

    [Theory]
    [InlineData("simple-text.pdf", 10)]
    [InlineData("many-pages-600.pdf", 600)]
    [InlineData("cad-dense-e1.pdf", 1)]
    [InlineData("cad-set-12-sheets.pdf", 12)]
    [InlineData("image-heavy.pdf", 8)]
    [InlineData("rotated.pdf", 4)]
    public async Task OpensSamples(string file, int pages)
    {
        var sample = SampleDocuments.All.Single(s => s.FileName == file);
        var (doc, info) = await Open(sample);
        Assert.Equal(pages, info.PageCount);
        _f.Pool.CloseDocument(doc);
    }

    [Fact]
    public async Task PasswordProtected_ReportsPassword_ThenOpensWithIt()
    {
        var e = await Assert.ThrowsAsync<PdfOpenException>(() => Open(SampleDocuments.Encrypted));
        Assert.Equal(OpenFailure.PasswordRequired, e.Failure);
        var (doc, info) = await Open(SampleDocuments.Encrypted, "ekpdf");
        Assert.Equal(3, info.PageCount);
        var hits = await _f.Pool.SearchPageAsync(doc, 1, SampleDocuments.SearchWord, false, false, 100, 0, default);
        Assert.Single(hits.Hits);
        _f.Pool.CloseDocument(doc);
    }

    [Fact]
    public async Task CorruptAndMissingFiles_FailGracefully()
    {
        var corrupt = await Assert.ThrowsAsync<PdfOpenException>(() => Open(SampleDocuments.Corrupt));
        Assert.Contains(corrupt.Failure, new[] { OpenFailure.Corrupt, OpenFailure.Other });
        int missing = _f.Pool.RegisterDocument(Path.Combine(_f.SampleDir, "does-not-exist.pdf"));
        var e = await Assert.ThrowsAsync<PdfOpenException>(() => _f.Pool.OpenDocumentAsync(missing));
        Assert.Equal(OpenFailure.FileNotFound, e.Failure);
        // The pool is still healthy afterwards.
        var (doc, info) = await Open(SampleDocuments.Simple);
        Assert.Equal(10, info.PageCount);
        _f.Pool.CloseDocument(doc);
    }

    [Fact]
    public async Task RotatedPages_ReportDisplaySizeAndHighlightsInsidePage()
    {
        var (doc, info) = await Open(SampleDocuments.Rotated);
        Assert.Equal(612, info.PageWidths[0], 1);
        Assert.Equal(792, info.PageWidths[1], 1); // /Rotate 90 swaps width and height
        Assert.Equal(612, info.PageHeights[1], 1);
        for (int p = 0; p < 4; p++)
        {
            var res = await _f.Pool.SearchPageAsync(doc, p, SampleDocuments.SearchWord, true, false, 10, 0, default);
            var hit = Assert.Single(res.Hits);
            float l = hit.Rects[0], t = hit.Rects[1], r = hit.Rects[2], b = hit.Rects[3];
            Assert.True(l >= 0 && t >= 0 && r <= info.PageWidths[p] && b <= info.PageHeights[p], $"page {p}: {l},{t},{r},{b}");
            bool vertical = (b - t) > (r - l);
            Assert.Equal(p % 2 == 1, vertical); // text on rotated pages runs vertically on screen
        }
        _f.Pool.CloseDocument(doc);
    }

    [Fact]
    public async Task Search_FindsOneMarkerPerPage_AndReportsCharCount()
    {
        var (doc, info) = await Open(SampleDocuments.ManyPages);
        int total = 0;
        foreach (int p in new[] { 0, 299, 599 })
        {
            var r = await _f.Pool.SearchPageAsync(doc, p, SampleDocuments.SearchWord.ToLowerInvariant(), false, false, 100, 0, default);
            Assert.True(r.CharCount > 1000);
            total += r.Hits.Count;
        }
        Assert.Equal(3, total);
        var none = await _f.Pool.SearchPageAsync(doc, 5, "zzzz-not-there", false, false, 100, 0, default);
        Assert.Empty(none.Hits);
        _f.Pool.CloseDocument(doc);
    }

    [Theory]
    [InlineData("cad-dense-e1.pdf", 0.9)]
    [InlineData("simple-text.pdf", 2.7)]
    [InlineData("simple-text.pdf", 9.3)]
    [InlineData("cad-dense-e1.pdf", 6.1)]
    public async Task AdjacentTiles_AreIdenticalToOneLargeRender(string file, double scale)
    {
        // Guarantees seamless tiling: rendering a page window in pieces gives the same pixels.
        var sample = SampleDocuments.All.Single(s => s.FileName == file);
        var (doc, info) = await Open(sample);
        long level = TileGrid.LevelOf(scale);
        var (pw, ph) = TileGrid.PagePixels(info.PageWidths[0], info.PageHeights[0], level);
        int x = pw / 3, y = ph / 3, w = 700, h = 500;
        var whole = await Render(_f.Pool, doc, 0, pw, ph, x, y, w, h);
        var left = await Render(_f.Pool, doc, 0, pw, ph, x, y, 300, h);
        var right = await Render(_f.Pool, doc, 0, pw, ph, x + 300, y, w - 300, h);
        Assert.Equal(JobOutcome.Completed, whole.Result.Outcome);
        int diff = 0;
        var cols = new List<int>();
        for (int row = 0; row < h; row++)
        for (int col = 0; col < w; col++)
        {
            var src = col < 300 ? left : right;
            int sc = col < 300 ? col : col - 300;
            for (int ch = 0; ch < 3; ch++)
                if (Math.Abs(whole.Pixels[row * whole.Result.Width * 4 + col * 4 + ch] - src.Pixels[row * src.Result.Width * 4 + sc * 4 + ch]) > 2) { diff++; cols.Add(col); }
        }
        if (Environment.GetEnvironmentVariable("EKF_DUMP") is string dump)
        {
            File.WriteAllBytes(Path.Combine(dump, file + ".whole.bgra"), whole.Pixels);
            File.WriteAllBytes(Path.Combine(dump, file + ".left.bgra"), left.Pixels);
            File.WriteAllBytes(Path.Combine(dump, file + ".right.bgra"), right.Pixels);
        }
        Assert.True(diff == 0, $"{diff} channel values differ; columns {string.Join(",", cols.Order().Distinct().Take(40))}");
        int ink = 0;
        for (int i = 0; i < whole.Pixels.Length; i += 4) if (whole.Pixels[i] < 200) ink++;
        Assert.True(ink > 100, "rendered region should contain content");
        _f.Pool.CloseDocument(doc);
    }

    [Fact]
    public async Task Cancel_InterruptsRunningRender()
    {
        var (doc, info) = await Open(SampleDocuments.CadExtreme);
        long level = TileGrid.LevelOf(2.0);
        var (pw, ph) = TileGrid.PagePixels(info.PageWidths[0], info.PageHeights[0], level);
        // Warm the page so we measure render cancellation, not parsing.
        await Render(_f.Pool, doc, 0, pw, ph, 0, 0, 64, 64);

        var sw = Stopwatch.StartNew();
        var full = await Render(_f.Pool, doc, 0, pw, ph, 0, 0, 3000, 3000);
        double uncanceled = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(JobOutcome.Completed, full.Result.Outcome);

        var tcs = new TaskCompletionSource<RenderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Same size, equally dense area of the drawing (the right-hand side is the sparse title block).
        var job = new RenderJob(doc, 0, pw, ph, 200, 600, 3000, 3000, Pdfium.FPDF_ANNOT, 0, (_, r) => tcs.SetResult(r));
        sw.Restart();
        _f.Pool.Submit(job);
        while (!job.IsRunning && !job.IsFinished) await Task.Delay(1);
        await Task.Delay(Math.Max(5, (int)(uncanceled / 8)));
        _f.Pool.Cancel(job);
        var result = await tcs.Task;
        double canceled = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(JobOutcome.Canceled, result.Outcome);
        Assert.True(canceled < uncanceled * 0.8, $"cancelled after {canceled:0} ms vs full render {uncanceled:0} ms");
        _f.Pool.CloseDocument(doc);
    }

    [Fact]
    public async Task CloseDocument_WithQueuedAndRunningJobs_CompletesEveryCallback()
    {
        var (doc, info) = await Open(SampleDocuments.CadSet);
        long level = TileGrid.LevelOf(3.0);
        var (pw, ph) = TileGrid.PagePixels(info.PageWidths[0], info.PageHeights[0], level);
        int callbacks = 0;
        var all = new TaskCompletionSource();
        const int Jobs = 60;
        for (int i = 0; i < Jobs; i++)
        {
            int page = i % 12;
            _f.Pool.Submit(new RenderJob(doc, page, pw, ph, (i % 8) * 512, (i / 8) * 512, 512, 512, 0, i, (_, r) =>
            {
                Assert.NotEqual(JobOutcome.Failed, r.Outcome);
                if (Interlocked.Increment(ref callbacks) == Jobs) all.SetResult();
            }));
        }
        await Task.Delay(50);
        _f.Pool.CloseDocument(doc);
        await all.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(Jobs, callbacks);
        Assert.False(_f.Pool.IsRegistered(doc));
        // Jobs for a closed document are refused immediately as canceled.
        var late = await Render(_f.Pool, doc, 0, pw, ph, 0, 0, 10, 10);
        Assert.Equal(JobOutcome.Canceled, late.Result.Outcome);
    }

    [Fact]
    public async Task Search_DoesNotBlockRendering()
    {
        var (doc, info) = await Open(SampleDocuments.ManyPages);
        using var cts = new CancellationTokenSource();
        var searches = Enumerable.Range(0, 600)
            .Select(p => _f.Pool.SearchPageAsync(doc, p, "transformer", false, false, 1000, 100 + p, cts.Token))
            .ToList();
        var sw = Stopwatch.StartNew();
        var (pw, ph) = TileGrid.PagePixels(info.PageWidths[0], info.PageHeights[0], TileGrid.LevelOf(2));
        var tile = await Render(_f.Pool, doc, 10, pw, ph, 0, 0, 512, 512);
        double renderMs = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(JobOutcome.Completed, tile.Result.Outcome);
        int finishedSearches = searches.Count(s => s.IsCompleted);
        Assert.True(finishedSearches < 600, "render should finish while search is still running");
        cts.Cancel();
        foreach (var s in searches)
        {
            try { await s; } catch (OperationCanceledException) { }
        }
        Assert.True(renderMs < 5000, $"render waited {renderMs:0} ms behind search");
        _f.Pool.CloseDocument(doc);
    }

    [Fact]
    public async Task WorkerCrash_IsRecoveredTransparently()
    {
        using var pool = new RenderPool(new RenderPoolOptions { WorkerExecutable = PoolFixture.WorkerExe, WorkerCount = 1 });
        int doc = pool.RegisterDocument(_f.PathOf(SampleDocuments.Simple));
        var info = await pool.OpenDocumentAsync(doc);
        var (pw, ph) = TileGrid.PagePixels(info.PageWidths[0], info.PageHeights[0], TileGrid.LevelOf(1));
        Assert.Equal(JobOutcome.Completed, (await Render(pool, doc, 0, pw, ph, 0, 0, 100, 100)).Result.Outcome);

        foreach (var p in Process.GetProcessesByName("EKFastPDF.Bench"))
        {
            if (p.Id != Environment.ProcessId) { try { p.Kill(); p.WaitForExit(2000); } catch (InvalidOperationException) { } }
        }
        await Task.Delay(100);
        var after = await Render(pool, doc, 3, pw, ph, 0, 0, 100, 100);
        Assert.Equal(JobOutcome.Completed, after.Result.Outcome);
        Assert.True(pool.Statistics.Starts >= 2);
    }
}

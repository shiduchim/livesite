using System.Globalization;
using EKFastPDF.Core.Library;
using EKFastPDF.Core.Samples;

namespace EKFastPDF.Tests;

public class LibraryTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 10, 45, 0); // a Wednesday
    private static LibraryFile F(string name, DateTime t) => new("/x/" + name + ".pdf", name, 1000, t);

    [Fact]
    public void Batches_SplitOnGaps_NewestFirst()
    {
        var files = new[]
        {
            F("a", Now.AddMinutes(-3)), F("b", Now.AddMinutes(-2)), F("c", Now.AddMinutes(-2.5)),  // together
            F("d", Now.AddHours(-20)),                                                              // alone
            F("e", Now.AddDays(-2)), F("f", Now.AddDays(-2).AddSeconds(30)),                        // together
        };
        var b = PdfLibrary.Batches(files);
        Assert.Equal(3, b.Count);
        Assert.Equal(new[] { "b", "c", "a" }, b[0].Files.Select(f => f.Name));
        Assert.Equal(new[] { "d" }, b[1].Files.Select(f => f.Name));
        Assert.Equal(new[] { "f", "e" }, b[2].Files.Select(f => f.Name));
        Assert.Equal(new[] { "b", "c", "a" }, PdfLibrary.NewestBatch(files).Select(f => f.Name));
        Assert.Empty(PdfLibrary.NewestBatch(Array.Empty<LibraryFile>()));
    }

    [Fact]
    public void Batches_ChainedArrivals_StayOneBatch()
    {
        // A 20-sheet set exported over 10 minutes, 30 s apart, is one batch.
        var files = Enumerable.Range(0, 20).Select(i => F("s" + i, Now.AddSeconds(-30 * i))).ToList();
        Assert.Single(PdfLibrary.Batches(files));
    }

    [Fact]
    public void Recent_UsesWindowAndSkipsOpened_OldestFirst()
    {
        var files = new[] { F("new", Now.AddSeconds(-20)), F("older", Now.AddSeconds(-50)), F("old", Now.AddMinutes(-3)) };
        Assert.Equal(new[] { "older", "new" }, PdfLibrary.Recent(files, Now, TimeSpan.FromMinutes(1)).Select(f => f.Name));
        var opened = new HashSet<string> { "/x/new.pdf" };
        Assert.Equal(new[] { "older" }, PdfLibrary.Recent(files, Now, TimeSpan.FromMinutes(1), opened).Select(f => f.Name));
        Assert.Equal(3, PdfLibrary.Recent(files, Now, TimeSpan.FromMinutes(5)).Count);
        Assert.Empty(PdfLibrary.Recent(files, Now, TimeSpan.Zero));
    }

    [Fact]
    public void BatchLabels()
    {
        var c = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("Today 10:42", PdfLibrary.BatchLabel(new DateTime(2026, 10, 28, 10, 42, 0), Now, c));
        Assert.Equal("Yesterday 16:05", PdfLibrary.BatchLabel(new DateTime(2026, 10, 27, 16, 5, 0), Now, c));
        Assert.Equal("Monday 09:12", PdfLibrary.BatchLabel(new DateTime(2026, 10, 26, 9, 12, 0), Now, c));
        Assert.Equal("12 Oct 09:12", PdfLibrary.BatchLabel(new DateTime(2026, 10, 12, 9, 12, 0), Now, c));
        Assert.Equal("12 Oct 2025", PdfLibrary.BatchLabel(new DateTime(2025, 10, 12, 9, 12, 0), Now, c));
    }

    [Fact]
    public void Move_ReordersFolders()
    {
        var list = new List<string> { "a", "b", "c", "d" };
        PdfLibrary.Move(list, 0, 2); Assert.Equal("bcad", string.Concat(list));
        PdfLibrary.Move(list, 3, 0); Assert.Equal("dbca", string.Concat(list));
        PdfLibrary.Move(list, 1, 99); Assert.Equal("dcab", string.Concat(list));
        PdfLibrary.Move(list, -1, 0); Assert.Equal("dcab", string.Concat(list));
    }

    [Fact]
    public void Scan_FindsTopLevelPdfsOnly_NewestFirst()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ekpdf-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "one.pdf"), "x");
            File.WriteAllText(Path.Combine(dir, "TWO.PDF"), "x");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "sub", "deep.pdf"), "x");
            File.SetLastWriteTime(Path.Combine(dir, "one.pdf"), DateTime.Now.AddDays(1));
            var found = PdfLibrary.Scan(dir);
            Assert.Equal(new[] { "one", "TWO" }, found.Select(f => f.Name));
            Assert.Empty(PdfLibrary.Scan(Path.Combine(dir, "missing")));
            Assert.True(PdfLibrary.IsSettled(Path.Combine(dir, "one.pdf")));
            Assert.False(PdfLibrary.IsSettled(Path.Combine(dir, "gone.pdf")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Folder_NameAndSamePath()
    {
        Assert.Equal("Downloads", new LibraryFolder { Path = Path.Combine(Path.GetTempPath(), "Downloads") + Path.DirectorySeparatorChar }.Name);
        Assert.True(PdfLibrary.SamePath(Path.GetTempPath(), Path.GetTempPath() + Path.DirectorySeparatorChar));
        Assert.Equal("2.0 MB", PdfLibrary.FormatSize(2L << 20).Replace(',', '.'));
    }
}

[Collection("pool")]
public class ThumbnailTests
{
    private readonly PoolFixture _f;
    public ThumbnailTests(PoolFixture f) { _f = f; }

    [Fact]
    public async Task RendersFirstAndLaterPages_WithPageAspect()
    {
        using var thumbs = new ThumbnailRenderer(_f.Pool, maxOpenDocuments: 2);
        string path = _f.PathOf(SampleDocuments.CadSet);
        var info = await thumbs.InfoAsync(path);
        Assert.NotNull(info);
        Assert.Equal(12, info!.PageCount);
        var first = await thumbs.RenderAsync(path, 0, 300);
        var last = await thumbs.RenderAsync(path, 11, 300);
        Assert.NotNull(first); Assert.NotNull(last);
        Assert.Equal(300, first!.Width);
        Assert.Equal((int)Math.Round(300 * info.PageHeights[0] / info.PageWidths[0]), first.Height);
        Assert.Equal(first.Stride * first.Height, first.Pixels.Length);
        // White paper at the corner, drawn content inside.
        Assert.True(first.Pixels[0] > 240 && first.Pixels[1] > 240 && first.Pixels[2] > 240, "paper at the corner");
        int center = (first.Height / 2) * first.Stride + first.Width / 2 * 4, varied = 0;
        for (int i = 0; i < first.Pixels.Length; i += 4) if (first.Pixels[i] != first.Pixels[center] || first.Pixels[i + 2] != first.Pixels[center + 2]) varied++;
        Assert.True(varied > first.Width * first.Height / 10, "drawn content");
        Assert.NotEqual(first.Pixels, last!.Pixels);
        Assert.Null(await thumbs.RenderAsync(path, 12, 300));
    }

    [Fact]
    public async Task ManyDocuments_ClosesOldOnes_AndBadFilesGiveNull()
    {
        using var thumbs = new ThumbnailRenderer(_f.Pool, maxOpenDocuments: 2);
        foreach (var s in new[] { SampleDocuments.Simple, SampleDocuments.Rotated, SampleDocuments.CadSet, SampleDocuments.Simple })
            Assert.NotNull(await thumbs.RenderAsync(_f.PathOf(s), 0, 120));
        Assert.Null(await thumbs.InfoAsync(_f.PathOf(SampleDocuments.Corrupt)));
        Assert.Null(await thumbs.InfoAsync(_f.PathOf(SampleDocuments.Encrypted)));
        Assert.Null(await thumbs.RenderAsync(Path.Combine(_f.SampleDir, "missing.pdf"), 0, 120));
    }

    [Fact]
    public async Task HugeWidth_IsLimitedToTheSharedBuffer()
    {
        using var thumbs = new ThumbnailRenderer(_f.Pool);
        var t = await thumbs.RenderAsync(_f.PathOf(SampleDocuments.CadDense), 0, 4096);
        Assert.NotNull(t);
        Assert.True((long)t!.Width * t.Height * 4 <= 48L << 20);
    }
}


[Collection("pool")]
public class SharedOpenTests
{
    private readonly PoolFixture _f;
    public SharedOpenTests(PoolFixture f) { _f = f; }

    [Fact]
    public async Task OpenPdf_CanBeRenamedAndDeleted_WhileStillRendering()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ekpdf-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "sheet.pdf");
            File.Copy(_f.PathOf(SampleDocuments.CadSet), path);
            using var thumbs = new ThumbnailRenderer(_f.Pool);
            Assert.NotNull(await thumbs.RenderAsync(path, 0, 200));
            // Still open in a worker: moving it (what the Recycle Bin does) and deleting must work.
            string moved = Path.Combine(dir, "moved.pdf");
            File.Move(path, moved);
            File.Delete(moved);
            Assert.False(File.Exists(moved));
        }
        finally { Directory.Delete(dir, true); }
    }
}

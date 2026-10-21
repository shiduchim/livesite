using EKFastPDF.Core.Caching;
using EKFastPDF.Core.Layout;
using EKFastPDF.Core.Rendering;

namespace EKFastPDF.Tests;

public class GeometryTests
{
    private static DocumentLayout Layout() =>
        new(new float[] { 612, 3024, 612 }, new float[] { 792, 2160, 792 }, gap: 14);

    [Fact]
    public void ZoomLadder_StepsAreRepeatable()
    {
        double z = 1;
        var up = new List<double>();
        for (int i = 0; i < 9; i++) { z = ZoomLadder.Step(z, 1); up.Add(z); }
        Assert.Equal(2.0, up[5], 6);           // six steps double the zoom
        for (int i = 7; i >= 0; i--) { z = ZoomLadder.Step(z, -1); Assert.Equal(up[i], z, 9); }
        Assert.Equal(1.0, ZoomLadder.Step(z, -1), 9);
    }

    [Fact]
    public void ZoomLadder_SnapsArbitraryZoomToNeighbours()
    {
        Assert.Equal(Math.Pow(2, 1.0 / 6), ZoomLadder.Step(1.05, 1), 6);
        Assert.Equal(1.0, ZoomLadder.Step(1.05, -1), 6);
        Assert.Equal(Viewport.MaxZoom, ZoomLadder.Step(Viewport.MaxZoom, 3));
    }

    [Fact]
    public void ZoomAt_KeepsPointUnderCursorFixed()
    {
        var v = new Viewport();
        v.SetSize(1600, 900, 1.25);
        var layout = Layout();
        v.Fit = FitMode.Page;
        v.ApplyFit(layout, 1);
        var before = v.ScreenToDoc(400, 300);
        v.ZoomAt(v.Zoom * 3.7, 400, 300);
        var after = v.ScreenToDoc(400, 300);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void Clamp_CentersSmallContent_AndRoundsToDevicePixels()
    {
        var v = new Viewport();
        v.SetSize(3000, 2000, 1.5);
        var layout = new DocumentLayout(new float[] { 100 }, new float[] { 100 });
        v.OffsetX = 12345.4; v.OffsetY = -999.7;
        v.Clamp(layout);
        var (x, y) = v.DocToScreen(0, 50);
        Assert.Equal(1500, x, 0);
        Assert.Equal(1000, y, 0);
        Assert.Equal(Math.Round(v.OffsetX), v.OffsetX);
    }

    [Fact]
    public void FitPage_ShowsWholePage()
    {
        var v = new Viewport();
        v.SetSize(1920, 1080, 1);
        var layout = Layout();
        v.Fit = FitMode.Page;
        v.ApplyFit(layout, 1);
        var r = layout.PageRect(1);
        var (x0, y0) = v.DocToScreen(r.X, r.Y);
        var (x1, y1) = v.DocToScreen(r.Right, r.Bottom);
        Assert.True(x0 >= 0 && y0 >= 0 && x1 <= 1920 && y1 <= 1080, $"{x0},{y0} {x1},{y1}");
        Assert.Equal(1, v.CurrentPage(layout));
    }

    [Fact]
    public void Layout_FindsPagesInArea()
    {
        var layout = Layout();
        Assert.Equal(0, layout.PageAtY(10));
        Assert.Equal(0, layout.PageAtY(800)); // in the gap below page 0
        Assert.Equal(1, layout.PageAtY(810));
        Assert.Equal(new[] { 0, 1 }, layout.PagesIn(new RectD(-50, 700, 100, 200)).ToArray());
        Assert.Equal(new[] { 2 }, layout.PagesIn(new RectD(-50, layout.PageRect(2).Y + 5, 10, 10)).ToArray());
        Assert.Empty(layout.PagesIn(new RectD(5000, 0, 10, 10)));
    }

    [Fact]
    public void TileGrid_RangeAndBleed()
    {
        Assert.Equal((0, 0, 1, 1), TileGrid.Range(0, 0, 1000, 600, 512, 2000, 2000));
        Assert.Equal((1, 0, 1, 0), TileGrid.Range(600, 0, 1000, 100, 512, 1000, 1000));
        Assert.Equal((0, 0, -1, -1), TileGrid.Range(5000, 0, 6000, 100, 512, 1000, 1000));
        Assert.Equal((511, 0, 489, 400), TileGrid.WithBleed(1, 0, 512, 1000, 400));
        Assert.Equal((0, 0, 513, 400), TileGrid.WithBleed(0, 0, 512, 1000, 400));
        long level = TileGrid.LevelOf(1.3333333333);
        Assert.Equal(level, TileGrid.LevelOf(TileGrid.ScaleOf(level)));
    }

    [Fact]
    public void Cache_EvictsLeastRecent_ButNeverCurrentFrame()
    {
        var evicted = new List<int>();
        var cache = new ByteBudgetCache<int, string>(300, (k, _) => evicted.Add(k));
        cache.CurrentFrame = 1;
        cache.Add(1, "a", 100);
        cache.Add(2, "b", 100);
        cache.Add(3, "c", 100);
        cache.CurrentFrame = 2;
        cache.Touch(1);
        cache.Add(4, "d", 100);
        Assert.Equal(new[] { 2 }, evicted);
        Assert.True(cache.Contains(1));
        // Everything used this frame: the budget may be exceeded rather than evict visible tiles.
        cache.CurrentFrame = 3;
        cache.Touch(1); cache.Touch(3); cache.Touch(4);
        cache.Add(5, "e", 100);
        Assert.Equal(400, cache.Bytes);
        cache.CurrentFrame = 4;
        cache.Trim();
        Assert.Equal(300, cache.Bytes);
    }
}

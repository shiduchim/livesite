using EKFastPDF.Core.Rendering;

namespace EKFastPDF.Tests;

public class PageLookTests
{
    private static (int R, int G, int B) Run(PageLook look, int r, int g, int b)
    {
        var px = new byte[] { (byte)b, (byte)g, (byte)r, 255 };
        PageLookPixels.Apply(px, look);
        return (px[2], px[1], px[0]);
    }

    [Fact]
    public void Original_LeavesPixelsAlone() => Assert.Equal((12, 200, 77), Run(PageLook.Original, 12, 200, 77));

    [Fact]
    public void Dark_SwapsPaperAndInk_SoftEnds()
    {
        Assert.Equal((18, 18, 18), Run(PageLook.Dark, 255, 255, 255));   // white paper → soft black
        Assert.Equal((230, 230, 230), Run(PageLook.Dark, 0, 0, 0));      // black lines → soft white
    }

    [Theory]
    [InlineData(255, 0, 0)]      // red stays red
    [InlineData(0, 0, 160)]      // dark blue becomes light blue
    [InlineData(0, 128, 0)]      // green stays green
    public void Dark_KeepsHue(int r, int g, int b)
    {
        var (r2, g2, b2) = Run(PageLook.Dark, r, g, b);
        int Max(int a, int c, int d) => Math.Max(a, Math.Max(c, d));
        // The strongest channel stays the strongest.
        string Top(int x, int y, int z) => x == Max(x, y, z) ? "r" : y == Max(x, y, z) ? "g" : "b";
        Assert.Equal(Top(r, g, b), Top(r2, g2, b2));
        // Lightness (max + min) is mirrored: dark colours become light; mid ones (pure red) stay.
        int l1 = Math.Max(r, Math.Max(g, b)) + Math.Min(r, Math.Min(g, b));
        int l2 = Math.Max(r2, Math.Max(g2, b2)) + Math.Min(r2, Math.Min(g2, b2));
        int mirrored = 2 * 18 + (510 - l1) * 212 / 255;
        Assert.InRange(l2, mirrored - 3, mirrored + 3);
    }

    [Fact]
    public void DimmedAndWarm_DarkenPaperOnly_AsDesigned()
    {
        Assert.Equal((209, 209, 209), Run(PageLook.Dimmed, 255, 255, 255));
        Assert.Equal((184, 184, 184), Run(PageLook.DimmedMore, 255, 255, 255));
        Assert.Equal((242, 242, 242), Run(PageLook.Soft, 255, 255, 255));
        Assert.Equal((251, 245, 228), Run(PageLook.WarmPaper, 255, 255, 255));
        Assert.Equal((0, 0, 0), Run(PageLook.WarmPaper, 0, 0, 0));
    }

    [Fact]
    public void LeavesTheFourthByteAlone()
    {
        var px = new byte[] { 255, 255, 255, 77, 0, 0, 0, 99 };
        PageLookPixels.Apply(px, PageLook.Dark);
        Assert.Equal(77, px[3]); Assert.Equal(99, px[7]);
    }
}

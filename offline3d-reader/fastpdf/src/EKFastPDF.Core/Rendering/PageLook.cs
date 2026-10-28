namespace EKFastPDF.Core.Rendering;

/// <summary>How PDF pages are shown. Original keeps the PDF's colours; the others are easier on
/// the eyes. Dark swaps light and dark but keeps each colour's hue (a red line stays red).</summary>
public enum PageLook { Original, Soft, Dimmed, DimmedMore, WarmPaper, Dark }

/// <summary>Pixel work for <see cref="PageLook"/> (BGRx, any thread).</summary>
public static class PageLookPixels
{
    /// <summary>Changes the pixels in place.</summary>
    public static void Apply(Span<byte> bgrx, PageLook look)
    {
        switch (look)
        {
            case PageLook.Original: return;
            case PageLook.Soft: Scale(bgrx, 242, 242, 242); return;
            case PageLook.Dimmed: Scale(bgrx, 209, 209, 209); return;
            case PageLook.DimmedMore: Scale(bgrx, 184, 184, 184); return;
            case PageLook.WarmPaper: Scale(bgrx, 251, 245, 228); return; // multiply by cream #FBF5E4
            case PageLook.Dark:
                for (int i = 0; i + 2 < bgrx.Length; i += 4)
                {
                    int b = bgrx[i], g = bgrx[i + 1], r = bgrx[i + 2];
                    int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    // Invert lightness, keep hue and saturation: c' = c + 255 - (max + min).
                    int shift = 255 - max - min;
                    // Then 18..230 instead of 0..255: a soft black page and soft white lines.
                    bgrx[i] = (byte)(18 + (b + shift) * 212 / 255);
                    bgrx[i + 1] = (byte)(18 + (g + shift) * 212 / 255);
                    bgrx[i + 2] = (byte)(18 + (r + shift) * 212 / 255);
                }
                return;
        }
    }

    /// <summary>Multiplies every pixel by a colour given as R, G, B (0..255).</summary>
    private static void Scale(Span<byte> bgrx, int r, int g, int b)
    {
        for (int i = 0; i + 2 < bgrx.Length; i += 4)
        {
            bgrx[i] = (byte)(bgrx[i] * b / 255);
            bgrx[i + 1] = (byte)(bgrx[i + 1] * g / 255);
            bgrx[i + 2] = (byte)(bgrx[i + 2] * r / 255);
        }
    }
}

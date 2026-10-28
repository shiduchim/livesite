using System.Globalization;
using System.Text;

namespace EKFastPDF.Core.Samples;

/// <summary>
/// Synthetic, reproducible documents covering the workloads EK FastPDF is built for. No private
/// drawings are needed: the "cad" documents imitate Revit sheet exports (every line a separate
/// path object, hatching, thousands of small symbols with 3 pt labels, thin line weights) at
/// controllable density.
/// </summary>
public static class SampleDocuments
{
    public sealed record Sample(string FileName, string Description);

    public static readonly Sample Simple = new("simple-text.pdf", "10 letter pages of searchable text");
    public static readonly Sample ManyPages = new("many-pages-600.pdf", "600 pages, text + light vector content");
    public static readonly Sample CadDense = new("cad-dense-e1.pdf", "42x30 in sheet, ~90k path objects + 8k labels (Revit-like)");
    public static readonly Sample CadExtreme = new("cad-extreme-e1.pdf", "42x30 in sheet, ~350k path objects + 33k labels");
    public static readonly Sample CadSet = new("cad-set-12-sheets.pdf", "12 drawing sheets, ~27k path objects each");
    public static readonly Sample Images = new("image-heavy.pdf", "8 pages, each a 4000x3000 RGB image");
    public static readonly Sample Rotated = new("rotated.pdf", "Mixed /Rotate 0/90 pages for coordinate tests");
    public static readonly Sample Encrypted = new("password-ekpdf.pdf", "RC4 encrypted, user password 'ekpdf'");
    public static readonly Sample Corrupt = new("corrupt.pdf", "Truncated garbage, must fail gracefully");

    public static IReadOnlyList<Sample> All { get; } = new[] { Simple, ManyPages, CadDense, CadExtreme, CadSet, Images, Rotated, Encrypted, Corrupt };

    /// <summary>Words guaranteed to exist for search tests (exact counts are documented per sample).</summary>
    public const string SearchWord = "EKMARKER";

    /// <summary>Writes every sample that does not already exist. Returns the directory.</summary>
    public static string EnsureAll(string directory, bool includeExtreme = true)
    {
        Directory.CreateDirectory(directory);
        foreach (var s in All)
        {
            if (!includeExtreme && s == CadExtreme) continue;
            string path = Path.Combine(directory, s.FileName);
            if (File.Exists(path)) continue;
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, Generate(s));
            File.Move(temp, path, overwrite: true);
        }
        return directory;
    }

    public static byte[] Generate(Sample s)
    {
        if (s == Simple) return Text(10);
        if (s == ManyPages) return Text(600, vectors: true);
        if (s == CadDense) return Cad(sheets: 1, density: 1.0);
        if (s == CadExtreme) return Cad(sheets: 1, density: 4.0);
        if (s == CadSet) return Cad(sheets: 12, density: 0.3);
        if (s == Images) return ImagePages(8, 4000, 3000);
        if (s == Rotated) return RotatedPages();
        if (s == Encrypted) return Text(3, password: "ekpdf");
        if (s == Corrupt) return CorruptFile();
        throw new ArgumentException("Unknown sample");
    }

    private static readonly string[] Words =
    {
        "panel", "circuit", "lighting", "receptacle", "conduit", "feeder", "breaker", "transformer",
        "level", "corridor", "office", "mechanical", "riser", "junction", "switch", "emergency",
    };

    private static byte[] Text(int pages, bool vectors = false, string? password = null)
    {
        var w = new PdfWriter();
        var rng = new Random(1234);
        for (int p = 0; p < pages; p++)
        {
            var c = new ContentBuilder();
            c.Raw("BT /F1 18 Tf 72 740 Td ").Text($"Document page {p + 1}").Raw(" Tj ET\n");
            c.Raw("BT /F1 10 Tf 72 712 Td 13 TL\n");
            for (int line = 0; line < 48; line++)
            {
                var sb = new StringBuilder();
                for (int k = 0; k < 11; k++) sb.Append(Words[rng.Next(Words.Length)]).Append(' ');
                // One marker per page (on the 5th line) for deterministic search counts.
                if (line == 4) sb.Append(SearchWord).Append(' ').Append(p + 1);
                c.Text(sb.ToString()).Raw(" '\n");
            }
            c.Raw("ET\n");
            if (vectors)
            {
                c.Raw("0.4 w 0.3 0.35 0.5 RG\n");
                for (int i = 0; i < 60; i++)
                    c.Line(72 + rng.NextDouble() * 468, 60 + rng.NextDouble() * 20, 72 + rng.NextDouble() * 468, 60 + rng.NextDouble() * 20);
            }
            w.AddPage(612, 792, c.ToString());
        }
        return w.ToBytes(password);
    }

    /// <summary>Revit-style sheet: 42 x 30 in (3024 x 2160 pt), landscape.</summary>
    private static byte[] Cad(int sheets, double density)
    {
        var w = new PdfWriter();
        for (int s = 0; s < sheets; s++)
            w.AddPage(3024, 2160, CadSheet(s, density));
        return w.ToBytes();
    }

    public static string CadSheet(int seed, double density)
    {
        const double W = 3024, H = 2160;
        var rng = new Random(9000 + seed);
        var c = new ContentBuilder();
        // Sheet border and title block.
        c.Raw("2 w 0 0 0 RG\n");
        c.Rect(36, 36, W - 72, H - 72);
        c.Rect(W - 432, 36, 396, 300);
        c.Raw("BT /F1 28 Tf ").Num(W - 410).Num(280).Raw("Td ").Text($"E-{101 + seed} POWER PLAN LEVEL {seed + 1}").Raw(" Tj ET\n");
        c.Raw("BT /F1 12 Tf ").Num(W - 410).Num(250).Raw("Td ").Text(SampleDocuments.SearchWord + " SHEET " + (seed + 1)).Raw(" Tj ET\n");

        // Building grid: rooms with double walls and hatching. Each line is its own path object
        // ("m l S"), as Revit exports usually are.
        int roomsX = (int)(18 * Math.Sqrt(density)) + 4, roomsY = (int)(12 * Math.Sqrt(density)) + 3;
        double x0 = 120, y0 = 380, gw = W - 600 - x0, gh = H - 160 - y0;
        double rw = gw / roomsX, rh = gh / roomsY;
        c.Raw("0.9 w 0.1 0.1 0.1 RG\n");
        for (int i = 0; i <= roomsX; i++) { c.Line(x0 + i * rw, y0, x0 + i * rw, y0 + gh); c.Line(x0 + i * rw + 6, y0, x0 + i * rw + 6, y0 + gh); }
        for (int j = 0; j <= roomsY; j++) { c.Line(x0, y0 + j * rh, x0 + gw, y0 + j * rh); c.Line(x0, y0 + j * rh + 6, x0 + gw, y0 + j * rh + 6); }

        c.Raw("0.12 w 0.55 0.55 0.6 RG\n");
        double hatch = 2.5 / Math.Sqrt(density);
        for (int i = 0; i < roomsX; i++)
        for (int j = 0; j < roomsY; j++)
        {
            if ((i * 7 + j * 3 + seed) % 2 != 0) continue; // hatch half of the rooms
            double rx = x0 + i * rw + 8, ry = y0 + j * rh + 8, ww = rw - 10, hh = rh - 10;
            for (double d = 0; d < ww + hh; d += hatch)
            {
                double ax = rx + Math.Max(0, d - hh), ay = ry + Math.Min(d, hh);
                double bx = rx + Math.Min(d, ww), by = ry + Math.Max(0, d - ww);
                c.Line(ax, ay, bx, by);
            }
        }

        // Electrical symbols: receptacle circles with two prongs and a 3 pt circuit label.
        int symbols = (int)(25000 * density);
        c.Raw("0.35 w 0 0 0.55 RG\n");
        for (int k = 0; k < symbols; k++)
        {
            double sx = x0 + rng.NextDouble() * gw, sy = y0 + rng.NextDouble() * gh;
            c.Circle(sx, sy, 3.2);
            c.Line(sx - 1.4, sy - 1.6, sx - 1.4, sy + 1.6);
            c.Line(sx + 1.4, sy - 1.6, sx + 1.4, sy + 1.6);
        }
        c.Raw("0 0 0.45 rg\n");
        for (int k = 0; k < symbols / 3; k++)
        {
            double sx = x0 + rng.NextDouble() * gw, sy = y0 + rng.NextDouble() * gh;
            c.Raw("BT /F1 3 Tf ").Num(sx).Num(sy).Raw("Td ").Text($"LP-{k % 42 + 1}-{k % 97 + 1}").Raw(" Tj ET\n");
        }

        // Conduit runs: long polylines of short segments (homeruns).
        c.Raw("0.5 w 0.7 0.1 0.1 RG\n");
        int runs = (int)(1500 * density);
        for (int k = 0; k < runs; k++)
        {
            double px = x0 + rng.NextDouble() * gw, py = y0 + rng.NextDouble() * gh;
            c.Move(px, py);
            for (int seg = 0; seg < 40; seg++)
            {
                px = Math.Clamp(px + (rng.NextDouble() - 0.5) * 40, x0, x0 + gw);
                py = Math.Clamp(py + (rng.NextDouble() - 0.5) * 40, y0, y0 + gh);
                c.LineTo(px, py);
            }
            c.Raw("S\n");
        }

        // Room tags.
        c.Raw("0 0 0 rg\n");
        for (int i = 0; i < roomsX; i++)
        for (int j = 0; j < roomsY; j++)
            c.Raw("BT /F1 7 Tf ").Num(x0 + i * rw + 14).Num(y0 + j * rh + rh / 2).Raw("Td ").Text($"ROOM {i + 1:00}{j + 1:00}").Raw(" Tj ET\n");
        return c.ToString();
    }

    private static byte[] ImagePages(int pages, int width, int height)
    {
        var w = new PdfWriter();
        for (int p = 0; p < pages; p++)
        {
            var rgb = new byte[width * height * 3];
            int i = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Blocky pseudo-noise: realistic decode work, moderate file size.
                int n = (int)((uint)((x >> 2) * 73856093 ^ (y >> 2) * 19349663 ^ p * 83492791) % 24);
                rgb[i++] = (byte)((x * 255 / width + n + p * 30) & 255);
                rgb[i++] = (byte)((y * 255 / height + n) & 255);
                rgb[i++] = (byte)((((x >> 6) ^ (y >> 6)) & 1) * 90 + 80 + n);
            }
            int img = w.AddImage(width, height, rgb);
            var c = new ContentBuilder();
            c.Raw("q 792 0 0 594 0 0 cm /Im1 Do Q\n");
            c.Raw("BT /F1 14 Tf 30 20 Td ").Text($"{SearchWord} image page {p + 1}").Raw(" Tj ET\n");
            w.AddPage(792, 594, c.ToString(), images: new Dictionary<string, int> { ["Im1"] = img });
        }
        return w.ToBytes();
    }

    private static byte[] RotatedPages()
    {
        var w = new PdfWriter();
        foreach (int rotate in new[] { 0, 90, 0, 270 })
        {
            var c = new ContentBuilder();
            c.Raw("1 w 0 0 0 RG\n").Rect(36, 36, 540, 720);
            c.Raw("BT /F1 24 Tf 100 600 Td ").Text($"{SearchWord} rotate {rotate}").Raw(" Tj ET\n");
            w.AddPage(612, 792, c.ToString(), rotate);
        }
        return w.ToBytes();
    }

    private static byte[] CorruptFile()
    {
        var bytes = Text(2);
        var rng = new Random(5);
        var data = bytes[..(bytes.Length / 3)];
        for (int i = 64; i < data.Length; i += 37) data[i] = (byte)rng.Next(256);
        return data;
    }

    /// <summary>Fast content-stream builder with invariant 2-decimal numbers.</summary>
    private sealed class ContentBuilder
    {
        private readonly StringBuilder _sb = new(1 << 20);
        public ContentBuilder Raw(string s) { _sb.Append(s); return this; }

        public ContentBuilder Num(double v)
        {
            _sb.Append(Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
            return this;
        }

        public ContentBuilder Text(string s)
        {
            _sb.Append('(');
            foreach (char ch in s)
            {
                if (ch is '(' or ')' or '\\') _sb.Append('\\');
                _sb.Append(ch);
            }
            _sb.Append(')');
            return this;
        }

        public ContentBuilder Move(double x, double y) { Num(x).Num(y); _sb.Append("m "); return this; }
        public ContentBuilder LineTo(double x, double y) { Num(x).Num(y); _sb.Append("l "); return this; }
        public ContentBuilder Line(double ax, double ay, double bx, double by) { Move(ax, ay); LineTo(bx, by); _sb.Append("S\n"); return this; }
        public ContentBuilder Rect(double x, double y, double w, double h) { Num(x).Num(y).Num(w).Num(h); _sb.Append("re S\n"); return this; }

        public ContentBuilder Circle(double cx, double cy, double r)
        {
            const double k = 0.5523;
            Move(cx + r, cy);
            Curve(cx + r, cy + k * r, cx + k * r, cy + r, cx, cy + r);
            Curve(cx - k * r, cy + r, cx - r, cy + k * r, cx - r, cy);
            Curve(cx - r, cy - k * r, cx - k * r, cy - r, cx, cy - r);
            Curve(cx + k * r, cy - r, cx + r, cy - k * r, cx + r, cy);
            _sb.Append("S\n");
            return this;
        }

        private void Curve(double a, double b, double c2, double d, double e, double f)
        {
            Num(a).Num(b).Num(c2).Num(d).Num(e).Num(f);
            _sb.Append("c ");
        }

        public override string ToString() => _sb.ToString();
    }
}

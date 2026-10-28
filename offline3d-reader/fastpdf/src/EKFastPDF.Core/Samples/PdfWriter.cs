using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace EKFastPDF.Core.Samples;

/// <summary>
/// Tiny PDF 1.4 writer used to generate the synthetic benchmark and test documents
/// (no third-party code, so the benchmark set is reproducible on any machine with EK FastPDF).
/// Supports Flate streams, images and the standard RC4 40-bit security handler (for the
/// password-protected test case).
/// </summary>
public sealed class PdfWriter
{
    private readonly List<(string Dict, byte[]? Stream, bool Compress)> _objects = new();
    private readonly List<int> _pages = new();
    private readonly int _pagesObj;
    private readonly int _fontObj;

    public PdfWriter()
    {
        _pagesObj = Reserve();
        _fontObj = Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
    }

    public int PageCount => _pages.Count;

    private int Reserve()
    {
        _objects.Add(("", null, false));
        return _objects.Count;
    }

    public int Add(string dict, byte[]? stream = null, bool compress = true)
    {
        _objects.Add((dict, stream, compress));
        return _objects.Count;
    }

    /// <summary>Adds an RGB image XObject (8 bits per component, Flate compressed).</summary>
    public int AddImage(int width, int height, byte[] rgb) =>
        Add($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8", rgb);

    public void AddPage(double width, double height, string content, int rotate = 0, IReadOnlyDictionary<string, int>? images = null)
    {
        int contents = Add("<<", Encoding.Latin1.GetBytes(content));
        var xobjects = new StringBuilder();
        if (images is { Count: > 0 })
        {
            xobjects.Append(" /XObject <<");
            foreach (var (name, obj) in images) xobjects.Append(CultureInfo.InvariantCulture, $" /{name} {obj} 0 R");
            xobjects.Append(" >>");
        }
        string rot = rotate != 0 ? $" /Rotate {rotate}" : "";
        int page = Add(string.Create(CultureInfo.InvariantCulture,
            $"<< /Type /Page /Parent {_pagesObj} 0 R /MediaBox [0 0 {width:0.###} {height:0.###}]{rot} /Resources << /Font << /F1 {_fontObj} 0 R >>{xobjects} >> /Contents {contents} 0 R >>"));
        _pages.Add(page);
    }

    public byte[] ToBytes(string? userPassword = null)
    {
        var kids = string.Join(" ", _pages.Select(p => $"{p} 0 R"));
        _objects[_pagesObj - 1] = ($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>", null, false);
        int catalog = Add($"<< /Type /Catalog /Pages {_pagesObj} 0 R >>");

        byte[] id = MD5.HashData(Encoding.ASCII.GetBytes("EKFastPDF" + _objects.Count + _pages.Count));
        Rc4Security? security = userPassword is null ? null : new Rc4Security(userPassword, "owner-" + userPassword, id);
        int encryptObj = security is null ? 0 : Add(security.Dictionary);

        using var output = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); output.Write(b); }
        W("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new long[_objects.Count + 1];
        for (int i = 0; i < _objects.Count; i++)
        {
            int num = i + 1;
            offsets[num] = output.Position;
            var (dict, stream, compress) = _objects[i];
            W($"{num} 0 obj\n");
            if (stream is null)
            {
                W(dict + "\nendobj\n");
                continue;
            }
            byte[] data = stream;
            string filter = "";
            if (compress)
            {
                using var ms = new MemoryStream();
                using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(data);
                data = ms.ToArray();
                filter = " /Filter /FlateDecode";
            }
            if (security != null && num != encryptObj) data = security.Encrypt(num, data);
            string head = dict.EndsWith(">>", StringComparison.Ordinal) ? dict[..^2] : dict;
            W($"{head}{filter} /Length {data.Length} >>\nstream\n");
            output.Write(data);
            W("\nendstream\nendobj\n");
        }
        long xref = output.Position;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {_objects.Count + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= _objects.Count; i++) sb.Append(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n");
        string hexId = Convert.ToHexString(id);
        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {_objects.Count + 1} /Root {catalog} 0 R /ID [<{hexId}> <{hexId}>]");
        if (encryptObj != 0) sb.Append(CultureInfo.InvariantCulture, $" /Encrypt {encryptObj} 0 R");
        sb.Append(CultureInfo.InvariantCulture, $" >>\nstartxref\n{xref}\n%%EOF\n");
        W(sb.ToString());
        return output.ToArray();
    }

    /// <summary>PDF Standard security handler, revision 2 (RC4, 40-bit key), per ISO 32000-1 7.6.3.</summary>
    private sealed class Rc4Security
    {
        private static readonly byte[] Pad =
        {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
        };
        private const int Permissions = -44;
        private readonly byte[] _key;
        public string Dictionary { get; }

        public Rc4Security(string user, string owner, byte[] id)
        {
            byte[] ownerKey = MD5.HashData(Padded(owner))[..5];
            byte[] o = Rc4(ownerKey, Padded(user));
            var buffer = new List<byte>();
            buffer.AddRange(Padded(user));
            buffer.AddRange(o);
            buffer.AddRange(BitConverter.GetBytes(Permissions));
            buffer.AddRange(id);
            _key = MD5.HashData(buffer.ToArray())[..5];
            byte[] u = Rc4(_key, Pad);
            Dictionary = $"<< /Filter /Standard /V 1 /R 2 /O <{Convert.ToHexString(o)}> /U <{Convert.ToHexString(u)}> /P {Permissions} >>";
        }

        private static byte[] Padded(string password)
        {
            var bytes = Encoding.Latin1.GetBytes(password);
            var result = new byte[32];
            int n = Math.Min(32, bytes.Length);
            Array.Copy(bytes, result, n);
            Array.Copy(Pad, 0, result, n, 32 - n);
            return result;
        }

        public byte[] Encrypt(int objectNumber, byte[] data)
        {
            var material = new byte[_key.Length + 5];
            _key.CopyTo(material, 0);
            material[_key.Length] = (byte)objectNumber;
            material[_key.Length + 1] = (byte)(objectNumber >> 8);
            material[_key.Length + 2] = (byte)(objectNumber >> 16);
            byte[] objectKey = MD5.HashData(material)[..Math.Min(16, _key.Length + 5)];
            return Rc4(objectKey, data);
        }

        private static byte[] Rc4(byte[] key, byte[] data)
        {
            var s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 255;
                (s[i], s[j]) = (s[j], s[i]);
            }
            var result = new byte[data.Length];
            for (int k = 0, i = 0, j = 0; k < data.Length; k++)
            {
                i = (i + 1) & 255;
                j = (j + s[i]) & 255;
                (s[i], s[j]) = (s[j], s[i]);
                result[k] = (byte)(data[k] ^ s[(s[i] + s[j]) & 255]);
            }
            return result;
        }
    }
}

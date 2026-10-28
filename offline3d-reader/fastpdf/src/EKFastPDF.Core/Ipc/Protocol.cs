using System.Buffers.Binary;
using System.Text;

namespace EKFastPDF.Core.Ipc;

/// <summary>Messages exchanged between the viewer process and render worker processes over
/// the worker's redirected stdin/stdout. Pixels never travel through the pipe: they are written
/// by PDFium directly into a shared-memory buffer that the viewer maps as well.</summary>
internal enum Op : byte
{
    OpenDocument = 1,
    CloseDocument = 2,
    RenderRegion = 3,
    SearchPage = 4,
    PrintDocument = 5,
    Shutdown = 6,
    PageMap = 7,
}

internal enum WorkerStatus : byte
{
    Ok = 0,
    Canceled = 1,
    Error = 2,
    PasswordRequired = 3,
    FileNotFound = 4,
    Corrupt = 5,
    UnsupportedSecurity = 6,
    TooLarge = 7,
}

internal static class Protocol
{
    public const int Version = 4;
    public const int Magic = 0x46504B45; // "EKPF"
    /// <summary>Bytes at the start of the shared buffer reserved for control words
    /// (offset 0: id of the request the viewer wants cancelled).</summary>
    public const int SharedHeaderBytes = 64;
    public const int MaxFrameBytes = 64 * 1024 * 1024;

    public static async Task WriteFrameAsync(Stream stream, byte[] payload, int length, CancellationToken token = default)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(payload.AsMemory(0, length), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static void WriteFrame(Stream stream, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Flush();
    }

    /// <summary>Reads one frame; returns null at a clean end of stream.</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken token = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, token).ConfigureAwait(false)) return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameBytes) throw new InvalidDataException($"Invalid worker frame length {length}.");
        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, token).ConfigureAwait(false))
            throw new EndOfStreamException("Render worker closed the pipe mid-message.");
        return payload;
    }

    public static byte[]? ReadFrame(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        int got = 0;
        while (got < 4)
        {
            int n = stream.Read(header[got..]);
            if (n == 0) return got == 0 ? null : throw new EndOfStreamException();
            got += n;
        }
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameBytes) throw new InvalidDataException($"Invalid frame length {length}.");
        var payload = new byte[length];
        stream.ReadExactly(payload);
        return payload;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(got), token).ConfigureAwait(false);
            if (n == 0)
            {
                if (got == 0) return false;
                throw new EndOfStreamException("Render worker closed the pipe mid-message.");
            }
            got += n;
        }
        return true;
    }
}

/// <summary>Minimal allocation-light little-endian message writer.</summary>
internal sealed class MessageWriter
{
    private byte[] _buffer = new byte[256];
    public int Length { get; private set; }
    public byte[] Buffer => _buffer;

    public MessageWriter Reset() { Length = 0; return this; }

    private Span<byte> Take(int n)
    {
        if (Length + n > _buffer.Length) Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + n));
        var span = _buffer.AsSpan(Length, n);
        Length += n;
        return span;
    }

    public MessageWriter U8(byte v) { Take(1)[0] = v; return this; }
    public MessageWriter Bool(bool v) => U8(v ? (byte)1 : (byte)0);
    public MessageWriter I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(Take(4), v); return this; }
    public MessageWriter I64(long v) { BinaryPrimitives.WriteInt64LittleEndian(Take(8), v); return this; }
    public MessageWriter F32(float v) { BinaryPrimitives.WriteSingleLittleEndian(Take(4), v); return this; }
    public MessageWriter F64(double v) { BinaryPrimitives.WriteDoubleLittleEndian(Take(8), v); return this; }

    public MessageWriter Str(string? v)
    {
        if (v is null) return I32(-1);
        int n = Encoding.UTF8.GetByteCount(v);
        I32(n);
        Encoding.UTF8.GetBytes(v, Take(n));
        return this;
    }

    public MessageWriter Bytes(byte[]? v)
    {
        if (v is null) return I32(-1);
        I32(v.Length);
        v.CopyTo(Take(v.Length));
        return this;
    }

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, Length);
}

internal sealed class MessageReader
{
    private readonly byte[] _data;
    private int _pos;
    public MessageReader(byte[] data) { _data = data; }

    private ReadOnlySpan<byte> Take(int n)
    {
        if (_pos + n > _data.Length) throw new InvalidDataException("Truncated worker message.");
        var s = _data.AsSpan(_pos, n);
        _pos += n;
        return s;
    }

    public byte U8() => Take(1)[0];
    public bool Bool() => U8() != 0;
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public string? Str()
    {
        int n = I32();
        return n < 0 ? null : Encoding.UTF8.GetString(Take(n));
    }

    public byte[]? Bytes()
    {
        int n = I32();
        return n < 0 ? null : Take(n).ToArray();
    }
}

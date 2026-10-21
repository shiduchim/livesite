using System.IO.MemoryMappedFiles;

namespace EKFastPDF.Core.Ipc;

/// <summary>
/// Shared memory between the viewer and one render worker. The worker's PDFium bitmap is
/// created directly on top of this memory (FPDFBitmap_CreateEx), so a rendered tile reaches
/// the viewer without any pipe transfer or intermediate copy.
/// Windows: pagefile-backed named section (never touches disk). Other OSes: a file in /dev/shm.
/// </summary>
internal sealed unsafe class SharedBuffer : IDisposable
{
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly string? _deleteOnDispose;
    private byte* _pointer;

    public string Name { get; }
    public long Size { get; }

    private SharedBuffer(MemoryMappedFile map, string name, long size, string? deleteOnDispose)
    {
        _map = map;
        Name = name;
        Size = size;
        _deleteOnDispose = deleteOnDispose;
        _view = map.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _pointer = p + _view.PointerOffset;
    }

    public static SharedBuffer Create(long size)
    {
        string id = $"EKFastPDF-{Environment.ProcessId}-{Guid.NewGuid():N}";
        if (OperatingSystem.IsWindows())
        {
            string name = "Local\\" + id;
            return new SharedBuffer(MemoryMappedFile.CreateNew(name, size, MemoryMappedFileAccess.ReadWrite), name, size, null);
        }
        string dir = Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath();
        string path = Path.Combine(dir, id);
        var map = MemoryMappedFile.CreateFromFile(path, FileMode.CreateNew, null, size, MemoryMappedFileAccess.ReadWrite);
        return new SharedBuffer(map, path, size, path);
    }

    public static SharedBuffer Open(string name, long size)
    {
        if (OperatingSystem.IsWindows())
            return new SharedBuffer(MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite), name, size, null);
        var map = MemoryMappedFile.CreateFromFile(name, FileMode.Open, null, size, MemoryMappedFileAccess.ReadWrite);
        return new SharedBuffer(map, name, size, null);
    }

    public byte* Pointer => _pointer;
    public long* CancelSlot => (long*)_pointer;
    public byte* Pixels => _pointer + Protocol.SharedHeaderBytes;
    public long PixelCapacity => Size - Protocol.SharedHeaderBytes;

    public void RequestCancel(long requestId) => Volatile.Write(ref *CancelSlot, requestId);

    public void Dispose()
    {
        if (_pointer != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointer = null;
        }
        _view.Dispose();
        _map.Dispose();
        if (_deleteOnDispose != null)
        {
            try { File.Delete(_deleteOnDispose); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

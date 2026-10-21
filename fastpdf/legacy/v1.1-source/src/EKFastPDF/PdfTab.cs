using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace EKFastPDF;

public sealed class PdfTab : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly string _filePath;
    private string _status = "Opening document…";
    private PdfDocument? _document;
    private int _page;
    private bool _closed;

    public PdfTab(string path)
    {
        _filePath = Path.GetFullPath(path);
        Title = Path.GetFileName(_filePath);
        FileSize = new FileInfo(_filePath).Length;
    }

    public string Title { get; }
    public string PathName => _filePath;
    public long FileSize { get; }
    public string FriendlySize => FileSize >= 1_073_741_824 ? $"{FileSize / 1_073_741_824d:0.0} GB"
        : FileSize >= 1_048_576 ? $"{FileSize / 1_048_576d:0.0} MB"
        : $"{FileSize / 1024d:0.0} KB";
    internal PdfDocument? Document { get => _document; private set { _document = value; Changed(); } }
    public int PageCount => Document?.PageCount ?? 0;
    public int Page { get => _page; set { _page = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public bool IsClosed => _closed;

    public async Task OpenAsync()
    {
        try
        {
            var document = await Task.Run(() => PdfEngine.Open(_filePath));
            if (_closed) { document.Dispose(); return; }
            Document = document;
            Status = $"{PageCount:N0} pages • {FriendlySize}";
        }
        catch (Exception e)
        {
            if (!_closed) Status = "Unable to open: " + e.Message;
        }
    }

    public void Dispose()
    {
        _closed = true;
        Document?.Dispose();
        Document = null;
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

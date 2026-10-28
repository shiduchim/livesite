using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Layout;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Services;

namespace EKFastPDF.Viewer;

public enum SessionState { Opening, NeedsPassword, Ready, Failed }

/// <summary>
/// One open document (one tab): the PDF registration in the render pool, its page layout,
/// its own view (zoom/scroll survive tab switches) and its search state.
/// </summary>
public sealed class DocumentSession : INotifyPropertyChanged, IDisposable
{
    private readonly AppServices _services;
    private SessionState _state = SessionState.Opening;
    private string _status = "Opening…";
    private bool _isActive;
    private int _currentPage;

    public DocumentSession(AppServices services, string path)
    {
        _services = services;
        FilePath = Path.GetFullPath(path);
        Title = Path.GetFileName(FilePath);
        try { FileSize = new FileInfo(FilePath).Length; } catch (IOException) { }
        Search = new SearchController(this, services);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath { get; }
    public string Title { get; }
    public long FileSize { get; }
    public string? Password { get; private set; }
    public int DocId { get; private set; }
    public DocumentInfo? Info { get; private set; }
    public DocumentLayout? Layout { get; private set; }
    public Viewport View { get; } = new();
    /// <summary>Page shown first (from My PDFs, where any page of a PDF can be clicked).</summary>
    public int InitialPage { get; set; }
    /// <summary>False until the viewer has sized the view and applied the initial fit.</summary>
    public bool ViewInitialized { get; set; }
    public SearchController Search { get; }
    public double OpenMilliseconds { get; private set; }
    public bool IsClosed { get; private set; }

    public SessionState State { get => _state; private set { _state = value; Changed(); Changed(nameof(IsReady)); } }
    public bool IsReady => _state == SessionState.Ready;
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public bool IsActive { get => _isActive; set { _isActive = value; Changed(); } }
    public int PageCount => Info?.PageCount ?? 0;
    public int CurrentPage { get => _currentPage; set { if (_currentPage != value) { _currentPage = value; Changed(); } } }

    public string FriendlySize => FileSize >= 1 << 30 ? $"{FileSize / (double)(1 << 30):0.0} GB"
        : FileSize >= 1 << 20 ? $"{FileSize / (double)(1 << 20):0.0} MB" : $"{FileSize / 1024.0:0} KB";

    public async Task OpenAsync(string? password = null)
    {
        var start = TileStore.Timestamp;
        Password = password;
        State = SessionState.Opening;
        Status = "Opening…";
        if (DocId != 0) _services.Pool.CloseDocument(DocId);
        DocId = _services.Pool.RegisterDocument(FilePath, password);
        try
        {
            var info = await _services.Pool.OpenDocumentAsync(DocId);
            if (IsClosed) return;
            Info = info;
            Layout = new DocumentLayout(info.PageWidths, info.PageHeights);
            OpenMilliseconds = TileStore.MsSince(start);
            Status = $"{info.PageCount:N0} page{(info.PageCount == 1 ? "" : "s")} · {FriendlySize}";
            Log.Info($"Opened {Title}: {info.PageCount} pages, {FriendlySize}, {OpenMilliseconds:0} ms");
            State = SessionState.Ready;
            Changed(nameof(PageCount));
        }
        catch (PdfOpenException e) when (e.Failure == OpenFailure.PasswordRequired)
        {
            if (IsClosed) return;
            Status = password is null ? "This PDF is password protected." : "The password is not correct.";
            State = SessionState.NeedsPassword;
        }
        catch (Exception e) when (e is PdfOpenException or InvalidOperationException or TaskCanceledException)
        {
            if (IsClosed) return;
            Log.Warn($"Could not open {FilePath}: {e.Message}");
            Status = e.Message;
            State = SessionState.Failed;
        }
    }

    public void Dispose()
    {
        if (IsClosed) return;
        IsClosed = true;
        Search.Cancel();
        if (DocId != 0)
        {
            _services.Pool.CloseDocument(DocId);
            _services.Tiles.ForgetDocument(DocId);
        }
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

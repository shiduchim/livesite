using System.ComponentModel;
using System.Runtime.CompilerServices;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Rendering;
using EKFastPDF.Services;

namespace EKFastPDF.Viewer;

/// <summary>
/// Background text search for one document. Pages are searched by the render workers (at most a
/// few at a time, below visible rendering in priority), starting at the current page and
/// wrapping, so the first match near the reader appears quickly and the view never waits.
/// </summary>
public sealed class SearchController : INotifyPropertyChanged
{
    public const int MaxHits = 5000;
    private const double BasePriority = 500_000;

    private readonly DocumentSession _session;
    private readonly AppServices _services;
    private readonly List<SearchHit> _hits = new();
    private readonly Dictionary<int, List<SearchHit>> _byPage = new();
    private CancellationTokenSource? _cts;
    private SearchHit? _current;
    private int _pagesDone, _pagesWithText;
    private bool _running;
    private string _query = "";

    public SearchController(DocumentSession session, AppServices services)
    {
        _session = session;
        _services = services;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Raised when the viewer should bring a hit into view.</summary>
    public event Action<SearchHit>? NavigateRequested;
    /// <summary>Raised when highlights changed (viewer redraws).</summary>
    public event Action? HighlightsChanged;

    public string Query => _query;
    public bool IsRunning => _running;
    public int Count => _hits.Count;
    public SearchHit? Current => _current;
    public int CurrentIndex => _current is null ? -1 : _hits.IndexOf(_current);
    public double FirstHitMs { get; private set; }
    public double CompletedMs { get; private set; }

    public IReadOnlyList<SearchHit> HitsOnPage(int page) =>
        _byPage.TryGetValue(page, out var list) ? list : Array.Empty<SearchHit>();

    public string Summary
    {
        get
        {
            if (_query.Length == 0) return "";
            int total = _session.PageCount;
            if (_hits.Count == 0)
            {
                if (_running) return total > 0 ? $"{100 * _pagesDone / total}%" : "…";
                return _pagesDone > 0 && _pagesWithText == 0 ? "No searchable text" : "No matches";
            }
            string suffix = _running ? "…" : _hits.Count >= MaxHits ? "+" : "";
            return $"{CurrentIndex + 1} / {_hits.Count}{suffix}";
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Start(string query)
    {
        Cancel();
        _query = query.Trim();
        _hits.Clear();
        _byPage.Clear();
        _current = null;
        _pagesDone = _pagesWithText = 0;
        _running = false;
        Notify();
        HighlightsChanged?.Invoke();
        if (_query.Length == 0 || !_session.IsReady) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = RunAsync(_query, cts.Token);
    }

    private async Task RunAsync(string query, CancellationToken token)
    {
        int pages = _session.PageCount;
        int start = Math.Clamp(_session.CurrentPage, 0, pages - 1);
        var started = TileStore.Timestamp;
        _running = true;
        FirstHitMs = CompletedMs = 0;
        Notify();
        var tasks = new List<Task<PageSearchResult>>(pages);
        for (int i = 0; i < pages; i++)
        {
            int page = (start + i) % pages;
            tasks.Add(_services.Pool.SearchPageAsync(_session.DocId, page, query, matchCase: false, wholeWord: false,
                maxHits: 1000, priority: BasePriority + i, token));
        }
        // Completions are consumed in arrival order through a channel (O(n), unlike WhenAny loops).
        var channel = System.Threading.Channels.Channel.CreateUnbounded<Task<PageSearchResult>>();
        foreach (var t in tasks) _ = t.ContinueWith(c => channel.Writer.TryWrite(c), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        int remaining = tasks.Count;
        long lastNotify = 0;
        try
        {
            while (remaining > 0)
            {
                var done = await channel.Reader.ReadAsync(CancellationToken.None);
                remaining--;
                if (token.IsCancellationRequested) return;
                PageSearchResult result;
                try { result = await done; }
                catch (OperationCanceledException) { return; }
                catch (InvalidOperationException e)
                {
                    Log.Warn("Search page failed: " + e.Message);
                    _pagesDone++;
                    continue;
                }
                _pagesDone++;
                if (result.CharCount > 0) _pagesWithText++;
                if (result.Hits.Count > 0 && _hits.Count < MaxHits)
                {
                    AddHits(result);
                    if (_current is null)
                    {
                        _current = FirstFrom(start);
                        FirstHitMs = TileStore.MsSince(started);
                        NavigateRequested?.Invoke(_current!);
                    }
                    HighlightsChanged?.Invoke();
                }
                if (TileStore.MsSince(lastNotify) > 120) { Notify(); lastNotify = TileStore.Timestamp; }
            }
            CompletedMs = TileStore.MsSince(started);
            Log.Info($"Search \"{query}\": {_hits.Count} hits on {pages} pages in {CompletedMs:0} ms (first hit {FirstHitMs:0} ms)");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _running = false;
                Notify();
            }
            foreach (var t in tasks) _ = t.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default); // observe faults
        }
    }

    private void AddHits(PageSearchResult result)
    {
        var list = result.Hits.Take(MaxHits - _hits.Count).ToList();
        _byPage[result.Page] = list;
        // Keep document order: page, then top, then left.
        int index = _hits.FindIndex(h => h.Page > result.Page);
        if (index < 0) _hits.AddRange(list); else _hits.InsertRange(index, list);
    }

    private SearchHit? FirstFrom(int page)
    {
        foreach (var h in _hits) if (h.Page >= page) return h;
        return _hits.FirstOrDefault();
    }

    public void Next() => Move(+1);
    public void Previous() => Move(-1);

    private void Move(int delta)
    {
        if (_hits.Count == 0) return;
        int i = CurrentIndex;
        if (i < 0)
        {
            _current = FirstFrom(_session.CurrentPage);
        }
        else _current = _hits[(i + delta + _hits.Count) % _hits.Count];
        Notify();
        HighlightsChanged?.Invoke();
        if (_current != null) NavigateRequested?.Invoke(_current);
    }

    private void Notify([CallerMemberName] string? _ = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
    }
}

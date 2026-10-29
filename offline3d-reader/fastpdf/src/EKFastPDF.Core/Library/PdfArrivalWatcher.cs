using EKFastPDF.Core.Diagnostics;

namespace EKFastPDF.Core.Library;

/// <summary>
/// Watches PDF folders and reports each new or rewritten PDF the moment it is complete.
/// Two sources: Windows change notifications (instant) and a listing of each folder every
/// few seconds (for folders where notifications are lost: synced or network folders, buffer
/// overflows). A PDF counts as complete when nothing is writing it any more and its size
/// held for one check (150 ms), so a half-written export or download is never opened.
/// Events are raised through <c>post</c> (the UI thread in the app).
/// </summary>
public sealed class PdfArrivalWatcher : IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ListInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(10);

    private readonly Action<Action> _post;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, (string Folder, long Size, DateTime Since)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, (long Size, DateTime Written)>> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    // What was last reported per file: late duplicate notifications for the same save are ignored.
    private readonly Dictionary<string, (long Size, DateTime Written)> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _check, _list;
    private List<string> _folders = new();
    private int _listing, _checking;
    private bool _disposed;

    public PdfArrivalWatcher(Action<Action> post)
    {
        _post = post;
        _check = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        _list = new Timer(_ => ListAll(), null, ListInterval, ListInterval);
    }

    /// <summary>Folders whose PDFs came, went or changed (for redrawing the gallery).</summary>
    public event Action<IReadOnlyCollection<string>>? Changed;
    /// <summary>A new or rewritten PDF is complete: (folder, file).</summary>
    public event Action<string, string>? Arrived;

    /// <summary>Watches <paramref name="folders"/> through Windows notifications; <paramref name="listed"/>
    /// (default: all of them) are also listed every few seconds as a backup.</summary>
    public void Watch(IEnumerable<string> folders, IEnumerable<string>? listed = null)
    {
        var list = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var backup = (listed ?? list).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        lock (_gate)
        {
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _folders = backup;
            foreach (var gone in _snapshots.Keys.Where(k => !backup.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) _snapshots.Remove(gone);
            // What is in a folder now is not new: take the first listing before watching.
            foreach (var folder in backup) if (!_snapshots.ContainsKey(folder)) _snapshots[folder] = Listing(folder);
            foreach (var folder in list)
            {
                try
                {
                    if (!Directory.Exists(folder)) { Log.Warn("Folder not found, not watched: " + folder); continue; }
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                        InternalBufferSize = 64 * 1024,
                    };
                    string f = folder;
                    w.Created += (_, e) => Seen(f, e.FullPath, "created");
                    w.Renamed += (_, e) => Seen(f, e.FullPath, "renamed");
                    w.Changed += (_, e) => Seen(f, e.FullPath, "written");
                    w.Deleted += (_, e) => { if (IsPdf(e.FullPath)) MarkChanged(f); };
                    w.Error += (_, e) => { Log.Warn($"Folder notifications lost in {f}: {e.GetException().Message}"); ListAll(); };
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    Log.Warn($"Cannot watch {folder} ({e.Message}); listing it every {ListInterval.TotalSeconds:0} s instead");
                }
            }
        }
        Log.Info("Watching folders: " + string.Join("; ", list));
    }

    private static bool IsPdf(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    // Notification threads, the list timer.
    private void Seen(string folder, string path, string how)
    {
        if (!IsPdf(path)) return;
        MarkChanged(folder);
        (long, DateTime)? current = null;
        try { var fi = new FileInfo(path); if (fi.Exists) current = (fi.Length, fi.LastWriteTimeUtc); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        lock (_gate)
        {
            if (_disposed || _pending.ContainsKey(path)) return;
            if (current is { } c && _reported.TryGetValue(path, out var last) && last == c) return;
            Log.Info($"PDF {how}: {path}");
            _pending[path] = (folder, -1, DateTime.UtcNow);
        }
        // Check at once: a file copied or moved in is usually complete already.
        Check();
    }

    private void MarkChanged(string folder)
    {
        bool first;
        lock (_gate) { first = _changed.Count == 0; _changed.Add(folder); }
        if (first) _post(() =>
        {
            List<string> folders;
            lock (_gate) { folders = _changed.ToList(); _changed.Clear(); }
            if (folders.Count > 0) Changed?.Invoke(folders);
        });
    }

    private void Check()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        var done = new List<(string Folder, string Path)>();
        bool more;
        try
        {
            List<KeyValuePair<string, (string Folder, long Size, DateTime Since)>> items;
            lock (_gate) items = _pending.ToList();
            foreach (var (path, state) in items)
            {
                long size = -1;
                try { var fi = new FileInfo(path); if (fi.Exists) size = fi.Length; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                lock (_gate)
                {
                    if (size < 0 || DateTime.UtcNow - state.Since > GiveUp) { _pending.Remove(path); continue; }
                    // Complete: the size held since the last check and no one is writing it.
                    if (size > 0 && size == state.Size && PdfLibrary.IsSettled(path))
                    {
                        _pending.Remove(path);
                        try
                        {
                            var fi = new FileInfo(path);
                            _reported[path] = (fi.Length, fi.LastWriteTimeUtc);
                            if (_snapshots.TryGetValue(state.Folder, out var snap)) snap[path] = (fi.Length, fi.LastWriteTimeUtc);
                        }
                        catch (IOException) { }
                        done.Add((state.Folder, path));
                    }
                    else _pending[path] = (state.Folder, size, state.Since);
                }
            }
            lock (_gate) more = _pending.Count > 0 && !_disposed;
        }
        finally { Volatile.Write(ref _checking, 0); }
        if (more) _check.Change(CheckInterval, Timeout.InfiniteTimeSpan);
        foreach (var (folder, path) in done)
        {
            Log.Info("PDF complete: " + path);
            _post(() => Arrived?.Invoke(folder, path));
        }
    }

    private static Dictionary<string, (long, DateTime)> Listing(string folder)
    {
        var map = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(folder)) return map;
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*.pdf", new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive }))
                map[f.FullName] = (f.Length, f.LastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return map;
    }

    /// <summary>Backup for lost notifications: each folder compared with its last listing.</summary>
    private void ListAll()
    {
        if (Interlocked.Exchange(ref _listing, 1) == 1) return;
        try
        {
            List<string> folders;
            lock (_gate) { if (_disposed) return; folders = _folders.ToList(); }
            foreach (var folder in folders)
            {
                var now = Listing(folder);
                Dictionary<string, (long Size, DateTime Written)>? before;
                lock (_gate) { _snapshots.TryGetValue(folder, out before); _snapshots[folder] = now; }
                if (before is null) continue;
                foreach (var (path, value) in now)
                    if (!before.TryGetValue(path, out var old) || old.Size != value.Item1 || old.Written != value.Item2) Seen(folder, path, "found in listing");
                if (before.Keys.Any(k => !now.ContainsKey(k))) MarkChanged(folder);
            }
        }
        finally { Volatile.Write(ref _listing, 0); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
        }
        _check.Dispose();
        _list.Dispose();
    }
}

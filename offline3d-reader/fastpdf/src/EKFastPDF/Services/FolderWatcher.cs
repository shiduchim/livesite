using System.IO;
using System.Windows.Threading;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Library;

namespace EKFastPDF.Services;

/// <summary>
/// Watches the My PDFs folders. <see cref="Changed"/> fires (UI thread, debounced) when PDFs come
/// or go; <see cref="Arrived"/> fires once per new PDF when it is complete: browsers download to
/// a temporary name and rename at the end, and exporters write for a while, so a new file is
/// reported only after it can be read and its size stopped changing.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, (string Folder, long Size, int Stable, DateTime Since)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _debounce, _poll;

    public FolderWatcher()
    {
        _ui = Dispatcher.CurrentDispatcher;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => FlushChanged(), _ui);
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Poll(), _ui);
    }

    /// <summary>A folder's PDFs changed (added, removed, renamed).</summary>
    public event Action<IReadOnlyCollection<string>>? Changed;
    /// <summary>A new PDF is complete: (folder, file).</summary>
    public event Action<string, string>? Arrived;

    public void Watch(IEnumerable<string> folders)
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                var w = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                string f = folder;
                w.Created += (_, e) => OnEvent(f, e.FullPath, isNew: true);
                w.Renamed += (_, e) => OnEvent(f, e.FullPath, isNew: true);
                w.Changed += (_, e) => OnEvent(f, e.FullPath, isNew: false);
                w.Deleted += (_, e) => OnEvent(f, e.FullPath, isNew: false);
                w.Error += (_, e) => { Log.Warn($"Folder watch error in {f}: {e.GetException().Message}"); _ui.BeginInvoke(() => MarkChanged(f)); };
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Log.Warn($"Cannot watch {folder}: {e.Message}");
            }
        }
    }

    // FileSystemWatcher threads.
    private void OnEvent(string folder, string path, bool isNew)
    {
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return;
        _ui.BeginInvoke(() =>
        {
            MarkChanged(folder);
            if (isNew && !_pending.ContainsKey(path)) { _pending[path] = (folder, -1, 0, DateTime.UtcNow); _poll.Start(); }
        });
    }

    private void MarkChanged(string folder)
    {
        _changedFolders.Add(folder);
        _debounce.Stop(); _debounce.Start();
    }

    private void FlushChanged()
    {
        _debounce.Stop();
        if (_changedFolders.Count == 0) return;
        var folders = _changedFolders.ToList();
        _changedFolders.Clear();
        Changed?.Invoke(folders);
    }

    private void Poll()
    {
        foreach (var (path, state) in _pending.ToList())
        {
            long size = -1;
            try { if (File.Exists(path)) size = new FileInfo(path).Length; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (size < 0 || DateTime.UtcNow - state.Since > TimeSpan.FromMinutes(10)) { _pending.Remove(path); continue; }
            int stable = size == state.Size && size > 0 ? state.Stable + 1 : 0;
            // Two unchanged checks (about a second) and nobody writing it.
            if (stable >= 2 && PdfLibrary.IsSettled(path))
            {
                _pending.Remove(path);
                Arrived?.Invoke(state.Folder, path);
            }
            else _pending[path] = (state.Folder, size, stable, state.Since);
        }
        if (_pending.Count == 0) _poll.Stop();
    }

    public void Dispose()
    {
        _debounce.Stop(); _poll.Stop();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}

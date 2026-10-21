using System.Diagnostics;
using System.Text;

namespace EKFastPDF.Core.Diagnostics;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Small, dependency-free diagnostic log. Writes to a size-capped file (one previous file kept)
/// and keeps the most recent lines in memory for the in-app diagnostics report.
/// Safe to call from any thread, including before the UI exists.
/// </summary>
public static class Log
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MemoryLines = 400;
    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private static string? _path;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public static string? FilePath => _path;
    /// <summary>Optional prefix such as "worker 3" so merged logs stay readable.</summary>
    public static string Source { get; set; } = "app";
    /// <summary>Extra sink (render workers forward their log lines to the viewer over stderr).</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>Elapsed time since the process started logging (monotonic).</summary>
    public static TimeSpan Uptime => Clock.Elapsed;

    public static void Initialize(string directory, string fileName)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, fileName);
        }
        catch (Exception e)
        {
            _path = null;
            Trace.WriteLine("EK FastPDF log unavailable: " + e.Message);
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? e = null) => Write(LogLevel.Error, e is null ? message : message + Environment.NewLine + e);

    public static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] {Source} #{Environment.ProcessId}: {message}";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > MemoryLines) Recent.Dequeue();
            Sink?.Invoke(line);
            if (_path is null) return;
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    string old = _path + ".1";
                    File.Delete(old);
                    File.Move(_path, old);
                }
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Another EK FastPDF process may hold the file for a moment; the line stays in memory.
            }
            catch (UnauthorizedAccessException)
            {
                _path = null;
            }
        }
    }

    public static string[] RecentLines()
    {
        lock (Gate) return Recent.ToArray();
    }
}

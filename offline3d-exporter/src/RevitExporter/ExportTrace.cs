using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Offline3D.RevitExporter;

/// <summary>
/// Live, flushed export log. One copy lives in the export work folder (the
/// viewer helper deletes that folder after success); the other stays under
/// %LOCALAPPDATA%\Offline3DPdfPrototype\Logs\Exports, so a slow, cancelled,
/// failed or crashed export always leaves a readable trace.
/// </summary>
internal sealed class ExportTrace : IDisposable
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<StreamWriter> writers = new List<StreamWriter>();
    private long lastHeartbeat = -1;
    private string stage = "Starting";
    private long stageStart;
    private readonly List<string> stages = new List<string>();

    public string? LogDirectory { get; }

    public ExportTrace(string workFolder)
    {
        Open(Path.Combine(workFolder, "export-trace.txt"));
        try
        {
            string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Offline3DPdfPrototype", "Logs", "Exports", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-revit-" + Path.GetFileName(workFolder));
            Directory.CreateDirectory(logs);
            Open(Path.Combine(logs, "export-trace.txt"));
            LogDirectory = logs;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Open(string path)
    {
        try { writers.Add(new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true }); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public TimeSpan Elapsed => clock.Elapsed;

    public void Line(string text)
    {
        string line = "[" + clock.Elapsed.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) + "] " + text;
        foreach (var writer in writers) { try { writer.WriteLine(line); } catch (IOException) { } catch (ObjectDisposedException) { } }
    }

    public void Lines(IEnumerable<string> lines) { foreach (var line in lines) Line(line); }

    /// <summary>Marks the start of a stage and logs how long the previous one took.</summary>
    public void Stage(string name)
    {
        long now = clock.ElapsedMilliseconds;
        string done = stage + ": " + (now - stageStart).ToString(CultureInfo.InvariantCulture) + " ms";
        stages.Add(done);
        Line("Stage done: " + done + " | next: " + name + " | " + Memory());
        stage = name; stageStart = now;
    }

    public IReadOnlyList<string> StageTimes => stages.Concat(new[] { stage + ": " + (clock.ElapsedMilliseconds - stageStart).ToString(CultureInfo.InvariantCulture) + " ms (current)" }).ToList();

    /// <summary>At most one line every few seconds; the last one shows what
    /// Revit was working on if it stops responding.</summary>
    public void Heartbeat(Func<string> snapshot, int intervalMs = 5000)
    {
        long now = clock.ElapsedMilliseconds;
        if (lastHeartbeat >= 0 && now - lastHeartbeat < intervalMs) return;
        lastHeartbeat = now;
        Line("Progress: " + snapshot() + " | " + Memory());
    }

    public static string Memory()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return "private " + (process.PrivateMemorySize64 >> 20) + " MiB, peak working set " + (process.PeakWorkingSet64 >> 20) + " MiB, managed " + (GC.GetTotalMemory(false) >> 20) + " MiB";
        }
        catch (InvalidOperationException) { return "memory unavailable"; }
    }

    /// <summary>Copies a diagnostics file into the persistent log folder.</summary>
    public void Keep(string file)
    {
        if (LogDirectory == null || !File.Exists(file)) return;
        try { File.Copy(file, Path.Combine(LogDirectory, Path.GetFileName(file)), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        foreach (var writer in writers) { try { writer.Dispose(); } catch (IOException) { } }
        writers.Clear();
    }
}

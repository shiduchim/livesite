using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EKFastPDF.Core.Diagnostics;

namespace EKFastPDF.Services;

/// <summary>
/// Opening a PDF from Explorer while EK FastPDF is running adds a tab to the running window
/// instead of starting a second copy. Per user and per session (named pipe restricted to the
/// current user).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private static readonly string Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.UserDomainName + "\\" + Environment.UserName + "|EKFastPDF")))[..16];

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _pipeName = "EKFastPDF-" + Id;
    }

    /// <summary>Returns the owner object when this is the first instance; otherwise forwards
    /// <paramref name="files"/> to the running instance and returns null.</summary>
    public static SingleInstance? Acquire(IReadOnlyList<string> files)
    {
        var mutex = new Mutex(true, "Local\\EKFastPDF-" + Id, out bool created);
        if (created) return new SingleInstance(mutex);
        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", "EKFastPDF-" + Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            AllowSetForegroundWindow(-1); // let the running window come to the front
            var payload = Encoding.UTF8.GetBytes(string.Join("\n", files.Prepend("EKFASTPDF-OPEN")));
            client.Write(payload);
            client.Flush();
            Log.Info($"Forwarded {files.Count} file(s) to the running instance");
            return null;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The other instance is not answering (e.g. still starting or hung): run independently.
            Log.Warn("Running instance did not respond (" + e.Message + "); starting a separate window");
            return new SingleInstance(new Mutex(false));
        }
    }

    public void Listen(Action<string[]> filesReceived)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var ms = new MemoryStream();
                    await server.CopyToAsync(ms, _stop.Token).ConfigureAwait(false);
                    var lines = Encoding.UTF8.GetString(ms.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0 && lines[0] == "EKFASTPDF-OPEN") filesReceived(lines.Skip(1).ToArray());
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("Single-instance pipe: " + e.Message);
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}

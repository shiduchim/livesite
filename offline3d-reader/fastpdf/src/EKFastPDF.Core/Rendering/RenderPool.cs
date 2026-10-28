using System.Diagnostics;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Ipc;
using EKFastPDF.Core.Native;

namespace EKFastPDF.Core.Rendering;

public sealed class RenderPoolOptions
{
    /// <summary>Executable that understands <see cref="WorkerHost.Argument"/> (EKFastPDF.exe itself).</summary>
    public required string WorkerExecutable { get; init; }
    public int WorkerCount { get; init; } = 4;
    /// <summary>Per-worker shared pixel buffer; bounds the largest single render request.</summary>
    public long SharedBufferBytes { get; init; } = 64L * 1024 * 1024;
    /// <summary>Parsed pages each worker keeps in memory (all documents together).</summary>
    public int MaxLoadedPagesPerWorker { get; init; } = 6;
    /// <summary>Text searches running at once, so search never occupies every worker.</summary>
    public int MaxConcurrentSearches { get; init; } = 1;
}

/// <summary>
/// Out-of-process PDFium render pool with a priority scheduler.
/// <list type="bullet">
/// <item>Each worker is a separate process with its own PDFium instance, so N workers rasterise
/// N tiles truly in parallel without violating PDFium's single-thread rule (the v1.1 design
/// serialised every PDFium call in the app behind one global lock).</item>
/// <item>Jobs carry a mutable priority; the scheduler always dispatches the most urgent job, so
/// the viewer can re-prioritise (cursor/centre first, active tab first) as the view changes.</item>
/// <item>Queued jobs can be withdrawn; running renders are interrupted through PDFium's
/// progressive-render pause callback (shared-memory cancel word).</item>
/// <item>A worker crash (e.g. a malformed PDF crashing PDFium) never takes the viewer down:
/// the worker restarts and the job is retried once.</item>
/// </list>
/// </summary>
public sealed class RenderPool : IDisposable
{
    private sealed class Worker
    {
        public required int Index;
        public Process? Process;
        public SharedBuffer? Shared;
        public Stream? Input;
        public Stream? Output;
        public readonly HashSet<int> OpenDocuments = new();
        public readonly Queue<int> PendingCloses = new();
        public PoolJob? Running;
        public readonly SemaphoreSlim Wake = new(0, 1);
        public readonly MessageWriter Writer = new();
        public int Crashes;
        public long Completed;
    }

    private readonly RenderPoolOptions _options;
    private readonly object _gate = new();
    private readonly List<PoolJob> _queue = new();
    private readonly Worker[] _workers;
    private readonly Dictionary<int, (string Path, string? Password)> _documents = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _loops;
    private int _nextDocumentId;
    private int _runningSearches;
    private readonly PoolStatistics _stats = new();

    public RenderPool(RenderPoolOptions options)
    {
        _options = options;
        _workers = new Worker[Math.Max(1, options.WorkerCount)];
        _loops = new Task[_workers.Length];
        for (int i = 0; i < _workers.Length; i++)
        {
            var worker = new Worker { Index = i };
            _workers[i] = worker;
            _loops[i] = Task.Run(() => RunLoopAsync(worker));
        }
        Log.Info($"Render pool: {_workers.Length} worker process(es), {options.SharedBufferBytes / (1024 * 1024)} MiB shared buffer each");
    }

    public int WorkerCount => _workers.Length;
    public PoolStatistics Statistics => _stats;
    public long MaxRegionBytes => _options.SharedBufferBytes - Protocol.SharedHeaderBytes;

    /// <summary>Starts all worker processes now instead of on first use (hides process start-up
    /// latency from the first page render).</summary>
    public void Warmup()
    {
        foreach (var w in _workers) _ = Task.Run(async () =>
        {
            try
            {
                bool start;
                lock (_gate) start = w.Process is null && w.Running is null;
                if (start) await EnsureStartedAsync(w).ConfigureAwait(false);
            }
            catch (Exception e) { Log.Warn($"Worker {w.Index} warm-up failed: {e.Message}"); }
        });
    }

    public int RegisterDocument(string path, string? password = null)
    {
        lock (_gate)
        {
            int id = ++_nextDocumentId;
            // Workers run with the program folder as working directory: always pass absolute paths.
            _documents[id] = (Path.GetFullPath(path), password);
            return id;
        }
    }

    public bool IsRegistered(int docId)
    {
        lock (_gate) return _documents.ContainsKey(docId);
    }

    public Task<DocumentInfo> OpenDocumentAsync(int docId)
    {
        (string Path, string? Password) entry;
        lock (_gate)
        {
            if (!_documents.TryGetValue(docId, out entry)) throw new InvalidOperationException("Unknown document.");
        }
        var job = new OpenJob(docId, entry.Path, entry.Password);
        Submit(job);
        return job.Completion.Task;
    }

    public Task<PdfPageMap> GetPageMapAsync(int docId,int page)
    {
        var job=new PageMapJob(docId,page);
        Submit(job);
        return job.Completion.Task;
    }

    public Task<PageSearchResult> SearchPageAsync(int docId, int page, string term, bool matchCase, bool wholeWord,
        int maxHits, double priority, CancellationToken token)
    {
        var job = new SearchJob(docId, page, term, matchCase, wholeWord, maxHits, priority);
        token.Register(() => Cancel(job));
        Submit(job);
        return job.Completion.Task;
    }

    /// <summary>Closes a document: drops its queued jobs, interrupts its running ones and makes
    /// every worker release its PDFium handles and parsed pages.</summary>
    public void CloseDocument(int docId)
    {
        var abandoned = new List<PoolJob>();
        lock (_gate)
        {
            if (!_documents.Remove(docId)) return;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i].DocId != docId) continue;
                abandoned.Add(_queue[i]);
                _queue[i].State = PoolJob.Finished;
                _queue.RemoveAt(i);
            }
            foreach (var w in _workers)
            {
                if (w.Running?.DocId == docId) w.Shared?.RequestCancel(w.Running.Id);
                if (w.OpenDocuments.Contains(docId)) w.PendingCloses.Enqueue(docId);
            }
        }
        foreach (var job in abandoned) SafeAbandon(job, JobOutcome.Canceled, "Document closed.");
        WakeIdleWorkers();
    }

    public void Submit(PoolJob job)
    {
        lock (_gate)
        {
            if (_shutdown.IsCancellationRequested) throw new ObjectDisposedException(nameof(RenderPool));
            if (!_documents.ContainsKey(job.DocId))
            {
                job.State = PoolJob.Finished;
                // Report outside the lock below.
            }
            else
            {
                job.State = PoolJob.Queued;
                job.EnqueuedTicks = Stopwatch.GetTimestamp();
                _queue.Add(job);
            }
        }
        if (job.State == PoolJob.Finished) { SafeAbandon(job, JobOutcome.Canceled, "Document closed."); return; }
        WakeIdleWorkers();
    }

    /// <summary>Withdraws a queued job (its callback reports Canceled) or interrupts a running one.
    /// Returns false when the job already finished.</summary>
    public bool Cancel(PoolJob job)
    {
        bool removed = false;
        lock (_gate)
        {
            if (job.State == PoolJob.Queued && _queue.Remove(job))
            {
                job.State = PoolJob.Finished;
                removed = true;
            }
            else if (job.State == PoolJob.Running)
            {
                foreach (var w in _workers)
                    if (ReferenceEquals(w.Running, job)) w.Shared?.RequestCancel(job.Id);
                return true;
            }
        }
        if (removed)
        {
            Interlocked.Increment(ref _stats.Canceled);
            SafeAbandon(job, JobOutcome.Canceled, "Canceled.");
        }
        return removed;
    }

    /// <summary>Number of jobs waiting (not running).</summary>
    public int QueuedCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    public int RunningCount
    {
        get { lock (_gate) return _workers.Count(w => w.Running != null); }
    }

    private PoolJob? TryDequeue(Worker worker)
    {
        lock (_gate)
        {
            int best = -1;
            for (int i = 0; i < _queue.Count; i++)
            {
                var job = _queue[i];
                if (job.Kind == JobKind.Search && _runningSearches >= _options.MaxConcurrentSearches) continue;
                if (best < 0 || job.Priority < _queue[best].Priority) best = i;
            }
            if (best < 0) return null;
            var chosen = _queue[best];
            _queue.RemoveAt(best);
            chosen.State = PoolJob.Running;
            chosen.StartedTicks = Stopwatch.GetTimestamp();
            if (chosen.Kind == JobKind.Search) _runningSearches++;
            worker.Running = chosen;
            return chosen;
        }
    }

    private void WakeIdleWorkers()
    {
        foreach (var w in _workers)
        {
            bool idle;
            lock (_gate) idle = w.Running is null;
            if (idle && w.Wake.CurrentCount == 0)
            {
                try { w.Wake.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task RunLoopAsync(Worker worker)
    {
        var token = _shutdown.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await DrainClosesAsync(worker).ConfigureAwait(false);
                var job = TryDequeue(worker);
                if (job is null)
                {
                    await worker.Wake.WaitAsync(token).ConfigureAwait(false);
                    continue;
                }
                await ExecuteAsync(worker, job).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                Log.Error($"Render worker {worker.Index} loop error", e);
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task DrainClosesAsync(Worker worker)
    {
        while (true)
        {
            int docId;
            lock (_gate)
            {
                if (worker.PendingCloses.Count == 0) return;
                docId = worker.PendingCloses.Dequeue();
                worker.OpenDocuments.Remove(docId);
            }
            if (worker.Process is null) continue;
            try
            {
                worker.Writer.Reset().U8((byte)Op.CloseDocument).I64(0).I32(docId);
                await RoundTripAsync(worker).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException or ObjectDisposedException)
            {
                HandleCrash(worker, e);
            }
        }
    }

    private async Task ExecuteAsync(Worker worker, PoolJob job)
    {
        try
        {
            await EnsureStartedAsync(worker).ConfigureAwait(false);
            (string Path, string? Password) doc;
            bool known;
            lock (_gate) known = _documents.TryGetValue(job.DocId, out doc);
            if (!known)
            {
                Finish(worker, job);
                SafeAbandon(job, JobOutcome.Canceled, "Document closed.");
                return;
            }

            if (job is not OpenJob && !worker.OpenDocuments.Contains(job.DocId))
            {
                OpenJob.WriteOpen(worker.Writer.Reset(), 0, job.DocId, doc.Path, doc.Password, wantInfo: false);
                var openReply = await RoundTripAsync(worker).ConfigureAwait(false);
                var status = (WorkerStatus)openReply.U8();
                string? message = openReply.Str();
                if (status != WorkerStatus.Ok)
                {
                    Finish(worker, job);
                    Interlocked.Increment(ref _stats.Failed);
                    SafeAbandon(job, JobOutcome.Failed, OpenJob.ToException(status, message).Message);
                    return;
                }
                lock (_gate) worker.OpenDocuments.Add(job.DocId);
            }

            job.WriteRequest(worker.Writer.Reset());
            var reply = await RoundTripAsync(worker).ConfigureAwait(false);
            var result = (WorkerStatus)reply.U8();
            string? error = reply.Str();
            if (job is OpenJob && result == WorkerStatus.Ok) lock (_gate) worker.OpenDocuments.Add(job.DocId);
            bool closed;
            lock (_gate) closed = !_documents.ContainsKey(job.DocId);
            if (closed && result == WorkerStatus.Ok) result = WorkerStatus.Canceled;

            // Clear a stale cancel request before the next job (ids are unique, this is hygiene).
            worker.Shared?.RequestCancel(0);
            try
            {
                job.OnResponse(reply, result, error, worker.Shared!);
            }
            catch (Exception e)
            {
                Log.Error("Render completion handler failed", e);
            }
            worker.Completed++;
            _stats.Record(job, result);
            Finish(worker, job);
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException or ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            HandleCrash(worker, e);
            Finish(worker, job);
            job.Attempts++;
            if (job.Attempts < 2 && !_shutdown.IsCancellationRequested)
            {
                Log.Warn($"Retrying job after worker {worker.Index} failure");
                lock (_gate)
                {
                    if (_documents.ContainsKey(job.DocId))
                    {
                        job.State = PoolJob.Queued;
                        _queue.Add(job);
                        return;
                    }
                }
            }
            Interlocked.Increment(ref _stats.Failed);
            SafeAbandon(job, JobOutcome.Failed, "The render worker stopped unexpectedly: " + e.Message);
            if (worker.Crashes > 3) await Task.Delay(Math.Min(5000, 250 * worker.Crashes)).ConfigureAwait(false);
        }
    }

    private void Finish(Worker worker, PoolJob job)
    {
        lock (_gate)
        {
            if (ReferenceEquals(worker.Running, job)) worker.Running = null;
            if (job.Kind == JobKind.Search && job.State == PoolJob.Running) _runningSearches--;
            job.State = PoolJob.Finished;
        }
        // A finished search may unblock another queued search on a different worker.
        if (job.Kind == JobKind.Search) WakeIdleWorkers();
    }

    private async Task<MessageReader> RoundTripAsync(Worker worker)
    {
        await Protocol.WriteFrameAsync(worker.Input!, worker.Writer.Buffer, worker.Writer.Length).ConfigureAwait(false);
        var frame = await Protocol.ReadFrameAsync(worker.Output!).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("Render worker exited.");
        var reader = new MessageReader(frame);
        reader.I64(); // request id echo
        return reader;
    }

    private async Task EnsureStartedAsync(Worker worker)
    {
        if (worker.Process is { HasExited: false }) return;
        if (worker.Process != null) HandleCrash(worker, new IOException("Worker process exited."));

        var sw = Stopwatch.StartNew();
        var shared = SharedBuffer.Create(_options.SharedBufferBytes);
        var psi = new ProcessStartInfo(_options.WorkerExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add(WorkerHost.Argument);
        psi.ArgumentList.Add("--index"); psi.ArgumentList.Add(worker.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--shm"); psi.ArgumentList.Add(shared.Name);
        psi.ArgumentList.Add("--shm-size"); psi.ArgumentList.Add(shared.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--max-pages"); psi.ArgumentList.Add(_options.MaxLoadedPagesPerWorker.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start render worker.");
        }
        catch
        {
            shared.Dispose();
            throw;
        }
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) Log.Write(LogLevel.Info, "[w] " + e.Data);
        };
        process.BeginErrorReadLine();

        worker.Process = process;
        worker.Shared = shared;
        worker.Input = process.StandardInput.BaseStream;
        worker.Output = process.StandardOutput.BaseStream;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        byte[]? hello;
        try
        {
            hello = await Protocol.ReadFrameAsync(worker.Output, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Render worker did not start within 30 seconds.");
        }
        if (hello is null) throw new IOException("Render worker exited during start-up (exit code " + SafeExitCode(process) + ").");
        var r = new MessageReader(hello);
        if (r.I32() != Protocol.Magic) throw new InvalidDataException("Render worker handshake was not recognised.");
        int version = r.I32();
        int pid = r.I32();
        string? error = r.Str();
        string? pdfiumPath = r.Str();
        if (version != Protocol.Version) throw new InvalidDataException($"Render worker protocol {version} does not match {Protocol.Version}.");
        if (error != null)
        {
            worker.Crashes = int.MaxValue / 2; // a start-up error (e.g. missing pdfium.dll) will not fix itself
            throw new InvalidOperationException("Render worker could not start: " + error);
        }
        Interlocked.Increment(ref _stats.WorkerStarts);
        _stats.LastWorkerStartMs = sw.Elapsed.TotalMilliseconds;
        Log.Info($"Worker {worker.Index} ready (pid {pid}) in {sw.ElapsedMilliseconds} ms; pdfium: {pdfiumPath}");
    }

    private static string SafeExitCode(Process p)
    {
        try { return p.HasExited ? p.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "running"; }
        catch (InvalidOperationException) { return "unknown"; }
    }

    private void HandleCrash(Worker worker, Exception e)
    {
        Interlocked.Increment(ref _stats.WorkerCrashes);
        worker.Crashes++;
        Log.Error($"Render worker {worker.Index} failed: {e.Message}");
        TearDown(worker);
    }

    private void TearDown(Worker worker)
    {
        var process = worker.Process;
        worker.Process = null;
        lock (_gate) { worker.OpenDocuments.Clear(); worker.PendingCloses.Clear(); }
        try { worker.Input?.Dispose(); } catch (IOException) { }
        worker.Input = null;
        worker.Output = null;
        if (process != null)
        {
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        worker.Shared?.Dispose();
        worker.Shared = null;
    }

    private static void SafeAbandon(PoolJob job, JobOutcome outcome, string message)
    {
        try { job.OnAbandoned(outcome, message); }
        catch (Exception e) { Log.Error("Job abandon handler failed", e); }
    }

    public void Dispose()
    {
        if (_shutdown.IsCancellationRequested) return;
        List<PoolJob> pending;
        lock (_gate)
        {
            _shutdown.Cancel();
            pending = _queue.ToList();
            _queue.Clear();
            foreach (var w in _workers) if (w.Running != null) w.Shared?.RequestCancel(w.Running.Id);
        }
        foreach (var job in pending) SafeAbandon(job, JobOutcome.Canceled, "Shutting down.");
        try { Task.WaitAll(_loops, TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        foreach (var w in _workers)
        {
            try
            {
                if (w.Process is { HasExited: false } && w.Input != null && w.Running is null)
                {
                    w.Writer.Reset().U8((byte)Op.Shutdown).I64(0);
                    Protocol.WriteFrame(w.Input, w.Writer.Span);
                    w.Process.WaitForExit(1000);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
            TearDown(w);
            w.Wake.Dispose();
        }
        _shutdown.Dispose();
    }

    /// <summary>
    /// Prints through a dedicated, short-lived worker process so the render pool stays free for
    /// the viewer while the printer driver works.
    /// </summary>
    public async Task<int> PrintAsync(string path, string? password, string printerName, byte[]? devMode, string jobName,
        string? outputFile, IReadOnlyList<int> pages, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Printing requires Windows.");
        using var pool = new RenderPool(new RenderPoolOptions
        {
            WorkerExecutable = _options.WorkerExecutable,
            WorkerCount = 1,
            SharedBufferBytes = 1024 * 1024,
            MaxLoadedPagesPerWorker = 2,
        });
        int doc = pool.RegisterDocument(path, password);
        await pool.OpenDocumentAsync(doc).ConfigureAwait(false);
        var job = new PrintJob(doc, printerName, devMode, jobName, outputFile, pages);
        using var reg = token.Register(() => pool.Cancel(job));
        pool.Submit(job);
        return await job.Completion.Task.ConfigureAwait(false);
    }

    private sealed class PrintJob : PoolJob
    {
        private readonly string _printer;
        private readonly byte[]? _devMode;
        private readonly string _name;
        private readonly string? _output;
        private readonly IReadOnlyList<int> _pages;
        public readonly TaskCompletionSource<int> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PrintJob(int docId, string printer, byte[]? devMode, string name, string? output, IReadOnlyList<int> pages)
            : base(docId, JobKind.Render, 0)
        {
            _printer = printer; _devMode = devMode; _name = name; _output = output; _pages = pages;
        }

        internal override void WriteRequest(MessageWriter w)
        {
            w.U8((byte)Op.PrintDocument).I64(Id).I32(DocId).Str(_printer).Bytes(_devMode).Str(_name).Str(_output).I32(_pages.Count);
            foreach (int p in _pages) w.I32(p);
        }

        internal override void OnResponse(MessageReader r, WorkerStatus status, string? message, SharedBuffer shared)
        {
            if (status == WorkerStatus.Ok) Completion.TrySetResult(r.I32());
            else if (status == WorkerStatus.Canceled) Completion.TrySetCanceled();
            else Completion.TrySetException(new InvalidOperationException(message ?? "Printing failed."));
        }

        internal override void OnAbandoned(JobOutcome outcome, string message)
        {
            if (outcome == JobOutcome.Canceled) Completion.TrySetCanceled();
            else Completion.TrySetException(new InvalidOperationException(message));
        }
    }
}

/// <summary>Scheduler counters and render latency samples (for diagnostics and benchmarks).</summary>
public sealed class PoolStatistics
{
    internal long Completed, Canceled, Failed, WorkerCrashes, WorkerStarts;
    private readonly object _gate = new();
    private readonly List<double> _renderMs = new();
    private readonly List<double> _queueMs = new();
    private readonly List<double> _parseMs = new();

    public long CompletedJobs => Interlocked.Read(ref Completed);
    public long CanceledJobs => Interlocked.Read(ref Canceled);
    public long FailedJobs => Interlocked.Read(ref Failed);
    public long Crashes => Interlocked.Read(ref WorkerCrashes);
    public long Starts => Interlocked.Read(ref WorkerStarts);
    public double LastWorkerStartMs { get; internal set; }

    internal void Record(PoolJob job, WorkerStatus status)
    {
        if (status == WorkerStatus.Canceled) { Interlocked.Increment(ref Canceled); return; }
        if (status != WorkerStatus.Ok) { Interlocked.Increment(ref Failed); return; }
        Interlocked.Increment(ref Completed);
        if (job is not RenderJob) return;
        double total = (Stopwatch.GetTimestamp() - job.StartedTicks) * 1000.0 / Stopwatch.Frequency;
        double queue = (job.StartedTicks - job.EnqueuedTicks) * 1000.0 / Stopwatch.Frequency;
        lock (_gate)
        {
            if (_renderMs.Count > 20000) { _renderMs.RemoveRange(0, 10000); _queueMs.RemoveRange(0, 10000); }
            _renderMs.Add(total);
            _queueMs.Add(queue);
        }
    }

    public (double P50, double P95, int Count) RenderLatency() => Percentiles(_renderMs);
    public (double P50, double P95, int Count) QueueLatency() => Percentiles(_queueMs);

    private (double, double, int) Percentiles(List<double> source)
    {
        double[] data;
        lock (_gate) data = source.ToArray();
        if (data.Length == 0) return (0, 0, 0);
        Array.Sort(data);
        return (data[(int)(0.50 * (data.Length - 1))], data[(int)(0.95 * (data.Length - 1))], data.Length);
    }

    public void Reset()
    {
        lock (_gate) { _renderMs.Clear(); _queueMs.Clear(); _parseMs.Clear(); }
    }
}

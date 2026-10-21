using EKFastPDF.Core.Ipc;

namespace EKFastPDF.Core.Rendering;

public enum JobKind { Open, Render, Search }

public enum JobOutcome { Completed, Canceled, Failed }

/// <summary>A unit of work for one render worker. Lower <see cref="Priority"/> runs first;
/// the priority may be changed while queued (the scheduler reads it at dispatch time).</summary>
public abstract class PoolJob
{
    internal const int Queued = 0, Running = 1, Finished = 2;
    private static long _nextId;

    internal readonly long Id = Interlocked.Increment(ref _nextId);
    internal int State;
    internal int Attempts;
    internal long EnqueuedTicks;
    internal long StartedTicks;

    protected PoolJob(int docId, JobKind kind, double priority)
    {
        DocId = docId;
        Kind = kind;
        Priority = priority;
    }

    public int DocId { get; }
    public JobKind Kind { get; }
    public double Priority { get; set; }
    public bool IsFinished => Volatile.Read(ref State) == Finished;
    public bool IsRunning => Volatile.Read(ref State) == Running;

    internal abstract void WriteRequest(MessageWriter writer);
    /// <summary>Runs on the worker's dispatch loop while the shared buffer still holds this
    /// job's output. Must be quick and must not block on the UI thread.</summary>
    internal abstract void OnResponse(MessageReader reader, WorkerStatus status, string? message, SharedBuffer shared);
    internal abstract void OnAbandoned(JobOutcome outcome, string message);
}

public readonly struct RenderResult
{
    public JobOutcome Outcome { get; init; }
    public string? Error { get; init; }
    /// <summary>BGRx pixels, valid only during the completion callback.</summary>
    public IntPtr Pixels { get; init; }
    public int Stride { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>Time the worker spent parsing the page (0 when the parsed page was cached).</summary>
    public double ParseMs { get; init; }
    public double RenderMs { get; init; }
    public double QueueMs { get; init; }
}

/// <summary>Renders a window (<see cref="X"/>,<see cref="Y"/>,<see cref="Width"/>,<see cref="Height"/>)
/// of a page rasterised at <see cref="PageWidth"/> x <see cref="PageHeight"/> device pixels.</summary>
public sealed class RenderJob : PoolJob
{
    /// <summary>Hidden render margin (px) around every window; see WorkerHost.RenderRegion.</summary>
    public const int SeamMargin = 24;
    private readonly Action<RenderJob, RenderResult> _completed;

    public RenderJob(int docId, int page, int pageWidth, int pageHeight, int x, int y, int width, int height,
        int flags, double priority, Action<RenderJob, RenderResult> completed, object? tag = null)
        : base(docId, JobKind.Render, priority)
    {
        Page = page; PageWidth = pageWidth; PageHeight = pageHeight;
        X = x; Y = y; Width = width; Height = height; Flags = flags;
        _completed = completed;
        Tag = tag;
    }

    public int Page { get; }
    public int PageWidth { get; }
    public int PageHeight { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Flags { get; }
    public object? Tag { get; }

    internal override void WriteRequest(MessageWriter w) =>
        w.U8((byte)Op.RenderRegion).I64(Id).I32(DocId).I32(Page).I32(PageWidth).I32(PageHeight)
         .I32(X).I32(Y).I32(Width).I32(Height).I32(Flags).I32(SeamMargin);

    internal override unsafe void OnResponse(MessageReader r, WorkerStatus status, string? message, SharedBuffer shared)
    {
        double queueMs = (StartedTicks - EnqueuedTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (status == WorkerStatus.Canceled) { _completed(this, new RenderResult { Outcome = JobOutcome.Canceled, QueueMs = queueMs }); return; }
        if (status != WorkerStatus.Ok) { _completed(this, new RenderResult { Outcome = JobOutcome.Failed, Error = message ?? status.ToString(), QueueMs = queueMs }); return; }
        int stride = r.I32();
        int offsetX = r.I32(), offsetY = r.I32();
        double parse = r.F64(), render = r.F64();
        _completed(this, new RenderResult
        {
            Outcome = JobOutcome.Completed, Pixels = (IntPtr)(shared.Pixels + (long)offsetY * stride + offsetX * 4), Stride = stride, Width = Width, Height = Height,
            ParseMs = parse, RenderMs = render, QueueMs = queueMs,
        });
    }

    internal override void OnAbandoned(JobOutcome outcome, string message) =>
        _completed(this, new RenderResult { Outcome = outcome, Error = message });
}

public sealed record DocumentInfo(int PageCount, float[] PageWidths, float[] PageHeights);

public enum OpenFailure { None, PasswordRequired, FileNotFound, Corrupt, UnsupportedSecurity, Other }

public sealed class PdfOpenException : Exception
{
    public PdfOpenException(OpenFailure failure, string message) : base(message) { Failure = failure; }
    public OpenFailure Failure { get; }
}

internal sealed class OpenJob : PoolJob
{
    private readonly string _path;
    private readonly string? _password;
    public readonly TaskCompletionSource<DocumentInfo> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OpenJob(int docId, string path, string? password) : base(docId, JobKind.Open, double.MinValue)
    {
        _path = path;
        _password = password;
    }

    internal override void WriteRequest(MessageWriter w) => WriteOpen(w, Id, DocId, _path, _password, wantInfo: true);

    internal static void WriteOpen(MessageWriter w, long id, int docId, string path, string? password, bool wantInfo) =>
        w.U8((byte)Op.OpenDocument).I64(id).I32(docId).Str(path).Str(password).Bool(wantInfo);

    internal override void OnResponse(MessageReader r, WorkerStatus status, string? message, SharedBuffer shared)
    {
        if (status != WorkerStatus.Ok) { Completion.TrySetException(ToException(status, message)); return; }
        int count = r.I32();
        var widths = new float[count];
        var heights = new float[count];
        for (int i = 0; i < count; i++) { widths[i] = r.F32(); heights[i] = r.F32(); }
        if (count < 1) { Completion.TrySetException(new PdfOpenException(OpenFailure.Corrupt, "This PDF contains no pages.")); return; }
        Completion.TrySetResult(new DocumentInfo(count, widths, heights));
    }

    internal static Exception ToException(WorkerStatus status, string? message) => status switch
    {
        WorkerStatus.PasswordRequired => new PdfOpenException(OpenFailure.PasswordRequired, "This PDF is protected by a password."),
        WorkerStatus.FileNotFound => new PdfOpenException(OpenFailure.FileNotFound, "The file could not be opened. It may have been moved, deleted or locked."),
        WorkerStatus.Corrupt => new PdfOpenException(OpenFailure.Corrupt, "The file is damaged or is not a PDF document."),
        WorkerStatus.UnsupportedSecurity => new PdfOpenException(OpenFailure.UnsupportedSecurity, "This PDF uses an unsupported security handler."),
        _ => new PdfOpenException(OpenFailure.Other, message ?? "The PDF could not be opened."),
    };

    internal override void OnAbandoned(JobOutcome outcome, string message)
    {
        if (outcome == JobOutcome.Canceled) Completion.TrySetCanceled();
        else Completion.TrySetException(new PdfOpenException(OpenFailure.Other, message));
    }
}

/// <summary>A search match: one or more rectangles in page display points (top-left origin,
/// page rotation applied), i.e. the same space the renderer draws in.</summary>
public sealed record SearchHit(int Page, float[] Rects);

public sealed record PageSearchResult(int Page, int CharCount, IReadOnlyList<SearchHit> Hits);

internal sealed class SearchJob : PoolJob
{
    private readonly int _page;
    private readonly string _term;
    private readonly uint _flags;
    private readonly int _maxHits;
    public readonly TaskCompletionSource<PageSearchResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SearchJob(int docId, int page, string term, bool matchCase, bool wholeWord, int maxHits, double priority)
        : base(docId, JobKind.Search, priority)
    {
        _page = page;
        _term = term;
        _flags = (matchCase ? Native.Pdfium.FPDF_MATCHCASE : 0) | (wholeWord ? Native.Pdfium.FPDF_MATCHWHOLEWORD : 0);
        _maxHits = maxHits;
    }

    internal override void WriteRequest(MessageWriter w) =>
        w.U8((byte)Op.SearchPage).I64(Id).I32(DocId).I32(_page).Str(_term).I32((int)_flags).I32(_maxHits);

    internal override void OnResponse(MessageReader r, WorkerStatus status, string? message, SharedBuffer shared)
    {
        if (status != WorkerStatus.Ok) { Completion.TrySetException(new InvalidOperationException(message ?? status.ToString())); return; }
        int chars = r.I32();
        int count = r.I32();
        var hits = new List<SearchHit>(count);
        for (int i = 0; i < count; i++)
        {
            int rects = r.I32();
            var data = new float[rects * 4];
            for (int k = 0; k < data.Length; k++) data[k] = r.F32();
            hits.Add(new SearchHit(_page, data));
        }
        Completion.TrySetResult(new PageSearchResult(_page, chars, hits));
    }

    internal override void OnAbandoned(JobOutcome outcome, string message)
    {
        if (outcome == JobOutcome.Canceled) Completion.TrySetCanceled();
        else Completion.TrySetException(new InvalidOperationException(message));
    }
}

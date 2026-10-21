using System.Diagnostics;

namespace EKFastPDF.Viewer;

/// <summary>Interaction measurements collected by the viewport (debug overlay, benchmark mode).</summary>
public sealed class ViewerMetrics
{
    private readonly object _gate = new();
    private readonly List<double> _frameMs = new();
    private readonly List<double> _intervals = new();
    private readonly List<double> _inputLatency = new();
    private readonly List<double> _timeToSharp = new();
    private long _lastFrameAt, _changeAt, _firstPixelStart;

    public double LastFrameMs { get; private set; }
    public double LastTimeToSharpMs { get; private set; }
    public double FirstPixelMs { get; private set; } = -1;
    public long FramesBuilt { get; private set; }

    public void RecordFrame(double buildMs, long inputAt)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            LastFrameMs = buildMs;
            FramesBuilt++;
            Add(_frameMs, buildMs);
            if (_lastFrameAt != 0)
            {
                double interval = (now - _lastFrameAt) * 1000.0 / Stopwatch.Frequency;
                if (interval < 250) Add(_intervals, interval); // ignore idle gaps between interactions
            }
            _lastFrameAt = now;
            if (inputAt != 0) Add(_inputLatency, (now - inputAt) * 1000.0 / Stopwatch.Frequency);
        }
    }

    public void MarkViewChange() => _changeAt = Stopwatch.GetTimestamp();

    public void MarkSharp(long frame)
    {
        if (_changeAt == 0) return;
        LastTimeToSharpMs = (Stopwatch.GetTimestamp() - _changeAt) * 1000.0 / Stopwatch.Frequency;
        lock (_gate) Add(_timeToSharp, LastTimeToSharpMs);
    }

    public void ResetFirstPixel()
    {
        _firstPixelStart = Stopwatch.GetTimestamp();
        FirstPixelMs = -1;
    }

    public void MarkFirstPixelIfNeeded(bool hasPixels)
    {
        if (!hasPixels || FirstPixelMs >= 0 || _firstPixelStart == 0) return;
        FirstPixelMs = (Stopwatch.GetTimestamp() - _firstPixelStart) * 1000.0 / Stopwatch.Frequency;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _frameMs.Clear(); _intervals.Clear(); _inputLatency.Clear(); _timeToSharp.Clear();
            _lastFrameAt = 0;
        }
    }

    public Summary Snapshot()
    {
        lock (_gate)
        {
            return new Summary(Stats(_frameMs), Stats(_intervals), Stats(_inputLatency), Stats(_timeToSharp));
        }
    }

    private static void Add(List<double> list, double v)
    {
        if (list.Count >= 50_000) list.RemoveRange(0, 25_000);
        list.Add(v);
    }

    private static Stat Stats(List<double> source)
    {
        if (source.Count == 0) return new Stat(0, 0, 0, 0, 0);
        var data = source.ToArray();
        Array.Sort(data);
        double P(double q) => data[(int)Math.Round(q * (data.Length - 1))];
        return new Stat(data.Length, P(0.5), P(0.95), data[^1], data.Average());
    }

    public sealed record Stat(int Count, double P50, double P95, double Max, double Mean);

    /// <summary>FrameBuildMs: UI-thread time per frame. FrameIntervalMs: time between consecutive
    /// frames while interacting (16.7 ms = 60 fps). InputLatencyMs: input event to frame built.</summary>
    public sealed record Summary(Stat FrameBuildMs, Stat FrameIntervalMs, Stat InputLatencyMs, Stat TimeToSharpMs);
}

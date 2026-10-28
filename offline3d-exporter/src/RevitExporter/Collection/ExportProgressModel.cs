namespace Offline3D.RevitExporter;

internal enum ExportStage { Preparing, Geometry, Writing, PdfPages, Finishing }

/// <summary>
/// Overall export progress, time left and final size, without Revit types (tested
/// in ExporterCoreTests). Geometry collection dominates, so its share is measured
/// by elements: host elements near the crop are counted before the traversal and
/// each loaded link gets an estimate of its elements inside the export box. The
/// later steps are estimated from the model size and refined as they run.
/// </summary>
internal sealed class ExportProgressModel
{
    // Share of the overall bar for each stage (sums to 1).
    private static readonly double[] Weights = { .05, .80, .06, .08, .01 };
    /// <summary>Typical deflate ratio of GLB geometry inside the PDF attachment.</summary>
    internal const double CompressionRatio = .62;
    private const double WriteBytesPerSecond = 40e6, PdfPageSeconds = 3.5, PageBytes = 400_000;

    private readonly Func<double> clock;
    private ExportStage stage = ExportStage.Preparing;
    private double stageStarted, geometryStarted = -1, lastEstimateAt = -1;
    private double? shownSecondsLeft;
    private long totalUnits, completedUnits, hostBegun;
    private readonly Queue<long> regionHosts = new Queue<long>();
    private readonly Queue<Dictionary<string, long>> regionLinks = new Queue<Dictionary<string, long>>();
    private long regionHostTotal;
    private Dictionary<string, long> links = new Dictionary<string, long>(StringComparer.Ordinal);
    private string? currentLink; private long currentLinkBegun; private int linkDepth;
    private long finishedLinkUnits;
    private int pdfPagesDone;
    private long modelBytes;

    public ExportProgressModel(Func<double> clockSeconds) { clock = clockSeconds; stageStarted = clock(); }

    public ExportStage Stage => stage;
    /// <summary>Overall progress, 0 to 1.</summary>
    public double Fraction { get; private set; }
    /// <summary>Estimated seconds left for the whole export, or null while too early to say.</summary>
    public double? SecondsLeft => shownSecondsLeft;
    /// <summary>Uncompressed GLB bytes collected so far.</summary>
    public long ModelBytes => modelBytes;
    /// <summary>Projected size of the finished PDF, or null while too early to say.</summary>
    public long? ProjectedPdfBytes { get; private set; }

    /// <summary>Elements to traverse, per region: host elements near the crop and an estimate per loaded link (by link key).</summary>
    public void AddRegion(long hostElements, IDictionary<string, long> linkEstimates)
    {
        regionHosts.Enqueue(Math.Max(0, hostElements));
        var copy = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pair in linkEstimates) copy[pair.Key] = Math.Max(1, pair.Value);
        regionLinks.Enqueue(copy);
        totalUnits += Math.Max(0, hostElements) + copy.Values.Sum() ;
    }

    public void Enter(ExportStage next)
    {
        if (next == stage && next != ExportStage.Geometry) return;
        stage = next; stageStarted = clock();
        if (next == ExportStage.Geometry && geometryStarted < 0) geometryStarted = stageStarted;
        Update();
    }

    /// <summary>The traversal of the next region starts.</summary>
    public void BeginRegion()
    {
        completedUnits += regionHostTotal + links.Values.Sum();
        regionHostTotal = regionHosts.Count > 0 ? regionHosts.Dequeue() : 0;
        links = regionLinks.Count > 0 ? regionLinks.Dequeue() : new Dictionary<string, long>(StringComparer.Ordinal);
        hostBegun = 0; finishedLinkUnits = 0; currentLink = null; currentLinkBegun = 0; linkDepth = 0;
        Enter(ExportStage.Geometry);
    }

    public void ElementBegun(bool inLink) { if (inLink && currentLink != null) currentLinkBegun++; else if (!inLink) hostBegun++; }

    /// <summary>A link is entered; nested links count toward their top-level link.</summary>
    public void LinkEntered(string key) { if (linkDepth++ == 0) { currentLink = key; currentLinkBegun = 0; } }

    public void LinkExited()
    {
        if (linkDepth == 0) return;
        if (--linkDepth == 0 && currentLink != null)
        {
            finishedLinkUnits += links.TryGetValue(currentLink, out var estimate) ? estimate : 0;
            currentLink = null; currentLinkBegun = 0;
        }
    }

    public void SetModelBytes(long bytes) => modelBytes = Math.Max(0, bytes);
    public void PdfPageDone() { pdfPagesDone++; Update(); }

    /// <summary>0 to 1 within geometry collection, never 1 before it ends.</summary>
    public double GeometryFraction
    {
        get
        {
            if (stage > ExportStage.Geometry) return 1;
            if (totalUnits <= 0) return 0;
            long current = 0;
            if (currentLink != null && links.TryGetValue(currentLink, out var estimate)) current = Math.Min(currentLinkBegun, (long)(estimate * .98));
            double done = completedUnits + Math.Min(hostBegun, regionHostTotal) + finishedLinkUnits + current;
            return Math.Min(.99, done / totalUnits);
        }
    }

    /// <summary>Recomputes the fraction and, at most every half second, the time left.</summary>
    public void Update()
    {
        double now = clock(), inStage = now - stageStarted;
        double geometry = GeometryFraction, projectedModel = ProjectedModelBytes(geometry);
        double writeSeconds = .5 + projectedModel / WriteBytesPerSecond, pdfSeconds = PdfPageSeconds * Math.Max(0, 2 - pdfPagesDone);
        double within = stage switch
        {
            ExportStage.Preparing => Math.Min(.9, inStage / 10),
            ExportStage.Geometry => geometry,
            ExportStage.Writing => Math.Min(.95, inStage / writeSeconds),
            ExportStage.PdfPages => Math.Min(.95, (pdfPagesDone + Math.Min(.9, inStage / PdfPageSeconds)) / 2),
            _ => 1
        };
        double before = 0; for (int i = 0; i < (int)stage; i++) before += Weights[i];
        Fraction = Math.Max(Fraction, Math.Min(1, before + Weights[(int)stage] * within));

        if (projectedModel > 0) ProjectedPdfBytes = (long)(projectedModel * CompressionRatio + 2 * PageBytes);
        if (lastEstimateAt >= 0 && now - lastEstimateAt < .5) return;
        double? raw = stage switch
        {
            ExportStage.Preparing => null,
            ExportStage.Geometry => geometry >= .03 && now - geometryStarted >= 2 ? (now - geometryStarted) * (1 - geometry) / geometry + writeSeconds + pdfSeconds : null,
            ExportStage.Writing => Math.Max(0, writeSeconds - inStage) + pdfSeconds,
            ExportStage.PdfPages => Math.Max(0, pdfSeconds - Math.Min(inStage, PdfPageSeconds)),
            _ => 0
        };
        if (raw == null) { lastEstimateAt = now; return; }
        // Smooth: the shown value counts down in real time and moves a quarter of the way to each new estimate.
        if (shownSecondsLeft == null || lastEstimateAt < 0) shownSecondsLeft = raw;
        else { double predicted = Math.Max(0, shownSecondsLeft.Value - (now - lastEstimateAt)); shownSecondsLeft = predicted + .25 * (raw.Value - predicted); }
        lastEstimateAt = now;
    }

    private double ProjectedModelBytes(double geometry)
    {
        if (modelBytes <= 0) return 0;
        if (stage > ExportStage.Geometry) return modelBytes;
        return geometry >= .05 ? modelBytes / geometry : 0;
    }

    /// <summary>"About 3 min left", "Less than a minute left", or "" while estimating.</summary>
    public static string FormatTimeLeft(double? seconds)
    {
        if (seconds == null) return "";
        double s = seconds.Value;
        if (s < 50) return "Less than a minute left";
        if (s < 90) return "About 1 minute left";
        if (s < 3600) return "About " + Math.Round(s / 60) + " minutes left";
        double hours = Math.Floor(s / 3600), minutes = Math.Round((s - hours * 3600) / 60 / 5) * 5;
        return "About " + hours + " h " + (minutes > 0 ? minutes + " min " : "") + "left";
    }

    public static string FormatBytes(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " GB" : Math.Max(1, (long)Math.Round(bytes / 1048576d)).ToString(System.Globalization.CultureInfo.CurrentCulture) + " MB";
}

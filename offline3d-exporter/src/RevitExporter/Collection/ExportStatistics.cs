using System.Globalization;

namespace Offline3D.RevitExporter;

/// <summary>Counters for one geometry collection. Written to the export
/// diagnostics so a native run shows where time and triangles went.</summary>
internal sealed class ExportStatistics
{
    public long ElementsBegun, LinkedElementsBegun, ElementsExported, LinkedElementsExported;
    public long SkippedNotModel, SkippedAlreadyExported, SkippedOutsideCrop, SkippedTiny;
    public long InstancesBegun, InstancesReused, InstancesCulled, InstancesStraddling, InstancesReplayedEmpty;
    public long Captures, CapturesIncomplete, ReuseVerified, ReuseRejected, TrianglesReused;
    public int ReuseKeys, ReuseKeysVerified, ReuseKeysUnsafe;
    public long LinksEntered, LinksUnloaded;
    public long MaterialChanges, Polymeshes, PolymeshesOutsideCrop, FacetsReceived, FacetsKept, FacetsOutsideCrop;
    public long BatchesWritten, BatchesSplit;
    public long PointsOnSectionBoxFaces, PointsOutsideSectionBox, PolymeshesTouchingSectionBox;
    public long UnbalancedEnds; public int OpenSpacesAtEnd;

    public Dictionary<string, long> CategoryTriangles { get; } = new Dictionary<string, long>(StringComparer.Ordinal);

    private readonly List<(double Ms, long Triangles, string Label, string Category)> heavy = new List<(double, long, string, string)>();
    private const int HeavyKeep = 400;

    public void AddCategoryTriangles(string category, long count)
    {
        CategoryTriangles.TryGetValue(category, out long prior);
        CategoryTriangles[category] = prior + count;
    }

    /// <summary>Keeps the slowest and the largest elements (bounded memory).</summary>
    public void RecordElement(ElementInfo info, double ms, long triangles)
    {
        if (ms < 25 && triangles < 20000) return;
        heavy.Add((ms, triangles, info.Label.Length > 0 ? info.Label : info.Key, info.Category));
        if (heavy.Count > HeavyKeep * 2)
        {
            var keep = heavy.OrderByDescending(h => h.Ms).Take(HeavyKeep).Concat(heavy.OrderByDescending(h => h.Triangles).Take(HeavyKeep)).Distinct().ToList();
            heavy.Clear(); heavy.AddRange(keep);
        }
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public IEnumerable<string> Report(int top = 25)
    {
        yield return "Elements begun: " + N(ElementsBegun) + " (in links: " + N(LinkedElementsBegun) + "); exported: " + N(ElementsExported) + " (linked: " + N(LinkedElementsExported) + ")";
        yield return "Elements skipped: not model " + N(SkippedNotModel) + ", already exported " + N(SkippedAlreadyExported) + ", outside crop " + N(SkippedOutsideCrop) + ", tiny (optional filter) " + N(SkippedTiny);
        yield return "Links entered: " + N(LinksEntered) + "; links without a loaded document: " + N(LinksUnloaded);
        yield return "Instances begun: " + N(InstancesBegun) + "; reused without tessellation: " + N(InstancesReused) + " (" + N(TrianglesReused) + " triangles); culled outside crop: " + N(InstancesCulled) + "; crossing the crop (traversed): " + N(InstancesStraddling) + "; empty replays: " + N(InstancesReplayedEmpty);
        yield return "Instance captures: " + N(Captures) + " complete, " + N(CapturesIncomplete) + " incomplete; verifications passed: " + N(ReuseVerified) + ", rejected: " + N(ReuseRejected) + "; symbol keys: " + ReuseKeys + " (verified " + ReuseKeysVerified + ", unsafe " + ReuseKeysUnsafe + ")";
        yield return "Polymeshes: " + N(Polymeshes) + " (fully outside crop: " + N(PolymeshesOutsideCrop) + "); facets received: " + N(FacetsReceived) + ", kept: " + N(FacetsKept) + ", outside crop: " + N(FacetsOutsideCrop);
        yield return "Material changes: " + N(MaterialChanges) + "; batches written: " + N(BatchesWritten) + " (split for size: " + N(BatchesSplit) + ")";
        yield return "Section-box evidence: points on box faces " + N(PointsOnSectionBoxFaces) + " in " + N(PolymeshesTouchingSectionBox) + " polymeshes; points outside the box " + N(PointsOutsideSectionBox) + ". Many on-face points suggest Revit cuts geometry at the section box; points outside mean it does not cull them.";
        yield return "Callback balance: unmatched ends " + N(UnbalancedEnds) + ", open transform levels at end " + OpenSpacesAtEnd + " (both should be 0)";
        foreach (var pair in CategoryTriangles.OrderByDescending(p => p.Value).Take(top)) yield return "Category: " + pair.Key + " | " + N(pair.Value) + " triangles";
        foreach (var h in heavy.OrderByDescending(h => h.Ms).Take(top)) yield return "Slow element: " + h.Ms.ToString("0", CultureInfo.InvariantCulture) + " ms | " + N(h.Triangles) + " triangles | " + h.Category + " | " + h.Label;
        foreach (var h in heavy.OrderByDescending(h => h.Triangles).Take(top)) yield return "Large element: " + N(h.Triangles) + " triangles | " + h.Ms.ToString("0", CultureInfo.InvariantCulture) + " ms | " + h.Category + " | " + h.Label;
    }
}

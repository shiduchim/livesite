using System.Runtime.InteropServices;

namespace EKFastPDF.Core.Diagnostics;

/// <summary>
/// CPU topology and memory, used to size the render pool and caches at run time instead of
/// hard-coding numbers for one PC. Detects Intel hybrid CPUs (P-cores vs E-cores) on Windows via
/// GetLogicalProcessorInformationEx's EfficiencyClass.
/// </summary>
public sealed record HardwareProfile(
    int LogicalProcessors,
    int PhysicalCores,
    int PerformanceCores,
    int EfficiencyCores,
    long TotalMemoryBytes)
{
    public bool IsHybrid => EfficiencyCores > 0 && PerformanceCores > 0;

    /// <summary>
    /// Default render worker count. Rationale (see docs/BENCHMARKS.md):
    /// rasterisation is CPU-bound and scales with real cores, not SMT threads; on hybrid CPUs the
    /// E-cores are roughly half as fast and a tile placed there becomes the last one to finish, so
    /// we size to the performance cores, keeping one core for the UI and composition threads.
    /// Each worker holds its own parsed copy of the visible page(s), so very large drawings also
    /// bound the count by RAM (about one worker per 3 GB).
    /// </summary>
    public int RecommendedWorkers
    {
        get
        {
            int cpu = IsHybrid ? PerformanceCores - 1 : Math.Min(PhysicalCores, LogicalProcessors - 1);
            int byMemory = (int)Math.Max(1, TotalMemoryBytes / (3L * 1024 * 1024 * 1024));
            return Math.Clamp(Math.Min(cpu, byMemory), 1, 8);
        }
    }

    /// <summary>Default tile cache: 1/32 of RAM between 256 MB and 1.5 GB (1 GB on a 32 GB PC).</summary>
    public long RecommendedCacheBytes =>
        Math.Clamp(TotalMemoryBytes / 32, 256L * 1024 * 1024, 1536L * 1024 * 1024);

    public override string ToString() =>
        $"{LogicalProcessors} logical / {PhysicalCores} cores" +
        (IsHybrid ? $" ({PerformanceCores} P + {EfficiencyCores} E)" : "") +
        $", {TotalMemoryBytes / (1024.0 * 1024 * 1024):0.#} GB RAM";

    public static HardwareProfile Detect()
    {
        int logical = Environment.ProcessorCount;
        long memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var (cores, perf, eff) = WindowsCores();
                if (cores > 0) return new HardwareProfile(logical, cores, perf, eff, memory);
            }
            catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or InvalidOperationException)
            {
                Log.Warn("CPU topology query failed: " + e.Message);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            int cores = LinuxCores();
            if (cores > 0) return new HardwareProfile(logical, cores, cores, 0, memory);
        }
        return new HardwareProfile(logical, logical, logical, 0, memory);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    private static (int Cores, int Performance, int Efficiency) WindowsCores()
    {
        const int RelationProcessorCore = 0;
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
        if (length == 0) return (0, 0, 0);
        IntPtr buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length)) return (0, 0, 0);
            var classes = new List<byte>();
            int offset = 0;
            while (offset < length)
            {
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship(4) Size(4) then PROCESSOR_RELATIONSHIP
                // whose first bytes are Flags(1) EfficiencyClass(1).
                int relationship = Marshal.ReadInt32(buffer, offset);
                int size = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0) break;
                if (relationship == RelationProcessorCore) classes.Add(Marshal.ReadByte(buffer, offset + 9));
                offset += size;
            }
            if (classes.Count == 0) return (0, 0, 0);
            byte max = classes.Max();
            byte min = classes.Min();
            if (max == min) return (classes.Count, classes.Count, 0);
            int perf = classes.Count(c => c == max);
            return (classes.Count, perf, classes.Count - perf);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int LinuxCores()
    {
        try
        {
            var ids = new HashSet<string>();
            foreach (var dir in Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*"))
            {
                string core = Path.Combine(dir, "topology", "core_id");
                string pkg = Path.Combine(dir, "topology", "physical_package_id");
                if (File.Exists(core)) ids.Add((File.Exists(pkg) ? File.ReadAllText(pkg).Trim() : "0") + ":" + File.ReadAllText(core).Trim());
            }
            return ids.Count;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }
}

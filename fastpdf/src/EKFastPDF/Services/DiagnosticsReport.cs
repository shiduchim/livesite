using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Media;
using EKFastPDF.Core.Diagnostics;
using Microsoft.Win32;

namespace EKFastPDF.Services;

/// <summary>
/// Plain-text troubleshooting report (menu: Diagnostics, or "EKFastPDF.exe --diagnose").
/// Includes the launch-related facts that matter for "Windows cannot access the specified
/// device, path, or file": security zone of the program folder, Mark-of-the-Web, cloud placeholder
/// attributes and Smart App Control state.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(AppServices? services)
    {
        var sb = new StringBuilder();
        void Line(string label, object? value) => sb.Append(label.PadRight(26)).Append(": ").AppendLine(value?.ToString());

        string exe = Environment.ProcessPath ?? "?";
        sb.AppendLine("EK FastPDF diagnostics report");
        sb.AppendLine(new string('=', 60));
        Line("Version", Assembly.GetExecutingAssembly().GetName().Version);
        Line("Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Line("Executable", exe);
        Line("Base directory", AppContext.BaseDirectory);
        Line("Working directory", Environment.CurrentDirectory);
        Line("Command line", Environment.CommandLine);
        Line("OS", RuntimeInformation.OSDescription + " " + RuntimeInformation.OSArchitecture);
        Line(".NET runtime", RuntimeInformation.FrameworkDescription + " " + RuntimeInformation.ProcessArchitecture);
        Line("Elevated", IsElevated());
        Line("Log file", Log.FilePath);
        sb.AppendLine();

        sb.AppendLine("Launch environment");
        Line("Security zone (exe)", SecurityZone(exe));
        Line("Mark-of-the-Web", ZoneIdentifier(exe) ?? "none");
        Line("Exe attributes", SafeAttributes(exe));
        Line("Smart App Control", SmartAppControl());
        foreach (var name in new[] { "EKFastPDF.dll", "EKFastPDF.Core.dll", "pdfium.dll", "EKFastPDF.runtimeconfig.json", "hostfxr.dll", "coreclr.dll", "wpfgfx_cor3.dll" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            Line("  " + name, File.Exists(path) ? $"{new FileInfo(path).Length:N0} bytes" : "MISSING");
        }
        sb.AppendLine();

        sb.AppendLine("Hardware");
        var hw = services?.Hardware ?? HardwareProfile.Detect();
        Line("CPU", hw);
        Line("Recommended workers", hw.RecommendedWorkers);
        var gpu = services?.Gpu ?? GpuInfo.Query();
        foreach (var a in gpu.Adapters) Line("GPU adapter", a);
        Line("WPF render tier", $"{RenderCapability.Tier >> 16} (2 = full GPU acceleration)");
        Line("WPF render mode", System.Windows.Media.RenderOptions.ProcessRenderMode);
        var vram = GpuInfo.QueryProcessVideoMemory();
        if (vram is { } v) Line("Process VRAM", $"{v.CurrentUsage / (1024 * 1024)} MB used of {v.Budget / (1024 * 1024)} MB budget");
        using (var p = Process.GetCurrentProcess())
            Line("Process memory", $"{p.WorkingSet64 / (1024 * 1024)} MB working set, {p.PrivateMemorySize64 / (1024 * 1024)} MB private");
        sb.AppendLine();

        if (services != null)
        {
            sb.AppendLine("Rendering");
            var pool = services.Pool;
            var stats = pool.Statistics;
            var (p50, p95, n) = stats.RenderLatency();
            var (q50, q95, _) = stats.QueueLatency();
            Line("Render workers", $"{pool.WorkerCount} ({pool.RunningCount} busy, {pool.QueuedCount} queued)");
            Line("Jobs", $"{stats.CompletedJobs} done, {stats.CanceledJobs} canceled, {stats.FailedJobs} failed, {stats.Crashes} worker restarts");
            Line("Tile render latency", $"p50 {p50:0.0} ms, p95 {p95:0.0} ms over {n} tiles");
            Line("Queue wait", $"p50 {q50:0.0} ms, p95 {q95:0.0} ms");
            var cache = services.Tiles;
            Line("Tile cache", $"{cache.Bytes / (1024 * 1024)} / {cache.BudgetBytes / (1024 * 1024)} MB, {cache.Count} bitmaps, hit rate {cache.HitRate:P0}");
            Line("Settings", $"tile {services.Settings.TileSize}px, LCD text {services.Settings.LcdText}, wheel zooms {services.Settings.WheelZooms}");
            sb.AppendLine();
        }

        sb.AppendLine("Recent log");
        foreach (var line in Log.RecentLines().TakeLast(80)) sb.AppendLine(line);
        return sb.ToString();
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception) { return false; }
    }

    private static string SafeAttributes(string path)
    {
        try
        {
            var attr = (int)File.GetAttributes(path);
            var flags = ((FileAttributes)attr).ToString();
            // Cloud placeholder (OneDrive Files On-Demand) markers are not in the FileAttributes enum.
            if ((attr & 0x00400000) != 0) flags += ", RECALL_ON_DATA_ACCESS (cloud placeholder)";
            if ((attr & 0x00040000) != 0) flags += ", RECALL_ON_OPEN";
            if ((attr & 0x00080000) != 0) flags += ", PINNED";
            if ((attr & 0x00100000) != 0) flags += ", UNPINNED";
            return flags;
        }
        catch (Exception e) { return e.Message; }
    }

    public static string? ZoneIdentifier(string path)
    {
        try
        {
            string ads = path + ":Zone.Identifier";
            return File.Exists(ads) ? File.ReadAllText(ads).Replace("\r", "").Replace("\n", " ").Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    private static string SmartAppControl()
    {
        try
        {
            var v = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", null);
            return v switch { 0 => "Off", 1 => "On (enforcing)", 2 => "Evaluation", null => "not present", _ => v.ToString() ?? "" };
        }
        catch (Exception e) { return e.Message; }
    }

    [ComImport, Guid("79eac9ee-baf9-11ce-8c82-00aa004ba90b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInternetSecurityManager
    {
        [PreserveSig] int SetSecuritySite(IntPtr site);
        [PreserveSig] int GetSecuritySite(out IntPtr site);
        [PreserveSig] int MapUrlToZone([MarshalAs(UnmanagedType.LPWStr)] string url, out int zone, int flags);
    }

    /// <summary>The URL security zone Windows assigns to the program path (0 Local machine,
    /// 1 Intranet, 2 Trusted, 3 Internet, 4 Restricted). Shell launches of programs from the
    /// Intranet/Internet zones are subject to "Launching applications and unsafe files" policy.</summary>
    public static string SecurityZone(string path)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("7b8a2d94-0ac9-11d1-896c-00c04fb6bfc4"));
            if (type is null) return "unavailable";
            var manager = (IInternetSecurityManager)Activator.CreateInstance(type)!;
            try
            {
                int hr = manager.MapUrlToZone(path, out int zone, 0);
                if (hr != 0) return $"unknown (0x{hr:X8})";
                return zone switch
                {
                    0 => "0 Local machine",
                    1 => "1 Local intranet",
                    2 => "2 Trusted sites",
                    3 => "3 Internet",
                    4 => "4 Restricted sites",
                    _ => zone.ToString(),
                };
            }
            finally { Marshal.ReleaseComObject(manager); }
        }
        catch (Exception e) { return "unavailable: " + e.Message; }
    }
}

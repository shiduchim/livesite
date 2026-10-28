using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EKFastPDF.Core.Diagnostics;

namespace EKFastPDF.Core.Native;

/// <summary>Windows scheduling hints for hybrid (P-core / E-core) processors.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsPerformance
{
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int SetProcessInformation(IntPtr process, int infoClass, void* info, uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Render workers own no window, so Windows 11 may treat them as background work and apply
    /// EcoQoS, which on Intel hybrid CPUs (e.g. i7-12700) steers threads to the efficiency cores at
    /// low clocks. Opting out keeps worker threads eligible for the performance cores while the
    /// user is waiting on a render. Harmless no-op on non-hybrid CPUs and older Windows.
    /// </summary>
    public static void OptOutOfPowerThrottling()
    {
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = 0, // control the policy, and turn throttling OFF
        };
        if (SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE)) == 0)
            Log.Debug("SetProcessInformation(power throttling) failed: " + Marshal.GetLastWin32Error());
    }
}

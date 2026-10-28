using System.Runtime.InteropServices;
using EKFastPDF.Core.Diagnostics;

namespace EKFastPDF.Services;

/// <summary>
/// Run-time GPU query through DXGI (no assumptions about which card or how much VRAM).
/// Used for the diagnostics report, for sizing, and for benchmark VRAM readings.
/// </summary>
public sealed unsafe class GpuInfo
{
    public sealed record Adapter(string Name, uint VendorId, ulong DedicatedVideoMemory, ulong SharedSystemMemory, bool IsSoftware)
    {
        public override string ToString() =>
            $"{Name} (vendor 0x{VendorId:X4}, {DedicatedVideoMemory / (1024 * 1024)} MB dedicated, {SharedSystemMemory / (1024 * 1024)} MB shared{(IsSoftware ? ", software" : "")})";
    }

    public IReadOnlyList<Adapter> Adapters { get; private init; } = Array.Empty<Adapter>();
    public Adapter? Primary => Adapters.FirstOrDefault(a => !a.IsSoftware) ?? Adapters.FirstOrDefault();

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIAdapter3 = new("645967A4-1392-4310-A798-8053CE3E93FD");

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoMemoryInfo
    {
        public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation;
    }

    public static GpuInfo Query()
    {
        var list = new List<Adapter>();
        try
        {
            if (CreateDXGIFactory1(IID_IDXGIFactory1, out var factory) < 0 || factory == IntPtr.Zero) return new GpuInfo();
            try
            {
                var vtbl = *(IntPtr**)factory;
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)vtbl[12];
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr adapter;
                    if (enumAdapters1(factory, i, &adapter) < 0) break;
                    try
                    {
                        var avtbl = *(IntPtr**)adapter;
                        var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)avtbl[10];
                        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<DXGI_ADAPTER_DESC1>());
                        try
                        {
                            if (getDesc1(adapter, buffer) >= 0)
                            {
                                var d = Marshal.PtrToStructure<DXGI_ADAPTER_DESC1>(buffer);
                                list.Add(new Adapter(d.Description.Trim(), d.VendorId, d.DedicatedVideoMemory, d.SharedSystemMemory, (d.Flags & 2) != 0));
                            }
                        }
                        finally { Marshal.FreeHGlobal(buffer); }
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            finally { Marshal.Release(factory); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException or SEHException)
        {
            Log.Warn("GPU query failed: " + e.Message);
        }
        return new GpuInfo { Adapters = list };
    }

    /// <summary>This process's local video memory usage on the primary adapter (DXGI 1.4,
    /// Windows 10+). Null when unavailable.</summary>
    public static VideoMemoryInfo? QueryProcessVideoMemory()
    {
        try
        {
            if (CreateDXGIFactory1(IID_IDXGIFactory1, out var factory) < 0) return null;
            try
            {
                var vtbl = *(IntPtr**)factory;
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)vtbl[12];
                IntPtr adapter;
                if (enumAdapters1(factory, 0, &adapter) < 0) return null;
                try
                {
                    var iid = IID_IDXGIAdapter3;
                    if (Marshal.QueryInterface(adapter, ref iid, out var adapter3) < 0) return null;
                    try
                    {
                        var v3 = *(IntPtr**)adapter3;
                        var query = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, VideoMemoryInfo*, int>)v3[14];
                        VideoMemoryInfo info;
                        return query(adapter3, 0, 0, &info) >= 0 ? info : null;
                    }
                    finally { Marshal.Release(adapter3); }
                }
                finally { Marshal.Release(adapter); }
            }
            finally { Marshal.Release(factory); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException or SEHException)
        {
            return null;
        }
    }
}

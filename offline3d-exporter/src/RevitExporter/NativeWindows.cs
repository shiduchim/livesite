using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Offline3D.RevitExporter;

/// <summary>The few Win32 calls the exporter windows need.</summary>
internal static class NativeWindows
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool WaitMessage();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    /// <summary>Working area of the monitor showing <paramref name="owner"/>, and the owner's bounds.</summary>
    public static Rectangle WorkingArea(IntPtr owner, out Rectangle ownerBounds)
    {
        ownerBounds = Rectangle.Empty;
        if (owner != IntPtr.Zero && GetWindowRect(owner, out var r) && r.Right > r.Left && r.Bottom > r.Top)
            ownerBounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return owner != IntPtr.Zero ? Screen.FromHandle(owner).WorkingArea : Screen.PrimaryScreen!.WorkingArea;
    }
}

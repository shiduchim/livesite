// EK Triangle Revit loader. Loads EKTriangleHook.dll (same folder) at Revit
// startup and stops it at shutdown. All drawing happens in the native DLL.
// Any failure is logged and leaves Revit's original glyphs in place; this
// class never throws into Revit and never shows UI.
//
// Compiled once per Revit year against that year's API:
//   2024 -> .NET Framework 4.8, 2025/2026 -> .NET 8, 2027 -> .NET 10.
// The native DLL is resolved by explicit handle (LoadLibraryEx + GetProcAddress)
// rather than [DllImport] by name, so it can never bind to the legacy
// EKGlyphHook.dll or another copy that happens to be loaded.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.Revit.UI;

namespace EKTriangle.Loader
{
    public class App : IExternalApplication
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        delegate int InitFn([MarshalAs(UnmanagedType.LPWStr)] string configDir);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void ShutdownFn();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string name);

        const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x8;
        const long MaxLogBytes = 256 * 1024;

        static ShutdownFn _shutdown;   // kept for the process lifetime
        static bool _started;

        static void Note(string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EKTriangle", "Logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "Loader.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxLogBytes)
                {
                    string old = Path.Combine(dir, "Loader.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        static string Describe(int status)
        {
            switch (status)
            {
                case 0: return "active";
                case 1: return "disabled by glyph.ini (Enabled=0)";
                case 2: return "inactive: host is not Revit.exe";
                case 3: return "inactive: another expand/collapse glyph hook (e.g. the EKGlyph test) is already running";
                default: return "inactive: native error " + status + " (see Native.log)";
            }
        }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string version = "";
                try { version = application.ControlledApplication.VersionNumber + " " + application.ControlledApplication.VersionBuild; } catch { }
                Note("EK Triangle loader " + Assembly.GetExecutingAssembly().GetName().Version + " in Revit " + version + " (" +
                     RuntimeInformation.FrameworkDescription + ")");

                if (GetModuleHandleW("EKGlyphHook.dll") != IntPtr.Zero)
                {
                    Note("legacy EKGlyphHook.dll already loaded - EK Triangle stays inactive (no duplicate hook)");
                    return Result.Succeeded;
                }
                string native = Path.Combine(dir, "EKTriangleHook.dll");
                IntPtr module = LoadLibraryExW(native, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                if (module == IntPtr.Zero)
                {
                    Note("could not load " + native + " (Win32 error " + Marshal.GetLastWin32Error() + ")");
                    return Result.Succeeded;
                }
                IntPtr init = GetProcAddress(module, "EKTriangle_Init");
                IntPtr shutdown = GetProcAddress(module, "EKTriangle_Shutdown");
                if (init == IntPtr.Zero || shutdown == IntPtr.Zero)
                {
                    Note("EKTriangleHook.dll has no expected exports; nothing started");
                    return Result.Succeeded;
                }
                // Settings live in the installation folder (one glyph.ini for every Revit version).
                string configDir = File.Exists(Path.Combine(dir, "glyph.ini")) ? dir : Path.GetDirectoryName(dir);
                var initFn = (InitFn)Marshal.GetDelegateForFunctionPointer(init, typeof(InitFn));
                _shutdown = (ShutdownFn)Marshal.GetDelegateForFunctionPointer(shutdown, typeof(ShutdownFn));
                int status = initFn(configDir);
                _started = status == 0;
                Note("native hook " + Describe(status));
            }
            catch (Exception ex)
            {
                _started = false;
                Note("startup error (Revit continues with original glyphs): " + ex);
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (_started && _shutdown != null) _shutdown();
                _started = false;
            }
            catch (Exception ex)
            {
                Note("shutdown error: " + ex);
            }
            return Result.Succeeded;
        }
    }
}

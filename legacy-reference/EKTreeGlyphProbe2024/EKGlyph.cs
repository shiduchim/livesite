// Revit add-in shim: loads EKGlyphHook.dll (from the same folder) into Revit
// at startup. All glyph work happens in the native DLL; this class only
// starts and stops it. Any failure leaves Revit's original glyph in place.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.Revit.UI;

namespace EKGlyph
{
    public class App : IExternalApplication
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);

        [DllImport("EKGlyphHook.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        static extern int EKGlyph_Init(string configDir);

        [DllImport("EKGlyphHook.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void EKGlyph_Shutdown();

        bool _started;
        static void Note(string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EKGlyph");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "Loader.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (LoadLibraryW(Path.Combine(dir, "EKGlyphHook.dll")) == IntPtr.Zero)
                {
                    Note("Could not load EKGlyphHook.dll. Win32 error " + Marshal.GetLastWin32Error());
                    return Result.Succeeded;
                }
                int result = EKGlyph_Init(dir);
                _started = result == 0;
                Note("EKGlyph_Init status: " + result);
            }
            catch (Exception ex)
            {
                _started = false;
                Note("Startup error: " + ex.ToString());
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (_started) EKGlyph_Shutdown();
            }
            catch (Exception ex)
            {
                Note("Shutdown error: " + ex.ToString());
            }
            return Result.Succeeded;
        }
    }
}

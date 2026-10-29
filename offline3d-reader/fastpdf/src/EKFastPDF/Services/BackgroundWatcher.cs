using System.Diagnostics;
using System.IO;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Library;
using Microsoft.Win32;

namespace EKFastPDF.Services;

/// <summary>
/// "When EKPDF is closed: start EKPDF when a PDF lands". The same executable started with
/// --watch: no window and no WPF, only the folder watcher for folders set to Open automatically.
/// When a PDF lands and no EKPDF window is running, it starts EKPDF with that PDF; while a
/// window runs, that window opens new PDFs itself. It stops on its own when the setting is
/// changed. Started at sign-in through the per-user Run key (removed when the setting is off).
/// </summary>
public static class BackgroundWatcher
{
    public const string Argument = "--watch";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "EKPDF watcher";
    private static string MutexName => "Local\\EKPDF-Watcher-" + SingleInstance.Id;

    /// <summary>Process entry for --watch.</summary>
    public static int Run()
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first) return 0; // already watching
        Log.Info("Background watcher started (pid " + Environment.ProcessId + ")");
        var gate = new object();
        var settings = AppSettings.Load();
        using var watcher = new PdfArrivalWatcher(action => { lock (gate) action(); });
        watcher.Arrived += (folder, path) =>
        {
            if (SingleInstance.IsRunning()) return; // the open window handles it
            var f = settings.Folders?.FirstOrDefault(x => PdfLibrary.SamePath(x.Path, folder));
            if (f?.AutoOpen != true) return;
            Log.Info("Starting EKPDF for a new PDF: " + path);
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                start.ArgumentList.Add(path);
                Process.Start(start);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warn("Could not start EKPDF: " + e.Message); }
        };
        string watched = "";
        while (true)
        {
            settings = AppSettings.Load();
            if (settings.ClosedMode != "Watcher") { Log.Info("Background watcher stopped (setting turned off)"); return 0; }
            var auto = (settings.Folders ?? new List<LibraryFolder>()).Where(f => f.AutoOpen).Select(f => f.Path).ToList();
            string key = string.Join("|", auto);
            if (key != watched) { watched = key; watcher.Watch(auto); }
            Thread.Sleep(3000); // settings are re-read every few seconds
        }
    }

    /// <summary>Applies the setting: adds or removes the sign-in entry and starts the watcher now.</summary>
    public static void Apply(string closedMode)
    {
        bool on = closedMode == "Watcher";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(RunValue, "\"" + Environment.ProcessPath + "\" " + Argument);
            else if (key.GetValue(RunValue) != null) key.DeleteValue(RunValue);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Log.Warn("Sign-in entry not changed: " + e.Message); }
        if (!on || IsRunning()) return;
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Argument) { UseShellExecute = false, CreateNoWindow = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warn("Could not start the background watcher: " + e.Message); }
    }

    private static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
        m.Dispose();
        return true;
    }
}

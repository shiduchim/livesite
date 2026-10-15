using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace EKTriangle.Core
{
    /// <summary>A running Revit process. Year is 0 when its location could not be read.</summary>
    public sealed class RunningRevit
    {
        public int ProcessId;
        public int Year;
        public string Path;
    }

    /// <summary>
    /// Every machine location the product touches. Tests point these at a
    /// simulated directory tree; the real tool uses <see cref="Machine"/>.
    /// </summary>
    public sealed class Env
    {
        public string ProgramFiles;
        public string ProgramData;
        public string AppData;
        public string LocalAppData;
        public Func<IReadOnlyList<RunningRevit>> RevitProcesses = () => new RunningRevit[0];
        public Func<int, IEnumerable<string>> ExtraRevitDirs = year => new string[0];
        public Func<bool> IsAdmin = () => true;

        public static Env Machine()
        {
            return new Env
            {
                ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                RevitProcesses = FindRevitProcesses,
                ExtraRevitDirs = WindowsRegistry.RevitInstallLocations,
                IsAdmin = WindowsRegistry.IsAdministrator,
            };
        }

        public static IReadOnlyList<RunningRevit> FindRevitProcesses()
        {
            var list = new List<RunningRevit>();
            Process[] procs;
            try { procs = Process.GetProcessesByName("Revit"); }
            catch (Exception ex) { throw new IOException("Cannot enumerate Revit processes; installation stopped safely.", ex); }
            foreach (var p in procs)
            {
                var r = new RunningRevit { ProcessId = p.Id };
                try
                {
                    r.Path = p.MainModule.FileName;
                    var m = Regex.Match(r.Path ?? "", @"Revit\s+(\d{4})", RegexOptions.IgnoreCase);
                    if (m.Success) r.Year = int.Parse(m.Groups[1].Value);
                }
                catch { /* elevated or exiting: year unknown, treated as affecting every version */ }
                finally { p.Dispose(); }
                list.Add(r);
            }
            return list;
        }

        // ---- locations ---------------------------------------------------

        public string DefaultRevitDir(int year) => Path.Combine(ProgramFiles, "Autodesk", "Revit " + year);

        /// <summary>Folder containing Revit.exe for that year, or null when not installed.</summary>
        public string FindRevitDir(int year)
        {
            var candidates = new List<string> { DefaultRevitDir(year) };
            try { candidates.AddRange(ExtraRevitDirs(year)); } catch { }
            foreach (var dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    if (File.Exists(Path.Combine(dir, "Revit.exe"))) return Path.GetFullPath(dir);
                }
                catch { }
            }
            return null;
        }

        public static string BrowserScript(string revitDir) =>
            Path.Combine(revitDir, "AddIns", "ProjectBrowser", "resource", "static", "js", "main.js");

        public string UserAddinsDir(int year) => Path.Combine(AppData, "Autodesk", "Revit", "Addins", year.ToString());
        public string MachineAddinsDir(int year) => Path.Combine(ProgramData, "Autodesk", "Revit", "Addins", year.ToString());

        public string MachineStore => Path.Combine(ProgramData, "EKTriangle");
        public string BrowserStore(int year) => Path.Combine(MachineStore, "Browser", "Revit" + year);
        public string MachineLogDir => Path.Combine(MachineStore, "Logs");
        public string EmergencyRestoreExe => Path.Combine(MachineStore, "EK Triangle Restore.exe");

        public string UserStore => Path.Combine(LocalAppData, "EKTriangle");
        public string UserLogDir => Path.Combine(UserStore, "Logs");
        public string LegacyBackupRoot => Path.Combine(UserStore, "LegacyBackup");

        public string SymbolSwapStore(int year) => Path.Combine(ProgramData, "EKSymbolSwap", "Revit" + year);

        // ---- Revit running ----------------------------------------------

        /// <summary>Running Revit instances that could be using any of the given years.</summary>
        public List<RunningRevit> RevitBlocking(IEnumerable<int> years)
        {
            var set = new HashSet<int>(years);
            var result = new List<RunningRevit>();
            foreach (var r in RevitProcesses())
                if (r.Year == 0 || set.Contains(r.Year)) result.Add(r);
            return result;
        }

        public static string DescribeRunning(IEnumerable<RunningRevit> list)
        {
            var parts = new List<string>();
            foreach (var r in list) parts.Add(r.Year == 0 ? "Revit (version unknown)" : "Revit " + r.Year);
            return string.Join(", ", parts);
        }
    }
}

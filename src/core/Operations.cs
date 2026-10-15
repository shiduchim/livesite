using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EKTriangle.Core
{
    /// <summary>INI-style result file read by the Inno Setup script (GetIniString).</summary>
    public sealed class Report
    {
        readonly SortedDictionary<string, Dictionary<string, string>> _sections =
            new SortedDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public void Set(string section, string key, string value)
        {
            Dictionary<string, string> s;
            if (!_sections.TryGetValue(section, out s)) _sections[section] = s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            s[key] = (value ?? "").Replace("\r", " ").Replace("\n", " ");
        }

        public string Get(string section, string key)
        {
            Dictionary<string, string> s;
            string v;
            return _sections.TryGetValue(section, out s) && s.TryGetValue(key, out v) ? v : null;
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var sb = new StringBuilder();
            foreach (var sec in _sections)
            {
                sb.Append('[').Append(sec.Key).Append("]\r\n");
                foreach (var kv in sec.Value) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                sb.Append("\r\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            // UTF-16 with BOM: what GetPrivateProfileString (used by Inno) reads reliably for non-ASCII paths.
            File.WriteAllText(path, sb.ToString(), Encoding.Unicode);
        }
    }

    public static class Operations
    {
        public const int ExitOk = 0, ExitProblems = 1, ExitBlocked = 2, ExitUsage = 3, ExitNeedsAdmin = 5;

        public static string LoaderPath(string payloadDir, int year) =>
            Path.Combine(payloadDir, "Revit" + year, Product.LoaderFileName);

        static string Line(ItemResult r) => r.Outcome + ": " + r.Detail;

        /// <summary>Read-only status of every supported year.</summary>
        public static Report Scan(Env env, string payloadDir)
        {
            var rep = new Report();
            var addins = new AddinManager(env, null);
            var browser = new BrowserManager(env, null);
            var installed = new List<int>();
            foreach (int y in Product.SupportedYears)
            {
                string sec = y.ToString();
                var ns = addins.Inspect(y);
                var bs = browser.Inspect(y);
                rep.Set(sec, "Revit", ns.RevitInstalled ? "1" : "0");
                if (!ns.RevitInstalled) continue;
                installed.Add(y);
                rep.Set(sec, "RevitDir", env.FindRevitDir(y));
                bool payload = payloadDir == null || File.Exists(LoaderPath(payloadDir, y));
                string nCode = ns.Conflict != null ? "Conflict" : !payload ? "NoPayload" : ns.Installed ? "Installed" : "Ready";
                string nText = ns.Conflict ?? (!payload ? "no loader for this version in setup"
                             : ns.Installed ? "dialog triangles installed" : "dialog triangles ready to install");
                if (ns.Legacy.Count > 0 && ns.Conflict == null)
                    nText += "; experimental EKGlyph test add-in found and will be deactivated (kept as backup)";
                rep.Set(sec, "Native", nCode);
                rep.Set(sec, "NativeText", nText);
                rep.Set(sec, "Legacy", string.Join(" | ", ns.Legacy.Select(l => l.ToString())));
                rep.Set(sec, "Browser", bs.Code);
                rep.Set(sec, "BrowserText", bs.Text);
                rep.Set(sec, "BrowserSHA256", bs.Sha256 ?? "");
            }
            var running = env.RevitBlocking(Product.SupportedYears);
            rep.Set("Summary", "Years", string.Join(",", installed));
            rep.Set("Summary", "NeedsBrowserRestore", string.Join(",", YearsNeedingBrowserRestore(env)));
            rep.Set("Summary", "Running", Env.DescribeRunning(running));
            rep.Set("Summary", "Result", "OK");
            return rep;
        }

        static bool AnyBlockingRevit(Env env, IEnumerable<int> years, Report rep, Log log)
        {
            var running = env.RevitBlocking(years);
            if (running.Count == 0) return false;
            string msg = "Please close " + Env.DescribeRunning(running) + " and try again. Nothing was changed.";
            rep.Set("Summary", "Result", "Blocked");
            rep.Set("Summary", "Message", msg);
            log?.Warn(msg);
            return true;
        }

        static void Finish(Report rep, bool problems, Log log, string what)
        {
            if (rep.Get("Summary", "Result") == null) rep.Set("Summary", "Result", problems ? "Problems" : "OK");
            rep.Set("Summary", "Log", log?.FilePath ?? "");
            log?.Info(what + " finished: " + rep.Get("Summary", "Result"));
        }

        /// <summary>Per-user part of setup: dialog add-in for each selected year (+ legacy migration).</summary>
        public static int InstallUser(Env env, Log log, IList<int> years, string payloadDir, Report rep, IList<int> removeYears = null)
        {
            removeYears = removeYears ?? new int[0];
            log?.Info("Install (add-in) for " + string.Join(",", years) + " from " + payloadDir +
                      (removeYears.Count > 0 ? "; remove for " + string.Join(",", removeYears) : ""));
            if (AnyBlockingRevit(env, years.Concat(removeYears), rep, log)) return ExitBlocked;
            var addins = new AddinManager(env, log);
            bool problems = false;
            foreach (int y in years)
            {
                var r = addins.Install(y, LoaderPath(payloadDir, y));
                rep.Set(y.ToString(), "NativeResult", r.Outcome.ToString());
                rep.Set(y.ToString(), "NativeResultText", r.Detail);
                log?.Info("Revit " + y + " add-in: " + Line(r));
                problems |= r.IsProblem;
            }
            foreach (int y in removeYears.Where(y => !years.Contains(y)))
            {
                var r = addins.Uninstall(y);
                rep.Set(y.ToString(), "NativeResult", r.Outcome == ItemOutcome.Done ? "Removed" : r.Outcome.ToString());
                rep.Set(y.ToString(), "NativeResultText", r.Detail);
                log?.Info("Revit " + y + " add-in (deselected): " + Line(r));
                problems |= r.IsProblem;
            }
            Finish(rep, problems, log, "Install (add-in)");
            return problems ? ExitProblems : ExitOk;
        }

        /// <summary>Administrator part of setup: Project Browser for each selected year.</summary>
        public static int BrowserApply(Env env, Log log, IList<int> years, Report rep, string selfExe, IList<int> restoreYears = null)
        {
            restoreYears = restoreYears ?? new int[0];
            log?.Info("Install (Project Browser) for " + string.Join(",", years) +
                      (restoreYears.Count > 0 ? "; restore for " + string.Join(",", restoreYears) : ""));
            if (!env.IsAdmin())
            {
                rep.Set("Summary", "Result", "NeedsAdmin");
                return ExitNeedsAdmin;
            }
            if (AnyBlockingRevit(env, years.Concat(restoreYears), rep, log)) return ExitBlocked;
            CopyEmergencyTool(env, log, selfExe);
            var mgr = new BrowserManager(env, log);
            bool problems = false;
            foreach (int y in years)
            {
                ItemResult r;
                try { r = mgr.Apply(y); }
                catch (Exception ex) { r = new ItemResult(ItemOutcome.Failed, ex.Message); }
                rep.Set(y.ToString(), "BrowserResult", r.Outcome.ToString());
                rep.Set(y.ToString(), "BrowserResultText", r.Detail);
                log?.Info("Revit " + y + " Project Browser: " + Line(r));
                problems |= r.IsProblem;
            }
            foreach (int y in restoreYears.Where(y => !years.Contains(y)))
            {
                ItemResult r;
                try { r = mgr.Restore(y); }
                catch (Exception ex) { r = new ItemResult(ItemOutcome.Failed, ex.Message); }
                rep.Set(y.ToString(), "BrowserResult", r.Outcome == ItemOutcome.Done ? "Restored" : r.Outcome.ToString());
                rep.Set(y.ToString(), "BrowserResultText", r.Detail);
                log?.Info("Revit " + y + " Project Browser (deselected): " + Line(r));
                problems |= r.IsProblem;
            }
            Finish(rep, problems, log, "Install (Project Browser)");
            return problems ? ExitProblems : ExitOk;
        }

        /// <summary>Per-user part of uninstall: remove our manifests for every year.</summary>
        public static int UninstallUser(Env env, Log log, Report rep)
        {
            log?.Info("Uninstall (add-in)");
            var years = Product.SupportedYears;
            if (AnyBlockingRevit(env, years, rep, log)) return ExitBlocked;
            var addins = new AddinManager(env, log);
            bool problems = false;
            foreach (int y in years)
            {
                ItemResult r;
                try { r = addins.Uninstall(y); }
                catch (Exception ex) { r = new ItemResult(ItemOutcome.Failed, ex.Message); }
                rep.Set(y.ToString(), "NativeResult", r.Outcome.ToString());
                rep.Set(y.ToString(), "NativeResultText", r.Detail);
                log?.Info("Revit " + y + " add-in: " + Line(r));
                problems |= r.IsProblem;
            }
            Finish(rep, problems, log, "Uninstall (add-in)");
            return problems ? ExitProblems : ExitOk;
        }

        /// <summary>Years whose Project Browser currently holds EK changes (read-only).</summary>
        public static List<int> YearsNeedingBrowserRestore(Env env)
        {
            var mgr = new BrowserManager(env, null);
            var list = new List<int>();
            foreach (int y in Product.SupportedYears)
            {
                var s = mgr.Inspect(y);
                if (s.Analysis != null && (s.Analysis.State == BrowserState.Patched ||
                    s.Analysis.EkCollapsedCount > 0 || s.Analysis.EkExpandedCount > 0) ||
                    s.Record != null && s.Record["Phase"] != "Restored" && s.Record["Phase"] != "NotApplied")
                    list.Add(y);
            }
            return list;
        }

        /// <summary>Administrator part of uninstall / emergency restore.</summary>
        public static int BrowserRestore(Env env, Log log, Report rep)
        {
            log?.Info("Restore (Project Browser)");
            if (!env.IsAdmin())
            {
                rep.Set("Summary", "Result", "NeedsAdmin");
                return ExitNeedsAdmin;
            }
            if (AnyBlockingRevit(env, Product.SupportedYears, rep, log)) return ExitBlocked;
            var mgr = new BrowserManager(env, log);
            bool problems = false;
            var unresolved = new List<string>();
            foreach (int y in Product.SupportedYears)
            {
                ItemResult r;
                try { r = mgr.Restore(y); }
                catch (Exception ex) { r = new ItemResult(ItemOutcome.Failed, ex.Message); }
                rep.Set(y.ToString(), "BrowserResult", r.Outcome.ToString());
                rep.Set(y.ToString(), "BrowserResultText", r.Detail);
                log?.Info("Revit " + y + " Project Browser: " + Line(r));
                if (r.IsProblem) { problems = true; unresolved.Add("Revit " + y + ": " + r.Detail); }
            }
            rep.Set("Summary", "Unresolved", string.Join(" || ", unresolved));
            Finish(rep, problems, log, "Restore (Project Browser)");
            return problems ? ExitProblems : ExitOk;
        }

        static void CopyEmergencyTool(Env env, Log log, string selfExe)
        {
            if (string.IsNullOrEmpty(selfExe) || !File.Exists(selfExe)) return;
            try
            {
                Directory.CreateDirectory(env.MachineStore);
                string dest = env.EmergencyRestoreExe;
                if (string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return;
                string tmp = dest + ".tmp-" + FileUtil.Nonce();
                File.Copy(selfExe, tmp);
                if (File.Exists(dest)) File.Replace(tmp, dest, null); else File.Move(tmp, dest);
                File.WriteAllText(Path.Combine(env.MachineStore, "README.txt"),
                    "EK Triangle keeps original Revit Project Browser files here (Browser\\RevitYYYY).\r\n" +
                    "To restore the original Revit icons at any time, close Revit and run \"EK Triangle Restore.exe\".\r\n" +
                    "The backups are kept after uninstall on purpose; you may delete this folder once Revit shows its original icons.\r\n",
                    Encoding.UTF8);
            }
            catch (Exception ex) { log?.Warn("could not refresh emergency restore copy: " + ex.Message); }
        }
    }
}

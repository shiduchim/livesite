using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using EKTriangle.Core;

namespace EKTriangle.Cmd
{
    // Console orchestration only. All Revit changes use the same Core sources
    // and the same elevated EKTriangle.exe worker as the existing EXE installer.
    static class Program
    {
        const int Partial = 4;
        static readonly string Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (!WindowsRegistry.IsWindows) throw new InvalidOperationException("Windows 10/11 is required.");
                string command = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
                if (command != "install" && command != "uninstall" && command != "status")
                    throw new ArgumentException("Use install, uninstall or status, with optional --yes and --years 2024,2025,...");
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--yes") continue;
                    if (args[i] == "--years" && command == "install" && ++i < args.Length) continue;
                    throw new ArgumentException("Unknown or incomplete option: " + args[i - (i == args.Length ? 1 : 0)]);
                }
                var env = Env.Machine();
                Console.WriteLine("EK Triangle CMD 1.0.2 - " + command);
                if (command == "status") { ShowStatus(env); return 0; }
                // Per-user manifests must never be redirected to an alternate
                // administrator account. Only the browser worker requests UAC.
                var running = Env.FindRevitProcesses();
                if (running.Count > 0) { Console.WriteLine("Close ALL Revit instances first: " + Env.DescribeRunning(running)); return Operations.ExitBlocked; }
                using (var mutex = new Mutex(false, @"Local\EKTriangle.CMD.Installer"))
                {
                    bool owned;
                    try { owned = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("Another EK Triangle CMD operation is running.");
                    try
                    {
                        var inventory = VerifyPackage();
                        return command == "install" ? Install(env, args, inventory) : Uninstall(env, args);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Console.WriteLine("Administrator permission declined. Project Browser was not completed. Run CHECK_STATUS.cmd; backups are preserved.");
                return Operations.ExitProblems;
            }
            catch (Exception ex)
            {
                Console.WriteLine("STOPPED: " + ex.Message);
                Console.WriteLine("If a helper or DLL is blocked or quarantined, stop and ask IT to review EXECUTABLE-INVENTORY.csv and SHA256SUMS.txt. Keep security controls enabled.");
                Console.WriteLine("A partial installation can be checked with CHECK_STATUS.cmd, repaired by INSTALL.cmd, or restored by UNINSTALL.cmd.");
                return Operations.ExitProblems;
            }
        }

        static void ShowStatus(Env env)
        {
            var addins = new AddinManager(env, null);
            var browser = new BrowserManager(env, null);
            foreach (int y in Product.SupportedYears)
            {
                var n = addins.Inspect(y);
                var b = browser.Inspect(y);
                Console.WriteLine("Revit " + y + ": " + (n.RevitInstalled ? env.FindRevitDir(y) : "not detected"));
                Console.WriteLine("  Dialogs: " + (n.Conflict ?? (n.Installed ? "installed" : "not installed")));
                if (n.Legacy.Count > 0) Console.WriteLine("  Legacy EKGlyph: " + string.Join("; ", n.Legacy));
                Console.WriteLine("  Project Browser: " + b.Text);
                if (b.Record != null) Console.WriteLine("  Recovery record: " + b.Record["Phase"] + "; " + env.BrowserStore(y));
            }
            var running = Env.FindRevitProcesses();
            Console.WriteLine("Running Revit: " + (running.Count == 0 ? "none" : Env.DescribeRunning(running)));
            Console.WriteLine("Status is read-only. No files or registry entries were written.");
        }

        static Dictionary<string, string> VerifyPackage()
        {
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(Path.Combine(Root, "SHA256SUMS.txt")))
            {
                var m = Regex.Match(line, @"^([0-9A-Fa-f]{64})  (.+)$");
                if (!m.Success) throw new IOException("Invalid package inventory.");
                string rel = m.Groups[2].Value.Replace('/', Path.DirectorySeparatorChar);
                string path = Path.GetFullPath(Path.Combine(Root, rel));
                if (Path.IsPathRooted(rel) || !FileUtil.IsUnder(path, Root) || hashes.ContainsKey(rel))
                    throw new IOException("Unsafe or duplicate inventory path: " + rel);
                if (!File.Exists(path) || !string.Equals(FileUtil.Sha256File(path), m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Missing or changed package file: " + rel + ". Extract a complete approved ZIP.");
                hashes.Add(rel, m.Groups[1].Value.ToUpperInvariant());
            }
            var required = new List<string> { "EKTriangle.CMD.exe", "EKTriangle.CMD.exe.config", "payload/EKTriangle.exe", "payload/EKTriangle.exe.config", "payload/glyph.ini" };
            foreach (int y in Product.SupportedYears)
                foreach (string f in new[] { Product.LoaderFileName, Product.NativeFileName }) required.Add("payload/Revit" + y + "/" + f);
            foreach (string f in required)
                if (!hashes.ContainsKey(f.Replace('/', Path.DirectorySeparatorChar))) throw new IOException("Incomplete package inventory: " + f);
            foreach (string f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                if ((f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) &&
                    !hashes.ContainsKey(f.Substring(Root.Length + 1))) throw new IOException("Unlisted executable: " + f);
            return hashes;
        }

        static List<int> ParseYears(string value, IList<int> detected)
        {
            var result = new List<int>();
            foreach (string s in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int y;
                if (!int.TryParse(s, out y) || !detected.Contains(y)) throw new ArgumentException("Not a detected supported Revit version: " + s);
                if (!result.Contains(y)) result.Add(y);
            }
            if (result.Count == 0) throw new ArgumentException("Select at least one detected Revit version.");
            return result;
        }

        static string Stage(Env env, Dictionary<string, string> hashes)
        {
            string prefix = "payload" + Path.DirectorySeparatorChar;
            var files = hashes.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
            string record = string.Join("\r\n", files.Select(kv => kv.Value + "  " + kv.Key.Substring(prefix.Length))) + "\r\n";
            string id = FileUtil.Sha256(Encoding.UTF8.GetBytes(record));
            string releases = Path.Combine(env.UserStore, "CMD", "Payloads");
            Directory.CreateDirectory(releases);
            string dest = Path.Combine(releases, id);
            if (!Directory.Exists(dest))
            {
                string pending = dest + ".pending-" + FileUtil.Nonce();
                // An interrupted copy is never referenced by a manifest. Its
                // .pending directory is kept for recovery/IT inspection.
                Directory.CreateDirectory(pending);
                foreach (var kv in files)
                {
                    string target = Path.Combine(pending, kv.Key.Substring(prefix.Length));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(Path.Combine(Root, kv.Key), target);
                    if (FileUtil.Sha256File(target) != kv.Value) throw new IOException("Copied DLL/helper did not verify: " + target);
                }
                File.WriteAllText(Path.Combine(pending, "payload-files.txt"), record, new UTF8Encoding(false));
                Directory.Move(pending, dest);
            }
            foreach (var kv in files)
                if (FileUtil.Sha256File(Path.Combine(dest, kv.Key.Substring(prefix.Length))) != kv.Value)
                    throw new IOException("Installed payload has been modified or blocked: " + dest + ". Ask IT to review before repairing.");
            return dest;
        }

        static int Install(Env env, string[] args, Dictionary<string, string> inventory)
        {
            var scan = Operations.Scan(env, Path.Combine(Root, "payload"));
            var detected = Product.SupportedYears.Where(y => scan.Get(y.ToString(), "Revit") == "1").ToList();
            if (detected.Count == 0) { Console.WriteLine("No supported Revit installation detected (2024-2027)."); return Operations.ExitProblems; }
            ShowStatus(env);
            string choice = null;
            for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--years") choice = args[i + 1];
            if (choice == null && !args.Contains("--yes"))
            {
                Console.Write("Install for " + string.Join(",", detected) + "? Enter = all; or type years to keep; Q = cancel: ");
                choice = Console.ReadLine();
                if (choice == null || choice.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) return Operations.ExitBlocked;
                if (string.IsNullOrWhiteSpace(choice)) choice = null;
            }
            var years = choice == null ? detected : ParseYears(choice, detected);
            var removed = Product.SupportedYears.Except(years).ToList();
            if (Env.FindRevitProcesses().Count > 0) return Operations.ExitBlocked;
            string payload = Stage(env, inventory);
            var log = new Log(Path.Combine(env.UserLogDir, "EKTriangle-CMD.log"));
            var rep = new Report();
            int native = Operations.InstallUser(env, log, years, payload, rep, removed);
            PrintResults(rep, "Native", Product.SupportedYears);
            if (native != 0) { Console.WriteLine("Dialog installation stopped. Backups and staged files are preserved."); return native; }
            bool partial = false;
            foreach (int y in years)
            {
                string outcome = rep.Get(y.ToString(), "NativeResult");
                partial |= outcome != "Done" && outcome != "AlreadyDone";
            }
            var compatible = years.Where(y => scan.Get(y.ToString(), "Browser") == "Ready" || scan.Get(y.ToString(), "Browser") == "Applied").ToList();
            foreach (int y in years.Except(compatible))
            {
                Console.WriteLine("Revit " + y + " Project Browser: SKIPPED - " + scan.Get(y.ToString(), "BrowserText"));
                partial = true;
            }
            var work = compatible.Where(y => !BrowserRecordVerified(env, y)).ToList();
            // Refresh the recovery worker on upgrades, but do not request UAC
            // for a verified, unchanged reinstall.
            if (compatible.Count > 0 && (!File.Exists(env.EmergencyRestoreExe) ||
                FileUtil.Sha256File(env.EmergencyRestoreExe) != FileUtil.Sha256File(Path.Combine(payload, "EKTriangle.exe"))) && work.Count == 0)
                work.Add(compatible[0]);
            foreach (int y in compatible.Except(work))
                Console.WriteLine("Revit " + y + " Project Browser: AlreadyDone - patch and original backup verified; no administrator step required.");
            var restore = RecoveryYears(env).Intersect(removed).ToList();
            if (work.Count > 0 || restore.Count > 0)
            {
                int code = BrowserStep(env, payload, "browser-apply", work, restore);
                if (code != 0) return code;
            }
            foreach (int y in years)
            {
                if (rep.Get(y.ToString(), "NativeResult") == "Skipped") continue;
                if (!new AddinManager(env, null).Inspect(y).Installed) throw new IOException("Revit " + y + " installed manifest/DLL verification failed.");
                foreach (string f in new[] { Product.LoaderFileName, Product.NativeFileName })
                    if (FileUtil.Sha256File(Path.Combine(payload, "Revit" + y, f)) != inventory["payload" + Path.DirectorySeparatorChar + "Revit" + y + Path.DirectorySeparatorChar + f])
                        throw new IOException("Revit " + y + " DLL missing or changed after installation.");
            }
            ShowStatus(env);
            Console.WriteLine(partial ? "Completed with skipped components (exit 4). See each version above." : "Installation verified. Start Revit for the acceptance check.");
            Console.WriteLine("Logs: " + env.UserLogDir + "; browser backups: " + env.MachineStore);
            return partial ? Partial : 0;
        }

        static bool BrowserRecordVerified(Env env, int year)
        {
            var s = new BrowserManager(env, null).Inspect(year);
            return s.Code == "Applied" && s.Record != null && s.Record["Phase"] == "Applied" &&
                s.Record["PatchedSHA256"] == s.Sha256 && s.Record["Backup"] != null &&
                FileUtil.IsUnder(s.Record["Backup"], env.BrowserStore(year)) && File.Exists(s.Record["Backup"]) &&
                FileUtil.Sha256File(s.Record["Backup"]) == s.Record["OriginalSHA256"];
        }

        static List<int> RecoveryYears(Env env)
        {
            var mgr = new BrowserManager(env, null);
            return Product.SupportedYears.Where(y =>
            {
                var s = mgr.Inspect(y);
                return (s.Analysis != null && (s.Analysis.EkCollapsedCount > 0 || s.Analysis.EkExpandedCount > 0)) ||
                       (s.Record != null && s.Record["Phase"] != "Restored" && s.Record["Phase"] != "NotApplied");
            }).ToList();
        }

        static int Uninstall(Env env, string[] args)
        {
            if (!args.Contains("--yes"))
            {
                Console.Write("Remove EK Triangle for this account and restore the Project Browser for all versions? [Y/N]: ");
                if (!string.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase)) return Operations.ExitBlocked;
            }
            var rep = new Report();
            int code = Operations.UninstallUser(env, new Log(Path.Combine(env.UserLogDir, "EKTriangle-CMD.log")), rep);
            PrintResults(rep, "Native", Product.SupportedYears);
            if (code != 0) return code;
            // Restore ALL years if any active record/marker remains, including
            // missing or unreadable files. Do not infer restoration from absence.
            if (RecoveryYears(env).Count > 0)
            {
                code = BrowserStep(env, Path.Combine(Root, "payload"), "browser-restore", null, null);
                if (code != 0) { Console.WriteLine("RESTORATION UNRESOLVED. Recovery data and DLLs retained."); return code; }
            }
            foreach (int y in Product.SupportedYears)
                if (new AddinManager(env, null).Inspect(y).Installed) throw new IOException("Add-in still active for " + y);
            CleanupPayloads(env);
            Console.WriteLine("Uninstall completed. Verified restorations are listed above; backups, legacy backups and emergency restore are retained.");
            return 0;
        }

        static int BrowserStep(Env env, string payload, string command, IList<int> years, IList<int> restore)
        {
            string output = Path.Combine(Path.GetTempPath(), "EKTriangle-CMD-" + Guid.NewGuid().ToString("N") + ".ini");
            string arguments = command + " --out \"" + output + "\"";
            if (years != null) arguments += " --years \"" + string.Join(",", years) + "\"";
            if (restore != null) arguments += " --restore-years \"" + string.Join(",", restore) + "\"";
            try
            {
                Console.WriteLine("Project Browser step" + (env.IsAdmin() ? "." : " requires Windows administrator permission (UAC)."));
                var psi = new ProcessStartInfo(Path.Combine(payload, "EKTriangle.exe"), arguments)
                { UseShellExecute = true, Verb = env.IsAdmin() ? "open" : "runas" };
                using (var p = Process.Start(psi))
                {
                    if (p == null) throw new IOException("Browser worker did not start.");
                    p.WaitForExit();
                    if (!File.Exists(output)) throw new IOException("Browser worker returned no report; a security policy may have blocked it.");
                    var report = ReadReport(output);
                    PrintResults(report, "Browser", Product.SupportedYears);
                    if (report.Get("Summary", "ExitCode") != p.ExitCode.ToString()) throw new IOException("Browser worker report/exit code mismatch.");
                    bool skipped = Product.SupportedYears.Any(y => report.Get(y.ToString(), "BrowserResult") == "Skipped");
                    return p.ExitCode != 0 ? p.ExitCode : skipped ? Partial : 0;
                }
            }
            finally { FileUtil.TryDelete(output); }
        }

        static Report ReadReport(string path)
        {
            var r = new Report(); string section = "";
            foreach (string line in File.ReadAllLines(path, Encoding.Unicode))
            {
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int eq = line.IndexOf('=');
                if (eq > 0) r.Set(section, line.Substring(0, eq), line.Substring(eq + 1));
            }
            return r;
        }

        static void PrintResults(Report report, string component, IEnumerable<int> years)
        {
            foreach (int y in years)
            {
                string outcome = report.Get(y.ToString(), component + "Result");
                if (outcome != null) Console.WriteLine("Revit " + y + " " + component + ": " + outcome + " - " + report.Get(y.ToString(), component + "ResultText"));
            }
            if (report.Get("Summary", "Message") != null) Console.WriteLine(report.Get("Summary", "Message"));
        }

        static void CleanupPayloads(Env env)
        {
            string releases = Path.Combine(env.UserStore, "CMD", "Payloads");
            if (!Directory.Exists(releases)) return;
            foreach (string dir in Directory.GetDirectories(releases))
            {
                string record = Path.Combine(dir, "payload-files.txt");
                if (!Regex.IsMatch(Path.GetFileName(dir), "^[A-F0-9]{64}$") || !File.Exists(record)) continue;
                string text = File.ReadAllText(record, Encoding.UTF8);
                if (FileUtil.Sha256(Encoding.UTF8.GetBytes(text)) != Path.GetFileName(dir)) continue;
                bool preserved = false;
                foreach (string line in text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length < 67) { preserved = true; continue; }
                    string path = Path.GetFullPath(Path.Combine(dir, line.Substring(66)));
                    if (!FileUtil.IsUnder(path, dir)) { preserved = true; continue; }
                    if (!File.Exists(path)) continue;
                    if (FileUtil.Sha256File(path) == line.Substring(0, 64)) File.Delete(path);
                    else { preserved = true; Console.WriteLine("Preserved modified file for IT review: " + path); }
                }
                if (!preserved) File.Delete(record);
                foreach (string sub in Directory.GetDirectories(dir).OrderByDescending(p => p.Length))
                    if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
        }
    }
}

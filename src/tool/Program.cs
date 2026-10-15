// EKTriangle.exe - the worker behind EK Triangle Setup, the uninstaller and
// the emergency restore. Users never need its command line: Setup calls it,
// and double-clicking it (or the Start-menu "EK Triangle Restore" entry)
// opens the guided restore.
//
//   scan            --out F [--payload D]      read-only status of each Revit year
//   running         --out F                    exit 2 when Revit is running
//   install         --years Y [--remove-years Y] --payload D --out F   per-user dialog add-in (+ legacy migration)
//   browser-apply   --years Y [--restore-years Y] --out F   Project Browser (administrator)
//   uninstall       --out F                    remove the add-in for this user
//   browser-restore --out F                    restore Project Browser (administrator)
//   restore                                    guided emergency restore (default)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using EKTriangle.Core;

namespace EKTriangle.Tool
{
    static class Program
    {
        const string Title = "EK Triangle";

        [STAThread]
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "restore";
            var opts = ParseOptions(args);
            Env env = Env.Machine();
            string outFile;
            opts.TryGetValue("out", out outFile);
            var rep = new Report();
            try
            {
                int code;
                switch (cmd)
                {
                    case "scan":
                        {
                            string payload;
                            opts.TryGetValue("payload", out payload);
                            rep = Operations.Scan(env, payload);
                            code = Operations.ExitOk;
                            break;
                        }
                    case "running":
                        {
                            var running = env.RevitBlocking(Product.SupportedYears);
                            rep.Set("Summary", "Running", Env.DescribeRunning(running));
                            code = running.Count > 0 ? Operations.ExitBlocked : Operations.ExitOk;
                            break;
                        }
                    case "install":
                        code = Operations.InstallUser(env, UserLog(env), Years(opts), Required(opts, "payload"), rep, Years(opts, "remove-years"));
                        break;
                    case "browser-apply":
                        code = Operations.BrowserApply(env, AdminLog(env), Years(opts), rep, Assembly.GetExecutingAssembly().Location, Years(opts, "restore-years"));
                        break;
                    case "uninstall":
                        code = Operations.UninstallUser(env, UserLog(env), rep);
                        break;
                    case "browser-restore":
                        code = Operations.BrowserRestore(env, AdminLog(env), rep);
                        break;
                    case "restore":
                    case "/restore":
                        return GuidedRestore(env);
                    default:
                        rep.Set("Summary", "Result", "Error");
                        rep.Set("Summary", "Message", "unknown command " + cmd);
                        code = Operations.ExitUsage;
                        break;
                }
                rep.Set("Summary", "ExitCode", code.ToString());
                rep.Save(outFile);
                return code;
            }
            catch (Exception ex)
            {
                rep.Set("Summary", "Result", "Error");
                rep.Set("Summary", "Message", ex.Message);
                rep.Set("Summary", "ExitCode", Operations.ExitProblems.ToString());
                try { UserLog(env).Error(cmd + ": " + ex); } catch { }
                try { rep.Save(outFile); } catch { }
                return Operations.ExitProblems;
            }
        }

        static Dictionary<string, string> ParseOptions(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                string key = args[i].Substring(2);
                string val = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "1";
                d[key] = val;
            }
            return d;
        }

        static string Required(Dictionary<string, string> opts, string key)
        {
            string v;
            if (!opts.TryGetValue(key, out v) || string.IsNullOrWhiteSpace(v)) throw new ArgumentException("--" + key + " is required");
            return v;
        }

        static IList<int> Years(Dictionary<string, string> opts, string key = "years")
        {
            var list = new List<int>();
            string value;
            if (!opts.TryGetValue(key, out value) || value == "1") value = "";
            foreach (var part in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int y;
                if (int.TryParse(part, out y) && Product.IsSupportedYear(y) && !list.Contains(y)) list.Add(y);
            }
            return list;
        }

        static Log UserLog(Env env) => new Log(Path.Combine(env.UserLogDir, "EKTriangle.log"));
        static Log AdminLog(Env env) => new Log(Path.Combine(env.MachineLogDir, "EKTriangle-admin.log"));

        // ------------------------------------------------------ guided restore --

        static int GuidedRestore(Env env)
        {
            Application.EnableVisualStyles();
            var ask = MessageBox.Show(
                "Restore Revit's original expand/collapse symbols?\n\n" +
                "This turns off the EK Triangle add-in for your Windows account and puts back the original " +
                "Project Browser files from the verified backups. Windows may ask for administrator permission.\n\n" +
                "Close every Revit window before you continue.",
                Title + " Restore", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ask != DialogResult.OK) return Operations.ExitOk;

            while (true)
            {
                var running = env.RevitBlocking(Product.SupportedYears);
                if (running.Count == 0) break;
                if (MessageBox.Show("Please close " + Env.DescribeRunning(running) + " (save your work first), then click Retry.",
                        Title + " Restore", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry)
                    return Operations.ExitBlocked;
            }

            var log = UserLog(env);
            var rep = new Report();
            Operations.UninstallUser(env, log, rep);
            var lines = new List<string>();
            bool problems = false;
            foreach (int y in Product.SupportedYears)
            {
                string r = rep.Get(y.ToString(), "NativeResult");
                if (r == "Done") lines.Add("Revit " + y + ": dialog triangles removed.");
                else if (r == "Failed" || r == "Blocked") { problems = true; lines.Add("Revit " + y + ": " + rep.Get(y.ToString(), "NativeResultText")); }
            }

            var needed = Operations.YearsNeedingBrowserRestore(env);
            if (needed.Count > 0)
            {
                Report br;
                string error = RunBrowserRestoreElevated(env, out br);
                if (error != null)
                {
                    problems = true;
                    lines.Add("Project Browser NOT restored: " + error);
                }
                else
                {
                    foreach (int y in needed)
                    {
                        string res = br.Get(y.ToString(), "BrowserResult");
                        string txt = br.Get(y.ToString(), "BrowserResultText");
                        if (res == "Done") lines.Add("Revit " + y + ": " + txt + ".");
                        else { problems = true; lines.Add("Revit " + y + " Project Browser NOT restored: " + txt); }
                    }
                }
            }
            if (lines.Count == 0) lines.Add("Nothing to restore: Revit already uses its original symbols.");
            lines.Add("");
            lines.Add(problems
                ? "Some items could not be restored safely (see above). Nothing unknown was overwritten. Log: " + log.FilePath
                : "Start Revit to see the original symbols. To remove EK Triangle completely use Windows Settings > Apps > Installed apps.");
            MessageBox.Show(string.Join("\n", lines), Title + " Restore", MessageBoxButtons.OK,
                problems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return problems ? Operations.ExitProblems : Operations.ExitOk;
        }

        /// <summary>Runs browser-restore directly when elevated, otherwise via a UAC prompt. Returns an error text or null.</summary>
        static string RunBrowserRestoreElevated(Env env, out Report result)
        {
            result = new Report();
            if (env.IsAdmin())
            {
                Operations.BrowserRestore(env, AdminLog(env), result);
                return null;
            }
            string outFile = Path.Combine(Path.GetTempPath(), "EKTriangle-restore-" + FileUtil.Nonce() + ".ini");
            try
            {
                var psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "browser-restore --out \"" + outFile + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
                if (!File.Exists(outFile)) return "the administrator step did not report a result";
                result = ReadReport(outFile);
                return null;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "administrator permission was declined";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                FileUtil.TryDelete(outFile);
            }
        }

        static Report ReadReport(string path)
        {
            var rep = new Report();
            string section = "";
            foreach (var raw in File.ReadAllLines(path, Encoding.Unicode))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int eq = line.IndexOf('=');
                if (eq > 0) rep.Set(section, line.Substring(0, eq), line.Substring(eq + 1));
            }
            return rep;
        }
    }
}

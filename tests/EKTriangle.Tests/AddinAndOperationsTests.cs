using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using EKTriangle.Core;
using Xunit;

namespace EKTriangle.Tests
{
    [Collection("filesystem")]
    public class AddinManagerTests
    {
        static AddinManager Mgr(Machine m) => new AddinManager(m.Env, new Log(null));

        [Fact]
        public void Install_writes_a_valid_manifest_with_absolute_path_and_new_addin_id()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            string payload = m.Payload();
            var r = Mgr(m).Install(2024, Operations.LoaderPath(payload, 2024));
            Assert.Equal(ItemOutcome.Done, r.Outcome);
            var doc = new XmlDocument();
            doc.Load(Mgr(m).ManifestPath(2024));
            Assert.Equal(Product.AddInId, doc.SelectSingleNode("/RevitAddIns/AddIn/AddInId").InnerText);
            Assert.NotEqual(Product.LegacyAddInId, Product.AddInId);
            Assert.Equal("EKTriangle.Loader.App", doc.SelectSingleNode("/RevitAddIns/AddIn/FullClassName").InnerText);
            string asm = doc.SelectSingleNode("/RevitAddIns/AddIn/Assembly").InnerText;
            Assert.True(Path.IsPathRooted(asm));
            Assert.True(File.Exists(asm));
            Assert.True(Mgr(m).Inspect(2024).Installed);
            Assert.Equal(ItemOutcome.AlreadyDone, Mgr(m).Install(2024, Operations.LoaderPath(payload, 2024)).Outcome);
        }

        [Fact]
        public void Legacy_probe_is_moved_aside_not_deleted_and_never_runs_next_to_the_new_hook()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            var (manifest, folder) = m.InstallLegacyProbe();
            Assert.Single(Mgr(m).Inspect(2024).Legacy);

            var r = Mgr(m).Install(2024, Operations.LoaderPath(m.Payload(), 2024));
            Assert.Equal(ItemOutcome.Done, r.Outcome);
            Assert.Contains("experimental EKGlyph test add-in deactivated", r.Detail);
            Assert.False(File.Exists(manifest));
            Assert.False(Directory.Exists(folder));
            var backup = Directory.GetDirectories(m.Env.LegacyBackupRoot).Single();
            Assert.True(File.Exists(Path.Combine(backup, "moved-items.txt")));
            Assert.Contains(manifest, File.ReadAllText(Path.Combine(backup, "moved-items.txt")));
            Assert.Single(Directory.GetFiles(backup, "manifest0-EKTreeGlyphProbe2024.addin"));
            Assert.True(File.Exists(Path.Combine(backup, "folder0-EKTreeGlyphProbe2024", "EKGlyphHook.dll")));
            // Exactly one hook manifest remains active for 2024.
            var active = Directory.GetFiles(m.Env.UserAddinsDir(2024), "*.addin")
                .Where(f => File.ReadAllText(f).Contains("EKGlyph.App") || File.ReadAllText(f).Contains(Product.AddInId)).ToList();
            Assert.Single(active);
        }

        [Fact]
        public void Original_ekglyph_addin_with_extra_files_moves_manifest_only()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            string dir = m.Env.UserAddinsDir(2024);
            Directory.CreateDirectory(Path.Combine(dir, "EKGlyph"));
            File.WriteAllText(Path.Combine(dir, "EKGlyph", "EKGlyph.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "EKGlyph", "my-notes.txt"), "user file");
            File.WriteAllText(Path.Combine(dir, "EKGlyph.addin"), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "legacy-EKGlyph.addin")));
            var r = Mgr(m).Install(2024, Operations.LoaderPath(m.Payload(), 2024));
            Assert.Equal(ItemOutcome.Done, r.Outcome);
            Assert.False(File.Exists(Path.Combine(dir, "EKGlyph.addin")));
            Assert.True(File.Exists(Path.Combine(dir, "EKGlyph", "my-notes.txt")));
        }

        [Fact]
        public void Machine_wide_legacy_addin_is_a_reported_conflict_and_nothing_changes()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            string mdir = m.Env.MachineAddinsDir(2024);
            Directory.CreateDirectory(mdir);
            File.WriteAllText(Path.Combine(mdir, "EKGlyph.addin"), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "legacy-EKGlyph.addin")));
            var before = m.Snapshot(m.Root);
            var r = Mgr(m).Install(2024, Operations.LoaderPath(m.Payload(), 2024));
            Assert.Equal(ItemOutcome.Skipped, r.Outcome);
            Assert.Contains("for all users", r.Detail);
            Assert.False(File.Exists(Mgr(m).ManifestPath(2024)));
            Assert.True(File.Exists(Path.Combine(mdir, "EKGlyph.addin")));
        }

        [Fact]
        public void Foreign_file_with_our_manifest_name_is_never_overwritten()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            string path = Mgr(m).ManifestPath(2024);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "<RevitAddIns><AddIn Type=\"Application\"><AddInId>99999999-2222-3333-4444-555555555555</AddInId></AddIn></RevitAddIns>");
            var r = Mgr(m).Install(2024, Operations.LoaderPath(m.Payload(), 2024));
            Assert.Equal(ItemOutcome.Skipped, r.Outcome);
            Assert.Contains("99999999", File.ReadAllText(path));
            Assert.Equal(ItemOutcome.AlreadyDone, Mgr(m).Uninstall(2024).Outcome);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Failure_after_moving_legacy_puts_legacy_back()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            var (manifest, folder) = m.InstallLegacyProbe();
            // A directory squatting on the manifest name makes the final write fail.
            Directory.CreateDirectory(Mgr(m).ManifestPath(2024));
            var r = Mgr(m).Install(2024, Operations.LoaderPath(m.Payload(), 2024));
            Assert.Equal(ItemOutcome.Failed, r.Outcome);
            Assert.Contains("left as they were", r.Detail);
            Assert.True(File.Exists(manifest));
            Assert.True(File.Exists(Path.Combine(folder, "EKGlyphHook.dll")));
        }

        [Fact]
        public void Missing_payload_fails_before_anything_moves()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            var (manifest, _) = m.InstallLegacyProbe();
            var r = Mgr(m).Install(2024, Path.Combine(m.Root, "nowhere", "EKTriangle.dll"));
            Assert.Equal(ItemOutcome.Failed, r.Outcome);
            Assert.True(File.Exists(manifest));
        }

        [Fact]
        public void Uninstall_removes_only_manifests_with_our_id()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            string dir = m.Env.UserAddinsDir(2024);
            var before = m.Snapshot(dir);
            Mgr(m).Install(2024, Operations.LoaderPath(m.Payload(), 2024));
            File.Copy(Mgr(m).ManifestPath(2024), Path.Combine(dir, "EKTriangle - Copy.addin"));
            Assert.Equal(ItemOutcome.Done, Mgr(m).Uninstall(2024).Outcome);
            Assert.Equal(before, m.Snapshot(dir));
            Assert.Equal(ItemOutcome.AlreadyDone, Mgr(m).Uninstall(2024).Outcome);
        }

        [Fact]
        public void Running_revit_blocks_addin_changes()
        {
            using var m = new Machine();
            m.InstallRevit(2025, null);
            m.Running.Add(new RunningRevit { Year = 2025 });
            Assert.Equal(ItemOutcome.Blocked, Mgr(m).Install(2025, Operations.LoaderPath(m.Payload(), 2025)).Outcome);
            Assert.False(File.Exists(Mgr(m).ManifestPath(2025)));
        }
    }

    [Collection("filesystem")]
    public class OperationsTests
    {
        static readonly int[] All = { 2024, 2025, 2026, 2027 };

        Machine FourYears(out byte[] js2024, out byte[] js2025, out byte[] js2027)
        {
            var m = new Machine();
            js2024 = Js.Bytes(Js.Original(1));
            js2025 = Js.Bytes(Js.Original(2, minus: ""));                 // unsupported layout
            js2027 = Js.Bytes(Js.Original(4), bom: true);
            m.InstallRevit(2024, js2024);
            m.InstallRevit(2025, js2025);
            m.InstallRevit(2027, js2027);                                 // 2026 not installed
            m.InstallLegacyProbe(2024);
            return m;
        }

        [Fact]
        public void Scan_reports_each_installed_version_and_its_compatibility()
        {
            using var m = FourYears(out _, out _, out _);
            string ini = Path.Combine(m.Root, "scan.ini");
            Operations.Scan(m.Env, m.Payload()).Save(ini);
            string text = File.ReadAllText(ini, Encoding.Unicode);
            Assert.Contains("Years=2024,2025,2027", text);
            var rep = Operations.Scan(m.Env, m.Payload());
            Assert.Equal("Ready", rep.Get("2024", "Browser"));
            Assert.Equal("Unsupported", rep.Get("2025", "Browser"));
            Assert.Equal("0", rep.Get("2026", "Revit"));
            Assert.Equal("Ready", rep.Get("2027", "Browser"));
            Assert.Contains("EKGlyph", rep.Get("2024", "NativeText"));
            Assert.Equal("Ready", rep.Get("2024", "Native"));
        }

        [Fact]
        public void Full_install_uninstall_cycles_leave_revit_and_other_addins_exactly_as_before()
        {
            using var m = FourYears(out var js2024, out var js2025, out var js2027);
            string payload = m.Payload();
            int[] years = { 2024, 2025, 2027 };
            // The legacy probe is intentionally migrated (moved to our backup), so exclude it from the comparison.
            var programBefore = m.Snapshot(m.Env.ProgramFiles);
            var addinsBefore = m.Snapshot(Path.Combine(m.Env.AppData, "Autodesk"), "EKTreeGlyphProbe2024");

            for (int cycle = 0; cycle < 3; cycle++)
            {
                var rep = new Report();
                Assert.Equal(Operations.ExitOk, Operations.InstallUser(m.Env, new Log(null), years, payload, rep));
                Assert.Equal(Operations.ExitOk, Operations.BrowserApply(m.Env, new Log(null), years, rep, null));
                Assert.Equal("Done", rep.Get("2024", "NativeResult"));
                Assert.Equal("Done", rep.Get("2024", "BrowserResult"));
                Assert.Equal("Skipped", rep.Get("2025", "BrowserResult"));
                Assert.Equal("Done", rep.Get("2027", "BrowserResult"));
                Assert.Equal(new[] { 2024, 2027 }, Operations.YearsNeedingBrowserRestore(m.Env));

                var un = new Report();
                Assert.Equal(Operations.ExitOk, Operations.UninstallUser(m.Env, new Log(null), un));
                Assert.Equal(Operations.ExitOk, Operations.BrowserRestore(m.Env, new Log(null), un));
                Assert.Equal("", un.Get("Summary", "Unresolved"));
                Assert.Empty(Operations.YearsNeedingBrowserRestore(m.Env));
                Assert.Equal(programBefore, m.Snapshot(m.Env.ProgramFiles));
                Assert.Equal(addinsBefore, m.Snapshot(Path.Combine(m.Env.AppData, "Autodesk"), "EKTreeGlyphProbe2024"));
            }
            Assert.False(File.Exists(Path.Combine(m.Env.UserAddinsDir(2024), "EKTreeGlyphProbe2024.addin")));
        }

        [Fact]
        public void Browser_operations_require_administrator()
        {
            using var m = FourYears(out var js2024, out _, out _);
            m.Admin = false;
            var rep = new Report();
            Assert.Equal(Operations.ExitNeedsAdmin, Operations.BrowserApply(m.Env, null, All, rep, null));
            Assert.Equal(Operations.ExitNeedsAdmin, Operations.BrowserRestore(m.Env, null, rep));
            Assert.Equal(FileUtil.Sha256(js2024), FileUtil.Sha256File(m.MainJs(2024)));
        }

        [Fact]
        public void Running_revit_blocks_whole_operations_with_a_clear_message()
        {
            using var m = FourYears(out var js2024, out _, out _);
            m.Running.Add(new RunningRevit { Year = 2027 });
            var rep = new Report();
            Assert.Equal(Operations.ExitBlocked, Operations.InstallUser(m.Env, null, new[] { 2024, 2027 }, m.Payload(), rep));
            Assert.Equal("Blocked", rep.Get("Summary", "Result"));
            Assert.Contains("Revit 2027", rep.Get("Summary", "Message"));
            Assert.Equal(Operations.ExitBlocked, Operations.UninstallUser(m.Env, null, new Report()));
            Assert.False(File.Exists(Path.Combine(m.Env.UserAddinsDir(2024), Product.ManifestFileName)));
        }

        [Fact]
        public void Partial_failure_is_reported_as_unresolved_on_restore()
        {
            using var m = FourYears(out _, out _, out _);
            Operations.BrowserApply(m.Env, null, new[] { 2024, 2027 }, new Report(), null);
            File.SetAttributes(m.MainJs(2027), FileAttributes.ReadOnly);
            var rep = new Report();
            Assert.Equal(Operations.ExitProblems, Operations.BrowserRestore(m.Env, null, rep));
            Assert.Contains("Revit 2027", rep.Get("Summary", "Unresolved"));
            Assert.Equal("Done", rep.Get("2024", "BrowserResult"));
            Assert.Equal(new[] { 2027 }, Operations.YearsNeedingBrowserRestore(m.Env));
        }

        [Fact]
        public void Emergency_restore_copy_is_placed_next_to_the_backups()
        {
            using var m = FourYears(out _, out _, out _);
            string fakeExe = Path.Combine(m.Root, "EKTriangle.exe");
            File.WriteAllText(fakeExe, "tool");
            Operations.BrowserApply(m.Env, null, new[] { 2024 }, new Report(), fakeExe);
            Assert.Equal("tool", File.ReadAllText(m.Env.EmergencyRestoreExe));
            Assert.True(File.Exists(Path.Combine(m.Env.MachineStore, "README.txt")));
        }
    }
}

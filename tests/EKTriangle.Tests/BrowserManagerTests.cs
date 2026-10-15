using System;
using System.IO;
using System.Linq;
using EKTriangle.Core;
using Xunit;

namespace EKTriangle.Tests
{
    [Collection("filesystem")]
    public class BrowserManagerTests
    {
        static BrowserManager Mgr(Machine m) => new BrowserManager(m.Env, new Log(null));

        [Fact]
        public void Install_then_restore_returns_the_exact_original_and_keeps_a_verified_backup()
        {
            using var m = new Machine();
            byte[] original = Js.Bytes(Js.Original());
            m.InstallRevit(2024, original);
            string js = m.MainJs(2024);
            string origHash = FileUtil.Sha256(original);

            var r = Mgr(m).Apply(2024);
            Assert.Equal(ItemOutcome.Done, r.Outcome);
            Assert.Equal(BrowserState.Patched, BrowserPatch.Analyze(File.ReadAllText(js)).State);

            var rec = KeyValueFile.Load(Path.Combine(m.Env.BrowserStore(2024), "state.txt"));
            Assert.Equal(origHash, rec["OriginalSHA256"]);
            Assert.Equal(FileUtil.Sha256File(js), rec["PatchedSHA256"]);
            Assert.Equal("Applied", rec["Phase"]);
            Assert.Equal(Product.BrowserPatchId, rec["PatchId"]);
            Assert.Equal(origHash, FileUtil.Sha256File(rec["Backup"]));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(js), "main.js.EKTriangle.*"));

            var u = Mgr(m).Restore(2024);
            Assert.Equal(ItemOutcome.Done, u.Outcome);
            Assert.Equal(origHash, FileUtil.Sha256File(js));
            Assert.True(File.Exists(rec["Backup"]), "backup must be preserved after restore");
            Assert.Equal("Restored", KeyValueFile.Load(Path.Combine(m.Env.BrowserStore(2024), "state.txt"))["Phase"]);
        }

        [Fact]
        public void Reinstall_and_repeated_cycles_are_idempotent()
        {
            using var m = new Machine();
            byte[] original = Js.Bytes(Js.Original(3), bom: true);
            m.InstallRevit(2025, original);
            string js = m.MainJs(2025);
            string patchedHash = null;
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(ItemOutcome.Done, Mgr(m).Apply(2025).Outcome);
                patchedHash ??= FileUtil.Sha256File(js);
                Assert.Equal(patchedHash, FileUtil.Sha256File(js));
                Assert.Equal(ItemOutcome.AlreadyDone, Mgr(m).Apply(2025).Outcome);
                Assert.Equal(patchedHash, FileUtil.Sha256File(js));
                Assert.Equal(ItemOutcome.Done, Mgr(m).Restore(2025).Outcome);
                Assert.Equal(FileUtil.Sha256(original), FileUtil.Sha256File(js));
                Assert.Equal(ItemOutcome.AlreadyDone, Mgr(m).Restore(2025).Outcome);
            }
            Assert.Single(Directory.GetFiles(m.Env.BrowserStore(2025), "*.original"));
        }

        [Fact]
        public void Unrecognised_bundle_is_skipped_and_untouched()
        {
            using var m = new Machine();
            byte[] js = Js.Bytes(Js.Original(minus: ""));
            m.InstallRevit(2026, js);
            var r = Mgr(m).Apply(2026);
            Assert.Equal(ItemOutcome.Skipped, r.Outcome);
            Assert.Contains("expanded-node icon", r.Detail);
            Assert.Equal(FileUtil.Sha256(js), FileUtil.Sha256File(m.MainJs(2026)));
            Assert.False(Directory.Exists(m.Env.BrowserStore(2026)));
        }

        [Fact]
        public void Missing_browser_file_is_reported_not_found()
        {
            using var m = new Machine();
            m.InstallRevit(2027, null);
            Assert.Equal("NotFound", Mgr(m).Inspect(2027).Code);
            Assert.Equal(ItemOutcome.Skipped, Mgr(m).Apply(2027).Outcome);
            Assert.Equal(ItemOutcome.NotApplicable, Mgr(m).Restore(2027).Outcome);
        }

        [Theory]
        [InlineData(2024, true)]
        [InlineData(0, true)]      // running Revit whose version could not be read
        [InlineData(2026, false)]  // a different version does not block
        public void Running_revit_blocks_changes(int runningYear, bool blocks)
        {
            using var m = new Machine();
            byte[] js = Js.Bytes(Js.Original());
            m.InstallRevit(2024, js);
            m.Running.Add(new RunningRevit { ProcessId = 42, Year = runningYear });
            var r = Mgr(m).Apply(2024);
            if (blocks)
            {
                Assert.Equal(ItemOutcome.Blocked, r.Outcome);
                Assert.Equal(FileUtil.Sha256(js), FileUtil.Sha256File(m.MainJs(2024)));
            }
            else Assert.Equal(ItemOutcome.Done, r.Outcome);
        }

        [Fact]
        public void Read_only_file_is_not_modified()
        {
            using var m = new Machine();
            byte[] js = Js.Bytes(Js.Original());
            m.InstallRevit(2024, js);
            File.SetAttributes(m.MainJs(2024), FileAttributes.ReadOnly);
            var r = Mgr(m).Apply(2024);
            Assert.Equal(ItemOutcome.Failed, r.Outcome);
            Assert.Contains("read-only", r.Detail);
            Assert.Equal(FileUtil.Sha256(js), FileUtil.Sha256File(m.MainJs(2024)));
        }

        [Fact]
        public void Write_failure_before_replace_leaves_original_and_records_not_applied()
        {
            using var m = new Machine();
            byte[] js = Js.Bytes(Js.Original());
            m.InstallRevit(2024, js);
            TestHooks.BeforeReplace = _ => throw new IOException("simulated access denied");
            var r = Mgr(m).Apply(2024);
            TestHooks.BeforeReplace = null;
            Assert.Equal(ItemOutcome.Failed, r.Outcome);
            Assert.Contains("left unchanged", r.Detail);
            Assert.Equal(FileUtil.Sha256(js), FileUtil.Sha256File(m.MainJs(2024)));
            Assert.Equal("NotApplied", KeyValueFile.Load(Path.Combine(m.Env.BrowserStore(2024), "state.txt"))["Phase"]);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(m.MainJs(2024)), "main.js.EKTriangle.*"));
            // A later attempt succeeds normally.
            Assert.Equal(ItemOutcome.Done, Mgr(m).Apply(2024).Outcome);
        }

        [Fact]
        public void Corruption_after_replace_is_rolled_back_to_the_original()
        {
            using var m = new Machine();
            byte[] js = Js.Bytes(Js.Original());
            m.InstallRevit(2024, js);
            TestHooks.AfterReplace = p => File.AppendAllText(p, "garbage");
            var r = Mgr(m).Apply(2024);
            TestHooks.AfterReplace = null;
            Assert.Equal(ItemOutcome.Failed, r.Outcome);
            Assert.Equal(FileUtil.Sha256(js), FileUtil.Sha256File(m.MainJs(2024)));
        }

        [Fact]
        public void Interrupted_operation_leftovers_are_cleaned_and_install_completes()
        {
            using var m = new Machine();
            byte[] js = Js.Bytes(Js.Original());
            m.InstallRevit(2024, js);
            string dir = Path.GetDirectoryName(m.MainJs(2024));
            File.WriteAllText(Path.Combine(dir, "main.js.EKTriangle.abc.tmp"), "partial");
            File.WriteAllBytes(Path.Combine(dir, "main.js.EKTriangle.abc.rollback"), js);
            File.WriteAllText(Path.Combine(dir, "main.js.EKTriangle.def.rollback"), "unknown content - keep");
            Directory.CreateDirectory(m.Env.BrowserStore(2024));
            File.WriteAllText(Path.Combine(m.Env.BrowserStore(2024), "state.txt"), "Phase=Applying\r\n");

            Assert.Equal(ItemOutcome.Done, Mgr(m).Apply(2024).Outcome);
            Assert.False(File.Exists(Path.Combine(dir, "main.js.EKTriangle.abc.tmp")));
            Assert.False(File.Exists(Path.Combine(dir, "main.js.EKTriangle.abc.rollback")));
            Assert.True(File.Exists(Path.Combine(dir, "main.js.EKTriangle.def.rollback")));
            Assert.True(File.Exists(Path.Combine(dir, "vendor.js")));
        }

        [Fact]
        public void Restore_never_overwrites_an_autodesk_update()
        {
            using var m = new Machine();
            m.InstallRevit(2024, Js.Bytes(Js.Original(1)));
            Assert.Equal(ItemOutcome.Done, Mgr(m).Apply(2024).Outcome);
            // Autodesk update replaces main.js with a newer original.
            byte[] updated = Js.Bytes(Js.Original(99));
            File.WriteAllBytes(m.MainJs(2024), updated);
            var r = Mgr(m).Restore(2024);
            Assert.Equal(ItemOutcome.AlreadyDone, r.Outcome);
            Assert.Contains("Autodesk update", r.Detail);
            Assert.Equal(FileUtil.Sha256(updated), FileUtil.Sha256File(m.MainJs(2024)));
            Assert.Single(Directory.GetFiles(m.Env.BrowserStore(2024), "*.original"));
        }

        [Fact]
        public void Restore_of_a_patched_but_otherwise_changed_file_removes_only_ek_text()
        {
            using var m = new Machine();
            m.InstallRevit(2024, Js.Bytes(Js.Original(1)));
            Mgr(m).Apply(2024);
            string text = File.ReadAllText(m.MainJs(2024)) + "\n/* later change by someone else */";
            File.WriteAllText(m.MainJs(2024), text);
            var r = Mgr(m).Restore(2024);
            Assert.Equal(ItemOutcome.Done, r.Outcome);
            Assert.Contains("differs from the backup", r.Detail);
            string after = File.ReadAllText(m.MainJs(2024));
            Assert.EndsWith("/* later change by someone else */", after);
            Assert.Equal(BrowserState.Original, BrowserPatch.Analyze(after).State);
        }

        [Theory]
        [InlineData("missing-backup")]
        [InlineData("corrupt-backup")]
        [InlineData("missing-record")]
        public void Restore_works_without_a_usable_backup_and_never_deletes_backups(string damage)
        {
            using var m = new Machine();
            byte[] original = Js.Bytes(Js.Original());
            m.InstallRevit(2024, original);
            Mgr(m).Apply(2024);
            string store = m.Env.BrowserStore(2024);
            string backup = Directory.GetFiles(store, "*.original").Single();
            if (damage == "missing-backup") File.Delete(backup);
            if (damage == "corrupt-backup") File.WriteAllText(backup, "corrupt");
            if (damage == "missing-record") File.Delete(Path.Combine(store, "state.txt"));

            var r = Mgr(m).Restore(2024);
            Assert.Equal(ItemOutcome.Done, r.Outcome);
            Assert.Equal(FileUtil.Sha256(original), FileUtil.Sha256File(m.MainJs(2024)));
            if (damage == "corrupt-backup") Assert.Equal("corrupt", File.ReadAllText(backup));
        }

        [Fact]
        public void Restore_refuses_altered_ek_content()
        {
            using var m = new Machine();
            m.InstallRevit(2024, Js.Bytes(Js.Original()));
            Mgr(m).Apply(2024);
            string altered = File.ReadAllText(m.MainJs(2024)).Replace("M4 6L12 6L8 10Z\",style:{fill:\"#555555\"}", "M4 6L12 6L8 10Z\",style:{fill:\"#000000\"}");
            File.WriteAllText(m.MainJs(2024), altered);
            string before = FileUtil.Sha256File(m.MainJs(2024));
            var r = Mgr(m).Restore(2024);
            Assert.Equal(ItemOutcome.Failed, r.Outcome);
            Assert.Contains("NOT modified", r.Detail);
            Assert.Equal(before, FileUtil.Sha256File(m.MainJs(2024)));
        }

        [Fact]
        public void Patched_file_with_lost_record_gets_its_record_and_backup_rebuilt()
        {
            using var m = new Machine();
            byte[] original = Js.Bytes(Js.Original());
            m.InstallRevit(2024, original);
            Mgr(m).Apply(2024);
            Directory.Delete(m.Env.BrowserStore(2024), true);
            Assert.Equal(ItemOutcome.AlreadyDone, Mgr(m).Apply(2024).Outcome);
            var rec = KeyValueFile.Load(Path.Combine(m.Env.BrowserStore(2024), "state.txt"));
            Assert.Equal(FileUtil.Sha256(original), rec["OriginalSHA256"]);
            Assert.Equal(FileUtil.Sha256(original), FileUtil.Sha256File(rec["Backup"]));
        }

        [Fact]
        public void Legacy_T_patch_is_taken_over_using_its_backup_record_and_restores_to_plus()
        {
            using var m = new Machine();
            byte[] original = Js.Bytes(Js.Original());
            string tText = Js.Original().Replace(BrowserPatch.CollapsedOriginal, BrowserPatch.LegacyT);
            byte[] t = Js.Bytes(tText);
            m.InstallRevit(2024, t);
            string ss = m.Env.SymbolSwapStore(2024);
            Directory.CreateDirectory(ss);
            File.WriteAllText(Path.Combine(ss, "fouryear-install-state.json"),
                "{\"PatchId\":\"EKSymbolSwap_2024_ConfirmedSvg_T_v2\",\"OriginalSHA256\":\"" + FileUtil.Sha256(original) +
                "\",\"PatchedSHA256\":\"" + FileUtil.Sha256(t) + "\"}");
            var r = Mgr(m).Apply(2024);
            Assert.Equal(ItemOutcome.Done, r.Outcome);
            Assert.Contains("verified", r.Detail);
            Assert.Equal(ItemOutcome.Done, Mgr(m).Restore(2024).Outcome);
            Assert.Equal(FileUtil.Sha256(original), FileUtil.Sha256File(m.MainJs(2024)));
            Assert.True(File.Exists(Path.Combine(ss, "fouryear-install-state.json")), "legacy records are left alone");
        }

        [Fact]
        public void Legacy_T_patch_with_mismatching_record_is_skipped()
        {
            using var m = new Machine();
            byte[] t = Js.Bytes(Js.Original().Replace(BrowserPatch.CollapsedOriginal, BrowserPatch.LegacyT));
            m.InstallRevit(2024, t);
            string ss = m.Env.SymbolSwapStore(2024);
            Directory.CreateDirectory(ss);
            File.WriteAllText(Path.Combine(ss, "install-state.json"),
                "{\"OriginalSHA256\":\"" + new string('A', 64) + "\",\"PatchedSHA256\":\"" + FileUtil.Sha256(t) + "\"}");
            Assert.Equal(ItemOutcome.Skipped, Mgr(m).Apply(2024).Outcome);
            Assert.Equal(FileUtil.Sha256(t), FileUtil.Sha256File(m.MainJs(2024)));
        }

        [Fact]
        public void Custom_revit_install_location_is_found()
        {
            using var m = new Machine();
            string dir = Path.Combine(m.Root, "D", "Apps", "Revit 2025");
            m.CustomDirs[2025] = dir;
            m.InstallRevit(2025, Js.Bytes(Js.Original()), dir);
            Assert.Equal(ItemOutcome.Done, Mgr(m).Apply(2025).Outcome);
            Assert.StartsWith(dir, Mgr(m).Inspect(2025).Target);
        }
    }
}

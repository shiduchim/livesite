using System.IO;
using EKTriangle.Core;
using Xunit;

namespace EKTriangle.Tests
{
    [Collection("filesystem")]
    public class CmdRecoveryTests
    {
        [Fact]
        public void Renamed_duplicate_is_backed_up_and_only_one_manifest_remains()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            var mgr = new AddinManager(m.Env, null);
            string loader = Operations.LoaderPath(m.Payload(), 2024);
            mgr.Install(2024, loader);
            File.Copy(mgr.ManifestPath(2024), Path.Combine(m.Env.UserAddinsDir(2024), "old-triangle.addin"));
            Assert.Equal(ItemOutcome.AlreadyDone, mgr.Install(2024, loader).Outcome);
            Assert.Single(Directory.GetFiles(m.Env.UserAddinsDir(2024), "*.addin", SearchOption.TopDirectoryOnly),
                p => File.ReadAllText(p).Contains(Product.AddInId));
            Assert.NotEmpty(Directory.GetFiles(m.Env.LegacyBackupRoot, "*-old-triangle.addin", SearchOption.AllDirectories));
        }

        [Fact]
        public void Mixed_manifest_is_never_overwritten_or_deleted()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            var mgr = new AddinManager(m.Env, null);
            string path = mgr.ManifestPath(2024);
            string text = "<RevitAddIns><AddIn><AddInId>" + Product.AddInId + "</AddInId></AddIn>" +
                "<AddIn><AddInId>11111111-2222-3333-4444-555555555555</AddInId></AddIn></RevitAddIns>";
            File.WriteAllText(path, text);
            Assert.Equal(ItemOutcome.Skipped, mgr.Install(2024, Operations.LoaderPath(m.Payload(), 2024)).Outcome);
            Assert.Equal(ItemOutcome.Failed, mgr.Uninstall(2024).Outcome);
            Assert.Equal(text, File.ReadAllText(path));
        }

        [Fact]
        public void Guid_in_comment_does_not_prove_manifest_ownership()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            var mgr = new AddinManager(m.Env, null);
            string text = "<!-- " + Product.AddInId + " -->not valid XML";
            File.WriteAllText(mgr.ManifestPath(2024), text);
            Assert.Equal(ItemOutcome.Skipped, mgr.Install(2024, Operations.LoaderPath(m.Payload(), 2024)).Outcome);
            mgr.Uninstall(2024);
            Assert.Equal(text, File.ReadAllText(mgr.ManifestPath(2024)));
        }

        [Fact]
        public void Missing_target_after_apply_is_an_unresolved_restore()
        {
            using var m = new Machine();
            m.InstallRevit(2026, Js.Bytes(Js.Original()));
            var mgr = new BrowserManager(m.Env, null);
            Assert.Equal(ItemOutcome.Done, mgr.Apply(2026).Outcome);
            File.Delete(m.MainJs(2026));
            var rep = new Report();
            Assert.Equal(Operations.ExitProblems, Operations.BrowserRestore(m.Env, null, rep));
            Assert.Equal("Failed", rep.Get("2026", "BrowserResult"));
            Assert.Contains("NOT verified", rep.Get("2026", "BrowserResultText"));
            Assert.NotEmpty(Directory.GetFiles(m.Env.BrowserStore(2026), "*.original"));
        }

        [Fact]
        public void Both_altered_icons_are_unresolved_even_if_no_exact_EK_literal_remains()
        {
            using var m = new Machine();
            m.InstallRevit(2024, Js.Bytes(Js.Original()));
            var mgr = new BrowserManager(m.Env, null);
            mgr.Apply(2024);
            string altered = File.ReadAllText(m.MainJs(2024)).Replace("#555555", "#000000");
            File.WriteAllText(m.MainJs(2024), altered);
            Assert.Equal(ItemOutcome.Failed, mgr.Restore(2024).Outcome);
            Assert.Equal(altered, File.ReadAllText(m.MainJs(2024)));
            Assert.Contains(2024, Operations.YearsNeedingBrowserRestore(m.Env));
        }

        [Fact]
        public void Missing_native_dll_is_not_reported_as_installed()
        {
            using var m = new Machine();
            m.InstallRevit(2024, null);
            var mgr = new AddinManager(m.Env, null);
            string loader = Operations.LoaderPath(m.Payload(), 2024);
            mgr.Install(2024, loader);
            File.Delete(Path.Combine(Path.GetDirectoryName(loader), Product.NativeFileName));
            Assert.False(mgr.Inspect(2024).Installed);
        }
    }
}

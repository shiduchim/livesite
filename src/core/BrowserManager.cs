using System;
using System.Collections.Generic;
using System.IO;

namespace EKTriangle.Core
{
    public enum ItemOutcome
    {
        Done,          // change made and verified
        AlreadyDone,   // nothing to change
        NotApplicable, // e.g. Revit or file not present
        Skipped,       // deliberately not changed (unsupported / unknown content)
        Blocked,       // Revit is running
        Failed,        // error; see detail
    }

    public sealed class ItemResult
    {
        public ItemOutcome Outcome;
        public string Detail = "";
        public ItemResult(ItemOutcome o, string d) { Outcome = o; Detail = d ?? ""; }
        public bool IsProblem => Outcome == ItemOutcome.Failed || Outcome == ItemOutcome.Blocked;
        public override string ToString() => Outcome + ": " + Detail;
    }

    public sealed class BrowserStatus
    {
        public int Year;
        public bool RevitInstalled;
        public string Target;
        public bool FileFound;
        public BrowserAnalysis Analysis;
        public string Sha256;
        public string ApprovedAs;
        public KeyValueFile Record;
        public string Error;

        /// <summary>Short code for the installer: Ready, Applied, Unsupported, NotFound, NoRevit, Error.</summary>
        public string Code
        {
            get
            {
                if (!RevitInstalled) return "NoRevit";
                if (Error != null) return "Error";
                if (!FileFound) return "NotFound";
                switch (Analysis.State)
                {
                    case BrowserState.Original:
                    case BrowserState.LegacyT: return "Ready";
                    case BrowserState.Patched: return "Applied";
                    default: return "Unsupported";
                }
            }
        }

        public string Text
        {
            get
            {
                switch (Code)
                {
                    case "NoRevit": return "Revit not installed";
                    case "Error": return "cannot read Project Browser file: " + Error;
                    case "NotFound": return "Project Browser file not found (unsupported Revit layout)";
                    case "Applied": return "triangles already installed";
                    case "Ready":
                        return (Analysis.State == BrowserState.LegacyT ? "compatible (replaces earlier T symbol)" : "compatible") +
                               (ApprovedAs != null ? ", approved build" : ", structurally verified");
                    default: return "not modified - " + Analysis.Reason;
                }
            }
        }
    }

    /// <summary>
    /// Applies and restores the Project Browser modification for one Revit
    /// version with backup, durable state, atomic replacement and verification.
    /// Requires write access to Program Files and ProgramData (administrator).
    /// </summary>
    public sealed class BrowserManager
    {
        const long MinBytes = 1024, MaxBytes = 40L * 1024 * 1024;
        readonly Env _env;
        readonly Log _log;

        public BrowserManager(Env env, Log log) { _env = env; _log = log; }

        string StatePath(int year) => Path.Combine(_env.BrowserStore(year), "state.txt");

        public BrowserStatus Inspect(int year)
        {
            var s = new BrowserStatus { Year = year };
            string dir = _env.FindRevitDir(year);
            s.RevitInstalled = dir != null;
            s.Record = KeyValueFile.Load(StatePath(year));
            if (dir == null) return s;
            s.Target = Env.BrowserScript(dir);
            s.FileFound = File.Exists(s.Target);
            if (!s.FileFound) return s;
            try
            {
                long len = new FileInfo(s.Target).Length;
                if (len < MinBytes || len > MaxBytes) throw new IOException("unexpected file size " + len);
                byte[] bytes = File.ReadAllBytes(s.Target);
                s.Sha256 = FileUtil.Sha256(bytes);
                s.Analysis = BrowserPatch.Analyze(BrowserPatch.Decode(bytes).Text);
                if (s.Analysis.State == BrowserState.Original) s.ApprovedAs = KnownBuilds.Describe(s.Sha256);
                else if (s.Analysis.State == BrowserState.Patched && s.Record != null)
                    s.ApprovedAs = KnownBuilds.Describe(s.Record["OriginalSHA256"]);
            }
            catch (Exception ex)
            {
                s.Error = ex.Message;
            }
            return s;
        }

        // ------------------------------------------------------------- apply --

        public ItemResult Apply(int year)
        {
            var st = Inspect(year);
            if (!st.RevitInstalled) return new ItemResult(ItemOutcome.NotApplicable, "Revit " + year + " not installed");
            if (st.Code == "Error") return new ItemResult(ItemOutcome.Skipped, st.Text);
            if (st.Code == "NotFound" || st.Code == "Unsupported")
                return new ItemResult(ItemOutcome.Skipped, "Project Browser " + st.Text);
            var running = _env.RevitBlocking(new[] { year });
            if (running.Count > 0) return new ItemResult(ItemOutcome.Blocked, "close " + Env.DescribeRunning(running));

            byte[] current = File.ReadAllBytes(st.Target);
            string currentHash = FileUtil.Sha256(current);
            if (currentHash != st.Sha256) return new ItemResult(ItemOutcome.Failed, "file changed during inspection; nothing modified");
            var decoded = BrowserPatch.Decode(current);

            if (st.Analysis.State == BrowserState.Patched)
            {
                EnsureRecordForPatched(year, st, decoded);
                return new ItemResult(ItemOutcome.AlreadyDone, "Project Browser triangles already installed");
            }

            string originalText = decoded.Text;
            string legacyNote = "";
            if (st.Analysis.State == BrowserState.LegacyT)
            {
                originalText = BrowserPatch.FromLegacyT(decoded.Text);
                string expected = LegacyOriginalHash(year, currentHash);
                string got = FileUtil.Sha256(BrowserPatch.Encode(new BrowserPatch.Decoded { HasBom = decoded.HasBom, Text = originalText }));
                if (expected != null && expected != got)
                    return new ItemResult(ItemOutcome.Skipped, "earlier T symbol does not match its own backup record; nothing modified");
                legacyNote = expected != null ? " (earlier T symbol replaced; original verified against its backup record)"
                                              : " (earlier T symbol replaced)";
            }

            byte[] originalBytes = BrowserPatch.Encode(new BrowserPatch.Decoded { HasBom = decoded.HasBom, Text = originalText });
            string originalHash = FileUtil.Sha256(originalBytes);
            byte[] patchedBytes = BrowserPatch.Encode(new BrowserPatch.Decoded { HasBom = decoded.HasBom, Text = BrowserPatch.Apply(originalText) });
            string patchedHash = FileUtil.Sha256(patchedBytes);

            if (FileUtil.IsReadOnly(st.Target))
                return new ItemResult(ItemOutcome.Failed, "main.js is marked read-only; nothing modified");

            // 1. Durable original backup (never overwritten, never deleted).
            string backup = EnsureBackup(year, originalBytes, originalHash);

            // 2. Recovery record BEFORE touching the Autodesk file.
            var rec = new KeyValueFile();
            rec["Product"] = Product.Name;
            rec["ProductVersion"] = Product.Version;
            rec["PatchId"] = Product.BrowserPatchId;
            rec["Year"] = year.ToString();
            rec["Target"] = st.Target;
            rec["OriginalSHA256"] = originalHash;
            rec["PatchedSHA256"] = patchedHash;
            rec["PreviousSHA256"] = currentHash;
            rec["Backup"] = backup;
            rec["Approved"] = KnownBuilds.Describe(originalHash) ?? "structurally verified";
            rec["Phase"] = "Applying";
            rec["UpdatedUtc"] = DateTime.UtcNow.ToString("o");
            rec.Save(StatePath(year), "EK Triangle Project Browser record - do not edit");

            // 3. Last-moment checks, then atomic replacement with verification and rollback.
            running = _env.RevitBlocking(new[] { year });
            if (running.Count > 0)
            {
                MarkPhase(year, rec, "NotApplied");
                return new ItemResult(ItemOutcome.Blocked, "close " + Env.DescribeRunning(running));
            }
            if (FileUtil.Sha256File(st.Target) != currentHash)
            {
                MarkPhase(year, rec, "NotApplied");
                return new ItemResult(ItemOutcome.Failed, "file changed just before writing; nothing modified");
            }
            FileUtil.CleanLeftovers(st.Target, new[] { currentHash, originalHash, patchedHash });
            try
            {
                FileUtil.ReplaceVerified(st.Target, patchedBytes, patchedHash, currentHash);
            }
            catch (Exception ex)
            {
                MarkPhase(year, rec, "NotApplied");
                bool intact = SafeHash(st.Target) == currentHash;
                return new ItemResult(ItemOutcome.Failed, "could not write main.js (" + ex.Message + ")" +
                    (intact ? "; original file left unchanged" : "; FILE STATE UNKNOWN - use EK Triangle Restore"));
            }
            MarkPhase(year, rec, "Applied");
            _log?.Info("Revit " + year + ": Project Browser patched " + originalHash + " -> " + patchedHash + legacyNote);
            return new ItemResult(ItemOutcome.Done, "Project Browser triangles installed" + legacyNote);
        }

        /// <summary>Patched file but lost/old record: rebuild the record and backup from the exact reversal.</summary>
        void EnsureRecordForPatched(int year, BrowserStatus st, BrowserPatch.Decoded decoded)
        {
            string patchedHash = st.Sha256;
            if (st.Record != null && st.Record["PatchedSHA256"] == patchedHash && st.Record["Phase"] == "Applied") return;
            byte[] original = BrowserPatch.Encode(new BrowserPatch.Decoded { HasBom = decoded.HasBom, Text = BrowserPatch.Reverse(decoded.Text) });
            string originalHash = FileUtil.Sha256(original);
            string backup = EnsureBackup(year, original, originalHash);
            var rec = st.Record ?? new KeyValueFile();
            rec["Product"] = Product.Name;
            rec["ProductVersion"] = Product.Version;
            rec["PatchId"] = Product.BrowserPatchId;
            rec["Year"] = year.ToString();
            rec["Target"] = st.Target;
            rec["OriginalSHA256"] = originalHash;
            rec["PatchedSHA256"] = patchedHash;
            rec["Backup"] = backup;
            rec["Phase"] = "Applied";
            rec["UpdatedUtc"] = DateTime.UtcNow.ToString("o");
            rec.Save(StatePath(year), "EK Triangle Project Browser record - do not edit");
            _log?.Info("Revit " + year + ": Project Browser record refreshed");
        }

        string EnsureBackup(int year, byte[] originalBytes, string originalHash)
        {
            string store = _env.BrowserStore(year);
            Directory.CreateDirectory(store);
            string backup = Path.Combine(store, "main.js." + originalHash.Substring(0, 16) + ".original");
            if (File.Exists(backup))
            {
                if (FileUtil.Sha256File(backup) == originalHash) return backup;
                // Never overwrite an existing file; keep it and write a second, verified copy.
                backup = Path.Combine(store, "main.js." + originalHash.Substring(0, 16) + "." + FileUtil.Nonce() + ".original");
            }
            FileUtil.WriteNewVerified(backup, originalBytes, originalHash);
            return backup;
        }

        void MarkPhase(int year, KeyValueFile rec, string phase)
        {
            rec["Phase"] = phase;
            rec["UpdatedUtc"] = DateTime.UtcNow.ToString("o");
            try { rec.Save(StatePath(year), "EK Triangle Project Browser record - do not edit"); }
            catch (Exception ex) { _log?.Warn("Revit " + year + ": could not update record: " + ex.Message); }
        }

        string LegacyOriginalHash(int year, string currentHash)
        {
            string store = _env.SymbolSwapStore(year);
            foreach (var name in new[] { "fouryear-install-state.json", "install-state.json" })
            {
                string p = Path.Combine(store, name);
                if (!File.Exists(p)) continue;
                try
                {
                    string json = File.ReadAllText(p);
                    string patched = JsonField(json, "PatchedSHA256");
                    string original = JsonField(json, "OriginalSHA256");
                    if (patched != null && original != null && patched.Equals(currentHash, StringComparison.OrdinalIgnoreCase))
                        return original.ToUpperInvariant();
                }
                catch { }
            }
            return null;
        }

        static string JsonField(string json, string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"([0-9A-Fa-f]{64})\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string SafeHash(string path)
        {
            try { return FileUtil.Sha256File(path); } catch { return null; }
        }

        // ----------------------------------------------------------- restore --

        public ItemResult Restore(int year)
        {
            var st = Inspect(year);
            if (st.RevitInstalled && !st.FileFound && st.Record != null &&
                st.Record["Phase"] != "Restored" && st.Record["Phase"] != "NotApplied")
                return new ItemResult(ItemOutcome.Failed, "Project Browser file is missing; restoration NOT verified. Backup kept in " + _env.BrowserStore(year));
            if (!st.RevitInstalled || !st.FileFound)
                return new ItemResult(ItemOutcome.NotApplicable, "Revit " + year + " Project Browser not present" +
                    (st.Record != null ? " (backup kept in " + _env.BrowserStore(year) + ")" : ""));
            if (st.Error != null) return new ItemResult(ItemOutcome.Failed, "cannot read main.js: " + st.Error);

            var rec = st.Record;
            switch (st.Analysis.State)
            {
                case BrowserState.Original:
                    if (rec != null && rec["Phase"] != "Restored" && rec["Phase"] != "NotApplied") MarkPhase(year, rec, "Restored");
                    if (rec != null && rec["OriginalSHA256"] != null && rec["OriginalSHA256"] != st.Sha256)
                        return new ItemResult(ItemOutcome.AlreadyDone, "no EK changes present (file was replaced, e.g. by an Autodesk update)");
                    return new ItemResult(ItemOutcome.AlreadyDone, "original Project Browser icons present");
                case BrowserState.LegacyT:
                    return new ItemResult(ItemOutcome.AlreadyDone,
                        "no EK Triangle changes present (contains the earlier EK SymbolSwap T symbol; left unchanged)");
                case BrowserState.Unsupported:
                    if (st.Analysis.EkCollapsedCount > 0 || st.Analysis.EkExpandedCount > 0)
                        return new ItemResult(ItemOutcome.Failed, "main.js contains EK icons in an unexpected form (" +
                            st.Analysis.Reason + "); NOT modified. Original backup kept in " + _env.BrowserStore(year));
                    return new ItemResult(ItemOutcome.AlreadyDone, "no EK changes present");
            }

            // Patched: remove exactly our two literals.
            var running = _env.RevitBlocking(new[] { year });
            if (running.Count > 0) return new ItemResult(ItemOutcome.Blocked, "close " + Env.DescribeRunning(running));
            if (FileUtil.IsReadOnly(st.Target))
                return new ItemResult(ItemOutcome.Failed, "main.js is marked read-only; NOT restored");

            byte[] current = File.ReadAllBytes(st.Target);
            string currentHash = FileUtil.Sha256(current);
            if (currentHash != st.Sha256) return new ItemResult(ItemOutcome.Failed, "file changed during inspection; NOT restored");
            var decoded = BrowserPatch.Decode(current);
            byte[] restored = BrowserPatch.Encode(new BrowserPatch.Decoded { HasBom = decoded.HasBom, Text = BrowserPatch.Reverse(decoded.Text) });
            string restoredHash = FileUtil.Sha256(restored);

            string note;
            string recOriginal = rec?["OriginalSHA256"];
            if (recOriginal != null && recOriginal == restoredHash)
            {
                note = "verified identical to the original backup";
                // Belt and braces: the backup, when intact, must equal the reversal.
                string backup = rec["Backup"];
                if (backup != null && FileUtil.IsUnder(backup, _env.BrowserStore(year)) && File.Exists(backup) &&
                    FileUtil.Sha256File(backup) != restoredHash)
                    _log?.Warn("Revit " + year + ": backup file differs from its record (kept, not used): " + backup);
            }
            else if (recOriginal != null)
            {
                note = "EK changes removed; the rest of the file differs from the backup taken at install (e.g. Autodesk update) and was kept";
            }
            else
            {
                note = "EK changes removed by exact reversal (no install record found)";
            }

            FileUtil.CleanLeftovers(st.Target, new[] { currentHash, restoredHash });
            try
            {
                FileUtil.ReplaceVerified(st.Target, restored, restoredHash, currentHash);
            }
            catch (Exception ex)
            {
                bool intact = SafeHash(st.Target) == currentHash;
                return new ItemResult(ItemOutcome.Failed, "could not write main.js (" + ex.Message + ")" +
                    (intact ? "; file unchanged, EK icons still present" : "; FILE STATE UNKNOWN - original backup in " + _env.BrowserStore(year)));
            }
            if (rec != null) MarkPhase(year, rec, "Restored");
            _log?.Info("Revit " + year + ": Project Browser restored " + currentHash + " -> " + restoredHash + " (" + note + ")");
            return new ItemResult(ItemOutcome.Done, "original Project Browser icons restored (" + note + ")");
        }
    }
}

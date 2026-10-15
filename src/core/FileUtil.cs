using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EKTriangle.Core
{
    /// <summary>Fault injection used only by the automated tests.</summary>
    public static class TestHooks
    {
        public static Action<string> BeforeReplace;
        public static Action<string> AfterReplace;
    }

    public static class FileUtil
    {
        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(data));
        }

        public static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ToHex(sha.ComputeHash(fs));
        }

        static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        public static string Nonce() => Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>Writes bytes to a new file and verifies them; the file must not exist yet.</summary>
        public static void WriteNewVerified(string path, byte[] bytes, string expectedHash)
        {
            string tmp = path + ".tmp-" + Nonce();
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                if (Sha256File(tmp) != expectedHash) throw new IOException("Written copy failed its checksum: " + tmp);
                File.Move(tmp, path);
            }
            finally
            {
                TryDelete(tmp);
            }
            if (Sha256File(path) != expectedHash) throw new IOException("Copy failed its checksum: " + path);
        }

        /// <summary>
        /// Atomically replaces <paramref name="target"/> with <paramref name="bytes"/>
        /// (temporary file in the same folder + File.Replace) and verifies the
        /// result. If verification fails the previous content is put back.
        /// The caller must have checked that target still has the expected content.
        /// </summary>
        public static void ReplaceVerified(string target, byte[] bytes, string expectedHash, string previousHash)
        {
            string dir = Path.GetDirectoryName(target);
            string name = Path.GetFileName(target);
            string nonce = Nonce();
            string tmp = Path.Combine(dir, name + ".EKTriangle." + nonce + ".tmp");
            string rollback = Path.Combine(dir, name + ".EKTriangle." + nonce + ".rollback");
            bool replaced = false;
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                if (Sha256File(tmp) != expectedHash) throw new IOException("Prepared file failed its checksum; nothing was changed.");
                TestHooks.BeforeReplace?.Invoke(target);
                File.Replace(tmp, target, rollback);
                replaced = true;
                TestHooks.AfterReplace?.Invoke(target);
                if (Sha256File(target) != expectedHash)
                    throw new IOException("The file did not verify after replacement.");
            }
            catch
            {
                if (replaced && File.Exists(rollback))
                {
                    // Put the previous content back, then make sure it is what it was.
                    string back = Path.Combine(dir, name + ".EKTriangle." + nonce + ".back");
                    File.Replace(rollback, target, back);
                    TryDelete(back);
                    if (previousHash != null && Sha256File(target) != previousHash)
                        throw new IOException("Replacement failed AND the previous file could not be verified after rollback: " + target);
                }
                throw;
            }
            finally
            {
                TryDelete(tmp);
                if (replaced) TryDelete(rollback);
            }
        }

        /// <summary>Removes leftovers of an interrupted <see cref="ReplaceVerified"/> run (only our own names).</summary>
        public static int CleanLeftovers(string target, ICollection<string> knownHashes)
        {
            int removed = 0;
            string dir = Path.GetDirectoryName(target);
            if (!Directory.Exists(dir)) return 0;
            string prefix = Path.GetFileName(target) + ".EKTriangle.";
            foreach (var f in Directory.GetFiles(dir, prefix + "*"))
            {
                try
                {
                    bool ours = f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".back", StringComparison.OrdinalIgnoreCase) ||
                                (f.EndsWith(".rollback", StringComparison.OrdinalIgnoreCase) &&
                                 knownHashes.Contains(Sha256File(f)));
                    if (ours) { File.Delete(f); removed++; }
                }
                catch { }
            }
            return removed;
        }

        public static void TryDelete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); } catch { }
        }

        public static bool IsReadOnly(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0; }
            catch { return false; }
        }

        /// <summary>Atomically writes a small text file (temporary file + replace/move).</summary>
        public static void WriteTextAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp-" + Nonce();
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            finally { TryDelete(tmp); }
        }

        public static bool IsUnder(string path, string root)
        {
            string p = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Simple "key=value" text file used for durable state (no external JSON dependency).</summary>
    public sealed class KeyValueFile
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string this[string key]
        {
            get { string v; return Values.TryGetValue(key, out v) ? v : null; }
            set { Values[key] = value ?? ""; }
        }

        public static KeyValueFile Load(string path)
        {
            if (!File.Exists(path)) return null;
            var kv = new KeyValueFile();
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                kv.Values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return kv;
        }

        public void Save(string path, string header)
        {
            var sb = new StringBuilder();
            if (header != null) sb.Append("# ").Append(header).Append("\r\n");
            foreach (var kv in Values)
                sb.Append(kv.Key).Append('=').Append((kv.Value ?? "").Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
            FileUtil.WriteTextAtomic(path, sb.ToString());
        }
    }
}

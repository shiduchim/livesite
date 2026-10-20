using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EKScriptLauncher.Core
{
    public sealed class ScriptEntry
    {
        public string Name;
        public string Path;
        public string FileName;
        public string Summary = "";
    }

    /// <summary>Finds and describes the .py files in a folder.</summary>
    public static class ScriptCatalog
    {
        static readonly Regex ModuleDocRegex = new Regex(
            "^\\s*(?:#[^\\n]*\\n\\s*)*[rRuU]{0,2}(\"\"\"|''')(.*?)\\1",
            RegexOptions.Singleline);

        static readonly Regex DocAssignRegex = new Regex(
            "^__doc__\\s*=\\s*[rRuU]{0,2}(\"\"\"|'''|\"|')(.*?)\\1",
            RegexOptions.Singleline | RegexOptions.Multiline);

        static readonly Regex WordRegex = new Regex("[^\\W_]+");
        static readonly Regex CamelRegex = new Regex("[A-Z][a-z0-9]*|[a-z0-9]+");

        public static string DisplayName(string path)
        {
            return System.IO.Path.GetFileNameWithoutExtension(path).Replace("_", " ").Trim();
        }

        /// <summary>All .py files directly in the folder, sorted by display name.</summary>
        public static List<ScriptEntry> GetPythonFiles(string folder)
        {
            var result = new List<ScriptEntry>();

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return result;

            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch
            {
                return result;
            }

            foreach (string file in files)
            {
                if (!file.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new ScriptEntry
                {
                    Name = DisplayName(file),
                    Path = file,
                    FileName = System.IO.Path.GetFileName(file),
                });
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Name.ToLowerInvariant(), b.Name.ToLowerInvariant()));
            return result;
        }

        /// <summary>
        /// First line of the script's docstring (or __doc__ = "..."), used as a
        /// short description under the script name. Empty if there is none.
        /// </summary>
        public static string ReadSummary(string path)
        {
            string text;
            try
            {
                var buffer = new byte[8192];
                int read;
                using (FileStream stream = File.OpenRead(path))
                    read = stream.Read(buffer, 0, buffer.Length);

                int start = read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
                text = Encoding.UTF8.GetString(buffer, start, read - start).Replace("\r\n", "\n");
            }
            catch
            {
                return "";
            }

            Match match = ModuleDocRegex.Match(text);
            if (!match.Success)
                match = DocAssignRegex.Match(text);
            if (!match.Success)
                return "";

            // Skip blank lines, ==== underlines and a heading that just repeats
            // the script name.
            string name = DisplayName(path).ToLowerInvariant();
            foreach (string rawLine in match.Groups[2].Value.Split('\n'))
            {
                string line = string.Join(" ", rawLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                if (line.Trim('=', '-', '~', '*', '#').Length == 0)
                    continue;
                if (line.Replace("_", " ").ToLowerInvariant() == name)
                    continue;
                return line.Length > 200 ? line.Substring(0, 200) : line;
            }
            return "";
        }

        public static string Initials(string displayName)
        {
            var words = new List<string>();
            foreach (Match m in WordRegex.Matches(displayName))
                words.Add(m.Value);

            if (words.Count == 0)
                return "Py";

            string first = words[0];
            if (IsAllDigits(first))
                return first.Length > 2 ? first.Substring(0, 2) : first;

            if (words.Count == 1)
            {
                MatchCollection parts = CamelRegex.Matches(first);
                if (parts.Count >= 2)
                    return (parts[0].Value.Substring(0, 1) + parts[1].Value.Substring(0, 1)).ToUpperInvariant();

                string two = first.Length > 2 ? first.Substring(0, 2) : first;
                return two.Substring(0, 1).ToUpperInvariant() + two.Substring(1).ToLowerInvariant();
            }

            return (first.Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
        }

        /// <summary>Deterministic, so a script keeps its colour between sessions.</summary>
        public static int ColorIndex(string displayName, int paletteSize)
        {
            long value = 0;
            foreach (char c in displayName.ToLowerInvariant())
                value = (value * 31 + c) & 0xFFFFFFF;
            return (int)(value % paletteSize);
        }

        static bool IsAllDigits(string text)
        {
            foreach (char c in text)
                if (!char.IsDigit(c))
                    return false;
            return text.Length > 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace EKScriptLauncher.Core
{
    /// <summary>
    /// The two folders the launcher remembers. Stored in the same
    /// %APPDATA%\EK\ScriptLauncher\settings.json file the pyRevit version used,
    /// so existing settings carry over.
    /// </summary>
    public sealed class LauncherSettings
    {
        public string ScriptsFolder = "";
        public string QuickLaunchFolder = "";

        static string AppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        }

        public static string ConfigDir
        {
            get { return Path.Combine(AppData, "EK", "ScriptLauncher"); }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDir, "settings.json"); }
        }

        /// <summary>
        /// Primary location plus a few harmless legacy guesses so replacing an
        /// older launcher has a chance to keep the user's existing folder.
        /// </summary>
        public static LauncherSettings Load()
        {
            var candidates = new[]
            {
                ConfigPath,
                Path.Combine(AppData, "EKScriptLauncher", "settings.json"),
                Path.Combine(AppData, "EK Script Launcher", "settings.json"),
            };

            foreach (string path in candidates)
            {
                Dictionary<string, object> data = ReadJson(path);
                if (data == null || data.Count == 0)
                    continue;

                var settings = new LauncherSettings();

                foreach (string key in new[] { "scripts_folder", "script_folder", "folder", "path" })
                {
                    string folder = GetString(data, key);
                    if (folder.Length > 0 && Directory.Exists(folder))
                    {
                        settings.ScriptsFolder = folder;
                        break;
                    }
                }

                foreach (string key in new[] { "quick_launch_folder", "quick_folder", "secondary_folder" })
                {
                    string folder = GetString(data, key);
                    if (folder.Length > 0)
                    {
                        settings.QuickLaunchFolder = folder;
                        break;
                    }
                }

                return settings;
            }

            return new LauncherSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(ConfigDir);

            var json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"scripts_folder\": ").Append(JsonString(ScriptsFolder)).Append(",\n");
            json.Append("  \"quick_launch_folder\": ").Append(JsonString(QuickLaunchFolder)).Append("\n");
            json.Append("}");

            File.WriteAllText(ConfigPath, json.ToString(), new UTF8Encoding(false));
        }

        static Dictionary<string, object> ReadJson(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var serializer = new DataContractJsonSerializer(
                    typeof(Dictionary<string, object>),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

                using (FileStream stream = File.OpenRead(path))
                    return serializer.ReadObject(stream) as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }
        }

        static string GetString(Dictionary<string, object> data, string key)
        {
            object value;
            if (data.TryGetValue(key, out value))
            {
                var text = value as string;
                if (text != null)
                    return text;
            }
            return "";
        }

        static string JsonString(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}

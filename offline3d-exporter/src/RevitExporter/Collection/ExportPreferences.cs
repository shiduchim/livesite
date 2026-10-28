namespace Offline3D.RevitExporter;

/// <summary>
/// The export choices remembered between exports (phase filter, detail, …), as
/// "key=value" lines in %LOCALAPPDATA%\Offline3DPdfPrototype\export-options.txt.
/// Plain text: the Revit DLL carries no JSON package. Unreadable files give defaults.
/// </summary>
internal sealed class ExportPreferences
{
    private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly string path;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Offline3DPdfPrototype", "export-options.txt");

    public ExportPreferences(string path)
    {
        this.path = path;
        try
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                int split = line.IndexOf('=');
                if (split > 0) values[line.Substring(0, split).Trim()] = Unescape(line.Substring(split + 1));
            }
        }
        catch (IOException) { values.Clear(); }
        catch (UnauthorizedAccessException) { values.Clear(); }
    }

    public string? Get(string key) => values.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, string? value) { if (value == null) values.Remove(key); else values[key] = value; }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllLines(temporary, values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + Escape(p.Value)));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // Values are single lines: backslash and line breaks are escaped.
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
    private static string Unescape(string value)
    {
        var text = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length) { text.Append(value[i]); continue; }
            char next = value[++i];
            text.Append(next == 'n' ? '\n' : next == 'r' ? '\r' : next);
        }
        return text.ToString();
    }
}

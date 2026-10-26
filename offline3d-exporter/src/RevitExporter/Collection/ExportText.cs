using System.Text;

namespace Offline3D.RevitExporter;

/// <summary>Model text (names, parameter values) is user data. The wire JSON
/// writer rejects unpaired UTF-16 surrogates, and the v0.5.2 exporter could
/// create one by cutting a long parameter value inside a surrogate pair,
/// failing the whole export at the end. Clean every model string once here.</summary>
internal static class ExportText
{
    public static string Clean(string? value, int maxLength = int.MaxValue)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string text = value!;
        if (text.Length > maxLength)
        {
            int cut = maxLength;
            if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--; // never split a pair
            text = text.Substring(0, cut);
        }
        StringBuilder? clean = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool bad = char.IsHighSurrogate(c) ? i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])
                     : char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(text[i - 1]));
            if (bad && clean == null) clean = new StringBuilder(text, 0, i, text.Length);
            if (clean != null) clean.Append(bad ? '�' : c);
        }
        return clean?.ToString() ?? text;
    }
}

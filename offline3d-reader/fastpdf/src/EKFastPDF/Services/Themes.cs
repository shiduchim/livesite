using System.Windows;
using System.Windows.Media;
using EKFastPDF.Core.Rendering;
using Microsoft.Win32;

namespace EKFastPDF.Services;

/// <summary>
/// Window themes (docs/mockups/themes.png): Light, Soft grey, Warm, Dark grey, Dark and Night,
/// or "Match Windows" (Light or Dark from the Windows app mode). A theme replaces the palette
/// brushes in the application resources; everything drawn with DynamicResource or
/// SetResourceReference follows at once.
/// </summary>
public static class Themes
{
    public static readonly string[] Names = { "Light", "Soft grey", "Warm", "Dark grey", "Dark", "Night" };
    public const string MatchWindows = "Match Windows";

    /// <summary>Raised after a theme or page look is applied.</summary>
    public static event Action? Changed;

    public static string Current { get; private set; } = "Light";

    // Palette keys, in this order in every theme below.
    private static readonly string[] Keys =
    {
        "Chrome", "Surface", "Ink", "InkSoft", "InkFaint", "DocBackground", "Line", "Field", "FieldBorder",
        "ButtonFill", "ButtonBorder", "ButtonEdge", "Hover", "Pressed", "Accent", "AccentHover", "AccentEdge",
        "AccentSoft", "AccentText", "Selected", "PageEdge", "TabBorder", "TitleBar", "Frame", "SliderTrack",
        "CloseHover", "ClosePressed", "Splitter", "ToastBack", "MenuBack", "ScrollThumb",
    };

    private static readonly Dictionary<string, string[]> Palettes = new()
    {
        ["Light"] = new[] { "F0F0F0", "FFFFFF", "1B1B1B", "5F6368", "9AA0A6", "E3E5E8", "E1E1E1", "FFFFFF", "C3C8CF",
            "FDFDFD", "D0D0D0", "BABABA", "F2F7FC", "E6EFF9", "5D75A5", "9DB0D8", "8A9ECA",
            "E8EDF7", "2F4A86", "E3E7EE", "D4D6DA", "DCDCDC", "F9F9F9", "BDBDBD", "D0D3D8",
            "EBEBEB", "E0E0E0", "D6D8DC", "F2FFFFFF", "FFFFFF", "C2C6CC" },
        ["Soft grey"] = new[] { "E2E3E5", "ECEDEF", "202124", "5A5E64", "8E9399", "C9CCD1", "D0D2D6", "F4F5F6", "B9BDC3",
            "F1F2F3", "C2C5CA", "AEB2B8", "E8EEF6", "DCE5F1", "5D75A5", "9DB0D8", "8A9ECA",
            "DDE3EF", "2F4A86", "D5DAE3", "BFC3C9", "C9CCD1", "EAEBED", "AEB2B8", "C2C5CA",
            "D8D9DC", "CDCED2", "BFC3C9", "F2ECEDEF", "F1F2F3", "AEB2B8" },
        ["Warm"] = new[] { "EFE9DF", "F6F1E9", "2B2620", "6B6258", "9C9286", "DCD3C4", "DDD4C6", "FBF7F0", "CDBFAA",
            "FBF7F0", "D3C8B6", "BCAF9B", "F3EADC", "EADFCC", "8C6A43", "C2A47E", "A98A62",
            "EFE3D0", "6E5232", "E6DCCB", "CFC4B2", "D8CDBB", "F5F0E8", "C7BBA8", "D6CBB9",
            "E6DFD3", "DDD4C6", "CFC4B2", "F2F6F1E9", "FBF7F0", "C9BCA8" },
        ["Dark grey"] = new[] { "3A3D42", "44474C", "E3E5E8", "AEB2B8", "7F848B", "4B4F55", "50545A", "2F3236", "62666D",
            "45484E", "5E6268", "33363A", "4A5059", "545B66", "8EA3CF", "7E93C2", "6D82B3",
            "3F4A60", "B8C7E6", "4A5160", "5A5E64", "55595F", "404348", "5A5E64", "5E6268",
            "4A4E54", "54585E", "55595F", "F244474C", "3F4247", "6A6E75" },
        ["Dark"] = new[] { "2B2B2B", "323232", "E6E6E6", "A8A8A8", "7A7A7A", "1E1E1E", "3C3C3C", "1F1F1F", "575757",
            "383838", "555555", "4A4A4A", "41454D", "4B5059", "8EA3CF", "7E93C2", "6D82B3",
            "39414F", "B8C7E6", "3A404B", "3C3C3C", "444444", "323232", "4A4A4A", "555555",
            "3D3D3D", "474747", "3C3C3C", "F2323232", "2F2F2F", "555555" },
        ["Night"] = new[] { "1B1B1B", "242424", "E0E0E0", "9A9A9A", "6E6E6E", "0E0E0E", "2E2E2E", "141414", "444444",
            "2A2A2A", "444444", "333333", "33373E", "3D424A", "8EA3CF", "7E93C2", "6D82B3",
            "2F3644", "B8C7E6", "2E333C", "2E2E2E", "333333", "242424", "3A3A3A", "444444",
            "2E2E2E", "383838", "2E2E2E", "F2242424", "202020", "4A4A4A" },
    };

    /// <summary>The page look each theme uses with "As the theme".</summary>
    public static PageLook DefaultPages(string theme) => theme switch
    {
        "Soft grey" => PageLook.Soft,
        "Warm" => PageLook.WarmPaper,
        "Dark grey" => PageLook.Dimmed,
        "Dark" => PageLook.DimmedMore,
        "Night" => PageLook.Dark,
        _ => PageLook.Original,
    };

    public static bool IsDark(string theme) => theme is "Dark grey" or "Dark" or "Night";

    /// <summary>Resolves "Match Windows" to Light or Dark.</summary>
    public static string Resolve(string? setting)
    {
        if (setting == MatchWindows) return WindowsUsesDarkApps() ? "Dark" : "Light";
        return setting != null && Palettes.ContainsKey(setting) ? setting : "Light";
    }

    /// <summary>Applies the theme and page look (<paramref name="pages"/>: "Theme" = as the theme).</summary>
    public static void Apply(string? theme, string? pages)
    {
        string name = Resolve(theme);
        var res = Application.Current.Resources;
        var values = Palettes[name];
        for (int i = 0; i < Keys.Length; i++)
        {
            var brush = new SolidColorBrush(Parse(values[i]));
            brush.Freeze();
            res[Keys[i]] = brush;
        }
        res["AccentColor"] = Parse(values[Array.IndexOf(Keys, "Accent")]);
        Current = name;
        PageTint.Look = pages is null or "Theme" ? DefaultPages(name) : Enum.TryParse<PageLook>(pages, out var look) ? look : DefaultPages(name);
        Changed?.Invoke();
    }

    private static Color Parse(string hex)
    {
        uint v = Convert.ToUInt32(hex, 16);
        return hex.Length == 8
            ? Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }
}

/// <summary>Applies the page look to rendered pixels (BGRx, any thread).</summary>
public static class PageTint
{
    private static volatile int _look;
    private static int _generation;

    /// <summary>Changes whenever the look changes; pictures made with an older one are dropped.</summary>
    public static int Generation => Volatile.Read(ref _generation);

    public static PageLook Look
    {
        get => (PageLook)_look;
        set { if (_look != (int)value) { _look = (int)value; Interlocked.Increment(ref _generation); } }
    }

    /// <summary>What a blank page looks like (drawn before its pixels arrive).</summary>
    public static Color Paper
    {
        get
        {
            Span<byte> px = stackalloc byte[] { 255, 255, 255, 255 };
            Apply(px, Look);
            return Color.FromRgb(px[2], px[1], px[0]);
        }
    }

    public static string Label(PageLook look) => look switch
    {
        PageLook.Original => "Original",
        PageLook.Soft => "Slightly dimmed",
        PageLook.Dimmed => "Dimmed",
        PageLook.DimmedMore => "Dimmed more",
        PageLook.WarmPaper => "Warm paper",
        PageLook.Dark => "Dark (light lines on black)",
        _ => look.ToString(),
    };

    /// <summary>Changes the pixels in place.</summary>
    public static void Apply(Span<byte> bgrx, PageLook look) => PageLookPixels.Apply(bgrx, look);
}

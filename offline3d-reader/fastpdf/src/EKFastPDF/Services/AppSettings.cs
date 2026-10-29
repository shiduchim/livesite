using System.IO;
using System.Text.Json;
using EKFastPDF.Core.Diagnostics;
using EKFastPDF.Core.Library;

using EKFastPDF.Core.Rendering;

namespace EKFastPDF.Services;

/// <summary>User settings in %APPDATA%\EK FastPDF\settings.json. Zero means "automatic".</summary>
public sealed class AppSettings
{
    public int RenderWorkers { get; set; }
    public int CacheMegabytes { get; set; }
    public int TileSize { get; set; } = 512;
    /// <summary>PDFium sub-pixel (LCD) text anti-aliasing, as in v1.1.</summary>
    public bool LcdText { get; set; } = true;
    /// <summary>Before 0.5.4: the wheel zoomed. Read for old files only; <see cref="WheelMode"/> replaces it.</summary>
    public bool WheelZooms { get; set; } = true;
    /// <summary>"Scroll" (default, like other PDF readers: Ctrl+wheel zooms) or "Zoom" (wheel zooms, Ctrl+wheel scrolls).</summary>
    public string WheelMode { get; set; } = "Scroll";
    /// <summary>"Compact" (default: one bar, no status bar) or "Regular" (toolbar and status bar).</summary>
    public string Layout { get; set; } = "Compact";
    /// <summary>"Auto" (GPU when available) or "Software".</summary>
    public string RenderMode { get; set; } = "Auto";
    public bool DebugOverlay { get; set; }
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1320;
    public double WindowHeight { get; set; } = 840;
    public bool WindowMaximized { get; set; }

    // ---- My PDFs and New PDFs (everything can be off)
    /// <summary>Folders shown in My PDFs, in the user's order. Null until first run: Downloads.</summary>
    public List<LibraryFolder>? Folders { get; set; }
    /// <summary>Size slider, 0 (smallest) to 1 (fit width).</summary>
    public double LibrarySize { get; set; } = 0.25;
    /// <summary>Ctrl+wheel zoom past fit width in My PDFs (1 = not past fit width).</summary>
    public double LibraryBeyondFit { get; set; } = 1;
    public bool ShowNewestButton { get; set; } = true;
    /// <summary>"Recent" means arrived in the last this many minutes (start and window-forward options).</summary>
    public int RecentMinutes { get; set; } = 1;
    /// <summary>When EKPDF starts without files, open PDFs that arrived recently.</summary>
    public bool OpenRecentOnStart { get; set; } = true;
    /// <summary>When the EKPDF window comes forward, open recent PDFs not opened yet.</summary>
    public bool OpenRecentOnActivate { get; set; }

    /// <summary>Light, Soft grey, Warm, Dark grey, Dark, Night or "Match Windows".</summary>
    public string Theme { get; set; } = "Light";
    /// <summary>"Theme" (as the theme) or a <see cref="PageLook"/> name.</summary>
    public string Pages { get; set; } = "Theme";

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offline3D-FastPDF-Dev");
    private static string FilePath => Path.Combine(Directory, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Settings could not be read, using defaults: " + e.Message);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Settings could not be saved: " + e.Message);
        }
    }
}

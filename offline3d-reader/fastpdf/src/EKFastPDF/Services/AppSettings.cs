using System.IO;
using System.Text.Json;
using EKFastPDF.Core.Diagnostics;

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

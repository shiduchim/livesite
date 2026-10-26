using System.Text.Json;

namespace Offline3D;

/// <summary>User preferences in metres, independent of the displayed unit or a stretched box.</summary>
public sealed class SectionBoxDefaults
{
    public double SquareSizeMeters { get; set; } = 3.048;
    public double AboveMeters { get; set; } = 3.048;
    public double BelowMeters { get; set; } = .1524;
    public string Unit { get; set; } = "feet";

    public BoxVolume At(Point3 point) => BoxVolume.Centered(point, SquareSizeMeters, SquareSizeMeters, AboveMeters, BelowMeters);
    public void Validate()
    {
        if (Unit != "feet" && Unit != "meters") throw new InvalidDataException("Choose Feet or Meters.");
        At(new Point3(0, 0, 0));
    }
    public static SectionBoxDefaults Load(string path)
    {
        try
        {
            var value = JsonSerializer.Deserialize<SectionBoxDefaults>(File.ReadAllText(path), Contract.Json) ?? throw new InvalidDataException();
            value.Validate();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new SectionBoxDefaults();
        }
    }
    public void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Contract.Json));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

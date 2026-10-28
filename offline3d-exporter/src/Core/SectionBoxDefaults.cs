using System.Text.Json;

namespace Offline3D;

/// <summary>
/// The section box size set in 3D Settings, in metres, with the displayed unit. Every
/// newly opened 3D view starts with it; a box stretched in 3D keeps its size only
/// until 3D is closed. <see cref="Factory"/> is the Reset size.
/// </summary>
public sealed class SectionBoxDefaults
{
    public const double FactorySquare = 3.048, FactoryAbove = 3.048, FactoryBelow = .1524;
    public double SquareSizeMeters { get; set; } = FactorySquare;
    public double AboveMeters { get; set; } = FactoryAbove;
    public double BelowMeters { get; set; } = FactoryBelow;
    public string Unit { get; set; } = "feet";

    public bool IsFactory() => Near(SquareSizeMeters, FactorySquare) && Near(AboveMeters, FactoryAbove) && Near(BelowMeters, FactoryBelow);
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    public static SectionBoxDefaults Factory(string unit = "feet") => new() { Unit = unit };
    public SectionBoxDefaults WithUnit(string unit) => new() { SquareSizeMeters = SquareSizeMeters, AboveMeters = AboveMeters, BelowMeters = BelowMeters, Unit = unit };

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

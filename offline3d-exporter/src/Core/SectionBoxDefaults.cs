using System.Text.Json;

namespace Offline3D;

/// <summary>
/// The section box size used for a new box, in metres. Since 0.5.4 it is the size
/// the user last stretched a box to; <see cref="Factory"/> restores the stock size.
/// The displayed unit is stored with it.
/// </summary>
public sealed class SectionBoxDefaults
{
    public const double FactorySquare = 3.048, FactoryAbove = 3.048, FactoryBelow = .1524;
    /// <summary>Width and depth before 0.5.4, which kept the box square. Still read for older files.</summary>
    public double SquareSizeMeters { get; set; } = FactorySquare;
    public double? WidthMeters { get; set; }
    public double? DepthMeters { get; set; }
    public double AboveMeters { get; set; } = FactoryAbove;
    public double BelowMeters { get; set; } = FactoryBelow;
    public string Unit { get; set; } = "feet";

    public double Width() => WidthMeters ?? SquareSizeMeters;
    public double Depth() => DepthMeters ?? SquareSizeMeters;
    public bool IsFactory() => Near(Width(), FactorySquare) && Near(Depth(), FactorySquare) && Near(AboveMeters, FactoryAbove) && Near(BelowMeters, FactoryBelow);
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    public static SectionBoxDefaults Factory(string unit = "feet") => new() { Unit = unit };
    /// <summary>The same unit with the size of <paramref name="box"/>.</summary>
    public SectionBoxDefaults Remember(BoxVolume box) => new() { SquareSizeMeters = box.Width, WidthMeters = box.Width, DepthMeters = box.Depth, AboveMeters = box.Above, BelowMeters = box.Below, Unit = Unit };
    public SectionBoxDefaults WithUnit(string unit) => new() { SquareSizeMeters = SquareSizeMeters, WidthMeters = WidthMeters, DepthMeters = DepthMeters, AboveMeters = AboveMeters, BelowMeters = BelowMeters, Unit = unit };

    public BoxVolume At(Point3 point) => BoxVolume.Centered(point, Width(), Depth(), AboveMeters, BelowMeters);
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

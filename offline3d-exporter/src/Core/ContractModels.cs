namespace Offline3D;
public sealed class Resource
{
    public string Id {get;set;}=""; public string Name {get;set;}=""; public string Kind {get;set;}="";
    public long Size {get;set;} public string Sha256 {get;set;}="";
}
public sealed class Manifest
{
    public int SchemaVersion {get;set;}=1;
    public string PackageId {get;set;}=Guid.NewGuid().ToString("D");
    public string CoordinateFrame {get;set;}="revit-host-internal-feet";
    public string[] RequiredCapabilities {get;set;}=new[]{"plan-ray-v1","glb-static-v1"};
    public bool Synthetic {get;set;}
    public string Coverage {get;set;}="";
    public string ExporterVersion {get;set;}="";
    public string[] Exclusions {get;set;}=Array.Empty<string>();
    public Resource[] Resources {get;set;}=Array.Empty<Resource>();
}
public sealed class Registration
{
    public int Page {get;set;}=1;
    public double[] ClickPlaneOrigin {get;set;}=Array.Empty<double>();
    public double[] ClickPlaneNormal {get;set;}=Array.Empty<double>();
    public ViewRegion[] ViewRegions {get;set;}=Array.Empty<ViewRegion>();
    public double[] ExportBoundsGraphics {get;set;}=Array.Empty<double>();
    public string ViewId {get;set;}=""; public string LevelId {get;set;}=""; public string DocumentId {get;set;}="";
    public double LevelProjectElevationFeet {get;set;}
    public Matrix4 ModelToPdf {get;set;}=Matrix4.Identity;
    public Matrix4 ModelToSheet {get;set;}=Matrix4.Identity;
    public Matrix4 ModelToGraphics {get;set;}=Matrix4.Graphics(new Point3());
    public double[] CropPdf {get;set;}=Array.Empty<double>();
    public double[] CropSheet {get;set;}=Array.Empty<double>();
    public double[] MediaBox {get;set;}=Array.Empty<double>();
    public double[] PageCropBox {get;set;}=Array.Empty<double>();
    public int Rotation {get;set;}
    public double UserUnit {get;set;}=1;
    public bool PreciseClicks {get;set;}
    public double MaxResidualMetres {get;set;}
    public int FitPointCount {get;set;} public int CheckPointCount {get;set;}
    public CalibrationInput? CalibrationEvidence {get;set;}
    public object? AutomaticRegistration {get;set;}
}
public sealed class ElementIdentity
{
    public string UniqueId {get;set;}=""; public string DocumentId {get;set;}="";
    public string? LinkInstanceId {get;set;}
    public string MeshName {get;set;}="";
    public string Category {get;set;}="";
    public string TypeName {get;set;}="";
    public string MaterialName {get;set;}="";
    public string CutPatternName {get;set;}="";
    public string ElementId {get;set;}="";
    public string FamilyName {get;set;}="";
    public string LevelName {get;set;}="";
    public string Mark {get;set;}="";
    public Dictionary<string,string> Parameters {get;set;}=new Dictionary<string,string>();
}

public sealed class CutPatternDefinition
{
    public string Name {get;set;}="";
    public string Target {get;set;}="";
    public bool Solid {get;set;}
    public CutGridDefinition[] Grids {get;set;}=Array.Empty<CutGridDefinition>();
}
public sealed class CutGridDefinition
{
    public double Angle {get;set;}
    public double OriginXFeet {get;set;}
    public double OriginYFeet {get;set;}
    public double OffsetFeet {get;set;}
    public double ShiftFeet {get;set;}
    public double[] SegmentsFeet {get;set;}=Array.Empty<double>();
}

public sealed class ViewRegion
{
    public string ViewId {get;set;}="";
    public string Name {get;set;}="";
    public string Kind {get;set;}="";
    public Matrix4 ModelToSheet {get;set;}=Matrix4.Identity;
    public Matrix4 ModelToPdf {get;set;}=Matrix4.Identity;
    public double[] CropSheet {get;set;}=Array.Empty<double>();
    public double[] CropPdf {get;set;}=Array.Empty<double>();
    public double[] PlaneOrigin {get;set;}=Array.Empty<double>();
    public double[] PlaneNormal {get;set;}=Array.Empty<double>();
    public double LevelFeet {get;set;}
    public double[] BoundsGraphics {get;set;}=Array.Empty<double>();
}

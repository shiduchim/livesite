namespace Offline3D;

/// <summary>Shared by the standalone Revit exporter and the PDF/viewer contract.</summary>
public static class SceneLimits
{
    public const int MaxSceneBytes = 512 * 1024 * 1024;
    public const int MaxMetadataBytes = 64 * 1024 * 1024;
    public const int MaxTotalBytes = MaxSceneBytes + 2 * MaxMetadataBytes;
    public const int MaxPdfBytes = 1024 * 1024 * 1024;
    public const string LargeSceneCapability = "glb-large-scene-v1";
    public static int ResourceBytes(string kind) => kind == "scene" ? MaxSceneBytes : MaxMetadataBytes;
}

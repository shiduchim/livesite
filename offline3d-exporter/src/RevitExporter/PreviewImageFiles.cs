namespace Offline3D.RevitExporter;

/// <summary>Each request gets a separate folder; never display an older request's image.</summary>
internal static class PreviewImageFiles
{
    public static string Find(string directory)
    {
        var files=Directory.GetFiles(directory).Where(p=>string.Equals(Path.GetExtension(p),".png",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(files.Length!=1)throw new IOException("Revit produced "+files.Length+" PNG images for this preview. Files: "+string.Join(", ",Directory.GetFiles(directory).Select(Path.GetFileName))+". Folder: "+directory);
        if(new FileInfo(files[0]).Length==0)throw new IOException("Revit produced an empty preview image: "+files[0]);
        return files[0];
    }
}

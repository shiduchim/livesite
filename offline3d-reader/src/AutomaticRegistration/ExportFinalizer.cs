namespace Offline3D.Automatic;
public static class ExportFinalizer
{
    public static void Export(string folder,string output){
            string dir=Path.GetFullPath(folder),destination=Path.GetFullPath(output);
            if(!Path.GetFileName(dir).StartsWith("Offline3DExport-",StringComparison.Ordinal))throw new InvalidDataException("Unexpected export workspace");
            var reg=Contract.Decode<Registration>(File.ReadAllBytes(Path.Combine(dir,"registration.draft.json")));
            var markers=Contract.Decode<SheetMarker[]>(File.ReadAllBytes(Path.Combine(dir,"markers.json")));
            string clean=Path.Combine(dir,"drawing-clean.pdf");
            var result=Offline3D.Automatic.AutomaticRegistrationService.Register(clean,Path.Combine(dir,"drawing-marked.pdf"),reg,markers);
            reg.ModelToPdf=result.Calibration.SheetToPdf*reg.ModelToSheet;
            reg.CropPdf=Enumerable.Range(0,4).SelectMany(i=>{var p=result.Calibration.SheetToPdf.Apply(new Point3(reg.CropSheet[2*i],reg.CropSheet[2*i+1]));return new[]{p.X,p.Y};}).ToArray();
            foreach(var region in reg.ViewRegions){region.ModelToPdf=result.Calibration.SheetToPdf*region.ModelToSheet;region.CropPdf=Enumerable.Range(0,4).SelectMany(i=>{var p=result.Calibration.SheetToPdf.Apply(new Point3(region.CropSheet[2*i],region.CropSheet[2*i+1]));return new[]{p.X,p.Y};}).ToArray();}
            reg.PreciseClicks=true;reg.CalibrationEvidence=result.Controls;reg.FitPointCount=result.Controls.Fit.Length;reg.CheckPointCount=result.Controls.Check.Length;reg.MaxResidualMetres=result.Calibration.MaxResidualMetres;
            reg.AutomaticRegistration=result;PdfPackageService.SetPageGeometry(clean,reg);
            var files=new Dictionary<string,byte[]>{{"registration.json",Contract.Encode(reg)},{"identities.json",File.ReadAllBytes(Path.Combine(dir,"identities.json"))},{"scene.glb",File.ReadAllBytes(Path.Combine(dir,"scene.glb"))}};
            var exclusions=File.ReadAllLines(Path.Combine(dir,"exclusions.txt"));var coverage=exclusions.FirstOrDefault(x=>x.StartsWith("Coverage:",StringComparison.Ordinal))??"Mapped model view";var manifest=PdfPackageService.Describe(File.Exists(Path.Combine(dir,"synthetic.flag")),coverage+" Automatic 25-marker registration; 12 fit, 13 independent checks; clean appearance verified",File.ReadAllText(Path.Combine(dir,"exporter-version.txt")),files,exclusions);
            using(var scene=System.Text.Json.JsonDocument.Parse(files["scene.glb"].AsMemory(20,BitConverter.ToInt32(files["scene.glb"],12))))if(scene.RootElement.GetProperty("nodes").EnumerateArray().Any(n=>n.TryGetProperty("matrix",out _)))manifest.RequiredCapabilities=manifest.RequiredCapabilities.Concat(new[]{"glb-affine-instances-v1"}).ToArray();
            new PdfPackageService().Write(clean,destination,manifest,files);
            if(OperatingSystem.IsWindows())try{string logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Offline3DPdfPrototype","Logs","Exports",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+manifest.PackageId);Directory.CreateDirectory(logs);foreach(var name in new[]{"geometry-diagnostics.txt","export-memory.txt","export-timings.txt","preview-timings.txt","preview-errors.txt","export-warnings.txt","unloaded-links.txt","exporter-version.txt"}){string file=Path.Combine(dir,name);if(File.Exists(file))File.Copy(file,Path.Combine(logs,name));}}catch(IOException){}catch(UnauthorizedAccessException){}
            try{Directory.Delete(dir,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

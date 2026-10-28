# Current v0.5.2 exporter / reader boundary

This is a source handoff, not a runtime separation or installer change. Both source ZIPs include matching shared libraries so each project graph is complete. Keep those copies synchronized if a contract change is ever authorized.

## Workflow

1. Revit ribbon: `Offline3D.RevitExporter.RibbonApplication` -> `ExportCommand`.
2. Plugin creates an `Offline3DExport-*` working folder. Core inputs to the helper are `registration.draft.json`, `markers.json`, `drawing-clean.pdf`, `drawing-marked.pdf`, `scene.glb`, `identities.json`, `exclusions.txt` and `exporter-version.txt`. Diagnostic files may also exist.
3. Plugin resolves the current installed release from its assembly location and starts the sibling executable:
   `Viewer/Offline3D.FastPDF.Dev.exe --auto-export "<draft folder>" "<destination.pdf>"`.
4. `fastpdf/src/EKFastPDF/Program.cs` dispatches `src/AutomaticRegistration/ExportFinalizer.cs`. The helper uses native PDFium and the clean/marked PDF pair to calibrate sheet/PDF coordinates, validates the mapping and packages the PDF using `src/Packaging/PdfPackage.cs`.
5. The PDF includes the manifest plus `registration.json`, `identities.json`, and `scene.glb`. The reader validates these before enabling 3D clicks. The source drawing remains a normal 2D PDF page.

The plugin launching the helper successfully does not itself prove final PDF creation succeeded. Preserve error propagation and diagnose the finalizer separately where relevant.

## Source boundaries

- Revit executable code: `src/RevitExporter/`.
- Ten pure data/geometry/writer files from `src/Core/` are explicitly linked by the Revit csproj. `ExportWireJson.cs` supplies the standalone serialization path.
- Full `src/Core/`, `src/Packaging/`, `src/AutomaticRegistration/` are included as shared boundary/test context. They compile separately for the reader/helper; Core.dll and System.Text.Json are not deployed into Revit.
- Active reader entry: `fastpdf/src/EKFastPDF/EKFastPDF.csproj`, plus `EKFastPDF.Core` and `src/Viewer/web/`. The older standalone Viewer C#/XAML project and legacy FastPDF v1.1 are excluded.
- The exporter archive excludes reader UI/rendering source. The reader archive excludes Revit plugin source.
- Existing combined installation scripts are preserved under `reference-only/combined-installation/` in the exporter ZIP. They require the full application tree and are reference material, not runnable split installers.

## Geometry and integrity

Preserve units and transforms as defined by `ContractModels.cs`, `Geometry.cs`, `ExportTarget.cs`, `ExportRegions.cs` and the strict `Contract.cs` validator. Revit internal coordinates use feet. Do not silently switch GLB coordinates/axis conventions or apply a placement twice.

The current reader accepts ordinary shared glTF meshes with flat node transforms, float position/normal attributes, uint32 triangle indices, material factors and feature-edge metadata. The strict validator rejects glTF extensions, textures/images/external URIs, animations, skins and nontriangle mesh primitives. Do not introduce meshopt, Draco, KHR_mesh_quantization or EXT_mesh_gpu_instancing in an exporter-only patch.

Current `SceneLimits.cs` policy: 512 MiB scene; 64 MiB per metadata resource; 640 MiB combined resources; 1 GiB PDF. Separate node/accessor/element safeguards also exist. Large resources require `glb-large-scene-v1`; affine node placements require `glb-affine-instances-v1`. Preserve schemas, integrity hashes and capability checks. Raising a safety limit is not a solution to excessive export work.

Old original `docs/CONTRACT.md` describes earlier limits and is not included as the current specification. Use the shared validator and this handoff.

## Build compatibility

SDK pin: .NET 10.0.100, rollForward latestFeature. Existing baseline variants:

- Revit 2024: net48.
- Revit 2025 / 2026: net8.0-windows and additional net10.0-windows host variants.
- Revit 2027: net10.0-windows.

The csproj uses pinned Nice3point Revit API reference NuGets, compile/build assets only. `RevitApiDir` can select the licensed installed Revit API for a local build. No Autodesk runtime DLLs are redistributed. Inspect actual installed runtime/update when selecting the variant; see `reference-only/combined-installation/scripts/Setup.Library.ps1`.

Reference NuGets and Microsoft targeting packages are build dependencies. Windows/Revit execution is a separate native validation step.

# Revit exporter 0.5.3

0.5.3 rewrites the parts of the v0.5.2 exporter that caused failures and slowness. Everything was cross-built and tested on Linux. **Nothing has been run inside Revit yet**; see [ACCEPTANCE-TEST.md](ACCEPTANCE-TEST.md).

Unchanged:
- the PDF reader;
- the viewer helper command (`--auto-export <draft> <pdf>`);
- the draft file names and JSON schemas;
- the GLB contract;
- the six build variants.

## Diagnosis

### Defects confirmed in the v0.5.2 source

| # | Defect | Evidence | Effect |
|---|---|---|---|
| 1 | `MeshContext.OnLinkEnd` compared `LinkNode.GetDocument()` with the stacked document using `==`. | Revit's `Document` has no `==` operator, so this is a reference comparison. The API overrides `Equals` and documents that two instances can represent the same document (checked in the RevitAPI 2024–2027 reference assemblies). | When Revit returns a new wrapper, the link's document, transform and key are never popped. Every host element after the first link is then looked up in the linked document and placed with the link transform, so it is missing or misplaced. |
| 2 | Each repeated family instance was fully tessellated by Revit. The exporter then re-indexed it, SHA-256-hashed it and compared it byte for byte from disk. | Source. In the simulation, 3,000 sockets requested 72,000 facets from Revit; only 1,440 were needed. | Sharing saved file size but no time. This fits the 109 M placed / 64,512 unique triangle screenshot. |
| 3 | Feature-edge building used `Dictionary<ulong>` with .NET's default hash for an edge key (`min ^ max`). | Same 90-mesh, 80 MB input: **64.3 s in the v0.5.2 writer, 1.9 s in 0.5.3**, with an identical binary chunk. | Quadratic time on every large unique mesh (slabs, topography, imports, long runs), with Revit blocked. The writer also re-encoded and SHA-256-hashed the material JSON for every placement. |
| 4 | Hard stops part-way through or at the end of a long export. | Source. | Three failure points:<br>• "One element's indexed geometry exceeds 64 MiB" (one large element).<br>• "exceeds 100,000 geometry placements", raised during collection.<br>• No check of the reader's limit of 300,000 accessors / 100,000 meshes, so a too-large export failed only in the helper, after the whole export. |
| 5 | Parameter values were cut at 2,048 UTF-16 units. | Test. | The cut could split a surrogate pair (emoji or some CJK text). The wire JSON writer then rejected it at the very end ("Unpaired Unicode surrogate"). |
| 6 | Preview: the options dialog closed, a separate progress window appeared, and the dialog reopened. | Source and your feedback. | The "unattractive popup". |
| 7 | Options panel: fixed 306 px widths in an auto-scrolling `FlowLayoutPanel`. | Source and your screenshot. | Horizontal scrollbar, clipped labels, long scrolling. |

### Hypotheses that only Revit can settle

- **The 109 M placed / 110 elements screenshot.** It fits one element that contains very many repeated instance nodes (an imported CAD file with blocks, a railing or a nested array), defect 1, or both. 0.5.3 logs the slowest and largest elements and writes a heartbeat with the current element every 5 s, so the next trace will name it.
- **Whether CustomExporter cuts geometry at the section box.** Unknown. 0.5.3 widens the native section box by 1 ft and still applies the exact crop to every triangle. If Revit does cut, the cut and any cap faces therefore land outside the crop. It also logs `Section-box evidence`, the points found exactly on the box faces.
- **Whether two instances with the same `SymbolGeometryId` can render differently.** By the API's documentation they cannot. 0.5.3 still verifies every key against one fully traversed instance before reusing it. It turns reuse off whenever source view filters are copied, which happens when "Include categories hidden in the view" is unticked. `OFFLINE3D_DISABLE_INSTANCE_REUSE=1` turns it off completely for comparison.
- **Non-rectangular or split crops** still stop with a clear message, as in v0.5.2. Arbitrary crop polygons are not claimed.

## What changed

### Geometry collection (rewritten)
- `Collection/GeometryStream.cs` (new) has no Revit types; all the bookkeeping lives here.
  - Begin/end stacks are balanced with explicit markers; Revit's rule that a skipped node still gets its end call is honoured.
  - Element crop culling uses the oriented crop.
  - Triangle selection is per crop volume. Crossing triangles are kept whole.
  - **Instance reuse** works through `InstanceNode.GetSymbolGeometryId()`, available in every supported year:
    1. The first complete instance is captured, including nested parts and mirrored placements.
    2. One more instance is traversed and must match it.
    3. After that, an instance fully inside the crop is placed from the capture without Revit tessellating it, one fully outside is skipped, and one crossing the crop is traversed normally.
  - Oversized batches are written as several placements.
- `RevitGeometryContext.cs` (replaces `MeshContext.cs`) is a thin `IExportContext` adapter that forwards Revit's callbacks to the stream.
- `Collection/MeshBuilder.cs` is bit-identical to the v0.5.2 `IndexedMeshAccumulator` output, without per-vertex allocations.
- `Collection/FeatureEdgeBuilder.cs` and `Collection/KeyHashing.cs` give the same edges in the same order as `Core/FeatureEdges.cs`, with mixed hashing.
- `Collection/SceneWriter.cs` (replaces the linked `Core/SpoolSceneWriter.cs` inside Revit):
  - With merging off, its output equals v0.5.2 (same JSON, byte-identical binary).
  - An element's single-use parts that share a material are merged into one mesh. Selection, identities and feature edges are kept.
  - Reader limits are checked early, with actionable messages.
  - Fast FNV hashing; every hash match is confirmed byte for byte.
- `Collection/ExportText.cs` cleans model text: safe cuts, and unpaired surrogates become U+FFFD.
- `Collection/ExportStatistics.cs` and `ExportTrace.cs` write a live trace to `%LOCALAPPDATA%\Offline3DPdfPrototype\Logs\Exports\…`, which survives success, cancel, failure and crashes. They log callback counts, reuse, culling, section-box evidence, the slowest and largest elements, stage times and memory.

### Export flow and UI
- `ExportCommand.cs` is rewritten as readable steps.
  - Kept from v0.5.2: region and registration math, temporary view, visibility copy, preview rendering, PDF pages, markers, draft files, helper launch and rollback.
  - Changed: a 1 ft native section-box margin, link instances no longer culled by their own bounding box, trace logging, the cancelled work folder removed, and setup errors that show the log folder.
- `ExportOptionsDialog.cs` replaces `ExportPreviewDialog.cs`.
  - Compact sections, sized from the screen's DPI (no mixing of WinForms auto-scaling with fixed sizes).
  - One column when narrow. Vertical scrolling only.
  - Quick export / Export… / Cancel are always visible.
  - The preview renders **inside the dialog** with a loading state. A stale-preview bar offers "Update preview". A failure message stays in the dialog.
  - The Revit API is still called only from the command's own loop, never from a WinForms event handler or another thread. The window is non-modal with Revit disabled, the same pattern as the progress window.
- `ExportProgressDialog.cs` is centred on Revit and shows the current element and the reuse count. Closing it means Cancel.
- `NativeWindows.cs` (new) holds the Win32 helpers.
- `RibbonApplication.cs`: the panel shows the version, and the tooltip lists the supported views.
- `RevitExporter.csproj`: version 0.5.3. It no longer links `Core/SpoolSceneWriter.cs` or `Core/IndexedGeometry.cs`. Still no Core.dll or JSON package inside Revit.

### Tests and tooling (new)
- `tests/ExporterCoreTests` (23 checks): simulated Revit traversal, covering:
  - links;
  - reuse on vs off giving identical world geometry;
  - culling, crossing and verification failure;
  - rotated crops;
  - batch splitting;
  - writer equivalence with v0.5.2;
  - merging;
  - limits;
  - text cleaning;
  - edge-builder performance;
  - helper-path packaging and a 76 MiB package round trip.
- `tests/ExporterAssemblyAudit` (150 checks): metadata of the six DLLs.
- `tests/DialogHarness`: opens the real dialogs and takes screenshots. Run under Wine at 96, 144 and 192 DPI; see `evidence/v053-dialog-*.png`.
- `build/build-exporters.sh` and `build/package-update.sh`: the six variants and the drop-in update ZIP.
- `build/update-package/`: `UPDATE.cmd`, `ROLLBACK.cmd` and `Update-Offline3DExporter.ps1`. They replace only the DLL each Revit add-in manifest points to, after a checksum check, and keep a backup.

## Measurements (synthetic, Linux; not Revit timings)

| Measurement | v0.5.2 | 0.5.3 |
|---|---|---|
| Write 90 distinct meshes (79.7 MB GLB) | 64.3 s | 1.9 s, byte-identical binary chunk |
| 3,000 repeated sockets: facets Revit must tessellate | 72,000 | 1,440 |
| 3,000 sockets: managed collection time | 336 ms (full traversal) | 186 ms |
| Feature edges, 320,000-triangle grid | – | 0.5 s |
| 80 MB scene → PDF package → reopen | – | 4.2 s, byte-identical |

Real export times depend on Revit's tessellation of the model. Measure them with ACCEPTANCE-TEST.md.

## Test results

| Suite | Result |
|---|---|
| New core checks | 23/23 |
| Assembly audit | 150/150 |
| Original v0.5.2 suites (crop, scope, large scene, wire interop, wire writer, link overrides, large resource, selection) | 119/119, unchanged |
| Dialog layout under Wine at 96, 144 and 192 DPI | No horizontal scrollbar, no buttons outside the window, in every state |

## Compatibility notes

- **Same files and entry points.** DLL names, add-in entry points and the install layout are unchanged. The update ZIP replaces the DLL in place.
- **Merged nodes.** Single-use parts are merged, so the GLB has fewer nodes. Each node keeps the element's key prefix, which the reader uses for selection, hiding and properties.
- **Reuse needs Revit 2023+.** `SymbolGeometryId` exists in every supported version (2024–2027). If a call fails, that instance is traversed normally.

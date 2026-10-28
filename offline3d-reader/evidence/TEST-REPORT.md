# Validation report — v0.5.2 experimental, 26 October 2026

**No Windows/Revit runtime acceptance for v0.5.2 was performed here.** Your v0.5.0 Revit 2026 log reached 12,235 geometry batches in 28.7 seconds before the 64 MiB unique-geometry guard. Its requested-preview stage took 9.3 seconds, but the native error/image file was not supplied; a successful displayed preview cannot be inferred from the stage timing.

## New v0.5.2 verification

- **16 crop/linked/height/Quick checks**, `tests/crop-export-v052-results.txt`: rotated crops/links, XY and Z rejection, unchanged crossing triangles, union of crops without filling gaps, independent all-up/down, explicit outside-crop retaining the chosen height range, 5,000 random triangle/box comparisons against an independent clipping oracle, 10,000 off-crop linked parts excluded, and a valid colored Quick GLB without feature-edge payload. This is pure geometry on Linux, not native callback testing.
- **55 core/native Linux PDFium/registration checks** and **18 shared geometry/defaults checks** pass; `tests/core-v052-results.txt`, `tests/large-scene-v052-results.txt`.
- Quick fixture passes Khronos validation, `tests/khronos-quick-v052-results.json`.
- Six standalone exporter cross-builds and self-contained Windows viewer publish, 75 PE audits and current payload hashes are checked for v0.5.2. Source checks assert crop-only/Medium defaults and confirmation wiring. They do not execute Windows controls.
- **37 existing FastPDF checks passed**, `tests/fastpdf-v052-results.txt`. A fresh browser also checks Medium in both the visible quality control and renderer, `tests/viewer-medium-default-v052-results.txt`.
- Viewer navigation/settings regressions are rerun after the version/default-quality update; `tests/viewer-v052-regression-results.json`.

**Unverified:** Revit/Windows scope filtering callbacks and linked nesting, actual time/file-size improvement, all-up/down model bounds, confirmation/dialog behaviour, source rollback, and the revised native preview workflow. Native calls can still block until they return. Quick represents the source's Coarse geometry; no timing or pixel-identical High/Ultra rendering guarantee is claimed. Your 109-million-triangle screenshot proves the previous run traversed excessive geometry; it does not measure this revision.

## Retained v0.5.1 verification

- **12 resource/preview-file checks:** the actual standalone serializer/spool code writes **79,733,700 bytes (76.0 MiB)** of distinct geometry: 90 unique meshes and **737,280 unique triangles**. Full strict validation, PDF packaging/reopening, explicit large-scene capability, independent metadata/scene limits, exact-size compressed-attachment decoding and destination preservation pass. Missing, empty and ambiguous preview files fail explicitly; native filenames/upper-case extensions are found. `tests/large-resource-v051-results.txt`. Synthetic Linux run: about 23.5 seconds, peak working set about 426 MiB. This is not a Revit export benchmark.
- **Khronos large unique fixture:** 0 errors, 0 warnings; `tests/khronos-large-resource-v051-results.json`. Hosted CI is configured, not represented as executed.
- **22 large-binary browser checks, 11 per engine:** the real 76 MiB GLB is fetched once from a local binary HTTP endpoint with a host descriptor below 300 bytes. All 90 unique meshes and 737,280 triangles remain; a small section activates 8,192 triangles. Edges remain unexpanded while disabled; enabling them expands one unique mesh, moving the box selects the next placement and expands only that visited mesh. Non-local scene URLs are rejected. `tests/viewer-large-resource-v051-results.json`. This uses Linux Chromium/SwiftShader and a loopback server, not native Windows WebView2 interception.
- Core/native Linux PDFium/registration checks: **55 passed**, `tests/core-v051-results.txt`. Existing FastPDF suite: **37 passed**, `tests/fastpdf-v051-results.txt`.
- All six exporter cross-builds, self-contained Windows viewer publish, **75 PE metadata audit checks**, and source/payload freshness checks pass. `tests/ExporterAssemblyAudit-v051-results.txt`, `tests/payload-v051-verification.txt`.
- The **62 control/defaults regressions** and **20 repeated-scene browser checks** were rerun after binary delivery/lazy-edge changes. `tests/viewer-v051-regression-results.json`, `tests/viewer-shared-scene-v051-results.json`.

Windows WebView2 resource interception, native preview rendering/display, full 512 MiB capacity on physical hardware and the actual project retry remain untested. Preview tests above exercise file selection/error handling, not Revit ExportImage or WinForms painting.

## Retained v0.5 verification

- **18 geometry/defaults/validation checks:** rotated copies share, world-space positions and winding agree with legacy transforms at 0/90/37 degrees and large host coordinates, mirrors are distinct and correct, changed geometry/materials stay distinct, forced hash collisions require byte equality, running bounds, spool cleanup on success/failure, placed counts exceed the old cap, malformed affine transforms/metadata references are rejected, stock bounds reset correctly, preferences survive save/reopen and corrupt/invalid preferences recover factory values. Evidence: `tests/LargeSceneTests-v05-results.txt`.
- **Synthetic large scene:** 2,000 placements × 1,024 triangles = **2,048,000 placed triangles**, stored as **one unique mesh / 1,024 unique triangles**. The GLB is approximately 1 MiB. The C# fixture run took about 0.5 seconds with about 75 MiB peak process working set on Linux. This reuses synthetic input arrays and does not measure Revit tessellation, actual project export or graphics-card speed.
- **Khronos validation:** local large fixture has **0 errors, 0 warnings**. Informational messages concern the custom edge accessor and degenerate pole triangles in the synthetic sphere. `tests/khronos-v05-results.json`. Hosted CI is configured, not represented as executed.
- **20 large-scene browser checks, 10 per engine:** actual bundled Babylon/Three load 2,000 transformed placements with one unique GPU geometry. A small box draws 1,024 active triangles, generates six caps, and visits only 6,144 crossing-candidate triangles. Moving the box retains loaded geometry and selects the correct next placement. No whole-project expanded triangle arrays or old comparison worker. `tests/viewer-large-scene-results.json`.
- **62 control/defaults regressions (28 per engine plus 6 preferences checks):** picking/properties, hide/isolate/X-ray, Home/focus, selected-object orbit, clear selection, middle pan, cursor zoom, large box, paired grips, transparent grab area, no-jump axis drag, camera stability through host echo, Top-view grip visibility, fixed thin outline, collapsed overlays preserving canvas size, first-drag and close/wide pixel-based pan, stronger wheel zoom, orientation-preserving stock-box fit, factory feet values, metres conversion without repeated-toggle drift, host preference acknowledgement, changed sizes and invalid-input rejection. `tests/viewer-v05-regression-results.json`.

Browser tests run actual engines offline under Linux Chromium/SwiftShader, with a simulated native bridge. They do not establish Windows input/DPI behaviour or physical GPU performance.

## Build, PDF and installation gates

| Gate | Evidence |
| --- | --- |
| Core / native Linux PDFium / registration / package checks | 55 passed; `tests/core-v05-results.txt` |
| Existing FastPDF suite | 37 passed; `tests/fastpdf-v05-results.txt` |
| Standalone output writer | 22 checks; `tests/ExportWireWriterTests-v05-results.txt` |
| Exporter/viewer wire interoperability | 13 checks; `tests/ExportWireInterop-v05-results.txt` |
| Exporter PE metadata | 75 checks across six DLLs, including embedded indexed/spool types, correct API references, no Core/JSON dependency or Autodesk redistribution; `tests/ExporterAssemblyAudit-v05-results.txt` |
| Setup helpers | 30 checks on PowerShell 7/Linux; `tests/setup-v05-results.txt`. No Windows registry/COM installation pass. |
| Section contour arithmetic | 9 passed; `tests/sections-v05-results.txt` |
| Exporter cross-builds | Six succeed: 2024/net48, 2025/net8+net10, 2026/net8+net10, 2027/net10; `tests/revit-*-build.txt` |
| Self-contained Windows x64 viewer | Cross-publish succeeds; `tests/viewer-publish.txt` |
| Payload freshness/integrity | All listed files and exact current web assets checked; `tests/payload-v05-verification.txt` |

Earlier adjacent-level/crop and optional link-override tests are retained in their versioned evidence files. Source region mathematics and the fallback policy are unchanged. The native PDF/registration tests use synthetic pages, 25 markers (12 fit, 13 independent checks), altered/missing control rejection, appearance comparison and destination preservation. They are not real Revit exports.

## Implementation and test boundary

The exporter indexes local geometry before storage, verifies hash matches by actual spooled bytes, retains transforms and uses mirror prototypes with corrected winding. Metadata is stored once per element with node references. Bounds accumulate from eight transformed local-box corners. It no longer retains all expanded model triangles or creates a second complete transformed vertex array. Revit still visits placement geometry: no family/type-only skip cache is used, because parameters/cuts/joins/materials can change it.

Native preview rendering is optional/cached. Options open before the full scan/image render. Temporary preview views hide datums, fit model-only host/loaded-link bounds and normalize bitmap DPI. Actual ExportImage time, image/control sizes and DPI are logged separately from dialog time. Source explicit-hiding scans are cached. All model changes remain inside temporary transaction groups. **Native image framing, linked visibility, cancellation, rollback and source preservation remain untested in Revit.**

Babylon retains shared buffers and can hardware-instance standard shared-mesh nodes. Three retains shared buffers with ordinary mesh placements. Both use GPU clip planes and lazy contour workers; Three no longer re-encodes full clipped GLBs. Off-box placements are disabled and picking avoids whole-scene expanded geometry. Cap correctness on real open/nonmanifold or compound-wall geometry still needs acceptance.

The supplied current FastPDF remains the PDF renderer; tiles/cache/printing are unchanged by this release. Ordinary FastPDF and ClickTo3D remain separate installations/settings identities with no PDF association change. Native Windows startup, installation/update/uninstall, cursor/DPI, undocking and 2D/3D contention remain checks on your hardware.

## Limits and deferred work

The old 1.5-million placed-triangle cutoff and 64 MiB whole-scene limit are replaced. Current bounds: **512 MiB per GLB, 64 MiB per metadata resource, 640 MiB total resources, 1 GiB PDF, 100,000 placements/element identities and bounded accessors**. Unique geometry plus scene metadata can still exceed these limits; output fails explicitly, without silently removing parts or lowering detail. A very complex individual element can still consume substantial temporary memory.

Quality presets allow **8/16/32/64 million active drawing triangles**; exceeding a budget pauses 3D with a message while preserving the PDF/model. Caps separately report a limit at **4 million candidate triangles visited per section update**. These are finite work guards, not measured GPU capacities. There is no universal 60 fps or 5–15 MB result.

Not included: meshopt, KHR quantisation, EXT_mesh_gpu_instancing, multi-GLB resource splitting, material textures/UV maps, procedural appearances, exact Autodesk shaders, native concrete/compound cut hatches or general model streaming. Local float32 coordinates use micrometre rounding; this is not a global precision guarantee on distant coordinates/GPU hardware. Closed contours are required for caps. Plan crops remain rectangular/unsplit.

The new large-scene capability and existing affine-instance capability are declared in exported package manifests; older prototype readers can reject those 3D resources. Ordinary PDF readers can still view/print the drawing. The v0.5.2 viewer retains compatibility with older prototype GLBs.

INSTALL.cmd uses included prebuilt files. BUILD_AND_INSTALL.cmd is an optional automatic Windows fallback. Microsoft WebView2 is reused or downloaded with consent if absent; its complete browser runtime is not bundled. Native Windows/Revit acceptance remains required using START-WINDOWS.md, including 20–50 independent plan-click references.

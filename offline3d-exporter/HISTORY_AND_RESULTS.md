# Exporter history and evidence — v0.5.2 handoff

This history combines user-reported native Windows/Revit failures with completed automated checks. It is not a claim that later changes fixed those failures on the real project. The current source is v0.5.2. During this handoff the user confirmed that preview generation now works; full native acceptance of export correctness and performance is still outstanding. The reader is considered a working reference based on the user's latest feedback.

## Goal and decisions

The product is a single normal PDF with embedded GLB, identities and PDF/model registration. The separate FastPDF-based reader turns a plan click into an orthographic 3D section. It keeps a white background, original model colors, section handles on the drawing and model, object selection, a ViewCube/Home, undocking and GPU rendering. The user's regular FastPDF and ClickTo3D applications must remain separate.

DWF was discussed and cancelled. Extra rendering engines were explored; Babylon.js/WebGL2 and Three.js remain the chosen pair. Walk mode was cancelled. “Astra Ultra” meant the AI working on the project, not the stock export quality. Medium is stock. Quick/Low/High/Ultra remain options.

The user initially requested entire-model export by default. After very large/slow exports, that requirement was explicitly replaced by crop-only export, independent All up/All down height options, and a confirmed opt-in to geometry outside the crop.

## What happened and what changed

| Stage / observation | Work attempted | Result / limits of evidence |
|---|---|---|
| Early sheet export threw “Select exactly one viewport on the active sheet”; ribbon disabled in plan views | Automatic supported-viewport choice and a temporary sheet workflow for standalone plans | Source and automated selection coverage exist. Real acceptance must still be checked across source views. |
| Revit could not load System.Text.Json 8.0.0.0 | Removed runtime Core/JSON dependencies from the Revit DLL; linked pure data/geometry source and a standalone wire writer | Six exporter variants cross-build; 75 PE/reference audit checks pass. This is stronger than assuming deployment will find a JSON DLL. |
| v0.4.0: GetLinkOverrides threw “The view does not support link graphical overrides” | Guarded transfer/fallback for unsupported API cases | Actual later runs proceeded past this stage and logged By Host View warnings. Exact appearance parity for custom overrides is not proven. |
| v0.4.1: stopped at 1.5 million triangles | Original cap counted placed triangles, including repeats | User log: 54,600 scanned items / 9.444 s; lightweight preview / 43.158 s; collection / 9.480 s at 1,499,136 triangles. This was not a successful export. |
| v0.5.0: introduced indexed local meshes, exact-content sharing, node transforms, mirror handling, streaming spool and cached bounds | Avoid storing repeated meshes as separate world-space geometry | Synthetic two-million-placed-triangle fixture succeeds using one stored mesh. 18 geometry/default checks pass. This does not prove Revit avoids retessellating all repeated instances; storing once and traversing once are different optimizations. |
| v0.5.0: “Unique geometry exceeds the 64 MiB scene budget” | Deduplication exposed a separate unique-data limit | User log: 56,200 items scanned in 1.259 s, requested preview 9.308 s, collecting shared geometry 28.677 s / 12,235 items before failure. The source still had an entire-model default at this stage. |
| v0.5.1: raised the coordinated scene limit and revised large-data transfer | 512 MiB scene, 64 MiB each metadata resource, 640 MiB package resources, 1 GiB PDF; binary WebView2 resource fetch instead of base64; lazy feature edges | Actual 79,733,700-byte synthetic GLB with 737,280 unique triangles validates, packages and reopens exactly. 12 large-resource/preview-file checks; Khronos validation zero errors/warnings. 22 browser checks load the large fixture with both engines. This proves the pipeline accepts larger synthetic data, not that export speed is good. |
| Repeated preview failures | Added explicit file discovery/error handling, diagnostics and image checks | File-level tests pass, but the user subsequently still reported no preview. No native preview success claim is justified. |
| Later screenshots: 110 processed elements; 40,842,217 placed / 64,512 unique at 1:23, then 109,519,154 placed / 64,512 unique at 4:05 | Investigated whole-model scope and pending geometry nested under parents/links | Symptoms may involve scope, traversal/emission, batching or legitimate repetition. No callback trace from the real model has established the cause. A low stored unique count alone does not establish low CPU or placement memory cost. |
| v0.5.2: crop/export throughput changes | Crop-only default; adjacent-level default height; All up/down affects height only; outside-crop warning/default No; native section box plus crop-local transformed mesh/triangle rejection; flush at instance/link completion; caches for material/identity reads | 16 geometry/scope/Quick checks pass, including 5,000 randomized independent intersection comparisons and rejecting 10,000 off-crop linked parts. Native callback behavior, speed and scope remain untested. |
| v0.5.2: preview and settings changes | Optional preview request returns from the modal dialog before native image export and reopens afterward; image layout/DPI handling; Medium default; Quick shortcut with Coarse/LOD1 and no feature-edge payload | Cross-build succeeds and Quick GLB validates with zero Khronos errors/warnings. User now reports the preview works. Its build popup looks poor and the options layout scrolls excessively; centering, speed and general native acceptance remain unverified. |

## Latest user feedback (supersedes the earlier missing-preview symptom)

The user now reports: **preview works**, but it opens an unattractive popup while building it. The latest screenshot also shows an excessive scroll area in the options panel: a long vertical scroll range, a horizontal scrollbar at the bottom, clipped option text, and large empty areas. The screenshot shows the pre-generation placeholder; successful generation is established by the user's separate statement, not by that image.

The new request is to preserve working generation while improving the exporter UI: an integrated loading/progress state when API-safe, compact sections, responsive wrapping, no horizontal scrollbar, and always-visible Quick export / Export / Cancel. Revit UI/API context and cancellation must remain safe. This is feedback for Claude to implement; the handoff itself does not change application code.

## Current source caveats to investigate

- Current crop representation is an oriented rectangular box. Irregular/split crop shapes need review; do not call them supported without proof.
- Crossing triangles are retained without managed triangle splitting. Their vertices can extend beyond the crop. Viewer clipping creates the visible section; investigate what Revit itself emits when its temporary section box is active.
- The reader has a strict GLB contract. Ordinary mesh sharing is compatible. Compression/quantization/GPU-instancing extensions and image textures are not currently accepted.
- The preview image is a native Revit snapshot, not a render of the final GLB. A successful snapshot does not verify exported geometry, cuts or final materials.
- Preview size/count estimates are approximate, not measured output promises. Actual final bytes/triangles should be reported separately.
- Source-hidden category inclusion does not mean “export all space.” Spatial scope and visibility are independent.
- Custom link graphics may fall back to By Host View with warnings. Audit actual nested link callback/document/transform behavior.
- The exported Revit DLL delegates final PDF registration and embedding to the existing viewer executable. A source split does not remove that runtime dependency.
- A strong GPU helps the reader. It does not by itself solve excessive Revit callback work, tessellation, serialization or duplication.

## Completed automated evidence in the baseline

The evidence directory contains original result files, not new results from Claude:

- 16 crop/linked/height/Quick checks (Linux pure geometry).
- 18 indexed/spooled sharing/default checks, including mirror handling and forced hash collisions.
- 55 Core/PDF/registration checks; 37 FastPDF tests.
- 62 actual-engine browser control/default regressions; fresh Medium UI/runtime default check.
- 75 assembly/reference audit checks covering six exporter variants.
- Six Revit cross-builds and self-contained Windows viewer publish.
- Retained large-resource checks and Khronos reports described above.

Cross-build and browser checks are useful evidence, but cannot replace Revit.

## Required real Windows/Revit acceptance

1. Use a small, known cropped plan first, then the user's large linked engineering model. Test a sheet with one viewport and an unplaced plan. Check that original views/document settings are unchanged after success, cancel and failure.
2. Measure stages, actual file bytes, unique/placed triangles, placements, retained/skipped objects and peak memory for Quick, Low, Medium and High. Repeat under comparable scope and cache conditions.
3. Put known objects inside/outside a rotated crop, and above/below the height range. Include repeated, mirrored and transformed linked instances. All up/down must never enlarge horizontal scope; outside-crop requires confirmation.
4. Verify preview appears on the actual monitor/DPI, stays centered, contains no level/grid graphics, and updates when scope changes. Failure must show the useful reason.
5. Open each resulting PDF in the unchanged reader. Check geometry, colors, identities and placement against Revit. Ordinary PDF rendering/printing must still work.
6. Check independent drawing/model reference points at several PDF zooms and viewport rotations. Automatic registration residuals alone are not an independent real-model click-accuracy test.
7. Verify cancel/rollback and unsupported link warnings. Do not claim native calls are instantly interruptible.

No real RVT model, native debug trace, or complete draft output from the failing export was supplied for this handoff. The reported logs and screenshots are summarized above; do not invent unseen trace results.

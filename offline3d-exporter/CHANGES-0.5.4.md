# Revit exporter 0.5.4 (in progress)

Built in stages from the feedback on 0.5.3. Version strings still say 0.5.3 until the 0.5.4 installer is built. Everything is cross-built for the six variants and tested on Linux. **Nothing here has run inside Revit yet.**

## Stage 2, part 1 (done)

### Phase filter
- The options have a **Phase filter**. It starts from the plan view's filter, which often comes from its view template. The filter you pick is remembered for the next export, in `%LOCALAPPDATA%\Offline3DPdfPrototype\export-options.txt`.
- The phase itself is always the plan view's own (Properties › Phasing › Phase). It is not offered, because view templates do not control it and the exported view already has the right one.
- Pick **Show Complete** to leave out demolished elements. "Show All" makes Revit report them with the red *Phase - Demo* material, which is what appeared in the PDF.
- The preview uses the same filter.

### Very low detail
- **Very low** replaces Quick. It uses:
  - Revit's lowest tessellation (level 0) and Coarse detail;
  - no precomputed edges;
  - skipping of parts under 2". This happens before Revit tessellates them, so it saves Revit time as well as file size.
- The Quick export button uses Very low.

### Overall progress, time left, size
- The progress window shows one bar for the **whole** export. Each step's share reflects how long it usually takes; collecting geometry is about 80 %.
- **Geometry progress is counted in elements:**
  - Before collection, the export counts the host elements near the crop.
  - For each loaded link, it counts that link's elements inside the export box.
  - A link never counts past its estimate until it finishes.
- **Time left** comes from the measured rate plus estimates for writing the model and the two PDF pages. It counts down smoothly and only appears once there is enough data (3 % and 2 s).
- **PDF size** is projected from the model bytes written so far and the typical compression of the model inside the PDF.
- The logic is in `Collection/ExportProgressModel.cs` and is tested without Revit.

### Speed
- **Model scan:** uses Revit's quick view-independent filter, so annotation and detail elements are never loaded.
- **Hide/unhide in the temporary 3D view:** 0.5.3 called `IsHidden` on every model element up to four times per export. The export now tracks the elements it hid itself.
- **Elements outside the crop:** they no longer build their text labels (each one a Revit call), and non-model elements skip the bounding-box work.
- The effect on real models has to be measured in Revit. The export log still records how long each step takes.

### Icon
- The ribbon panel now has a blank title and one large **Export 3D PDF** button with the new icon.
- The icon comes as one embedded PNG per display scale, loaded at its exact DPI, as the UI style guide describes.
- The viewer's app and window icon is the same logo. `branding/make-icons.py` builds every size.

### Viewer: cut patterns
- Section caps now draw the material's Revit cut pattern, clipped to the cut shape:
  - model patterns at real size;
  - drafting patterns at 1/4" = 1'-0";
  - dashes and shifts included.
- Solid fills keep the material colour.

## Stage 2, part 2 (done)

- **Export dialog: layout B with both check boxes**, styled by `docs/EK_Revit_Addin_UI_Style_Guide.md`:
  - **Window:** own title bar with the word Close; fixed size of 640 x 352.
  - **Left:** the preview, with "Preview · Generate / Update".
  - **Right:**
    - Height: View range, All levels above, All levels below or All levels; plans only;
    - Phase filter;
    - Detail: Very low to Very high;
    - **Include categories hidden in the view** and **Include geometry outside the crop** (which asks for confirmation);
    - the size estimate.
  - **Footer:** Reset (link), Cancel, and Export… as the default button.
  - **Removed:**
    - Quick export, replaced by Very low;
    - the tiny-parts filter, now part of Very low;
    - the properties box, which keeps Mark, Comments and Type Mark;
    - every helper paragraph.
  - **Remembered between exports:** detail, the hidden-categories box and the phase filter.
  - **Reset** returns to: View range, the view's own filter, Medium, hidden categories on, outside crop off.
- **Progress window: layout P2.** It shows:
  - the four steps;
  - the current element count;
  - one bar for the whole export;
  - time left and projected PDF size;
  - what Revit is working on.
  - "Cancel" in the title bar (and Esc) cancels at the next safe point.
- **Theme:** both windows follow Revit's light or dark theme (`UIThemeManager`, Revit 2024+), using the guide's palettes.
- **Code:** both windows use no Revit types, so `tests/DialogHarness` opens them under Wine. Shots are in `evidence/v054-dialogs/`, light and dark at 96 DPI, with Wine's fonts.
- **Shared controls** (EK title bar, buttons, links, drop-downs, check boxes) are in `EkUi.cs`, ready for the other EK windows.

## Tests
- `tests/ExporterCoreTests`: 26 checks (2 new for progress, 1 for preferences).
- `tests/DialogHarness`: both windows, light and dark, under Wine (no layout overflow).
- Original suites: crop 16, scope 21, link overrides 10, wire writer 22.
- `tests/ExporterAssemblyAudit`: 150 checks. PresentationCore and WindowsBase, Revit's own UI stack, are now allowed.
- `tests/LargeSceneTests`: section box settings.
- Viewer: `offline3d-reader/tests/web/hatch-check.mjs` and the 40-item browser check.

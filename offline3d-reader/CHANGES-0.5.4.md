# Offline 3D PDF Viewer 0.5.4 (in progress)

0.5.4 is being built in stages from the feedback on 0.5.3. This file lists what each finished stage changed. The version strings still say 0.5.3 until the 0.5.4 installer is built.

## Stage 1: 3D viewer behaviour

Tested in headless Chromium (39 checks in `tests/web/viewer-3d-check.cjs`) and with the viewer's .NET test suites. The WebView2 host side (ClickTo3D, close and reopen) is compiled and unit-tested but **has not run on Windows yet**; see "Check on Windows" below.

### Opening 3D: ClickTo3D

- The toolbar button is now **ClickTo3D**. Pressing it arms 3D:
  - the button turns blue;
  - the pointer becomes a blue arrow (`Assets/ClickTo3D.cur`, replacing the dot at the pointer tip);
  - a dashed box follows the pointer over the plan, showing where the 3D box will go.
- **The 3D panel opens only after you click the plan.** Esc or the button cancels.
- While you choose the spot, the 3D page loads in the background and receives the model, so the panel opens on a model that is already loaded. The page shows a spinner if it still needs a moment; the Babylon.js logo no longer appears.
- Once the panel is open, the button reads **Close 3D**. Clicking the plan again moves the box, as before.

### Close and reopen

- **Before:** closing 3D disposed the WebView2 and the next open created a new WebView2 environment on the same profile. That can fail while the previous browser process is still closing, so 3D sometimes would not reopen.
- **Now:**
  - Closing hides the panel but keeps it loaded for the document, so reopening is instant.
  - The panel is disposed only when the document changes or the window closes.
  - The process creates one WebView2 environment and reuses it for every panel.
  - If the panel was undocked, it reopens undocked.
- **Page files:** `BoxPanel` blocked every page file not on its allowlist. That list was missing `outlines.mjs` (the 0.5.3 outline pass) and `comparison-worker.mjs`. Both are now allowed, along with the new `measure.mjs`.

### Navigation

- **Zoom no longer stalls:** the transparent hit area of a resize arrow swallowed wheel events whenever the pointer was over it. The page now forwards those to the view.
- **Orbit direction reversed to match Revit.** This applies to the left drag, Shift + middle drag, and dragging the orientation cube.
- **Three.js controls match Babylon.js:**
  - Shift + middle orbits (it used to pan).
  - Double-clicking the wheel fits the box exactly (it did nothing).
  - Home fits exactly too.
- **Home view matches Revit's default {3D} view.** It now looks from the south-east and above, so the cube shows FRONT, RIGHT and TOP. 0.5.3 looked from the north-east, which showed the back of the model. (Graphics +Z is Revit south; see `Matrix4.Graphics`.)
- **Resize arrows** are 3/4 of their 0.5.3 size.

### Section box size

- **Settings › Section box** follows the ClickTo3D add-in and the [UI style guide](../docs/EK_Revit_Addin_UI_Style_Guide.md):
  - the box picture with its dimension lines;
  - **Height above level**, **Height below level** and **Square size**, as 2-decimal fields with ▲▼ steppers (and the Up/Down keys) that step exactly 1 ft or 1 m;
  - the small **Feet / Meters** segmented buttons, lined up with the right edge of the fields;
  - a quiet **Reset** link.
- This size is used every time 3D opens. Changes save at once (no Save button), last across viewer restarts, and resize the open box in place.
- **Reset** puts back 10 ft above, 0.5 ft below and a 10 ft square, shown in Feet.
- **Stretching a box** in 3D or on the PDF is not saved. It lasts until 3D is closed; the next open starts from the Settings size. While 3D stays open, clicking a new spot keeps the stretched size.
- Invalid values are refused. Height above and square size must be greater than 0; height below may be 0.
- **Reset box** in Tools returns the open box to the Settings size.
- The settings are stored in `%LOCALAPPDATA%\Offline3DPdfPrototype\section-box-defaults.json`, the same file 0.5.3 used.

### Settings

- **Brightness** replaces Exposure and Environment. It has a **Reset** button, and 1.0 is the calibrated default.
  - On Babylon.js it scales exposure in Shaded and Realistic.
  - On Three.js it scales the lights in Shaded.
  - Consistent Colors and Hidden Line keep their exact colours.
- **Realistic is greyed out for Three.js**, and switching to Three.js changes a Realistic view to Shaded.
- **Orientation cube and Home** can be turned off; turning it off hides both.
- The cube is a little larger, with larger labels. "Home view" is now **Home**.
- View settings (renderer, quality, display, brightness, checkboxes) are remembered between sessions.

### Measure, rebuilt

- Click two points on the model.
- Points snap to the nearest corner within 12 px, shown as a square; otherwise they sit on the surface, shown as a dot.
- A dashed line follows the pointer to the second point.
- The result shows:
  - the distance in feet-inches to 1/16" (or millimetres/metres when the unit switch is set to m);
  - the horizontal and vertical components below it.
- The line stays attached to the model while you orbit or zoom.
- Hold **Shift** to keep the second point along X, Y or Z.
- Esc or the button ends measuring. Resize arrows don't catch clicks while measuring.

### Check on Windows

1. Open a 3D PDF and press **ClickTo3D**. The pointer should turn blue and a dashed box should follow it over the plan. The 3D panel must stay closed.
2. Click the plan. The panel should open straight away, showing the model or a spinner (never the Babylon.js logo).
3. Close 3D and repeat steps 1–2 at least five times. The panel should reopen instantly each time.
4. In Settings, change Height above level, close the viewer and reopen it. The value should be kept, and new boxes should use it. Stretch a box, close 3D, and open it again: the box should be back to the Settings size. Reset should give 10 / 0.5 / 10 ft.
5. Scroll to zoom with the pointer over a blue arrow. Zoom should never stall.
6. Shift + middle drag left and right. The model should turn the same way as in Revit.
7. Switch to Three.js. Realistic should be greyed out.

## Next stages

2. Exporter: speed, overall progress and size estimate, a cleaned-up options dialog, a very low detail mode, a phase filter, the cut material on section caps, and the new icon.
3. Separate installers for the reader and the Revit add-in.
4. PDF viewer layout, with Compact and Regular modes, and the scroll direction.
5. Library: recent PDFs gallery, folders, Open latest, drag into Revit.

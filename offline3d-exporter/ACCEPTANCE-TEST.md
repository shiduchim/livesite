# Exporter 0.5.3: acceptance test in Revit

0.5.3 has been built and tested automatically, but it has not run inside Revit yet. These steps check what only Revit can show. Allow about 30 minutes. A run that fails is still useful if you send the log folder.

**Logs.** Every export, including cancelled and failed ones, writes a live trace to
`%LOCALAPPDATA%\Offline3DPdfPrototype\Logs\Exports\<date>-revit-Offline3DExport-…\`.
The main files are `export-trace.txt` (stages, timings, heartbeat, statistics) and `geometry-diagnostics.txt`.

## 0. Install

1. Close Revit, extract the update ZIP, and run `UPDATE.cmd`.
2. Start Revit. The Add-Ins panel should read **Offline 3D PDF v0.5.3**.
3. To undo the update, close Revit and run `ROLLBACK.cmd`.

## 1. Small known plan (about 5 minutes)

1. Open a small, cropped floor plan that is **not** placed on a sheet, then click **Export 3D PDF**.
2. Check the dialog at your normal Windows scaling. It should show:
   - no horizontal scrollbar;
   - **Quick export / Export… / Cancel** visible;
   - nothing clipped when you make the window narrow or short.
3. Click **Generate preview**. The image appears **inside the dialog**, with no separate popup. It is centred, and levels and grids are hidden.
4. Tick **All up**. A yellow "Options changed" bar appears. Click **Update preview**: the snapshot now reaches the top of the model, and the crop is not wider.
5. Untick **All up**, then click **Export…** with Medium detail. The helper should report "Export complete".
6. Open the PDF in the viewer and click a few known objects. Compare their position, colour and properties with Revit.

## 2. Sheet

Use a sheet with one model viewport. It should be selected automatically, without a picker and without selecting the viewport.

## 3. Your large linked engineering model (the export that failed before)

1. Export a Medium, crop-only plan.
2. In `export-trace.txt`, check:
   - `Callback balance: unmatched ends 0, open transform levels at end 0`
   - `Links entered:` matches your loaded links
   - `Instances begun … reused without tessellation:` shows the repeated-family saving
3. In the PDF, check both of these:
   - linked objects sit where they are in Revit;
   - **host** objects drawn after a link are present and correctly placed. 0.5.2 could lose or shift them.
4. Note the total time (`Stage done` lines) and the `GLB … bytes` summary line.

## 4. Detail levels

On the same view, export **Quick, Low, Medium and High**. For each, record the time, GLB bytes and the triangle numbers from `geometry-diagnostics.txt`. Quick must still show coloured, clickable objects, not boxes.

## 5. Repeated sockets and fixtures

Zoom into repeated, rotated and **mirrored** families in the viewer and compare them with Revit.

If anything looks wrong:
1. Set the Windows user environment variable `OFFLINE3D_DISABLE_INSTANCE_REUSE` = `1`.
2. Restart Revit and export again.
3. Send both logs. The trace states whether reuse was on or off.

## 6. Crop and height

1. Use a rotated crop. Objects just outside it must be absent, and objects crossing it present.
2. **All up** and **All down** change height only.
3. **Include geometry outside the crop** asks for confirmation, with **No** as the default.

## 7. Cancel and clean-up

1. Click **Cancel** during "Collecting model geometry".
2. Revit returns.
3. There are no `Offline3D_TEMP…` views or temporary sheets left.
4. Your views and settings are unchanged. Repeat the cancel from the options dialog and from the file chooser.

## What to send back

- The log folder of each export.
- A screenshot of the dialog at your Windows scaling.
- The text of any error message.

The `Section-box evidence` line in `geometry-diagnostics.txt` answers an open question: whether Revit cuts geometry at the section box.

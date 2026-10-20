# Changelog

## 2.0.2

- Pin: after you use another tab, clicking back into a view, sheet, Properties,
  the Project Browser or a floating view returns to EK Custom. Clicks on the
  ribbon, its menus and in dialogs don't count. New setting "Come back when I
  click into the view" (on by default).
- Pin waits for the mouse button to be released before switching tabs.

## 2.0.1

- Fixed: the pin did not keep EK Custom open. It now watches the ribbon on its
  own timer instead of Revit's Idling event, which fires too rarely. Revit
  switching to the plain Modify tab also returns to EK Custom now; the
  "Modify | ..." tabs still stay while you work. Writes `pin_YEAR.txt`.
- Pin and Ribbon Builder are full-size buttons on the tab, so their icons are clear.
- New, bolder pin icon.
- Ribbon Builder window is more compact: the preview uses Revit's real icon sizes,
  library tiles and spacing are smaller, and the window opens smaller.

## 2.0.0

- New: pin button keeps the EK Custom tab open; Modify tabs still open while you work.
- New: EK Custom is placed next to the Modify tab (or first), and holds its own
  Ribbon Builder button, so the separate EK launcher tab is gone.
- New: redesigned Ribbon Builder: true-to-Revit preview, drag and drop from the
  library and within the tab, arrow-key moves on the button grid, undo/redo,
  right-click menu, unsaved-changes prompt.
- New icons for Ribbon Builder and the pin; commands without an icon show their
  initials instead of a generic "EK".
- Empty spots in a stack are now real, movable gaps (old "row position" settings
  are converted automatically).
- Spacer buttons no longer show "Unused Position" when labels are on.
- Ready-built DLLs: installing no longer needs the .NET SDKs.
- Removed: one-time migrations from the Python ribbon and pre-1.1 layouts,
  per-button pixel diagnostics, preview-only options ("box around buttons",
  list view, preview icon size) and the duplicate root scripts.

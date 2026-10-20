# EK Ribbon Builder 2.0

Your own **EK Custom** tab in Revit 2024, 2025, 2026 and 2027, with the buttons you
use, in the order you want. One layout is shared by every Revit version.

## Install

1. Close every Revit window.
2. Extract the whole ZIP to a normal folder.
3. Double-click **INSTALL.cmd**.
4. Start Revit.

Your existing layout, icons and settings from v1.x are kept. The ZIP contains
ready-built DLLs, so no .NET SDK is needed. (`INSTALL.cmd -BuildFromSource`
compiles them on your PC instead, using the .NET 10 SDK.)

**UNINSTALL.cmd** removes the add-in and keeps your layout.
**DIAGNOSTICS.cmd** checks every Revit version and puts a ZIP of the logs on your
Desktop.

## The EK Custom tab

At the right end of the tab there are two small buttons:

| Button | What it does |
| --- | --- |
| Pin | **Keeps EK Custom open.** Revit comes back to this tab instead of jumping to Architecture, View, etc. The Modify tabs still open while you select or edit, and any tab you click yourself stays open until Revit switches tabs again. Click to unpin. |
| Ribbon Builder | Opens the settings window. |

The tab is placed **right after Modify** by default, so the two tabs you use sit
together. You can change this to *First tab* in the Ribbon Builder.

Tip: in Revit's *Options > User Interface > Tab switching behavior*, choose
**Return to the previous tab**. Then deselecting returns straight to EK Custom.

## Ribbon Builder

The top of the window shows your tab exactly as Revit will build it.

**Mouse**

- Click a command in the library to add it (after the selected button), click again to remove it.
- Drag commands from the library onto the tab, onto a button, or into an empty spot.
- Drag buttons around the tab to move them. Drag one back onto the library to remove it.
- Double-click a button to switch between small and large. Right-click for more.

**Keyboard** (after clicking a button in the preview)

| Keys | |
| --- | --- |
| ← → ↑ ↓ | Move the button on the grid. Past the edge it crosses a divider. |
| Tab / Shift+Tab | Select the next / previous button |
| Del | Remove |
| L | Large / small |
| D | Add a divider after it |
| + / − / 0 | Icon zoom in / out / reset |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Ctrl+S | Save |
| Ctrl+F | Search the library |

On the right: the selected button's size, icon zoom, rows of its group (2 or 3),
and the tab settings (pin, tab position, labels).

Revit can't rebuild a tab while it's running, so new buttons and layout changes
show after you **restart Revit**. The pin and tab position apply right away.

**Rescan Revit** re-reads every tab: new add-ins appear in the library, changed
icons are picked up, and if an add-in was replaced by a new C# version it offers
to swap your saved buttons over (with a backup, and only after you confirm).

## Files

Everything lives in `%APPDATA%\EKRibbonCSharp`:

- `layout_shared.json`: your layout, shared by all Revit versions
- `catalog_YEAR.json`, `Icons\`: commands and icons found by each Revit version
- `startup_YEAR.txt`, `scan_YEAR.txt`, `last-error.log`: logs
- `Backups\`: copies made before installs and Smart Rescan updates

## Building

`pwsh Build.ps1` runs the tests, builds all four DLLs and writes the ZIP to `out/`.
It needs the .NET 8 and .NET 10 SDKs. Revit API references come from the
Nice3point reference packages on NuGet, or from your Revit install when
`REVIT_INSTALL_DIR` is set.

| Revit | Framework |
| --- | --- |
| 2024 | .NET Framework 4.8 |
| 2025, 2026 | .NET 8 |
| 2027 | .NET 10 |

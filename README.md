# EK Triangle

Replaces Revit's **+ / −** expand/collapse symbols with small dark-gray
(**#555555**) triangles: **▶ collapsed**, **▼ expanded**.

* **Revit dialogs** (Visibility/Graphics and other tree lists) – via a small Revit add-in.
* **Project Browser** (views, sheets …) – by changing exactly two icon definitions in
  Revit's Project Browser resource file, after a verified backup.

Supports Revit **2024, 2025, 2026 and 2027**. Expanding and collapsing work exactly as before.

## CMD installer

Download **EK_Triangle_CMD_Installer.zip** from the [CMD release](https://github.com/shiduchim/revit-triangle-/releases/tag/cmd-v1.0.1).
Extract the entire ZIP, close Revit and double-click **INSTALL.cmd**. Press Enter
for all detected versions or type the years to keep. Only the Project Browser
worker requests UAC. **UNINSTALL.cmd** restores/removes; **CHECK_STATUS.cmd** is
read-only. No compilation or dependency downloads occur on the user's PC.

This uses the existing engine and precompiled DLLs. See the included README,
executable inventory, SHA-256 checksums and Windows test report. Actual Revit
acceptance and company IT approval are still required before distribution.

## Install (separate EXE option)

1. Close Revit.
2. Run **`EK_Triangle_Setup.exe`** (in [`release/`](release/)).
   *If company security or Windows blocks a component, stop and ask IT to review the release inventory and checksums.*
3. Leave the detected Revit versions ticked and click **Install**. Allow the administrator prompt (needed only for the Project Browser).
4. Start Revit. If it asks whether to load **EK Triangle**, choose **Always Load**.

## Uninstall

**Windows Settings → Apps → Installed apps → EK Triangle → Uninstall.**
This removes the add-in and puts back Revit's original Project Browser files.

Emergency restore (works even if Revit does not start): **Start menu → EK Triangle Restore**,
or run `C:\ProgramData\EKTriangle\EK Triangle Restore.exe`.

Turn the dialog triangles off without uninstalling: set `Enabled=0` in
`%LOCALAPPDATA%\Programs\EK Triangle\glyph.ini` and restart Revit.

## More

* [Short acceptance test](docs/ACCEPTANCE-TEST.md) – a few minutes in Revit
* [Compatibility, safety and limitations](docs/COMPATIBILITY.md)
* [Building from source](docs/BUILD.md)
* Latest automated test results: `release/<version>/TEST-RESULTS.md`

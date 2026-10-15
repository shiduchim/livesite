# EK Triangle

Replaces Revit's **+ / −** expand/collapse symbols with small dark-gray
(**#555555**) triangles: **▶ collapsed**, **▼ expanded**.

* **Revit dialogs** (Visibility/Graphics and other tree lists) – via a small Revit add-in.
* **Project Browser** (views, sheets …) – by changing exactly two icon definitions in
  Revit's Project Browser resource file, after a verified backup.

Supports Revit **2024, 2025, 2026 and 2027**. Expanding and collapsing work exactly as before.

## SIMPLE installer (CMD + PowerShell)

Download **EK_Triangle_SIMPLE_Installer.zip** from the [SIMPLE release](https://github.com/shiduchim/revit-triangle-/releases/tag/simple-v1.0.0).
Extract the complete ZIP, close Revit and double-click **INSTALL.cmd**. Press Enter
for all detected versions or type the years to keep. **UNINSTALL.cmd** removes and
restores; **CHECK_STATUS.cmd** reads status without administrator privileges.

Each CMD launches an ordinary readable `.ps1` file. The package contains **no EXEs**.
PowerShell loads the existing `EKTriangle.Core.dll` compatibility/install/restore
library directly; all eight application DLLs are precompiled. No compilation or
dependency download occurs on the user's computer. Only protected Project Browser
work requests UAC. The current glyph and Project Browser engines are unchanged.

See the included inventory, SHA-256 checksums, Windows test report and
[launch investigation](docs/LAUNCH-INVESTIGATION.md). The user's original browser
and native installer examples informed the staging, ownership, backup and restore
workflow. Their old bypass flags and development compilation steps are omitted.
If company policy blocks scripts or DLLs, stop for IT review; security controls
and execution policies are preserved. Actual Revit acceptance is still required.

The former EXE-dependent CMD package is superseded by SIMPLE. Previous release
artifacts remain available for review; the SIMPLE scripts do not launch them.

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

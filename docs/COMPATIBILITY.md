# Compatibility, safety and limitations

## Status per Revit version

| Revit | Dialog add-in (loader runtime) | Project Browser | Verified inside Revit |
|-------|-------------------------------|-----------------|------------------------|
| 2024  | .NET Framework 4.8, API 24.0  | automatic check; the user-verified 2024 build is on the approved list | Native glyph engine: yes (earlier EKGlyph test). This release: **pending your acceptance test** |
| 2025  | .NET 8, API 25.0              | automatic structural check | **No** – build + automated tests only |
| 2026  | .NET 8, API 26.0.4            | automatic structural check | **No** – build + automated tests only |
| 2027  | .NET 10, API 27.0             | automatic structural check | **No** – build + automated tests only |

Each loader is compiled against the **first public** API release of its year,
so it also loads in every later update of that year.

## How the Project Browser change is made safe

For each installed Revit version setup:

1. Locates `AddIns\ProjectBrowser\resource\static\js\main.js` (standard folder or the install location registered by Revit).
2. Requires strict UTF-8 that round-trips byte-for-byte.
3. Finds the **collapsed** icon (8 px plus `M7 7V4h2v3h3v2H9v3H7V9H4V7h3z`) and the
   **expanded** icon (8 px minus `M4 7h8v2H4z`). Each must occur **exactly once**,
   as the only path of a **16×16 SVG** component.
4. Never touches the toolbar **Add** button (12 px plus `M7 7V2h2v5h5v2H9v5H7V9H2V7h5z`)
   or any other icon; the result is checked to have the same Add button and SVG count.
5. Stores a verified original backup (SHA-256) in `C:\ProgramData\EKTriangle\Browser\RevitYYYY`
   and a recovery record **before** writing.
6. Writes atomically (temporary file + `File.Replace`), verifies the SHA-256 and rolls back on any mismatch.

If any check fails, that version's Project Browser is **left unchanged** and setup says why.
The dialog add-in is still installed for it.

**Restore** removes exactly the two EK icon definitions (an exact reversal, verified
against the original's SHA-256 when the record exists). Consequences:
* an Autodesk update that replaced `main.js` is never overwritten with an old backup;
* a missing or damaged backup does not prevent restoring;
* backups are never deleted (they stay after uninstall, by design).

The earlier EK SymbolSwap "T" patch is recognised; setup converts it (verified
against EK SymbolSwap's own backup record) and restore returns the true original.

## Dialog add-in (native glyph hook)

* Same uxtheme/MinHook interception as the verified EKGlyph 2024 test; nothing on disk is patched.
* Hooks only inside `Revit.exe`; refuses to start if the experimental `EKGlyphHook.dll` or a second copy is active.
* Replaces only TreeView glyph parts (normal and hot), only the two known states; everything else is drawn by Windows.
* Triangles are computed from the size Revit asks for (8 px at 100 % DPI, scaled and pixel-aligned at higher DPI).
* A drawing fault switches the replacement off for the rest of the session (MSVC structured exception guard);
  a crash of Revit caused by the hook cannot be ruled out completely.
* `Enabled=0` in `glyph.ini` turns it off; log `%LOCALAPPDATA%\EKTriangle\Logs\Native.log` (size-limited).

## Migration from the experimental add-in

`EKTreeGlyphProbe2024` / `EKGlyph` manifests (identified by their AddInId or `EKGlyph.App`)
are moved – not deleted – to `%LOCALAPPDATA%\EKTriangle\LegacyBackup\…` with a
`moved-items.txt` that lists their original locations. If installing EK Triangle
for that version fails, they are moved back. A machine-wide copy (ProgramData)
is reported as a conflict and EK Triangle is not installed for that version.

## Known limitations

* Not code-signed: Windows SmartScreen and Revit's "unsigned add-in" prompt appear once.
* The Project Browser colour is a fixed #555555 (also in Revit's dark theme).
* The dialog add-in is installed per Windows user; the Project Browser change applies to all users of the PC.
* Other UI technologies in Revit (WPF/web panels other than the Project Browser) are not changed.
* Revit updates may replace `main.js`; run setup again afterwards (it re-checks and re-applies if compatible).

EK TRIANGLE - CMD INSTALLER 1.0.2

1. Extract the entire ZIP to a local folder approved by your IT team.
2. Close every Revit window and double-click INSTALL.cmd as your normal user.
3. Press Enter for all detected versions, or type the years to keep, e.g. 2024,2027.
4. Allow Windows UAC for the Project Browser step when requested.
5. Read the result for each version. Start Revit for the acceptance check.

UNINSTALL.cmd removes the dialog add-in for your Windows account, restores
recognized Project Browser changes, and removes verified CMD-installed DLLs.
It works without starting Revit. CHECK_STATUS.cmd is read-only and needs no UAC.
Run the scripts as the same Windows user who installed the add-in. Running the
whole INSTALL/UNINSTALL script under another administrator account targets that
account; the program elevates only the machine-wide browser worker itself.

No build tools, downloads, PowerShell scripts or separately installed .NET SDK
are needed. The helpers use .NET Framework 4.8, included in supported Windows
10/11 installations. The loaders use Revit's own runtime: 2024 .NET 4.8;
2025/2026 .NET 8; 2027 .NET 10. All eight managed/native DLLs are included.

This package calls the existing installation/restoration engine, not Setup.exe.
The separate EXE installer remains available. Both formats use the same AddInId
and canonical manifest. Installing CMD over an EXE installation repoints that
manifest without a second add-in. The old EXE's application files and Installed
Apps entry are retained for its uninstaller; CMD never deletes its directory.
Either uninstaller removes the shared feature. Re-run the desired installer
after changing formats if necessary. Do not run installers simultaneously.

Compatibility is checked automatically. Unknown browser layouts and machine-wide
legacy/mixed add-in registrations are reported and left unchanged. IT must review
an all-users conflict before that component can be installed. Known user EKGlyph
prototypes and duplicate EK Triangle registrations are moved to recovery storage.
Unrelated EK applications and their manifests are preserved.

The small filled triangles stay #555555: right when collapsed, down when expanded,
without a border/square. Native hook and Project Browser patch designs are reused.
Automated simulation is not actual Revit acceptance. See ACCEPTANCE-TEST.md.

RECOVERY
Browser originals, records and the existing emergency restore tool are retained:
  %PROGRAMDATA%\EKTriangle
Legacy add-in backups and logs:
  %LOCALAPPDATA%\EKTriangle\LegacyBackup
  %LOCALAPPDATA%\EKTriangle\Logs
Verified versioned CMD payloads:
  %LOCALAPPDATA%\EKTriangle\CMD\Payloads
Incomplete copies end in .pending-... and are never referenced by an add-in.
They are preserved for inspection. Retry INSTALL.cmd after resolving the cause.
If restoration is unresolved, keep the extracted package and all recovery data.
Never copy an old main.js backup over a newer Autodesk file. Restoration verifies
exact reversal of EK changes and preserves unrelated later content.

SECURITY / IT REVIEW
No security settings, exclusions or execution-policy changes are made. Nothing
is encoded or disguised. A missing, changed, blocked or quarantined component
stops the affected operation; ask IT to approve it through your normal process.
SHA256SUMS.txt covers every package file except itself. EXECUTABLE-INVENTORY.csv
lists each EXE, DLL and CMD file with role, size and SHA-256. Check the ZIP's
external SHA-256 against the release checksum to establish distribution integrity.
Checksums detect changes; they are not a code-signing or trust decision.

Exit codes: 0 completed; 1 unresolved/error; 2 blocked/cancelled; 3 usage;
4 completed with skipped components; 5 administrator permission required.
Automation: INSTALL.cmd --yes [--years 2024,2027], UNINSTALL.cmd --yes.
Set EKTRIANGLE_NO_PAUSE=1 to suppress the final CMD pause for automation.

See TEST-RESULTS.md for this build's Windows simulation results and limitations.

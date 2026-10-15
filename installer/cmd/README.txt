EK TRIANGLE - SIMPLE INSTALLER 1.0.1

1. Extract the complete EK_Triangle_SIMPLE_Installer.zip to an IT-approved local folder.
2. Close ALL Revit windows. Double-click INSTALL.cmd as your normal Windows user.
3. Press Enter for all detected Revit versions, or type years to keep, e.g. 2024,2027.
4. Allow standard Windows UAC only if compatible protected Project Browser files need work.
5. Read the result for each version; press a key to close the CMD window.

UNINSTALL.cmd removes this user's owned EK Triangle manifests and verified payloads
and restores recognized Project Browser changes without starting Revit.
CHECK_STATUS.cmd is read-only, needs no administrator privileges, and includes
read-only launch/policy context. The underlying .ps1 scripts can also be run directly.

CONTENTS / RUNTIMES
The three CMD files launch ordinary Windows PowerShell 5.1 scripts. No EK installer
EXE is included or launched. No compilation or downloads occur on your computer.
The existing Core library is included as engine/EKTriangle.Core.dll and loaded by
PowerShell as a normal .NET Framework 4.8 assembly. Its compatibility checks,
manifest ownership/migration logic and verified restore engine are reused unchanged.
The exact eight application DLLs from release 1.0.2 are included unchanged: Revit 2024 (.NET 4.8),
2025/2026 (.NET 8), and 2027 (.NET 10), each with the existing x64 native glyph hook.
The glyph engine and Project Browser replacement are unchanged: small filled
#555555 right/down triangles without a surrounding square or border.

SAFETY / SECURITY
Keep security controls enabled. Scripts use the configured execution policy.
If a script or required DLL is blocked, STOP and ask IT to approve the inventory.
No policy bypass, encoded PowerShell, exclusions or alternate execution fallback
is used. Scripts are unsigned; organizations requiring signatures must have IT
review and sign them. LAUNCH-INVESTIGATION.md explains exit 5 and the limits of
read-only diagnosis. Lack of a security event does not establish permission.

Only the browser script requests UAC. Keep the main installer running as the user
who owns the Revit add-in; alternate administrator credentials are used only for
the machine-wide browser step. Unknown Autodesk resources are skipped. All-users
legacy registrations and mixed/foreign manifests are left unchanged for IT review.
Known user EKGlyph prototypes and duplicate owned manifests are backed up safely.

RECOVERY / OWNERSHIP
Original Project Browser backups and state: %PROGRAMDATA%\EKTriangle\Browser
Legacy/duplicate backups: %LOCALAPPDATA%\EKTriangle\LegacyBackup
Logs: %LOCALAPPDATA%\EKTriangle\Logs and %PROGRAMDATA%\EKTriangle\Logs
Verified payloads: %LOCALAPPDATA%\EKTriangle\SIMPLE\Payloads
Incomplete copies end in .pending-... and are never referenced by a manifest.
Unresolved restoration preserves payloads and recovery data; retry after resolving
its reported cause. A recognized modified Autodesk file is reversed precisely;
old backups are never copied over updated Autodesk content. Originals and legacy
backups remain after successful uninstall. Keep this ZIP for future recovery.

The prior CMD/EXE versions use the same AddInId. Installing SIMPLE repoints the
canonical manifest, avoids duplicate registrations, and reuses existing browser
records. Uninstall also cleans verified owned payloads from the prior CMD format.
Files belonging to the old EXE's Installed Apps entry are retained for its own
uninstaller. No old executable is launched by these scripts.

IT review: CODE-INVENTORY.csv lists all nine DLLs, five PS1 scripts and three CMD
entry points with role, size and SHA-256. SHA256SUMS.txt covers package contents
except itself. SIMPLE-SHA256SUMS.txt on the release page covers the complete ZIP.
Hashes detect changes; IT approval and any required signing remain separate.
TEST-RESULTS.md reports Windows automated simulations, not actual Revit operation.

Exit codes: 0 completed; 1 error/unresolved; 2 Revit running/cancelled;
4 completed with skipped components; 5 PowerShell language restriction.
Automation: INSTALL.cmd -Yes [-Years 2024,2027]; UNINSTALL.cmd -Yes.
Set EKTRIANGLE_NO_PAUSE=1 to omit only the final keypress for automation.

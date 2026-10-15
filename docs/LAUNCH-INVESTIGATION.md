# Access denied / exit 5 investigation

The superseded CHECK_STATUS.cmd ran EKTriangle.CMD.exe directly. It did not
invoke PowerShell. The console helper's status path returned 0 on success or 1
on a caught exception; it did not request administrator access or return the
engine's administrator-required code (5). Its pushd failure path returned 1.
Consequently the reported Windows 'Access is denied' with exit 5 is consistent
with the executable launch being denied before the status handler ran.

Win32 error 5 is ERROR_ACCESS_DENIED. NTFS execute permission and application
control can both prevent a process launch. A missing visible endpoint event
does not distinguish them. This workspace cannot inspect the affected company
computer, so its exact cause has not been established. The Windows CI suite
tests a denied execute permission on its own disposable fixture and separately
tests denied DLL read permission. These tests do not change company policies.

The SIMPLE package launches ordinary Windows PowerShell 5.1 scripts and contains
zero EXEs. The scripts load the project's existing EKTriangle.Core.dll directly
using Add-Type -Path. They do not compile C#, launch the previous helpers, use
encoded commands, alter execution policies, remove Zone.Identifier streams,
create exclusions, or modify application-control/antivirus settings.

CHECK_STATUS.ps1 prints an explicit 'started' marker, effective execution-policy
scopes, language mode, read-access checks and available relevant operational-log
event IDs, then reads component status. It writes no application files, logs,
backups, registry settings or policy. If policy prevents the script itself from
starting, the CMD file preserves PowerShell's error and exit code and explains
that no started marker means the script did not reach its checks. The absence
of an event still does not establish permission.

Restricted language mode, blocked scripts, unreadable or blocked DLLs, and
missing/changed inventory files stop the operation. IT must approve the scripts
and all required DLLs through the normal process. If the organization requires
signed scripts or DLLs, obtain approved signing from IT; this installer does not
try another execution host or weaken the configured policy.

The native hook DLL is used by Revit, not loaded into PowerShell. Its hash and
readability can be verified before installation; only running inside Revit can
establish whether that process is allowed to load it. CI simulation does not
establish permission or real-Revit compatibility on the company computer.

## Original working examples reviewed

The user supplied `EK_Two_Original_Working_Installers_For_Codex.zip` containing:

- `EK_SymbolSwap_AllRevit_2024-2027_OneFile.cmd` (SHA-256
  `6103856307D9CFFB6D40CBF9390DCF27F14148299FE65D9B76EF3DD102947724`):
  four-version discovery, readable PowerShell, strict SVG recognition, verified
  backups, durable state, protected file replacement and independent restore.
- `RESTORE_PROJECT_BROWSER_ONLY_2024-2027.cmd`: ownership and backup validation,
  changed Autodesk file refusal, a separate restore path and clear error results.
- `EKTreeGlyph_Revit2024_SAFE_TEST_PACKAGE_CHECK_FIXED.zip`: per-user manifests,
  isolated staging, ownership checks, copy hashes, publishing manifests last and
  rollback. Its development compiler step is deliberately not carried forward.

These files were inspected as reference material and were not executed. Their
historical execution-policy bypass and embedded ScriptBlock launch are not used.
The SIMPLE scripts follow the conventional CMD `-File` PowerShell structure and
use the already-tested current Core routines rather than reimplementing the old
T-shaped SVG replacement. Windows tests include the original T-symbol record
format migrating to current dark-gray triangles and exact original restoration.

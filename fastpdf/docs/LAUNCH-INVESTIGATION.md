# "Windows cannot access the specified device, path, or file" – investigation

## Symptom (v1.0 and v1.1)
Double-clicking the freshly built `EKFastPDF.exe` showed *"Windows cannot access the specified device, path, or
file. You may not have the appropriate permissions to access the item."* Windows Security reported no block.
The EXE, .NET runtime files and `pdfium.dll` were present, the file had no `Zone.Identifier` stream, the user had
full permissions. Starting the same EXE from PowerShell with `ProcessStartInfo { UseShellExecute = false }`
worked, and afterwards double-clicking worked too.

## What the evidence shows
1. **The process was never created.** That message is an Explorer/ShellExecute error dialog. If the program had
   started and failed, the result would be different: a .NET host error dialog or Application event 1000/1023/1026,
   a WPF crash, or the process staying alive for a moment. None of these happened ("no recent Windows
   Application events"). So .NET hosting, dependency loading, working directory, DPI code, PDFium and all of
   EK FastPDF's own code are **ruled out** – none of them had run yet.
2. **The same file started with `CreateProcess` (`UseShellExecute = false`).** So the file was readable and
   executable by the user at that moment: NTFS permissions, a missing/locked file, and architecture are ruled out.
   The difference between the two paths is everything the *shell* does before `CreateProcess`: security-zone
   policy for the file's location, Attachment Manager / Mark-of-the-Web, SmartScreen/Smart App Control reputation
   checks, and cloud-file (OneDrive placeholder) handling.
3. **The problem disappeared after the first successful direct start, also for the older version.** So it was a
   *state* that changed (a reputation/scan result being cached, a cloud placeholder being hydrated, a policy cache)
   rather than something wrong inside the files.

## Candidate causes (not confirmed – the original machine state is gone)
| Candidate | Produces exactly this dialog? | Fits "worked after a direct start"? | Notes |
|---|---|---|---|
| Location in a URL security zone (Intranet/Internet: network share, redirected/synced Desktop or Documents, mapped drive) with "Launching applications and unsafe files" disabled | Yes – this is the classic source of this exact text | Partly | Shell-only check; `CreateProcess` bypasses it. The diagnostics report now prints the zone. |
| Mark-of-the-Web on the EXE (downloaded ZIP extracted by a tool that propagates it) | Yes, with restrictive zone policy | Yes (if the mark was removed) | v1.0 diagnostic found no `Zone.Identifier`, which argues against it for that copy. |
| Defender / SmartScreen / Smart App Control reputation check of a brand-new unsigned EXE | Usually a different, branded message, but shell-path only | Yes – the cloud verdict is cached after the first run | Every rebuild produces a new, unknown hash. |
| OneDrive Files-On-Demand placeholder (Windows 11 backs up Desktop/Documents to OneDrive by default) | Possible | Yes – reading the file hydrates it | The report shows `RECALL_ON_DATA_ACCESS` / `PINNED` attributes. |
| AppLocker / Software Restriction Policy | No – shows "blocked by group policy" | No | Unlikely. |

Because the machine state that caused it cannot be reproduced here, **the exact cause is not claimed**. All
candidates share two properties: they only affect *shell* launches, and they are specific to an unsigned EXE built
or extracted into a user-profile folder.

## What v1.2 changes
1. **A real installer** puts the program into `C:\Program Files\EK FastPDF` (Local Machine zone, not synced by
   OneDrive, written by the installer so the files carry no Mark-of-the-Web). Start menu, desktop shortcut, App
   Paths and the "Open with" registration all point there. The installer is an unsigned EXE: when it is downloaded,
   SmartScreen may say *"Windows protected your PC"* – choose *More info → Run anyway* once. This is expected for
   any unsigned download and does not affect the installed program.
2. **CI verifies normal shell launches of the installed program** on a clean Windows Server 2022 VM: Explorer
   start of the EXE, Start-menu shortcut, desktop shortcut, App Paths (Win+R), the "Open with" ProgID and
   `Applications\EKFastPDF.exe` registrations exactly as Explorer invokes them, a Unicode/space path from another
   working directory, single-instance hand-over, upgrade and uninstall (`tests/installer/Test-FastPdfInstall.ps1`,
   results in `docs/BENCHMARKS.md` / CI artifacts).
3. **Start-up logging from the first line of `Main`** (`%LOCALAPPDATA%\EK FastPDF\logs\fastpdf.log`): executable
   path, working directory, arguments, OS and runtime – before any WPF code runs. If a start attempt produces no
   log line, Windows refused to create the process (point 1 above); if it produces a line, the log contains the
   failure.
4. **Diagnostics report** (menu ⋯ → *Diagnostics report*, or `EKFastPDF.exe --diagnose`): security zone of the
   EXE, Mark-of-the-Web, cloud-placeholder attributes, Smart App Control state, presence and size of the runtime
   and PDFium files, GPU/CPU, render statistics, recent log lines.
5. **No dependence on the working directory**: PDFium is loaded by absolute path from the program folder; render
   workers are started by absolute path with the program folder as their working directory.
6. **No single-file self-extraction** to `%TEMP%` (plain folder deployment), `asInvoker` manifest (never asks for
   elevation), no scripts needed to start the program.

## If it ever happens again
1. Note whether a new line appeared in `%LOCALAPPDATA%\EK FastPDF\logs\fastpdf.log` for that attempt.
2. Start EK FastPDF from the Start menu and choose ⋯ → *Diagnostics report* → *Copy*, or run
   `"C:\Program Files\EK FastPDF\EKFastPDF.exe" --diagnose` – the path of the report is printed and written to the
   log folder.
3. The report's *Launch environment* section (zone, Mark-of-the-Web, attributes, Smart App Control) identifies
   which candidate applies.

The permanent fix for reputation-based warnings is **code signing** (an Authenticode certificate, e.g. Azure
Trusted Signing or an OV/EV certificate). The build has a single place to add `signtool` once a certificate exists.

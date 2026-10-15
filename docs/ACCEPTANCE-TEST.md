# Acceptance test (about 5 minutes)

Do this once in **Revit 2024**. For 2025, 2026 or 2027, repeat it only for the
versions you actually use – these have been built and tested automatically but
have **not yet been opened in real Revit**.

1. Close Revit. For the SIMPLE CMD/PowerShell package, extract the entire ZIP and run **INSTALL.cmd**,
   press Enter for the detected versions, and allow UAC for the Project Browser step.
   Read the result for each version. The separate EXE option uses `EK_Triangle_Setup.exe`.
2. Start Revit 2024 (choose **Always Load** if asked about EK Triangle) and open
   any project or a new empty project.
3. **Project Browser:** collapsed groups show a small dark-gray **▶**,
   expanded groups a **▼**. Click one to collapse and expand it.
   The **+** button in the browser's toolbar (if present) must still be a plus.
4. **Visibility/Graphics** (type `VG`): the category tree shows **▶ / ▼**
   (no boxed + / −). Expand and collapse one category.
5. Close and restart Revit: both places still show triangles.
6. Close Revit and run **UNINSTALL.cmd** for a CMD installation (or use
   **Settings → Apps → Installed apps → EK Triangle** for an EXE installation).
   Read the restoration results, then restart Revit: original + / − symbols return
   in both places. For SIMPLE, **CHECK_STATUS.cmd** must report no dialog add-in and
   original Project Browser icons. Keep backups if any restoration is unresolved.

If something looks wrong, uninstall (step 6) and send these two folders:
`%LOCALAPPDATA%\EKTriangle\Logs` and `C:\ProgramData\EKTriangle\Logs`.

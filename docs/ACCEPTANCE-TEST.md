# Acceptance test (about 5 minutes)

Do this once in **Revit 2024**. For 2025, 2026 or 2027, repeat it only for the
versions you actually use – these have been built and tested automatically but
have **not yet been opened in real Revit**.

1. Close Revit. Run `EK_Triangle_Setup.exe`, keep the defaults, click **Install**,
   allow the administrator prompt. The last page lists what was done per version.
2. Start Revit 2024 (choose **Always Load** if asked about EK Triangle) and open
   any project or a new empty project.
3. **Project Browser:** collapsed groups show a small dark-gray **▶**,
   expanded groups a **▼**. Click one to collapse and expand it.
   The **+** button in the browser's toolbar (if present) must still be a plus.
4. **Visibility/Graphics** (type `VG`): the category tree shows **▶ / ▼**
   (no boxed + / −). Expand and collapse one category.
5. Close and restart Revit: both places still show triangles.
6. Uninstall from **Settings → Apps → Installed apps → EK Triangle**, restart
   Revit: the original + / − symbols are back in both places.

If something looks wrong, uninstall (step 6) and send these two folders:
`%LOCALAPPDATA%\EKTriangle\Logs` and `C:\ProgramData\EKTriangle\Logs`.

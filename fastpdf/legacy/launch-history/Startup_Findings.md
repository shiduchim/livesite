# Historical launch findings

- Running directly via Windows Explorer initially showed: "Windows cannot access the specified device, path, or file. You may not have the appropriate permissions to access the item."
- Same error reported again for the v1.1 build.
- The user checked Windows security; they reported the executable was not blocked.
- v1.0 diagnostic recorded: EXE 164352 bytes, EKFastPDF.dll 74752 bytes, pdfium.dll 7512064 bytes and runtime files present; no alternate data stream beyond `$DATA`; unsigned EXE; user account had full inherited file permissions.
- Diagnostic `System.Diagnostics.Process.Start` with `UseShellExecute=false` succeeded; process alive at 2 seconds, no recent Windows Application events. The user later confirmed normal launch was successful.
- v1.1 one-click helper used the same direct ProcessStartInfo method, and the user reported it opened.
- Do NOT infer a confirmed root cause from these observations. Determine whether shell/explorer invocation vs direct process launch or file association/shortcut working-directory differences are involved. Installer packaging and clean machine testing are needed.

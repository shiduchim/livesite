# Checklist for the i7-12700 / T1000 workstation

Everything below was **not** possible in the build environment (no NVIDIA GPU, no Intel hybrid CPU, no Foxit,
no real Revit drawings). It takes about 20 minutes.

## 1. Install and launch (5 min)
- [ ] Run `EK_FastPDF_Setup_v1.2.exe` (SmartScreen: *More info → Run anyway* if shown). Tick the desktop shortcut.
- [ ] Start from the **Start menu**, from the **desktop shortcut**, and by double-clicking
      `C:\Program Files\EK FastPDF\EKFastPDF.exe` in Explorer. All three must open the window directly.
- [ ] Right-click a Revit PDF → *Open with* → *EK FastPDF*. Open a second PDF the same way: it must open as a
      **second tab** in the same window.
- [ ] Optional: *Open with → Choose another app → EK FastPDF → Always*; then double-click a PDF.
- [ ] If any of these fails: ⋯ → *Diagnostics report* → *Copy to clipboard* and keep the text
      (also `%LOCALAPPDATA%\EK FastPDF\logs\fastpdf.log`).

## 2. Hardware detection (1 min)
Press **Ctrl+Shift+D** (Diagnostics). Check:
- [ ] `CPU` shows `20 logical / 12 cores (8 P + 4 E)` and `Recommended workers 7`.
- [ ] `GPU adapter` lists the NVIDIA T1000 with its real dedicated memory; `WPF render tier 2`.

## 3. Viewer benchmark on this PC (5–10 min, unattended)
Close EK FastPDF, then in a Command Prompt:
```
"C:\Program Files\EK FastPDF\EKFastPDF.exe" --benchmark "%USERPROFILE%\Desktop\FastPDF-bench"
```
The window runs scripted open / zoom / pan / page-jump / tab / search tests on the synthetic drawings and closes
itself. Keep `results.md`, `results.json` and the PNG screenshots from that folder. Try it once more with
`RenderWorkers` set to 4, 6 and 8 in `%APPDATA%\EK FastPDF\settings.json` to confirm the best worker count
(delete the setting afterwards to return to automatic).

## 4. Real drawings (5 min)
With F12 (render statistics) on, open your largest Revit electrical sheet:
- [ ] The page appears immediately as a whole (overview), then becomes sharp **all at once** (no square-by-square).
- [ ] Ctrl+wheel quickly in and out over a dense area: the picture follows the mouse without blank areas; after you
      stop, it becomes sharp. Zooming back to a previous level is sharp immediately (cached).
- [ ] Drag-pan across the sheet at high zoom: no white gaps; edges sharpen within a fraction of a second.
- [ ] Small text and thin lines are as crisp as in v1.1 at 100 %, 200 % and 800 % (also at your Windows display
      scaling). Optionally compare with ⋯ → *Sub-pixel (LCD) text smoothing* off.
- [ ] Open three large PDFs, switch tabs with Ctrl+Tab while one is still rendering: switching is instant.
- [ ] Search for a panel or circuit name: results highlight in place; Enter/F3 jumps between them.
- [ ] Print one sheet to *Microsoft Print to PDF* and to a real printer (fit to paper, rotated automatically).
- [ ] Move the window to a monitor with different scaling: content stays sharp after it settles.

## 5. Side-by-side with Foxit (optional, 5 min)
Same PDF, same window size, same monitor:
- [ ] Time from double-click to readable page.
- [ ] Ctrl+wheel 10 notches in on a dense area, then stop: time until fully sharp.
- [ ] Fast pan at high zoom: gaps or blur while moving.
Note the results; this comparison has **not** been made by the developer.

## 6. Software fallback (1 min)
- [ ] `"C:\Program Files\EK FastPDF\EKFastPDF.exe" --software-rendering` opens and renders (slower presentation, same sharpness).

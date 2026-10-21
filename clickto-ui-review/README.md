# ClickTo UI review

Design review of the ribbon buttons, Settings windows and icon for **ClickTo3D 1.0.0** and
**ClickToSection** (Revit 2024-2027). Nothing in either app's code has been changed.

- `clickto-ui-review.html`: the full review with mockups (open in a browser). It shows today's ribbon and
  Settings windows rebuilt from the code, 3 ribbon options for reaching Settings, and 3 Settings window
  directions for both apps.
- `icons/shipped-style/`: optional 48 px (150 %) and 64 px (200 %) copies of the ClickTo3D icon, made with
  the same Lanczos reduction as the shipped 32 px and 16 px files. The shipped icons stay as they are.
- `icons/icon-test-versions.png`: the sharper test versions that were compared and not chosen.

Recommendation: one visible Settings button, small, with the app's own 16 px icon (S2), and the
"Native Modern" WPF Settings window as one shared template for every app.

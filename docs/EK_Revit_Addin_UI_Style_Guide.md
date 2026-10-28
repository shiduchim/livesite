# Revit Add-in UI Style Guide (from ClickTo3D 1.0.0)

Use this as the design standard for all my Revit add-ins (ClickTo3D, ClickToSection and
future apps), so every Settings window and ribbon looks like one family.
These decisions were tested in Revit 2024-2027 on Windows 11 at 100 % and 200 % scaling.

When you (an AI) design or change UI for one of my apps:
- Follow this guide exactly unless I say otherwise.
- Never change working functionality (commands, geometry, settings storage, validation)
  when doing UI work. UI only.
- Show me mockups/renderings of options before changing code.
- Keep things simple, quiet and native-looking: it must look at home next to Revit's own
  dialogs and still look current in 5+ years.


## 1. Principles

- Match Revit, don't compete with it. Neutral greys like Revit's dialogs; blue only as a
  small accent (Revit's own accent colour is blue).
- One clear main action (Save). Everything else is quiet.
- Nothing bright, nothing saturated, no gradients, no shadows (Revit dialogs have none).
- All settings visible at once. No collapsible sections, no tabs for a few settings.
- Small and compact; don't take up screen space. Rarely changed options (units) are small.
- Hover feedback is subtle and the same for every button.
- Use complete words ("Feet", "Meters", "Settings", "Close").
- Light and dark versions that follow Revit's theme.


## 2. Ribbon

- One large button per app on the Add-Ins tab. Panel title left blank (" ") so the app
  name is not shown twice; the button label is the app name.
- Settings is NOT a separate visible ribbon button. It goes in the panel slide-out
  (RibbonPanel.AddSlideOut()), as a text-only PushButton labelled just "Settings"
  (not "ClickTo3D Settings"). Users open it with the small arrow under the main button.
  Settings stays its own PushButton/command so ribbon builders and the Quick Access
  Toolbar can still use it.
- No gear-wheel icons.
- Note: Revit draws the slide-out arrow slightly right of centre and lists slide-out
  items left-aligned. That is normal Revit behaviour; accept it.

### Ribbon icons (sharpness)
- Never ship only a 32 px icon (Windows stretches it and it looks blurry at 125-300 %).
- Ship one PNG per display scale, made from the high-resolution logo source:
  large (32 DIP): App.png 32, App_40, App_48, App_56, App_64, App_72, App_80, App_96
  small (16 DIP): App_16.png 16, App_16_20, App_16_24, App_16_28, App_16_32, App_16_36,
                  App_16_40, App_16_48
- Method "C": exact-area (box) downsampling of the source, nudged up to half a pixel so
  edges land on whole pixels. Same colours, no redrawing, not darker.
- Loader: read display scale (Graphics.DpiX / 96), pick the smallest file >= needed
  pixels, rebuild the bitmap with exact DPI (96 * pixels / logical size) so WPF draws it
  pixel for pixel. Any error -> null (never crash the ribbon).
- Keep the icon design and colours exactly as the original logo.


## 3. Settings window - layout

- WPF window built in code. Fixed size: 367 x 315 DIP (1 px frame included).
- Font: Segoe UI 12 for everything (title bar text 12, Close 11.5, Feet/Meters 11).
- Opens centred on Revit's main window and owned by it
  (WindowInteropHelper.Owner = commandData.Application.MainWindowHandle).
- No taskbar entry. Not resizable.

### Title bar (own, via WindowChrome)
- Height 30. Title on the left ("<App> Settings"), 12 px from the edge.
- Right side: the word "Close" (no X), padding 12 left/right, full title-bar height.
- WindowChrome: CaptionHeight 30, ResizeBorderThickness 0,
  GlassFrameThickness (0,0,0,1), UseAeroCaptionButtons false.
  Dragging by the title bar, Alt+F4 and Esc (Cancel is IsCancel) all still work.
- Windows 10: draw a 1 px frame; Windows 11 draws its own (keep the 1 px space either way).
- No drop shadow.

### Content area
- Padding: left 18, top 14, right 12, bottom 14.
- Units row at the top right: height 23, right margin 20 (lines up with the right edge of
  the number fields), 16 below it. No "Units" label.
- Grid: column 1 = 94 (picture), column 2 = labels (fills), column 3 = fields.
  Rows 42 px apart (comfortable, not cramped).
- Labels are short, sentence case: "Height above level", "Height below level",
  "Square size", "3D view name".
- Number field: 81 wide total (text 62 + spin column 16 + borders), height 23,
  corner radius 4, numbers right-aligned, 2 decimals. Unit text ("ft"/"m") 6 to the
  right, 14 wide.
- Text field (e.g. view name): spans label + field columns, field 134 wide, right edge
  aligned with the number fields (right margin 20).
- Picture (if the app has one): 94 x 126 canvas, logo colours, dimension lines aligned
  to the text they describe (check alignment on screen; text sits slightly below the
  geometric row centre).

### Number fields: up/down arrows
- Small ▲▼ arrows inside the right end of each number field (16 wide, two equal halves,
  divider line on the left). Up/Down keys do the same.
- Each step adds or subtracts exactly 1 in the current unit (1 ft or 1 m).
- Never step to an invalid value (0 allowed only where 0 is valid).

### Feet / Meters
- Two small joined buttons (segmented), NOT a slider/toggle switch.
- Height 19 (inner buttons 17), padding 6 left/right, font 11, outer corner radius 4,
  1 px divider between them.
- Chosen unit: shaded grey, regular weight. Other unit: field colour, grey text.
- Neutral greys only, no blue tint (rarely changed, should be quiet).

### Footer
- Top line 1 px. Padding: left 30, top 12, right 26, bottom 12.
- Left: "Reset" as a quiet text link (no box).
- Right: Cancel then Save (Save is the rightmost), 16 apart.
- Buttons: height 23, min width 71, padding 10 left/right, corner radius 4.
- Save and Cancel are the SAME button style (like Revit's OK/Cancel). Save is the
  default (Enter) button and is marked only by a soft slate-blue outline. Regular weight
  text, not bold, not filled with colour.
- Cancel is IsCancel (Esc). Save is IsDefault (Enter).
- Buttons have a 1 px slightly darker bottom edge (like Windows 11 buttons).


## 4. Colours

Light (used when Revit is in its light theme) - "Revit neutral":
| Use                         | Colour   |
|-----------------------------|----------|
| Window body / footer        | #F0F0F0  |
| Title bar                   | #F9F9F9  |
| Text                        | #1B1B1B  |
| Secondary text (units, Reset, Close) | #5F6368 |
| Fields background           | #FFFFFF  |
| Field border                | #C3C8CF  |
| Field focus border          | #5D75A5  |
| Footer line                 | #E1E1E1  |
| Windows 10 frame            | #BDBDBD  |
| Button fill (Save, Cancel)  | #FDFDFD  |
| Cancel border / bottom edge | #D0D0D0 / #BABABA |
| Save outline / bottom edge  | #9DB0D8 / #8A9ECA (soft slate) |
| Button + arrow hover        | #F2F7FC (very soft blue); hover outline #9DB0D8 |
| Button + arrow pressed      | #E6EFF9 |
| Feet/Meters border          | #D1D1D1 |
| Feet/Meters chosen          | #E8E8E8 (pressed #E2E2E2) |
| Feet/Meters other: text / hover / pressed | #6E6E6E / #F2F2F2 / #E6E6E6 |
| Close hover / pressed       | #EBEBEB / #E0E0E0 (plain grey, no blue) |
| Dimension lines in picture  | #6B7280 |

Dark (used automatically when Revit is in its dark theme):
| Use                         | Colour   |
|-----------------------------|----------|
| Window body / footer        | #2B2B2B  |
| Title bar                   | #323232  |
| Text                        | #E6E6E6  |
| Secondary text              | #A8A8A8  |
| Fields background / border  | #1F1F1F / #575757 |
| Field focus border          | #8EA3CF  |
| Footer line                 | #3C3C3C  |
| Frame                       | #4A4A4A  |
| Button fill / border / bottom | #383838 / #555555 / #4A4A4A |
| Save outline / bottom edge  | #7E93C2 / #6D82B3 |
| Hover / pressed             | #41454D / #4B5059 |
| Feet/Meters border / chosen / pressed | #555555 / #4A4A4A / #545454 |
| Feet/Meters other: text / hover / pressed | #A8A8A8 / #3D3D3D / #474747 |
| Close hover / pressed       | #3D3D3D / #474747 |
| Dimension lines             | #9AA0A8 |

Logo / drawing colours (same in light and dark): slate #758DBB, dark slate #5D75A5,
box fills #E1E8F5 and #EEF2F9, level plane #758DBB at 10 %.

Rules:
- Text boxes must set Foreground and CaretBrush from the palette (otherwise dark mode
  shows black text).
- Keep every colour in one palette object per theme; never hard-code colours elsewhere.


## 5. Dark mode

- Read Revit's theme when the window opens:
  UIThemeManager.CurrentTheme == UITheme.Dark (Revit 2024+), in try/catch -> light.
- It follows Revit, not Windows directly. (If Revit is set to follow Windows, it follows
  Windows through Revit.)
- On Windows 11 ask for a dark frame: DwmSetWindowAttribute(hwnd, 20, 1). Ignore errors.


## 6. Things we tried and rejected (don't suggest again)

- Gear-wheel Settings icon; separate visible Settings button.
- Slider/toggle switch for Feet/Meters; "Units" label.
- Bright blue Save button; bold Save; filled tinted Save.
- Windows "X" close button and circle-only close buttons (we use the word "Close").
- Drop shadows (Revit dialogs have none; may not render on all PCs).
- Warm, green or blue-tinted window backgrounds (looked off next to Revit's greys).
- Strong Windows-blue hover (#E5F1FB with #7FA7DA outline) - too loud on Cancel.
- Collapsible sections, helper paragraphs, header banners, extra decoration.


## 7. Process

1. Show renderings/mockups of a few options before coding.
2. When I pick, change UI code only; keep approved logic copied verbatim.
3. Build for Revit 2024 (net48), 2025/2026 (net8.0-windows), 2027 (net10.0-windows).
4. Test in Revit light and dark theme, at 100 % and the user's scaling, Windows 10 and 11.
5. For App Store packages: keep version, ProductCode/UpgradeCode, AddInId unchanged unless
   I say so; zip with forward-slash paths.

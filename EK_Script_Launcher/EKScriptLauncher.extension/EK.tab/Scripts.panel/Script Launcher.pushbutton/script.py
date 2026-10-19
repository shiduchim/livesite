# -*- coding: utf-8 -*-
"""
EK Script Launcher
==================

pyRevit / Revit 2024-2027 friendly Windows Forms launcher.

- Every script is shown as its own row: a coloured initials badge, the
  script name, and underneath it the file name plus the first line of the
  script's docstring (when it has one).
- Search filters as you type and highlights the matching text.
  Up / Down move the selection, Enter launches, Esc closes.
- Window automatically widens for long script names, up to the screen width.
  A horizontal scrollbar appears only if a name is still too long.
- Window is manually resizable; Settings / Refresh / Cancel stay aligned.
- Large LAUNCH SCRIPT button stays full-width at the bottom and shows which
  script it will run.
- Settings window contains only the two folder paths + Browse + Save.
- Settings stores both the launcher folder and the Quick Launch folder.
- Launcher closes after one script run.
- Only one launcher window can be open in each Revit instance.
"""

import clr
import os
import re
import json
import traceback

clr.AddReference("System")
clr.AddReference("System.Drawing")
clr.AddReference("System.Windows.Forms")

import System
from System.Drawing import (
    Color,
    ContentAlignment,
    Font,
    FontStyle,
    Graphics,
    Pen,
    Point,
    Rectangle,
    Size,
    SolidBrush,
)
from System.Drawing.Drawing2D import GraphicsPath, SmoothingMode
from System.Windows.Forms import (
    Application,
    AnchorStyles,
    BorderStyle,
    Button,
    Cursors,
    DialogResult,
    DockStyle,
    DrawItemState,
    DrawMode,
    FlatStyle,
    FolderBrowserDialog,
    Form,
    FormBorderStyle,
    FormStartPosition,
    FormWindowState,
    Keys,
    Label,
    ListBox,
    MessageBox,
    MessageBoxButtons,
    MessageBoxIcon,
    MouseButtons,
    Padding,
    Panel,
    Screen,
    SystemInformation,
    TextBox,
    TextFormatFlags,
    TextRenderer,
    ToolTip,
)

TITLE = "EK Script Launcher"


# ----------------------------------------------------------------------
# Look and feel
# ----------------------------------------------------------------------

def _screen_scale():
    try:
        graphics = Graphics.FromHwnd(System.IntPtr.Zero)
        try:
            return max(1.0, graphics.DpiX / 96.0)
        finally:
            graphics.Dispose()
    except:
        return 1.0


SCALE = _screen_scale()


def dp(value):
    """Convert a 96-DPI layout size to pixels on this screen."""
    return int(round(value * SCALE))


# Muted blue, intentionally less bright/tiring than standard Revit blue.
BLUE = Color.FromArgb(70, 101, 128)
BLUE_HOVER = Color.FromArgb(82, 116, 146)
BLUE_DOWN = Color.FromArgb(58, 85, 109)
BLUE_SOFT_TEXT = Color.FromArgb(208, 221, 233)

WINDOW_BG = Color.FromArgb(241, 243, 246)
SURFACE = Color.White
BORDER = Color.FromArgb(211, 216, 224)
TEXT = Color.FromArgb(30, 38, 50)
TEXT_MUTED = Color.FromArgb(104, 112, 125)
TEXT_FAINT = Color.FromArgb(150, 157, 168)

ROW_HOVER = Color.FromArgb(245, 247, 250)
ROW_SELECTED = Color.FromArgb(225, 233, 241)
ROW_DIVIDER = Color.FromArgb(234, 237, 241)
MATCH_BG = Color.FromArgb(255, 224, 138)
BUTTON_HOVER = Color.FromArgb(234, 238, 243)
BUTTON_DOWN = Color.FromArgb(222, 228, 235)

# Badge colours: muted so a long list stays calm, but different enough that
# neighbouring scripts no longer blend together.
BADGE_COLORS = [
    Color.FromArgb(70, 101, 128),   # slate blue
    Color.FromArgb(38, 124, 118),   # teal
    Color.FromArgb(78, 128, 66),    # green
    Color.FromArgb(168, 112, 30),   # amber
    Color.FromArgb(168, 76, 60),    # brick
    Color.FromArgb(122, 82, 140),   # plum
    Color.FromArgb(74, 84, 160),    # indigo
    Color.FromArgb(148, 78, 108),   # rose
    Color.FromArgb(92, 106, 60),    # olive
    Color.FromArgb(48, 112, 158),   # sky
]

FONT_UI = Font("Segoe UI", 9.75)
FONT_TITLE = Font("Segoe UI Semibold", 14.0)
FONT_PATH = Font("Segoe UI", 9.0)
FONT_SEARCH = Font("Segoe UI", 11.0)
FONT_COUNT = Font("Segoe UI", 9.0)
FONT_NAME = Font("Segoe UI Semibold", 10.5)
FONT_DETAIL = Font("Segoe UI", 8.75)
FONT_BADGE = Font("Segoe UI Semibold", 10.0)
FONT_EMPTY = Font("Segoe UI", 10.5)
FONT_LAUNCH = Font("Segoe UI", 13.0, FontStyle.Bold)
FONT_LAUNCH_SUB = Font("Segoe UI", 9.0)

MIN_CLIENT_W = dp(560)
MAX_CLIENT_W_FALLBACK = dp(1100)
WINDOW_H = dp(660)

LEFT = dp(18)
RIGHT = dp(18)
TOP = dp(16)
GAP = dp(12)

TOP_BUTTON_H = dp(32)
SEARCH_TOP = dp(84)
SEARCH_H = dp(40)
LIST_TOP = SEARCH_TOP + SEARCH_H + GAP
LAUNCH_H = dp(62)

ROW_H = dp(52)
ROW_PAD = dp(14)
BADGE_SIZE = dp(34)
BADGE_GAP = dp(12)
COUNT_W = dp(120)

# "None" is a keyword in Python 3, so BorderStyle.None cannot be written directly.
NO_BORDER = getattr(BorderStyle, "None")

TEXT_FLAGS = (
    TextFormatFlags.NoPadding |
    TextFormatFlags.SingleLine |
    TextFormatFlags.NoPrefix
)

CONFIG_DIR = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~")),
    "EK",
    "ScriptLauncher"
)
CONFIG_PATH = os.path.join(CONFIG_DIR, "settings.json")


# ----------------------------------------------------------------------
# Configuration
# ----------------------------------------------------------------------

def ensure_dir(path):
    if not os.path.isdir(path):
        try:
            os.makedirs(path)
        except:
            pass


def read_json(path):
    try:
        if not os.path.isfile(path):
            return None
        with open(path, "r") as stream:
            value = json.load(stream)
        if isinstance(value, dict):
            return value
    except:
        pass
    return None


def load_launcher_settings():
    """
    Primary location plus a few harmless legacy guesses so replacing an
    older launcher has a chance to keep the user's existing folder.
    """
    candidates = [
        CONFIG_PATH,
        os.path.join(
            os.environ.get("APPDATA", ""),
            "EKScriptLauncher",
            "settings.json"
        ),
        os.path.join(
            os.environ.get("APPDATA", ""),
            "EK Script Launcher",
            "settings.json"
        ),
    ]

    for path in candidates:
        data = read_json(path)
        if not data:
            continue

        scripts_folder = ""
        for key in ("scripts_folder", "script_folder", "folder", "path"):
            folder = data.get(key)
            if folder and os.path.isdir(folder):
                scripts_folder = folder
                break

        quick_folder = ""
        for key in ("quick_launch_folder", "quick_folder", "secondary_folder"):
            folder = data.get(key)
            if folder:
                quick_folder = folder
                break

        return {
            "scripts_folder": scripts_folder,
            "quick_launch_folder": quick_folder,
        }

    return {
        "scripts_folder": "",
        "quick_launch_folder": "",
    }


def save_launcher_settings(scripts_folder, quick_launch_folder):
    ensure_dir(CONFIG_DIR)
    data = {
        "scripts_folder": scripts_folder,
        "quick_launch_folder": quick_launch_folder,
    }
    with open(CONFIG_PATH, "w") as stream:
        json.dump(data, stream, indent=2)


# ----------------------------------------------------------------------
# Script handling
# ----------------------------------------------------------------------

def display_name_from_path(path):
    name = os.path.splitext(os.path.basename(path))[0]
    name = name.replace("_", " ")
    return name.strip()


def get_python_files(folder):
    result = []

    if not folder or not os.path.isdir(folder):
        return result

    try:
        names = os.listdir(folder)
    except:
        return result

    for filename in names:
        full_path = os.path.join(folder, filename)

        if not os.path.isfile(full_path):
            continue

        if not filename.lower().endswith(".py"):
            continue

        # Do not accidentally list this launcher if it lives in the scripts dir.
        try:
            if os.path.abspath(full_path) == os.path.abspath(__file__):
                continue
        except:
            pass

        result.append((display_name_from_path(full_path), full_path))

    result.sort(key=lambda pair: pair[0].lower())
    return result


MODULE_DOC_RE = re.compile(
    r'^\s*(?:#[^\n]*\n\s*)*[rRuU]{0,2}("""|\'\'\')(.*?)\1',
    re.DOTALL
)
DOC_ASSIGN_RE = re.compile(
    r'^__doc__\s*=\s*[rRuU]{0,2}("""|\'\'\'|"|\')(.*?)\1',
    re.DOTALL | re.MULTILINE
)


def script_summary(path):
    """
    First line of the script's docstring (or __doc__ = "..."), used as a
    short description under the script name. Empty if there is none.
    """
    try:
        with open(path, "rb") as stream:
            raw = stream.read(8192)
        text = raw.decode("utf-8-sig", "ignore").replace("\r\n", "\n")
    except:
        return ""

    match = MODULE_DOC_RE.match(text) or DOC_ASSIGN_RE.search(text)
    if not match:
        return ""

    # Skip blank lines, ==== underlines and a heading that just repeats
    # the script name.
    name = display_name_from_path(path).lower()
    for line in match.group(2).split("\n"):
        line = " ".join(line.split())
        if not line.strip("=-~*#"):
            continue
        if line.replace("_", " ").lower() == name:
            continue
        return line[:200]
    return ""


def badge_initials(display_name):
    words = re.findall(r"[^\W_]+", display_name, re.UNICODE)
    if not words:
        return "Py"

    if words[0].isdigit():
        return words[0][:2]

    if len(words) == 1:
        parts = re.findall(r"[A-Z][a-z0-9]*|[a-z0-9]+", words[0])
        if len(parts) >= 2:
            return (parts[0][0] + parts[1][0]).upper()
        return words[0][:2].capitalize()

    return (words[0][0] + words[1][0]).upper()


def badge_color(display_name):
    # Deterministic, so a script keeps its colour between sessions.
    value = 0
    for char in display_name.lower():
        value = (value * 31 + ord(char)) & 0xFFFFFFF
    return BADGE_COLORS[value % len(BADGE_COLORS)]


def run_python_script(path):
    """
    Execute the chosen .py inside the current pyRevit/Revit process.

    Each script gets a fresh global dictionary while retaining __revit__ when
    the launcher itself has it.
    """
    try:
        with open(path, "rb") as stream:
            raw = stream.read()

        # UTF-8 BOM safe.
        try:
            source = raw.decode("utf-8-sig")
        except:
            try:
                source = raw.decode("utf-8")
            except:
                source = raw.decode("latin-1")

        script_globals = {
            "__name__": "__main__",
            "__file__": path,
            "__builtins__": __builtins__,
        }

        try:
            script_globals["__revit__"] = __revit__
        except:
            pass

        compiled = compile(source, path, "exec")
        exec(compiled, script_globals, script_globals)

        return True, ""

    except System.Exception as ex:
        return False, str(ex)

    except Exception as ex:
        return False, traceback.format_exc()


# ----------------------------------------------------------------------
# Drawing helpers
# ----------------------------------------------------------------------

def fill_rect(graphics, color, rect):
    brush = SolidBrush(color)
    try:
        graphics.FillRectangle(brush, rect)
    finally:
        brush.Dispose()


def rounded_path(rect, radius):
    diameter = radius * 2
    path = GraphicsPath()
    path.AddArc(rect.X, rect.Y, diameter, diameter, 180.0, 90.0)
    path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270.0, 90.0)
    path.AddArc(
        rect.Right - diameter, rect.Bottom - diameter,
        diameter, diameter, 0.0, 90.0
    )
    path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90.0, 90.0)
    path.CloseFigure()
    return path


def fill_rounded(graphics, color, rect, radius):
    path = rounded_path(rect, radius)
    brush = SolidBrush(color)
    try:
        graphics.SmoothingMode = SmoothingMode.AntiAlias
        graphics.FillPath(brush, path)
    finally:
        graphics.SmoothingMode = SmoothingMode.Default
        brush.Dispose()
        path.Dispose()


def text_width(text, font, graphics=None):
    try:
        if graphics is not None:
            return TextRenderer.MeasureText(
                graphics, text, font, Size(32767, 32767), TEXT_FLAGS
            ).Width
        return TextRenderer.MeasureText(
            text, font, Size(32767, 32767), TEXT_FLAGS
        ).Width
    except:
        return int(len(text) * font.Size * SCALE)


def style_button(button, primary=False):
    button.FlatStyle = FlatStyle.Flat
    button.Cursor = Cursors.Hand
    button.UseVisualStyleBackColor = False
    if primary:
        button.BackColor = BLUE
        button.ForeColor = Color.White
        button.FlatAppearance.BorderSize = 0
        button.FlatAppearance.MouseOverBackColor = BLUE_HOVER
        button.FlatAppearance.MouseDownBackColor = BLUE_DOWN
    else:
        button.BackColor = SURFACE
        button.ForeColor = TEXT
        button.FlatAppearance.BorderColor = BORDER
        button.FlatAppearance.MouseOverBackColor = BUTTON_HOVER
        button.FlatAppearance.MouseDownBackColor = BUTTON_DOWN


def show_message(owner, text, icon):
    # The owner may already be closed (e.g. after a script run).
    if owner is not None and not owner.IsDisposed:
        return MessageBox.Show(owner, text, TITLE, MessageBoxButtons.OK, icon)
    return MessageBox.Show(text, TITLE, MessageBoxButtons.OK, icon)


# ----------------------------------------------------------------------
# Settings dialog
# ----------------------------------------------------------------------

class SettingsForm(Form):
    def __init__(self, current_folder, current_quick_folder):
        Form.__init__(self)

        self.Text = "Launcher Settings"
        self.ShowIcon = False
        self.StartPosition = FormStartPosition.CenterParent
        self.FormBorderStyle = FormBorderStyle.FixedDialog
        self.MaximizeBox = False
        self.MinimizeBox = False
        self.ShowInTaskbar = False
        self.ClientSize = Size(dp(640), dp(262))
        self.BackColor = WINDOW_BG
        self.Font = FONT_UI
        self.KeyPreview = True
        self.KeyDown += self.on_key_down
        self.saved_folder = None
        self.saved_quick_folder = None

        self.path_box = self.add_folder_row(
            dp(18),
            "Script Launcher folder",
            "Every .py file in this folder appears in the launcher list.",
            current_folder,
            self.on_browse
        )

        self.quick_path_box = self.add_folder_row(
            dp(108),
            "Quick Launch folder (exactly one .py file)",
            "The Quick Launch ribbon button runs the single script in here. "
            "Leave blank if unused.",
            current_quick_folder,
            self.on_quick_browse
        )

        save = Button()
        save.Text = "Save"
        save.Font = Font("Segoe UI Semibold", 9.75)
        save.Size = Size(dp(110), dp(36))
        save.Location = Point(
            self.ClientSize.Width - dp(18) - save.Width,
            self.ClientSize.Height - dp(18) - save.Height
        )
        style_button(save, primary=True)
        save.Click += self.on_save
        self.Controls.Add(save)

        self.AcceptButton = save

    def add_folder_row(self, top, title, hint, value, on_browse):
        label = Label()
        label.Text = title
        label.Font = Font("Segoe UI Semibold", 9.75)
        label.ForeColor = TEXT
        label.AutoSize = True
        label.Location = Point(dp(18), top)
        self.Controls.Add(label)

        hint_label = Label()
        hint_label.Text = hint
        hint_label.ForeColor = TEXT_MUTED
        hint_label.Font = FONT_PATH
        hint_label.AutoSize = True
        hint_label.Location = Point(dp(18), top + dp(22))
        self.Controls.Add(hint_label)

        browse = Button()
        browse.Text = "Browse..."
        browse.Size = Size(dp(104), dp(32))
        browse.Location = Point(
            self.ClientSize.Width - dp(18) - browse.Width,
            top + dp(46)
        )
        style_button(browse)
        browse.Click += on_browse
        self.Controls.Add(browse)

        # Borderless text box inside a white panel, so the field is the same
        # height as the Browse button.
        field = Panel()
        field.BackColor = SURFACE
        field.Cursor = Cursors.IBeam
        field.Location = Point(dp(18), browse.Top)
        field.Size = Size(browse.Left - dp(8) - dp(18), browse.Height)
        self.Controls.Add(field)

        box = TextBox()
        box.Text = value or ""
        box.BorderStyle = NO_BORDER
        box.BackColor = SURFACE
        box.ForeColor = TEXT
        box.Location = Point(dp(8), (field.Height - box.Height) // 2)
        box.Width = field.Width - dp(16)
        field.Controls.Add(box)

        def on_field_paint(sender, args):
            pen = Pen(BLUE if box.Focused else BORDER, 1.0)
            try:
                args.Graphics.DrawRectangle(
                    pen, 0, 0, field.Width - 1, field.Height - 1
                )
            finally:
                pen.Dispose()

        def on_box_focus_changed(sender, args):
            field.Invalidate()

        def on_field_click(sender, args):
            box.Focus()

        field.Paint += on_field_paint
        field.Click += on_field_click
        box.GotFocus += on_box_focus_changed
        box.LostFocus += on_box_focus_changed
        return box

    def on_key_down(self, sender, args):
        if args.KeyCode == Keys.Escape:
            self.Close()

    def on_browse(self, sender, args):
        dialog = FolderBrowserDialog()
        dialog.Description = "Choose the folder containing your Python scripts"

        if self.path_box.Text and os.path.isdir(self.path_box.Text):
            dialog.SelectedPath = self.path_box.Text

        if dialog.ShowDialog(self) == DialogResult.OK:
            self.path_box.Text = dialog.SelectedPath

    def on_quick_browse(self, sender, args):
        dialog = FolderBrowserDialog()
        dialog.Description = (
            "Choose a folder containing exactly one Python script"
        )

        if self.quick_path_box.Text and os.path.isdir(self.quick_path_box.Text):
            dialog.SelectedPath = self.quick_path_box.Text

        if dialog.ShowDialog(self) == DialogResult.OK:
            self.quick_path_box.Text = dialog.SelectedPath

    def on_save(self, sender, args):
        folder = self.path_box.Text.strip()
        quick_folder = self.quick_path_box.Text.strip()

        if not folder or not os.path.isdir(folder):
            show_message(
                self,
                "Choose a valid scripts folder.",
                MessageBoxIcon.Warning
            )
            return

        if quick_folder and not os.path.isdir(quick_folder):
            show_message(
                self,
                "Choose a valid Quick Launch folder, or leave it blank.",
                MessageBoxIcon.Warning
            )
            return

        if quick_folder:
            quick_scripts = get_python_files(quick_folder)

            if len(quick_scripts) != 1:
                show_message(
                    self,
                    (
                        "The Quick Launch folder must contain exactly one "
                        ".py file.\n\nFound: {0}"
                    ).format(len(quick_scripts)),
                    MessageBoxIcon.Warning
                )
                return

        try:
            save_launcher_settings(folder, quick_folder)
        except Exception as ex:
            show_message(
                self,
                "Could not save settings:\n\n" + str(ex),
                MessageBoxIcon.Error
            )
            return

        self.saved_folder = folder
        self.saved_quick_folder = quick_folder
        self.DialogResult = DialogResult.OK
        self.Close()


# ----------------------------------------------------------------------
# Main launcher
# ----------------------------------------------------------------------

class LauncherForm(Form):
    def __init__(self):
        Form.__init__(self)

        self.Text = TITLE
        self.ShowIcon = False
        self.StartPosition = FormStartPosition.CenterScreen
        self.FormBorderStyle = FormBorderStyle.Sizable
        self.ClientSize = Size(MIN_CLIENT_W, WINDOW_H)
        self.MinimumSize = Size(
            self.Width,
            self.Height - WINDOW_H + dp(460)
        )
        self.BackColor = WINDOW_BG
        self.Font = FONT_UI

        try:
            self.DoubleBuffered = True
        except:
            pass

        saved_settings = load_launcher_settings()
        self.scripts_folder = saved_settings.get("scripts_folder", "")
        self.quick_launch_folder = saved_settings.get(
            "quick_launch_folder",
            ""
        )
        self.all_scripts = []
        self.visible_scripts = []
        self.summaries = {}
        self.query = ""
        self.hover_index = -1
        self.launch_hover = False
        self.launch_pressed = False

        self.build_ui()
        self.refresh_scripts()

    # -- layout ---------------------------------------------------------

    def build_ui(self):
        width = self.ClientSize.Width

        # Header: title + current folder on the left, buttons on the right.
        self.title_label = Label()
        self.title_label.Text = TITLE
        self.title_label.Font = FONT_TITLE
        self.title_label.ForeColor = TEXT
        self.title_label.AutoSize = True
        self.title_label.Location = Point(LEFT - dp(3), TOP)
        self.Controls.Add(self.title_label)

        self.folder_label = Label()
        self.folder_label.Font = FONT_PATH
        self.folder_label.ForeColor = TEXT_MUTED
        self.folder_label.AutoSize = False
        self.folder_label.AutoEllipsis = True
        self.folder_label.Location = Point(LEFT - dp(1), TOP + dp(34))
        self.folder_label.Size = Size(width - LEFT - RIGHT, dp(20))
        self.folder_label.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.Controls.Add(self.folder_label)

        self.settings_btn = self.add_top_button("Settings", self.on_settings)
        self.refresh_btn = self.add_top_button("Refresh", self.on_refresh)
        self.cancel_btn = self.add_top_button("Cancel", self.on_cancel)

        self.size_top_button(self.settings_btn, dp(88))
        self.size_top_button(self.refresh_btn, dp(84))
        self.size_top_button(self.cancel_btn, dp(80))

        # Search field: a white box with a magnifier, placeholder and count.
        self.search_panel = Panel()
        self.search_panel.BackColor = SURFACE
        self.search_panel.Location = Point(LEFT, SEARCH_TOP)
        self.search_panel.Size = Size(width - LEFT - RIGHT, SEARCH_H)
        self.search_panel.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.search_panel.Cursor = Cursors.IBeam
        self.search_panel.Paint += self.on_search_panel_paint
        self.search_panel.Click += self.on_focus_search
        self.Controls.Add(self.search_panel)

        self.search_box = TextBox()
        self.search_box.BorderStyle = NO_BORDER
        self.search_box.Font = FONT_SEARCH
        self.search_box.BackColor = SURFACE
        self.search_box.ForeColor = TEXT
        self.search_box.Location = Point(
            dp(38),
            (SEARCH_H - self.search_box.Height) // 2
        )
        self.search_box.Width = self.search_panel.Width - dp(38) - COUNT_W - dp(8)
        self.search_box.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.search_box.TextChanged += self.on_search_changed
        self.search_box.KeyDown += self.on_search_key_down
        self.search_box.GotFocus += self.on_search_focus_changed
        self.search_box.LostFocus += self.on_search_focus_changed
        self.search_panel.Controls.Add(self.search_box)

        self.placeholder = Label()
        self.placeholder.Text = "Search scripts..."
        self.placeholder.Font = FONT_SEARCH
        self.placeholder.ForeColor = TEXT_FAINT
        self.placeholder.BackColor = SURFACE
        self.placeholder.AutoSize = True
        self.placeholder.Cursor = Cursors.IBeam
        self.placeholder.Location = Point(
            self.search_box.Left + dp(2),
            self.search_box.Top
        )
        self.placeholder.Click += self.on_focus_search
        self.search_panel.Controls.Add(self.placeholder)
        self.placeholder.BringToFront()

        self.count_label = Label()
        self.count_label.Font = FONT_COUNT
        self.count_label.ForeColor = TEXT_MUTED
        self.count_label.BackColor = SURFACE
        self.count_label.AutoSize = False
        self.count_label.TextAlign = ContentAlignment.MiddleRight
        self.count_label.Size = Size(COUNT_W, SEARCH_H - 2)
        self.count_label.Location = Point(
            self.search_panel.Width - COUNT_W - dp(12),
            1
        )
        self.count_label.Anchor = AnchorStyles.Top | AnchorStyles.Right
        self.count_label.Click += self.on_focus_search
        self.search_panel.Controls.Add(self.count_label)

        # Script list: 1px border around an owner-drawn list.
        self.launch_btn = Button()
        self.launch_btn.Text = "LAUNCH SCRIPT"
        self.launch_btn.Font = FONT_LAUNCH
        self.launch_btn.ForeColor = Color.White
        self.launch_btn.BackColor = BLUE
        self.launch_btn.FlatStyle = FlatStyle.Flat
        self.launch_btn.FlatAppearance.BorderSize = 0
        self.launch_btn.Cursor = Cursors.Hand
        self.launch_btn.Location = Point(
            LEFT,
            self.ClientSize.Height - LAUNCH_H - dp(18)
        )
        self.launch_btn.Size = Size(width - LEFT - RIGHT, LAUNCH_H)
        self.launch_btn.Anchor = (
            AnchorStyles.Bottom |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.launch_btn.Click += self.on_launch
        self.launch_btn.Paint += self.on_launch_paint
        self.launch_btn.MouseEnter += self.on_launch_mouse_enter
        self.launch_btn.MouseLeave += self.on_launch_mouse_leave
        self.launch_btn.MouseDown += self.on_launch_mouse_down
        self.launch_btn.MouseUp += self.on_launch_mouse_up
        self.Controls.Add(self.launch_btn)

        self.list_frame = Panel()
        self.list_frame.BackColor = BORDER
        self.list_frame.Padding = Padding(1)
        self.list_frame.Location = Point(LEFT, LIST_TOP)
        self.list_frame.Size = Size(
            width - LEFT - RIGHT,
            self.launch_btn.Top - GAP - LIST_TOP
        )
        self.list_frame.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Bottom |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.Controls.Add(self.list_frame)

        self.list_box = ListBox()
        self.list_box.BorderStyle = NO_BORDER
        self.list_box.Dock = DockStyle.Fill
        self.list_box.BackColor = SURFACE
        self.list_box.Font = FONT_NAME
        self.list_box.DrawMode = DrawMode.OwnerDrawFixed
        self.list_box.ItemHeight = ROW_H
        self.list_box.HorizontalScrollbar = True
        self.list_box.IntegralHeight = False
        self.list_box.DrawItem += self.on_draw_item
        self.list_box.DoubleClick += self.on_launch
        self.list_box.SelectedIndexChanged += self.on_selection_changed
        self.list_box.MouseMove += self.on_list_mouse_move
        self.list_box.MouseLeave += self.on_list_mouse_leave
        self.list_box.KeyPress += self.on_list_key_press
        self.list_box.Resize += self.on_list_resize
        self.list_frame.Controls.Add(self.list_box)

        self.empty_label = Label()
        self.empty_label.Dock = DockStyle.Fill
        self.empty_label.BackColor = SURFACE
        self.empty_label.ForeColor = TEXT_MUTED
        self.empty_label.Font = FONT_EMPTY
        self.empty_label.TextAlign = ContentAlignment.MiddleCenter
        self.empty_label.Visible = False
        self.list_frame.Controls.Add(self.empty_label)
        self.empty_label.BringToFront()

        self.tooltip = ToolTip()
        self.tooltip.InitialDelay = 700

        self.AcceptButton = self.launch_btn
        self.CancelButton = self.cancel_btn
        self.ActiveControl = self.search_box

        self.Resize += self.on_form_resize
        self.Shown += self.on_shown

        self.position_top_buttons()

    def add_top_button(self, text, handler):
        button = Button()
        button.Text = text
        button.Font = FONT_UI
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right
        style_button(button)
        button.Click += handler
        self.Controls.Add(button)
        return button

    def size_top_button(self, button, minimum_width):
        width = text_width(button.Text, button.Font)
        button.Size = Size(
            max(int(minimum_width), int(width + dp(30))),
            TOP_BUTTON_H
        )

    def position_top_buttons(self):
        right = self.ClientSize.Width - RIGHT
        top = TOP + dp(1)

        self.cancel_btn.Location = Point(right - self.cancel_btn.Width, top)
        self.refresh_btn.Location = Point(
            self.cancel_btn.Left - dp(8) - self.refresh_btn.Width,
            top
        )
        self.settings_btn.Location = Point(
            self.refresh_btn.Left - dp(8) - self.settings_btn.Width,
            top
        )

    def on_form_resize(self, sender, args):
        try:
            self.position_top_buttons()
        except:
            pass

    def on_shown(self, sender, args):
        self.search_box.Focus()

    def max_allowed_client_width(self):
        try:
            working = Screen.FromControl(self).WorkingArea
            return max(MIN_CLIENT_W, working.Width - dp(70))
        except:
            return MAX_CLIENT_W_FALLBACK

    def row_width_for_name(self, display_name):
        return (
            ROW_PAD + BADGE_SIZE + BADGE_GAP +
            text_width(display_name, FONT_NAME) +
            ROW_PAD
        )

    def longest_row_width(self):
        longest = 0
        for display_name, path in self.all_scripts:
            longest = max(longest, self.row_width_for_name(display_name))
        return longest

    def ideal_width_for_names(self):
        # Row content + list border/scrollbar + outer margins.
        needed = (
            self.longest_row_width() +
            SystemInformation.VerticalScrollBarWidth + 2 +
            LEFT + RIGHT
        )

        # Also guarantee room for the title and the top buttons.
        top_buttons_needed = (
            LEFT +
            text_width(TITLE, FONT_TITLE) +
            dp(24) +
            self.settings_btn.Width +
            dp(8) +
            self.refresh_btn.Width +
            dp(8) +
            self.cancel_btn.Width +
            RIGHT
        )
        needed = max(needed, top_buttons_needed, MIN_CLIENT_W)

        return min(needed, self.max_allowed_client_width())

    def expand_for_long_names(self):
        wanted = self.ideal_width_for_names()

        # Only expand automatically. Do not shrink while the user works.
        if wanted > self.ClientSize.Width:
            old_center_x = self.Left + self.Width // 2
            old_center_y = self.Top + self.Height // 2

            self.ClientSize = Size(wanted, self.ClientSize.Height)

            # Keep the expanded launcher approximately centered.
            try:
                working = Screen.FromControl(self).WorkingArea
                new_left = old_center_x - self.Width // 2
                new_top = old_center_y - self.Height // 2

                new_left = max(
                    working.Left,
                    min(new_left, working.Right - self.Width)
                )
                new_top = max(
                    working.Top,
                    min(new_top, working.Bottom - self.Height)
                )

                self.Left = new_left
                self.Top = new_top
            except:
                pass

            self.position_top_buttons()

    # -- data -----------------------------------------------------------

    def refresh_scripts(self):
        self.all_scripts = get_python_files(self.scripts_folder)
        self.summaries = {}
        for display_name, path in self.all_scripts:
            self.summaries[path] = script_summary(path)

        self.folder_label.Text = (
            self.scripts_folder or "No scripts folder selected"
        )
        self.tooltip.SetToolTip(self.folder_label, self.scripts_folder or "")

        # Lets the list scroll sideways if a name is wider than the window.
        self.list_box.HorizontalExtent = self.longest_row_width()

        self.expand_for_long_names()
        self.apply_filter()

    def apply_filter(self):
        self.query = self.search_box.Text.strip().lower()
        self.placeholder.Visible = not self.search_box.Text

        if self.query:
            self.visible_scripts = [
                pair for pair in self.all_scripts
                if self.query in pair[0].lower()
            ]
        else:
            self.visible_scripts = list(self.all_scripts)

        self.hover_index = -1
        self.list_box.BeginUpdate()
        try:
            self.list_box.Items.Clear()

            for display_name, path in self.visible_scripts:
                self.list_box.Items.Add(display_name)

            if self.list_box.Items.Count > 0:
                self.list_box.SelectedIndex = 0
        finally:
            self.list_box.EndUpdate()

        self.update_status()
        self.launch_btn.Invalidate()

    def update_status(self):
        total = len(self.all_scripts)
        shown = len(self.visible_scripts)

        if self.query:
            self.count_label.Text = "{0} of {1}".format(shown, total)
        elif total == 1:
            self.count_label.Text = "1 script"
        else:
            self.count_label.Text = "{0} scripts".format(total)

        if shown:
            self.empty_label.Visible = False
            return

        if not self.scripts_folder or not os.path.isdir(self.scripts_folder):
            message = (
                "No scripts folder selected.\n"
                "Click Settings to choose one."
            )
        elif not total:
            message = (
                "This folder has no .py files yet.\n"
                "Add scripts to it, then click Refresh."
            )
        else:
            message = u"No scripts match “{0}”".format(
                self.search_box.Text.strip()
            )

        self.empty_label.Text = message
        self.empty_label.Visible = True

    def selected_script(self):
        index = self.list_box.SelectedIndex

        if index < 0:
            return None

        if index >= len(self.visible_scripts):
            return None

        return self.visible_scripts[index]

    # -- list drawing ---------------------------------------------------

    def on_draw_item(self, sender, args):
        graphics = args.Graphics
        bounds = args.Bounds
        index = args.Index

        if index < 0 or index >= len(self.visible_scripts):
            fill_rect(graphics, SURFACE, bounds)
            return

        display_name, path = self.visible_scripts[index]
        selected = args.State.HasFlag(DrawItemState.Selected)

        if selected:
            back = ROW_SELECTED
        elif index == self.hover_index:
            back = ROW_HOVER
        else:
            back = SURFACE

        fill_rect(graphics, back, bounds)
        fill_rect(
            graphics,
            ROW_DIVIDER,
            Rectangle(bounds.Left, bounds.Bottom - 1, bounds.Width, 1)
        )

        if selected:
            fill_rect(
                graphics,
                BLUE,
                Rectangle(bounds.Left, bounds.Top, dp(4), bounds.Height)
            )

        # Coloured initials badge.
        badge = Rectangle(
            bounds.Left + ROW_PAD,
            bounds.Top + (bounds.Height - BADGE_SIZE) // 2,
            BADGE_SIZE,
            BADGE_SIZE
        )
        fill_rounded(graphics, badge_color(display_name), badge, dp(8))
        TextRenderer.DrawText(
            graphics,
            badge_initials(display_name),
            FONT_BADGE,
            badge,
            Color.White,
            TEXT_FLAGS |
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter
        )

        # Name on the first line, file name + description on the second.
        text_left = badge.Right + BADGE_GAP
        text_right = bounds.Right - ROW_PAD
        name_h = FONT_NAME.Height
        detail_h = FONT_DETAIL.Height
        name_top = bounds.Top + (bounds.Height - name_h - detail_h) // 2

        if self.query:
            start = display_name.lower().find(self.query)
            if start >= 0:
                before = text_width(display_name[:start], FONT_NAME, graphics)
                match = text_width(
                    display_name[start:start + len(self.query)],
                    FONT_NAME,
                    graphics
                )
                fill_rounded(
                    graphics,
                    MATCH_BG,
                    Rectangle(
                        text_left + before - dp(1),
                        name_top,
                        match + dp(2),
                        name_h
                    ),
                    dp(3)
                )

        TextRenderer.DrawText(
            graphics,
            display_name,
            FONT_NAME,
            Point(text_left, name_top),
            TEXT,
            TEXT_FLAGS
        )

        detail = os.path.basename(path)
        summary = self.summaries.get(path, "")
        if summary:
            detail = u"{0}  ·  {1}".format(detail, summary)

        TextRenderer.DrawText(
            graphics,
            detail,
            FONT_DETAIL,
            Rectangle(
                text_left,
                name_top + name_h,
                max(0, text_right - text_left),
                detail_h
            ),
            TEXT_MUTED,
            TEXT_FLAGS | TextFormatFlags.EndEllipsis
        )

    def invalidate_row(self, index):
        if 0 <= index < self.list_box.Items.Count:
            self.list_box.Invalidate(self.list_box.GetItemRectangle(index))

    def on_list_mouse_move(self, sender, args):
        index = self.list_box.IndexFromPoint(args.Location)
        if index == self.hover_index:
            return

        self.invalidate_row(self.hover_index)
        self.hover_index = index
        self.invalidate_row(index)

        if 0 <= index < len(self.visible_scripts):
            self.tooltip.SetToolTip(self.list_box, self.visible_scripts[index][1])
        else:
            self.tooltip.SetToolTip(self.list_box, "")

    def on_list_mouse_leave(self, sender, args):
        self.invalidate_row(self.hover_index)
        self.hover_index = -1

    def on_list_resize(self, sender, args):
        self.list_box.Invalidate()

    def on_list_key_press(self, sender, args):
        # Typing while the list has focus goes into the search box.
        if System.Char.IsControl(args.KeyChar):
            return
        self.search_box.Focus()
        self.search_box.AppendText(str(args.KeyChar))
        args.Handled = True

    def on_selection_changed(self, sender, args):
        self.launch_btn.Invalidate()

    # -- search box -----------------------------------------------------

    def on_search_panel_paint(self, sender, args):
        graphics = args.Graphics
        panel = self.search_panel
        focused = self.search_box.Focused

        pen = Pen(BLUE if focused else BORDER, 1.0)
        try:
            graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1)
        finally:
            pen.Dispose()

        # Magnifier glyph.
        size = dp(11)
        x = dp(14)
        y = (panel.Height - size) // 2 - dp(1)
        pen = Pen(BLUE if focused else TEXT_MUTED, max(1.5, 1.6 * SCALE))
        try:
            graphics.SmoothingMode = SmoothingMode.AntiAlias
            graphics.DrawEllipse(pen, x, y, size, size)
            graphics.DrawLine(
                pen,
                x + size * 0.85, y + size * 0.85,
                x + size * 1.3, y + size * 1.3
            )
        finally:
            graphics.SmoothingMode = SmoothingMode.Default
            pen.Dispose()

    def on_search_focus_changed(self, sender, args):
        self.search_panel.Invalidate()

    def on_focus_search(self, sender, args):
        self.search_box.Focus()

    def on_search_key_down(self, sender, args):
        count = self.list_box.Items.Count
        if count == 0:
            return

        page = max(1, self.list_box.ClientSize.Height // ROW_H - 1)
        key = args.KeyCode
        if key == Keys.Down:
            step = 1
        elif key == Keys.Up:
            step = -1
        elif key == Keys.PageDown:
            step = page
        elif key == Keys.PageUp:
            step = -page
        else:
            return

        current = max(0, self.list_box.SelectedIndex)
        self.list_box.SelectedIndex = max(0, min(count - 1, current + step))
        args.Handled = True
        args.SuppressKeyPress = True

    def on_search_changed(self, sender, args):
        self.apply_filter()

    # -- launch button --------------------------------------------------

    def on_launch_paint(self, sender, args):
        graphics = args.Graphics
        rect = self.launch_btn.ClientRectangle

        if self.launch_pressed:
            back = BLUE_DOWN
        elif self.launch_hover:
            back = BLUE_HOVER
        else:
            back = BLUE
        fill_rect(graphics, back, rect)

        main_text = "LAUNCH SCRIPT"
        main_h = FONT_LAUNCH.Height
        centered = TEXT_FLAGS | TextFormatFlags.HorizontalCenter
        selected = self.selected_script()

        if selected is None:
            TextRenderer.DrawText(
                graphics, main_text, FONT_LAUNCH, rect, Color.White,
                centered | TextFormatFlags.VerticalCenter
            )
            return

        sub_h = FONT_LAUNCH_SUB.Height
        top = (rect.Height - main_h - sub_h) // 2

        TextRenderer.DrawText(
            graphics, main_text, FONT_LAUNCH,
            Rectangle(0, top, rect.Width, main_h),
            Color.White, centered
        )
        TextRenderer.DrawText(
            graphics, selected[0], FONT_LAUNCH_SUB,
            Rectangle(dp(16), top + main_h, rect.Width - dp(32), sub_h),
            BLUE_SOFT_TEXT, centered | TextFormatFlags.EndEllipsis
        )

    def on_launch_mouse_enter(self, sender, args):
        self.launch_hover = True
        self.launch_btn.Invalidate()

    def on_launch_mouse_leave(self, sender, args):
        self.launch_hover = False
        self.launch_pressed = False
        self.launch_btn.Invalidate()

    def on_launch_mouse_down(self, sender, args):
        if args.Button == MouseButtons.Left:
            self.launch_pressed = True
            self.launch_btn.Invalidate()

    def on_launch_mouse_up(self, sender, args):
        self.launch_pressed = False
        self.launch_btn.Invalidate()

    # -- commands -------------------------------------------------------

    def on_refresh(self, sender, args):
        self.refresh_scripts()

    def on_settings(self, sender, args):
        settings = SettingsForm(
            self.scripts_folder,
            self.quick_launch_folder
        )

        if settings.ShowDialog(self) == DialogResult.OK:
            if settings.saved_folder:
                self.scripts_folder = settings.saved_folder
                self.quick_launch_folder = settings.saved_quick_folder or ""
                self.refresh_scripts()

    def on_cancel(self, sender, args):
        self.Close()

    def on_launch(self, sender, args):
        selected = self.selected_script()

        if selected is None:
            show_message(
                self,
                "Select a script first.",
                MessageBoxIcon.Information
            )
            return

        display_name, path = selected

        # Hide the launcher while the selected script runs, so dialogs/UI from
        # the selected script are not trapped behind this window.
        self.Hide()

        ok = False
        error = ""

        try:
            ok, error = run_python_script(path)
        finally:
            # One launch per opening: close instead of showing the launcher again.
            try:
                self.Close()
            except:
                pass

        if not ok:
            show_message(
                self,
                "Script failed:\n\n{0}\n\n{1}".format(display_name, error),
                MessageBoxIcon.Error
            )


# ----------------------------------------------------------------------
# Single-instance guard and run
# ----------------------------------------------------------------------

def bring_existing_launcher_forward():
    try:
        for open_form in Application.OpenForms:
            if open_form.Text != TITLE:
                continue

            if open_form.WindowState == FormWindowState.Minimized:
                open_form.WindowState = FormWindowState.Normal

            open_form.Show()
            open_form.Activate()
            open_form.BringToFront()
            return True
    except:
        pass

    return False


def main():
    form = LauncherForm()

    # If no folder has ever been configured, open Settings immediately.
    if not form.scripts_folder or not os.path.isdir(form.scripts_folder):
        settings = SettingsForm("", form.quick_launch_folder)
        if settings.ShowDialog(form) != DialogResult.OK or not settings.saved_folder:
            # Settings was closed without choosing a folder: nothing to show.
            form.Dispose()
            return

        form.scripts_folder = settings.saved_folder
        form.quick_launch_folder = settings.saved_quick_folder or ""
        form.refresh_scripts()

    Application.Run(form)


# Use a named semaphore scoped to this Revit process. Unlike a mutex, it also
# blocks a nested second launch occurring on Revit's same UI thread.
process_id = System.Diagnostics.Process.GetCurrentProcess().Id
guard_name = "Local\\EK_Script_Launcher_{0}".format(process_id)
instance_guard = System.Threading.Semaphore(1, 1, guard_name)
owns_guard = False

try:
    owns_guard = instance_guard.WaitOne(0, False)

    if not owns_guard:
        bring_existing_launcher_forward()
    else:
        main()
finally:
    if owns_guard:
        try:
            instance_guard.Release()
        except:
            pass

    try:
        instance_guard.Dispose()
    except:
        pass

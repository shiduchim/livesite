# -*- coding: utf-8 -*-
"""
EK Script Launcher - Auto Width
===============================

pyRevit / Revit 2024-2027 friendly Windows Forms launcher.

Changes:
- Window automatically widens for long script filenames.
- Script list uses the same width as the expanded window.
- Maximum width is limited to the current screen.
- Horizontal scrollbar appears only if a name is still too long.
- Window is manually resizable.
- Search / Settings / Refresh / Cancel stay aligned as width changes.
- Large LAUNCH SCRIPT button stays full-width at the bottom.
- Settings window contains only folder path + Browse + Save.
- Settings window stores both the launcher folder and Quick Launch folder.
- Launcher closes after one script run.
- Only one launcher window can be open in each Revit instance.
"""

import clr
import os
import json
import traceback

clr.AddReference("System")
clr.AddReference("System.Drawing")
clr.AddReference("System.Windows.Forms")

import System
from System.Drawing import Color, Font, FontStyle, Point, Size
from System.Windows.Forms import (
    Application,
    AnchorStyles,
    Button,
    DialogResult,
    DockStyle,
    FolderBrowserDialog,
    Form,
    FormBorderStyle,
    Label,
    ListBox,
    MessageBox,
    MessageBoxButtons,
    MessageBoxIcon,
    Screen,
    TextBox,
    TextRenderer,
)

TITLE = "EK Script Launcher"

# Muted blue, intentionally less bright/tiring than standard Revit blue.
BLUE = Color.FromArgb(70, 101, 128)
SELECT_BLUE = Color.FromArgb(50, 127, 200)
WINDOW_BG = Color.FromArgb(245, 245, 245)

MIN_CLIENT_W = 600
MAX_CLIENT_W_FALLBACK = 1100
WINDOW_H = 730

LEFT = 18
RIGHT = 18
TOP = 18

LIST_TOP = 174
LIST_BOTTOM_MARGIN = 92
LAUNCH_H = 82

FONT_UI = Font("Segoe UI", 10.0)
FONT_LIST = Font("Segoe UI", 10.0)
FONT_LAUNCH = Font("Segoe UI", 14.0, FontStyle.Bold)

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
    Primary v2 location plus a few harmless legacy guesses so replacing an
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

        for key in (
            "scripts_folder",
            "script_folder",
            "folder",
            "path",
        ):
            folder = data.get(key)
            if folder and os.path.isdir(folder):
                scripts_folder = folder
                break
        else:
            scripts_folder = ""

        quick_folder = ""
        for key in (
            "quick_launch_folder",
            "quick_folder",
            "secondary_folder",
        ):
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

        result.append(
            (display_name_from_path(full_path), full_path)
        )

    result.sort(key=lambda pair: pair[0].lower())
    return result


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
# Settings dialog
# ----------------------------------------------------------------------

class SettingsForm(Form):
    def __init__(self, current_folder, current_quick_folder):
        Form.__init__(self)

        self.Text = "Launcher Settings"
        self.ShowIcon = False
        self.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        self.FormBorderStyle = FormBorderStyle.FixedDialog
        self.MaximizeBox = False
        self.MinimizeBox = False
        self.ShowInTaskbar = False
        self.ClientSize = Size(760, 255)
        self.BackColor = WINDOW_BG
        self.Font = FONT_UI
        self.saved_folder = None
        self.saved_quick_folder = None

        label = Label()
        label.Text = "Script Launcher folder"
        label.AutoSize = True
        label.Location = Point(18, 18)
        self.Controls.Add(label)

        self.path_box = TextBox()
        self.path_box.Text = current_folder or ""
        self.path_box.Location = Point(18, 52)
        self.path_box.Size = Size(590, 31)
        self.path_box.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.Controls.Add(self.path_box)

        browse = Button()
        browse.Text = "Browse"
        browse.Size = Size(118, 42)
        browse.Location = Point(621, 47)
        browse.Click += self.on_browse
        self.Controls.Add(browse)

        quick_label = Label()
        quick_label.Text = "Quick Launch folder (exactly one .py file)"
        quick_label.AutoSize = True
        quick_label.Location = Point(18, 108)
        self.Controls.Add(quick_label)

        self.quick_path_box = TextBox()
        self.quick_path_box.Text = current_quick_folder or ""
        self.quick_path_box.Location = Point(18, 142)
        self.quick_path_box.Size = Size(590, 31)
        self.quick_path_box.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.Controls.Add(self.quick_path_box)

        quick_browse = Button()
        quick_browse.Text = "Browse"
        quick_browse.Size = Size(118, 42)
        quick_browse.Location = Point(621, 137)
        quick_browse.Click += self.on_quick_browse
        self.Controls.Add(quick_browse)

        save = Button()
        save.Text = "Save"
        save.Size = Size(118, 42)
        save.Location = Point(621, 197)
        save.Click += self.on_save
        self.Controls.Add(save)

        self.AcceptButton = save

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
            MessageBox.Show(
                self,
                "Choose a valid scripts folder.",
                TITLE,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            )
            return

        if quick_folder and not os.path.isdir(quick_folder):
            MessageBox.Show(
                self,
                "Choose a valid Quick Launch folder, or leave it blank.",
                TITLE,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            )
            return

        if quick_folder:
            quick_scripts = get_python_files(quick_folder)

            if len(quick_scripts) != 1:
                MessageBox.Show(
                    self,
                    (
                        "The Quick Launch folder must contain exactly one "
                        ".py file.\n\nFound: {0}"
                    ).format(len(quick_scripts)),
                    TITLE,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                )
                return

        try:
            save_launcher_settings(folder, quick_folder)
        except Exception as ex:
            MessageBox.Show(
                self,
                "Could not save settings:\n\n" + str(ex),
                TITLE,
                MessageBoxButtons.OK,
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
        self.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        self.FormBorderStyle = FormBorderStyle.Sizable
        self.MinimumSize = Size(MIN_CLIENT_W + 18, 620)
        self.ClientSize = Size(MIN_CLIENT_W, WINDOW_H)
        self.BackColor = WINDOW_BG
        self.Font = FONT_UI

        saved_settings = load_launcher_settings()
        self.scripts_folder = saved_settings.get("scripts_folder", "")
        self.quick_launch_folder = saved_settings.get(
            "quick_launch_folder",
            ""
        )
        self.all_scripts = []
        self.visible_scripts = []

        self.build_ui()
        self.refresh_scripts()

    def build_ui(self):
        # Search label.
        self.search_label = Label()
        self.search_label.Text = "Search"
        self.search_label.AutoSize = True
        self.search_label.Location = Point(22, 22)
        self.Controls.Add(self.search_label)

        # Right aligned top buttons.
        self.settings_btn = Button()
        self.settings_btn.Text = "Settings"
        self.settings_btn.Font = FONT_UI
        self.settings_btn.Anchor = AnchorStyles.Top | AnchorStyles.Right
        self.settings_btn.Click += self.on_settings
        self.Controls.Add(self.settings_btn)

        self.refresh_btn = Button()
        self.refresh_btn.Text = "Refresh"
        self.refresh_btn.Font = FONT_UI
        self.refresh_btn.Anchor = AnchorStyles.Top | AnchorStyles.Right
        self.refresh_btn.Click += self.on_refresh
        self.Controls.Add(self.refresh_btn)

        self.cancel_btn = Button()
        self.cancel_btn.Text = "Cancel"
        self.cancel_btn.Font = FONT_UI
        self.cancel_btn.Anchor = AnchorStyles.Top | AnchorStyles.Right
        self.cancel_btn.Click += self.on_cancel
        self.Controls.Add(self.cancel_btn)

        self.size_top_button(self.settings_btn, 92)
        self.size_top_button(self.refresh_btn, 88)
        self.size_top_button(self.cancel_btn, 84)

        self.search_box = TextBox()
        self.search_box.Location = Point(LEFT, 74)
        self.search_box.Size = Size(
            self.ClientSize.Width - LEFT - RIGHT,
            34
        )
        self.search_box.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.search_box.TextChanged += self.on_search_changed
        self.Controls.Add(self.search_box)

        self.list_box = ListBox()
        self.list_box.Location = Point(LEFT, 124)
        self.list_box.Size = Size(
            self.ClientSize.Width - LEFT - RIGHT,
            self.ClientSize.Height - 124 - 112
        )
        self.list_box.Anchor = (
            AnchorStyles.Top |
            AnchorStyles.Bottom |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.list_box.Font = FONT_LIST
        self.list_box.HorizontalScrollbar = True
        self.list_box.IntegralHeight = False
        self.list_box.DoubleClick += self.on_launch
        self.Controls.Add(self.list_box)

        self.launch_btn = Button()
        self.launch_btn.Text = "LAUNCH SCRIPT"
        self.launch_btn.Font = FONT_LAUNCH
        self.launch_btn.ForeColor = Color.White
        self.launch_btn.BackColor = BLUE
        self.launch_btn.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        self.launch_btn.FlatAppearance.BorderSize = 0
        self.launch_btn.Location = Point(
            LEFT,
            self.ClientSize.Height - LAUNCH_H - 18
        )
        self.launch_btn.Size = Size(
            self.ClientSize.Width - LEFT - RIGHT,
            LAUNCH_H
        )
        self.launch_btn.Anchor = (
            AnchorStyles.Bottom |
            AnchorStyles.Left |
            AnchorStyles.Right
        )
        self.launch_btn.Click += self.on_launch
        self.Controls.Add(self.launch_btn)

        self.AcceptButton = self.launch_btn

        self.position_top_buttons()

    def size_top_button(self, button, minimum_width):
        try:
            text_width = TextRenderer.MeasureText(
                button.Text,
                button.Font
            ).Width
        except:
            text_width = len(button.Text) * 10

        button.Size = Size(
            max(int(minimum_width), int(text_width + 34)),
            39
        )

    def position_top_buttons(self):
        right = self.ClientSize.Width - RIGHT

        self.cancel_btn.Location = Point(
            right - self.cancel_btn.Width,
            16
        )

        self.refresh_btn.Location = Point(
            self.cancel_btn.Left - 12 - self.refresh_btn.Width,
            16
        )

        self.settings_btn.Location = Point(
            self.refresh_btn.Left - 12 - self.settings_btn.Width,
            16
        )

    def OnResize(self, event):
        Form.OnResize(self, event)
        try:
            self.position_top_buttons()
        except:
            pass

    def max_allowed_client_width(self):
        try:
            working = Screen.FromControl(self).WorkingArea
            return max(
                MIN_CLIENT_W,
                working.Width - 70
            )
        except:
            return MAX_CLIENT_W_FALLBACK

    def ideal_width_for_names(self):
        longest = 0

        for display_name, path in self.all_scripts:
            try:
                measured = TextRenderer.MeasureText(
                    display_name,
                    FONT_LIST
                ).Width
            except:
                measured = len(display_name) * 11

            if measured > longest:
                longest = measured

        # List text + list padding/scrollbar + outer margins.
        needed = longest + 130

        # Also guarantee room for the top buttons.
        top_buttons_needed = (
            22 +
            self.settings_btn.Width +
            12 +
            self.refresh_btn.Width +
            12 +
            self.cancel_btn.Width +
            RIGHT
        )
        needed = max(needed, top_buttons_needed, MIN_CLIENT_W)

        return min(
            needed,
            self.max_allowed_client_width()
        )

    def expand_for_long_names(self):
        wanted = self.ideal_width_for_names()

        # Only expand automatically. Do not shrink while the user works.
        if wanted > self.ClientSize.Width:
            old_center_x = self.Left + self.Width // 2
            old_center_y = self.Top + self.Height // 2

            self.ClientSize = Size(
                wanted,
                self.ClientSize.Height
            )

            # Keep the expanded launcher approximately centered.
            try:
                working = Screen.FromControl(self).WorkingArea
                new_left = old_center_x - self.Width // 2
                new_top = old_center_y - self.Height // 2

                new_left = max(
                    working.Left,
                    min(
                        new_left,
                        working.Right - self.Width
                    )
                )
                new_top = max(
                    working.Top,
                    min(
                        new_top,
                        working.Bottom - self.Height
                    )
                )

                self.Left = new_left
                self.Top = new_top
            except:
                pass

            self.position_top_buttons()

    def refresh_scripts(self):
        self.all_scripts = get_python_files(
            self.scripts_folder
        )

        self.expand_for_long_names()
        self.apply_filter()

    def apply_filter(self):
        query = self.search_box.Text.strip().lower()

        if query:
            self.visible_scripts = [
                pair for pair in self.all_scripts
                if query in pair[0].lower()
            ]
        else:
            self.visible_scripts = list(
                self.all_scripts
            )

        self.list_box.BeginUpdate()
        try:
            self.list_box.Items.Clear()

            for display_name, path in self.visible_scripts:
                self.list_box.Items.Add(
                    display_name
                )

            if self.list_box.Items.Count > 0:
                self.list_box.SelectedIndex = 0
        finally:
            self.list_box.EndUpdate()

    def selected_script(self):
        index = self.list_box.SelectedIndex

        if index < 0:
            return None

        if index >= len(self.visible_scripts):
            return None

        return self.visible_scripts[index]

    def on_search_changed(self, sender, args):
        self.apply_filter()

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
            MessageBox.Show(
                self,
                "Select a script first.",
                TITLE,
                MessageBoxButtons.OK,
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
            MessageBox.Show(
                self,
                "Script failed:\n\n{0}\n\n{1}".format(
                    display_name,
                    error
                ),
                TITLE,
                MessageBoxButtons.OK,
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

            if open_form.WindowState == System.Windows.Forms.FormWindowState.Minimized:
                open_form.WindowState = System.Windows.Forms.FormWindowState.Normal

            open_form.Show()
            open_form.Activate()
            open_form.BringToFront()
            return True
    except:
        pass

    return False


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
        form = LauncherForm()

        # If no folder has ever been configured, open Settings immediately.
        if not form.scripts_folder or not os.path.isdir(form.scripts_folder):
            settings = SettingsForm("", form.quick_launch_folder)
            if settings.ShowDialog(form) == DialogResult.OK and settings.saved_folder:
                form.scripts_folder = settings.saved_folder
                form.quick_launch_folder = settings.saved_quick_folder or ""
                form.refresh_scripts()
            else:
                form.Dispose()
                raise System.Exception(
                    "No script folder selected."
                )

        Application.Run(form)
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

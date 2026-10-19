# -*- coding: utf-8 -*-
"""
EK Quick Launch
===============

Runs the single .py file in the Quick Launch folder configured through
Script Launcher > Settings.
"""

import clr
import json
import os
import sys
import traceback

clr.AddReference("System")
clr.AddReference("System.Windows.Forms")

import System
from System.Windows.Forms import (
    MessageBox,
    MessageBoxButtons,
    MessageBoxIcon,
)

TITLE = "EK Quick Launch"

CONFIG_DIR = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~")),
    "EK",
    "ScriptLauncher"
)
CONFIG_PATH = os.path.join(CONFIG_DIR, "settings.json")


def show_warning(message):
    MessageBox.Show(
        message,
        TITLE,
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning
    )


def show_error(message):
    MessageBox.Show(
        message,
        TITLE,
        MessageBoxButtons.OK,
        MessageBoxIcon.Error
    )


def read_quick_launch_folder():
    try:
        if not os.path.isfile(CONFIG_PATH):
            return ""

        with open(CONFIG_PATH, "r") as stream:
            data = json.load(stream)

        if not isinstance(data, dict):
            return ""

        return data.get("quick_launch_folder", "") or ""
    except:
        return ""


def python_files_in(folder):
    result = []

    try:
        names = os.listdir(folder)
    except:
        return result

    for filename in names:
        full_path = os.path.join(folder, filename)

        if os.path.isfile(full_path) and filename.lower().endswith(".py"):
            result.append(full_path)

    result.sort(key=lambda path: os.path.basename(path).lower())
    return result


def run_python_script(path):
    script_folder = os.path.dirname(path)
    inserted_path = False

    try:
        with open(path, "rb") as stream:
            raw = stream.read()

        try:
            source = raw.decode("utf-8-sig")
        except:
            try:
                source = raw.decode("utf-8")
            except:
                source = raw.decode("latin-1")

        if script_folder and script_folder not in sys.path:
            sys.path.insert(0, script_folder)
            inserted_path = True

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

    except Exception:
        return False, traceback.format_exc()

    finally:
        if inserted_path:
            try:
                sys.path.remove(script_folder)
            except:
                pass


quick_folder = read_quick_launch_folder()

if not quick_folder:
    show_warning(
        "Quick Launch is not configured.\n\n"
        "Open Script Launcher, click Settings, and choose the "
        "Quick Launch folder."
    )
elif not os.path.isdir(quick_folder):
    show_warning(
        "The Quick Launch folder does not exist:\n\n{0}\n\n"
        "Choose it again in Script Launcher > Settings.".format(
            quick_folder
        )
    )
else:
    scripts = python_files_in(quick_folder)

    if len(scripts) != 1:
        show_warning(
            "The Quick Launch folder must contain exactly one .py file.\n\n"
            "Found: {0}\n\nFolder:\n{1}".format(
                len(scripts),
                quick_folder
            )
        )
    else:
        ok, error = run_python_script(scripts[0])

        if not ok:
            show_error(
                "Script failed:\n\n{0}\n\n{1}".format(
                    os.path.basename(scripts[0]),
                    error
                )
            )

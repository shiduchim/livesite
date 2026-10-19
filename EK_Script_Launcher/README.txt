EK SCRIPT LAUNCHER
==================

BUILD: v5 - cleaned-up UI, with Quick Launch

WHAT IT DOES

- Adds a separate "EK" tab to Revit.
- Adds a "Select & Launch" button and a separate "Quick Launch" button.
- Opens a searchable list of Python scripts.
- Each script gets its own row with a coloured initials badge, the script
  name, and underneath it the file name plus the first line of the script's
  docstring, so scripts are easy to tell apart.
- Search filters as you type and highlights the matching letters.
  Up/Down arrows move the selection, Enter launches, Esc closes.
- The LAUNCH SCRIPT button shows which script it is about to run.
- Hovering a script shows its full file path.
- Includes Settings inside the popup to configure both folders.
- Quick Launch immediately runs the only .py file in its assigned folder.
- Quick Launch stops with a warning if its folder has zero or multiple .py files.
- Automatically widens for long script names, up to the available screen width.
- Shows a horizontal scrollbar if a name is still too long.
- Rescans the folder when the launcher opens and when Refresh is clicked.
- Remembers the scripts folder.
- Double-clicking a script launches it.
- Closes automatically after running one script.
- Allows only one launcher window per Revit instance; reopening brings the existing window forward.
- Keeps Settings, Refresh, and Cancel aligned as the window changes size.
- Scales cleanly on high-DPI (125% / 150% / 200%) displays.
- Uses a muted Launch button and hides the window title-bar logo.

TIP: SCRIPT DESCRIPTIONS

To give a script a description line in the launcher, start the .py file with
a docstring, for example:

    """Renumber sheets in the order they appear in a schedule."""

Scripts without one simply show their file name.

INSTALL

1. Extract this complete package to a normal folder.
2. Double-click "INSTALL EK LAUNCHER.bat".
3. In every open Revit version, click pyRevit > Reload once, or restart Revit.
4. Open the new EK ribbon tab.
5. Click Script Launcher to configure your folders and browse scripts.

The installer also creates this default live scripts folder:

    Desktop\RevitScripts

You can change it at any time with Settings inside the launcher window.

QUICK LAUNCH

1. Create a separate folder for the Quick Launch command.
2. Put exactly one .py file in that folder.
3. Open Script Launcher and click Settings.
4. Choose the folder beside "Quick Launch folder" and click Save.
5. Use the separate Quick Launch button on the EK ribbon tab.

Quick Launch reads the folder every time it is pressed. You can replace the
single .py file without reloading pyRevit. If the folder is missing, or contains
zero or more than one .py file, nothing runs and a warning explains the issue.

REVIT VERSIONS

The same extension is intended for Revit 2024, 2025, 2026, and 2027. pyRevit
must be installed/attached for each Revit version. All versions read the same
extension and Desktop\RevitScripts folder, so you maintain only one copy.

SCRIPT COMPATIBILITY

This launcher is designed for normal pyRevit/IronPython scripts, including the
scripts that start with imports such as:

    from pyrevit import revit, DB, forms

The launcher supplies __revit__ and __file__. Scripts can import the usual
pyRevit objects such as revit, DB, and forms themselves.

The launcher itself is version-independent, but an individual script can still
need a small Revit-version adjustment if Autodesk changed an API method between
Revit releases.

ADDING OR EDITING SCRIPTS

1. Add, rename, edit, or delete a .py file in your selected Script Launcher folder.
2. Return to Revit.
3. Open Script Launcher, or click Refresh if it is already open.

No Revit restart and no pyRevit Reload are needed when changing scripts.

UPDATING THE LAUNCHER

Run "INSTALL EK LAUNCHER.bat" again. It replaces only the installed
EKScriptLauncher.extension folder. It does not touch Desktop\RevitScripts.

UNINSTALL

Double-click "UNINSTALL EK LAUNCHER.bat". Your scripts folder is deliberately
kept.

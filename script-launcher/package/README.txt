EK SCRIPT LAUNCHER
==================

Version 2.0 - C# Revit add-in for Revit 2024, 2025, 2026 and 2027.

WHAT IT DOES

- Adds an "EK" tab to Revit with a "Scripts" panel.
- "Select & Launch" opens a searchable list of the Python (.py) scripts in
  your folder and runs the one you pick.
- "Quick Launch" immediately runs the only .py file in its assigned folder.
  It stops with a warning if that folder has zero or several .py files.
- Each script gets its own row with a coloured initials badge, the script
  name, and underneath it the file name plus the first line of the
  script's docstring, so scripts are easy to tell apart.
- Search filters as you type and highlights the matching letters.
  Up/Down arrows move the selection, Enter launches, Esc closes.
- The LAUNCH SCRIPT button shows which script it is about to run.
  Double-clicking a script also launches it.
- Hovering a script shows its full file path.
- Settings (inside the launcher) chooses both folders.
- The folder is rescanned every time the launcher opens and when Refresh
  is clicked. No Revit restart is needed when you add or edit scripts.
- The window widens for long script names, up to the screen width.
- Closes automatically after running one script.
- Sharp on high-DPI (125% / 150% / 200%) displays.

REQUIREMENTS

- pyRevit must be installed for each Revit version. The launcher itself is
  a normal C# add-in, but your scripts are Python, so it hands each script
  to pyRevit to run - exactly as if you clicked a pyRevit button. Scripts
  that use "from pyrevit import revit, DB, forms" keep working, and print()
  output and errors appear in pyRevit's output window as usual.

INSTALL

1. Extract this complete ZIP to a normal folder.
2. Close Revit.
3. Double-click "INSTALL EK LAUNCHER.bat".
4. Open Revit and use the EK tab > Scripts panel.

The installer:
- installs for every Revit 2024-2027 version it finds on this computer,
  for your Windows user only (no administrator rights needed);
- keeps your saved folders (%APPDATA%\EK\ScriptLauncher\settings.json);
- removes the old pyRevit version of EK Script Launcher, if present, so you
  don't get two EK tabs. A backup is kept in
  %LOCALAPPDATA%\EK\ScriptLauncher\InstallBackups;
- creates Desktop\RevitScripts as a default scripts folder.

QUICK LAUNCH

1. Create a separate folder for the Quick Launch command.
2. Put exactly one .py file in that folder.
3. Open Select & Launch and click Settings.
4. Choose the folder beside "Quick Launch folder" and click Save.
5. Use the Quick Launch button on the EK tab.

SCRIPT DESCRIPTIONS

To give a script a description line in the launcher, start the .py file
with a docstring, for example:

    """Renumber sheets in the order they appear in a schedule."""

Scripts without one simply show their file name.

UPDATING

Close Revit and run "INSTALL EK LAUNCHER.bat" from the new package.

UNINSTALL

Close Revit and double-click "UNINSTALL EK LAUNCHER.bat". Your scripts
folders and settings are kept.

FILES

Revit2024\EKScriptLauncher.dll   .NET Framework 4.8 build for Revit 2024
Revit2025\EKScriptLauncher.dll   .NET 8 build for Revit 2025
Revit2026\EKScriptLauncher.dll   .NET 8 build for Revit 2026
Revit2027\EKScriptLauncher.dll   .NET 10 build for Revit 2027
SHA256SUMS.txt                   checksums of the files in this package

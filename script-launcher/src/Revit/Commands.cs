using System;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using EKScriptLauncher.Core;
using EKScriptLauncher.UI;

namespace EKScriptLauncher.Revit
{
    /// <summary>Revit's main window as a WinForms owner, so dialogs stay on top of Revit.</summary>
    sealed class RevitWindow : IWin32Window
    {
        public RevitWindow(UIApplication app)
        {
            try
            {
                Handle = app.MainWindowHandle;
            }
            catch
            {
                Handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
        }

        public IntPtr Handle { get; private set; }
    }

    /// <summary>Select &amp; Launch: pick a script from the folder and run it.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class SelectAndLaunchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var owner = new RevitWindow(commandData.Application);
            LauncherSettings settings = LauncherSettings.Load();

            // If no folder has ever been configured, open Settings immediately.
            if (string.IsNullOrEmpty(settings.ScriptsFolder) || !Directory.Exists(settings.ScriptsFolder))
            {
                using (var first = new SettingsForm("", settings.QuickLaunchFolder))
                {
                    first.StartPosition = FormStartPosition.CenterScreen;
                    if (first.ShowDialog(owner) != DialogResult.OK || string.IsNullOrEmpty(first.SavedFolder))
                        return Result.Cancelled;

                    settings.ScriptsFolder = first.SavedFolder;
                    settings.QuickLaunchFolder = first.SavedQuickFolder ?? "";
                }
            }

            // The window is modal, so only one launcher can be open per Revit.
            ScriptEntry script;
            using (var launcher = new LauncherForm(settings))
            {
                if (launcher.ShowDialog(owner) != DialogResult.OK || launcher.SelectedScript == null)
                    return Result.Cancelled;
                script = launcher.SelectedScript;
            }

            return ScriptRunning.Run(script.Path, script.Name, commandData, elements, owner, Theme.Title);
        }
    }

    /// <summary>Quick Launch: run the only .py file in the Quick Launch folder.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class QuickLaunchCommand : IExternalCommand
    {
        const string Title = "EK Quick Launch";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var owner = new RevitWindow(commandData.Application);
            string folder = LauncherSettings.Load().QuickLaunchFolder;

            if (string.IsNullOrEmpty(folder))
            {
                Theme.Message(owner,
                    "Quick Launch is not configured.\n\n" +
                    "Open Select & Launch, click Settings, and choose the Quick Launch folder.",
                    MessageBoxIcon.Warning, Title);
                return Result.Cancelled;
            }

            if (!Directory.Exists(folder))
            {
                Theme.Message(owner,
                    "The Quick Launch folder does not exist:\n\n" + folder +
                    "\n\nChoose it again in Select & Launch > Settings.",
                    MessageBoxIcon.Warning, Title);
                return Result.Cancelled;
            }

            var scripts = ScriptCatalog.GetPythonFiles(folder);
            if (scripts.Count != 1)
            {
                Theme.Message(owner,
                    "The Quick Launch folder must contain exactly one .py file.\n\n" +
                    "Found: " + scripts.Count + "\n\nFolder:\n" + folder,
                    MessageBoxIcon.Warning, Title);
                return Result.Cancelled;
            }

            return ScriptRunning.Run(scripts[0].Path, scripts[0].FileName, commandData, elements, owner, Title);
        }
    }

    static class ScriptRunning
    {
        public static Result Run(
            string path, string displayName, ExternalCommandData commandData, ElementSet elements,
            IWin32Window owner, string title)
        {
            PyRevitRunner.RunResult result = PyRevitRunner.Run(path, commandData, elements);

            if (!result.Started)
            {
                Theme.Message(owner, "Script failed:\n\n" + displayName + "\n\n" + result.Problem,
                    MessageBoxIcon.Error, title);
                return Result.Cancelled;
            }

            return result.Succeeded ? Result.Succeeded : Result.Cancelled;
        }
    }
}

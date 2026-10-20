using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace EKScriptLauncher.Revit
{
    /// <summary>Adds the EK tab > Scripts panel with Select &amp; Launch and Quick Launch.</summary>
    public sealed class App : IExternalApplication
    {
        const string TabName = "EK";
        const string PanelName = "Scripts";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch
            {
                // The EK tab already exists (another EK add-in created it).
            }

            RibbonPanel panel = null;
            foreach (RibbonPanel existing in application.GetRibbonPanels(TabName))
            {
                if (existing.Name == PanelName)
                    panel = existing;
            }
            if (panel == null)
                panel = application.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var launch = new PushButtonData(
                "EKScriptLauncher_SelectAndLaunch",
                "Select & Launch",
                assemblyPath,
                typeof(SelectAndLaunchCommand).FullName)
            {
                ToolTip = "Select and run a Python script from your chosen folder. The folder is rescanned every time.",
                LargeImage = LoadIcon("launcher_32.png"),
                Image = LoadIcon("launcher_16.png"),
            };

            var quick = new PushButtonData(
                "EKScriptLauncher_QuickLaunch",
                "Quick Launch",
                assemblyPath,
                typeof(QuickLaunchCommand).FullName)
            {
                ToolTip = "Run the only Python script in your configured Quick Launch folder. " +
                          "Set the folder under Select & Launch > Settings.",
                LargeImage = LoadIcon("quick_32.png"),
                Image = LoadIcon("quick_16.png"),
            };

            panel.AddItem(launch);
            panel.AddItem(quick);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        /// <summary>
        /// Icons are stored at twice their ribbon size with 192 DPI, so they stay
        /// sharp at 100 % and 200 % display scaling.
        /// </summary>
        static BitmapSource LoadIcon(string fileName)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("EKScriptLauncher.Resources." + fileName))
                {
                    if (stream == null)
                        return null;
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

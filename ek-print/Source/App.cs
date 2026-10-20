using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace EKPrint
{
    public class App : IExternalApplication
    {
        private const string TabName = "EK";
        private const string PanelName = "Print";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                try { application.CreateRibbonTab(TabName); }
                catch { }

                RibbonPanel panel = null;
                try
                {
                    panel = application.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == PanelName);
                }
                catch { }

                if (panel == null)
                    panel = application.CreateRibbonPanel(TabName, PanelName);

                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string baseDir = Path.GetDirectoryName(assemblyPath);

                AddButton(panel, "EKPrint_QuickPDF", "Quick\nPDF", typeof(QuickPdfCommand).FullName,
                    assemblyPath, Path.Combine(baseDir, "QuickPDF.png"),
                    "Quick PDF. If printable sheets/views are selected in Revit, prints those directly. Otherwise prints the active view/sheet.");

                AddButton(panel, "EKPrint_SelectPDF", "Select\nPDF", typeof(SelectPdfCommand).FullName,
                    assemblyPath, Path.Combine(baseDir, "SelectPDF.png"),
                    "Choose sheets or views, search, name, and export PDF.");

                AddButton(panel, "EKPrint_QuickDWG", "Quick\nDWG", typeof(QuickDwgCommand).FullName,
                    assemblyPath, Path.Combine(baseDir, "QuickDWG.png"),
                    "Quick DWG. Exports selected printable sheets/views, or the active view/sheet, using EK DWG settings.");

                AddButton(panel, "EKPrint_SelectDWG", "Select\nDWG", typeof(SelectDwgCommand).FullName,
                    assemblyPath, Path.Combine(baseDir, "SelectDWG.png"),
                    "Choose sheets or views and export DWGs using EK DWG settings.");

                AddButton(panel, "EKPrint_SheetPrint", "Sheet\nPrint", typeof(SheetPrintCommand).FullName,
                    assemblyPath, Path.Combine(baseDir, "SheetPrint.png"),
                    "Print the sheet(s) containing the views selected in the Project Browser. Duplicate sheets are removed automatically.");

                try
                {
                    panel.AddSlideOut();
                    AddButton(panel, "EKPrint_Settings", "Settings", typeof(SettingsCommand).FullName,
                        assemblyPath, Path.Combine(baseDir, "Settings.png"),
                        "EK Print settings: save folder and Quick PDF multiple-selection behavior.");
                }
                catch
                {
                    // If a slide-out is unavailable for any reason, keep Settings accessible.
                    AddButton(panel, "EKPrint_SettingsFallback", "Settings", typeof(SettingsCommand).FullName,
                        assemblyPath, Path.Combine(baseDir, "Settings.png"),
                        "EK Print settings: save folder and Quick PDF multiple-selection behavior.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("EK Print", "EK Print could not create its ribbon panel.\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static PushButton AddButton(RibbonPanel panel, string internalName, string text,
            string className, string assemblyPath, string iconPath, string tooltip)
        {
            PushButtonData data = new PushButtonData(internalName, text, assemblyPath, className);
            PushButton button = panel.AddItem(data) as PushButton;
            if (button != null)
            {
                button.ToolTip = tooltip;
                string dir = Path.GetDirectoryName(iconPath);
                string name = Path.GetFileNameWithoutExtension(iconPath);
                ImageSource largeIcon = RibbonIcons.Load(dir, name, 32);
                ImageSource smallIcon = RibbonIcons.Load(dir, name, 16);
                if (smallIcon != null)
                    button.Image = smallIcon;
                else if (largeIcon != null)
                    button.Image = largeIcon;

                if (largeIcon != null)
                    button.LargeImage = largeIcon;
            }
            return button;
        }
    }
}

namespace EKPrint
{
    /// <summary>
    /// Sharp ribbon icons at every display scale.
    ///  * Picks the asset that matches the monitor scale exactly (100/125/150/200%):
    ///      large 32 DIP: Name.png (32px), Name_40.png, Name_48.png, Name_64.png
    ///      small 16 DIP: Name_16.png (16px), Name_16_20.png, Name_16_24.png, Name_16_32.png
    ///  * Rebuilds the bitmap with an exact DPI so it occupies exactly 32 or 16 DIPs.
    ///    (PNG files store DPI as pixels-per-metre, so "96 DPI" is read back as 96.012 and
    ///    WPF would otherwise resample every icon slightly - the soft/blurry look.)
    /// Missing sizes fall back to the nearest available file, so older asset sets still work.
    /// </summary>
    internal static class RibbonIcons
    {
        public static ImageSource Load(string dir, string name, int logicalSize)
        {
            try
            {
                List<KeyValuePair<int, string>> candidates = new List<KeyValuePair<int, string>>();
                if (logicalSize == 16)
                {
                    Add(candidates, dir, name + "_16.png", 16);
                    Add(candidates, dir, name + "_16_20.png", 20);
                    Add(candidates, dir, name + "_16_24.png", 24);
                    Add(candidates, dir, name + "_16_32.png", 32);
                }
                if (logicalSize != 16 || candidates.Count == 0)
                {
                    Add(candidates, dir, name + ".png", 32);
                    Add(candidates, dir, name + "_40.png", 40);
                    Add(candidates, dir, name + "_48.png", 48);
                    Add(candidates, dir, name + "_64.png", 64);
                }
                if (candidates.Count == 0) return null;

                int need = (int)Math.Ceiling(logicalSize * Ui.Scale - 0.01);
                string pick = null;
                foreach (KeyValuePair<int, string> c in candidates.OrderBy(k => k.Key))
                {
                    if (c.Key >= need) { pick = c.Value; break; }
                }
                if (pick == null) pick = candidates.OrderBy(k => k.Key).Last().Value;
                return Decode(pick, logicalSize);
            }
            catch
            {
                return null;
            }
        }

        private static void Add(List<KeyValuePair<int, string>> list, string dir, string file, int pixels)
        {
            string path = Path.Combine(dir ?? "", file);
            if (File.Exists(path)) list.Add(new KeyValuePair<int, string>(pixels, path));
        }

        private static ImageSource Decode(string path, int logicalSize)
        {
            BitmapImage source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            source.UriSource = new Uri(path, UriKind.Absolute);
            source.EndInit();

            BitmapSource bgra = source.Format == PixelFormats.Pbgra32
                ? (BitmapSource)source
                : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            double dpi = 96.0 * width / logicalSize;
            BitmapSource exact = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Pbgra32, null, pixels, stride);
            exact.Freeze();
            return exact;
        }
    }
}

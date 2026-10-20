using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace EKRibbon
{
    // A supported, explicit bridge for EK Modify Panels v2.2. This is NOT a
    // generic external DLL loader. The supplied source identifies its exact
    // public IExternalCommand class and per-year installation manifest.
    // Add-on buttons whose Autodesk.Windows IDs do not resolve to postable
    // Revit commands can still be inserted natively using PushButtonData.
    internal static class KnownAddins
    {
        internal const string ModifyId="DIRECT:EK.ModifyPanels.ToggleModifyPanelsCommand";
        internal const string ModifyClass="EK.ModifyPanels.ToggleModifyPanelsCommand";
        static string Manifest(int year)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk","Revit","Addins",year.ToString(),"EKModifyPanels.addin");
        }
        internal static bool IsDirect(string id)
        {
            return string.Equals(id,ModifyId,StringComparison.OrdinalIgnoreCase);
        }
        internal static bool Resolve(string id,int year,out string assembly,out string type)
        {
            assembly="";type="";
            if(!IsDirect(id)||year<2024||year>2027)return false;
            try
            {
                string manifest=Manifest(year);
                if(!File.Exists(manifest))return false;
                var doc=new XmlDocument();doc.Load(manifest);
                foreach(XmlNode entry in doc.SelectNodes("/RevitAddIns/AddIn"))
                {
                    if(!string.Equals(entry.Attributes?["Type"]?.Value,"Application",StringComparison.OrdinalIgnoreCase))continue;
                    if(!string.Equals(entry.SelectSingleNode("FullClassName")?.InnerText?.Trim(),
                         "EK.ModifyPanels.ModifyPanelsApp",StringComparison.Ordinal))continue;
                    string dll=entry.SelectSingleNode("Assembly")?.InnerText?.Trim();
                    if(string.IsNullOrWhiteSpace(dll) ||
                       !string.Equals(Path.GetFileName(dll),"EKModifyPanels.dll",StringComparison.OrdinalIgnoreCase) ||
                       !File.Exists(dll))continue;
                    assembly=Path.GetFullPath(dll);type=ModifyClass;
                    return true;
                }
            }
            catch(Exception ex){Log.Error("Modify Panels manifest (Revit "+year+")",ex);}
            return false;
        }
        internal static string Icon(string id,int year)
        {
            if(!Resolve(id,year,out string dll,out string unused))return "";
            string icon=Path.Combine(Path.GetDirectoryName(dll),"Icons","Visible32.png");
            return File.Exists(icon)?icon:"";
        }
        internal static CommandInfo ModifyCommand(int year)
        {
            if(!Resolve(ModifyId,year,out string dll,out string type))return null;
            var cmd=new CommandInfo{Id=ModifyId,Name="Modify Panels",Tab="EK",
                Panel="Modify Panels",Registered=true,Icon=Icon(ModifyId,year)};
            cmd.Key=CommandInfo.MakeKey(cmd);
            return cmd;
        }
        internal static void AugmentScan(int year,List<CommandInfo> commands)
        {
            var direct=ModifyCommand(year);
            if(direct==null)return;
            // The real native ribbon control can have a generated/non-postable
            // Autodesk.Windows ID. Replace only this exact button with an
            // independently launchable command; avoid duplicate entries.
            commands.RemoveAll(x=>string.Equals(x.Tab,"EK",StringComparison.OrdinalIgnoreCase) &&
                string.Equals((x.Name??"").Replace("\n"," ").Replace("\r"," ").Trim(),
                    "Modify Panels",StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Panel,"Modify Panels",StringComparison.OrdinalIgnoreCase));
            commands.Add(direct);
        }
    }
}

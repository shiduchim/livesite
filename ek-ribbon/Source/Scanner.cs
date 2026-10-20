using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using Autodesk.Revit.UI;
using RevitRibbonItem = Autodesk.Revit.UI.RibbonItem;
using RevitPushButton = Autodesk.Revit.UI.PushButton;
using RevitPulldown = Autodesk.Revit.UI.PulldownButton;

namespace EKRibbon
{
    internal static class Scanner
    {
        internal static bool LastSuccess {get;private set;}
        // Executable custom commands seen in the raw live scan, BEFORE selected-key
        // browser deduplication hides alternative implementations.
        internal static List<CommandInfo> LastLiveExternal {get;private set;}=new List<CommandInfo>();
        internal static string LastError {get;private set;}="";
        internal static string LastDetail {get;private set;}="";
        static string nativePasteIcon="";
        // Only the user's explicit Rescan forces C# icon extraction. Merely
        // opening the editor should remain fast on large Revit installations.
        static bool refreshCustomIcons;
        static readonly List<string> apiDetails=new List<string>();
        static readonly List<CommandInfo> apiExecutable=new List<CommandInfo>();
        static readonly List<string> scanWarnings=new List<string>();
        static int scanWarningCount;
        static void ScanWarning(string context,Exception error)
        {
            // A whole Revit resource library can be unavailable: keep a sample for scan_YEAR.txt.
            scanWarningCount++;
            if(scanWarnings.Count<100)scanWarnings.Add(context+": "+error.GetType().Name+": "+error.Message);
        }
        internal static object Get(object x,string property)
        {
            if(x==null)return null;try {return x.GetType().GetProperty(property, BindingFlags.Instance|BindingFlags.Public)?.GetValue(x,null);}catch{return null;}
        }
        static IEnumerable<object> Children(object x)
        {
            var found=new HashSet<int>();
            foreach(var source in new[]{x,Get(x,"Source")})
            {
                if(source==null)continue;
                foreach(string field in new[]{"Items","SlideOutItems","SlideOutPanelItemsView"})
                {
                    var children=Get(source,field) as IEnumerable;
                    if(children==null)continue;
                    foreach(var v in children)
                        if(v!=null&&found.Add(RuntimeHelpers.GetHashCode(v)))yield return v;
                }
            }
        }
        static string Name(object x)
        {
            foreach(string k in new[]{"Title","Text","Name"})
            {
                string s=Convert.ToString(Get(x,k));if(!string.IsNullOrWhiteSpace(s))return s.Trim();
            }
            return "";
        }
        static bool Menu(string name) {return name.Contains("Split")||name.Contains("Combo")||name.Contains("Gallery")||name.Contains("Pulldown");}
        static bool LayoutNode(string n) {return n.Contains("RowPanel")||n.Contains("RowBreak")||n.Contains("RibbonSeparator")||n.Contains("PanelBreak");}
        static string StoreImage(object node,string key,string oldIcon,bool refresh=false)
        {
            if(!refresh && !string.IsNullOrEmpty(oldIcon)&&File.Exists(oldIcon))return oldIcon;
            // Some Revit native icons are WPF pack/resource URIs such as
            // UIFrameworkRes;component/Ribbon/ThemedImages/... . Accessing
            // BitmapSource.PixelWidth/PixelHeight (before rendering) can throw
            // FileNotFoundException. The ENTIRE icon pipeline must be isolated.
            try
            {
                var imgs=new List<ImageSource>();
                foreach(var source in new[]{node,Get(node,"Source")})
                {
                    if(source==null)continue;
                    foreach(string property in new[]{"LargeImage","Image","Icon","SmallImage"})
                    {
                        try
                        {
                            var value=Get(source,property);
                            var img=value as ImageSource ?? Get(value,"Source") as ImageSource;
                            if(img!=null)imgs.Add(img);
                        }
                        catch(Exception ex){ScanWarning("Read icon "+property+" / "+key,ex);}
                    }
                }
                if(imgs.Count==0)return oldIcon;
                // Image metadata itself can lazily resolve a missing resource.
                // Never let a failed native image interrupt the command scan.
                var ranked=new List<Tuple<long,ImageSource>>();
                foreach(var img in imgs)
                {
                    try
                    {
                        var bitmap=img as BitmapSource;
                        long score=bitmap==null?50000L:(long)bitmap.PixelWidth*bitmap.PixelHeight;
                        ranked.Add(Tuple.Create(score,img));
                    }
                    catch(Exception ex){ScanWarning("Inspect native image / "+key,ex);}
                }
                if(ranked.Count==0)return oldIcon;
                Directory.CreateDirectory(State.Icons);
                string path=Path.Combine(State.Icons,key+".png");
                foreach(var candidate in ranked.OrderByDescending(x=>x.Item1))
                {
                    string temp=path+".new";
                    try
                    {
                        var render=new RenderTargetBitmap(96,96,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                        var img=new System.Windows.Controls.Image{Source=candidate.Item2,Width=96,Height=96,Stretch=Stretch.Uniform};
                        img.Measure(new Size(96,96));img.Arrange(new Rect(0,0,96,96));render.Render(img);
                        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write))
                        {var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));png.Save(stream);}
                        if(refresh)
                        {
                            // Content-addressed files avoid changing a PNG that
                            // a live Revit/WPF ImageSource may still be reading.
                            string digest;
                            using(var hash=System.Security.Cryptography.SHA256.Create())
                            using(var file=File.OpenRead(temp))
                                digest=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").Substring(0,16);
                            string fresh=Path.Combine(State.Icons,key+"_"+digest+".png");
                            if(File.Exists(fresh))File.Delete(temp);
                            else File.Move(temp,fresh);
                            return fresh;
                        }
                        if(File.Exists(path))File.Delete(path);
                        File.Move(temp,path);
                        return path;
                    }
                    catch(Exception ex)
                    {
                        ScanWarning("Render native image / "+key,ex);
                        try{if(File.Exists(temp))File.Delete(temp);}catch{}
                        // Try the next image candidate, or proceed without an icon.
                    }
                }
            }
            catch(Exception ex){ScanWarning("StoreImage / "+key,ex);}
            return oldIcon;
        }
        static void Walk(object node,string tab,string panel,string position,List<CommandInfo> output,
            Dictionary<string,CommandInfo> old,HashSet<int> visited,int depth)
        {
            if(node==null||depth>10)return;
            int identity=RuntimeHelpers.GetHashCode(node);if(!visited.Add(identity))return;
            string type=node.GetType().Name;
            var kids=Children(node).ToList();
            if(!LayoutNode(type))
            {
                string id=Convert.ToString(Get(node,"Id"))??"";
                if(string.IsNullOrWhiteSpace(id))id=Convert.ToString(Get(Get(node,"Source"),"Id"))??"";
                if(string.IsNullOrWhiteSpace(id))id=Convert.ToString(Get(node,"CommandId"))??"";
                string name=Name(node);
                if(string.IsNullOrWhiteSpace(name))name=Name(Get(node,"Source"));
                // Some Paste dropdowns have an icon but no directly postable
                // parent command. Capture that icon for the aligned children.
                if(type.Contains("Button") && name.Trim().Equals("Paste",StringComparison.OrdinalIgnoreCase))
                    nativePasteIcon=StoreImage(node,"native_paste_visual",nativePasteIcon);
                // Split/pulldown headings are usually not executable. Include only
                // actual command buttons with a Revit command ID, even when they
                // own submenu items (e.g. certain Paste ribbon controls).
                if((type.Contains("Button")||type.Contains("Toggle")||type.Contains("CheckBox"))
                    && !string.IsNullOrWhiteSpace(name)
                    && (!string.IsNullOrWhiteSpace(id)||tab.Equals("EK",StringComparison.OrdinalIgnoreCase)))
                {
                    var candidate=new CommandInfo{Id=id,Name=name,Tab=tab,Panel=panel};
                    candidate.Key=CommandInfo.MakeKey(candidate);
                    var prior=old.TryGetValue(candidate.Key,out var matched)?matched:null;
                    candidate.Registered=!string.IsNullOrWhiteSpace(id)&&RibbonBuilder.CommandAvailable(candidate);
                    // Keep visible but non-executable ribbon controls in the browser
                    // instead of making them disappear. Never create a fake launcher.
                    // Browsing includes visible buttons; selected icons are only
                    // launchable when the Revit command ID is actually registered.
                    candidate.Icon=StoreImage(node,candidate.Key,prior?.Icon??"");
                    output.Add(candidate);
                }
            }
            for(int i=0;i<kids.Count;i++)
                Walk(kids[i],tab,panel,position+"/"+i,output,old,visited,depth+1);
        }
        // SECOND discovery route: supported Revit API custom ribbon panels.
        // The visual Autodesk.Windows tree is not a complete command registry;
        // newly installed ordinary C# buttons can be visible without a visual
        // command ID that the previous scanner recognizes.
        static void WalkApi(RevitRibbonItem item,string tab,string panel,List<CommandInfo> output,
            Dictionary<string,CommandInfo> old,ref int inspected,ref int added)
        {
            if(item==null)return;
            if(item is RevitPushButton push)
            {
                inspected++;
                string dll=push.AssemblyName??"";
                string cls=push.ClassName??"";
                string title=(push.ItemText??"").Replace("\r"," ").Replace("\n"," ").Trim();
                if(title.Length==0)title=(push.Name??"").Trim();
                apiDetails.Add(tab+" / "+panel+" / "+title+" | "+
                    (File.Exists(dll)?"DLL exists":"DLL missing")+" | "+
                    "class="+cls+" | assembly="+dll);
                if(title.Length>0 && dll.Length>0 && cls.Length>0 && File.Exists(dll))
                {
                    // Keep the old entry if it is already runnable: this avoids
                    // disturbing saved layouts and proven proxy commands.
                    var existing=output.FirstOrDefault(x=>
                        string.Equals(x.Tab,tab,StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.Panel,panel,StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.Name,title,StringComparison.OrdinalIgnoreCase));
                    var ci=new CommandInfo {Id=ExternalCommands.IdFor(dll,cls),Name=title,
                        Tab=tab,Panel=panel,AssemblyPath=dll,ClassName=cls,Registered=true};
                    ci.Key=CommandInfo.MakeKey(ci);
                    old.TryGetValue(ci.Key,out var previous);
                    // Re-extract supported C# command icons on every explicit
                    // scan. Installed apps can change icons without changing
                    // their command identity. Keep the old file as fallback.
                    ci.Icon=StoreImage(push,ci.Key,previous?.Icon??"",refreshCustomIcons);
                    if(string.IsNullOrWhiteSpace(ci.Icon) && previous!=null &&
                        !string.IsNullOrWhiteSpace(previous.Icon) && File.Exists(previous.Icon))
                        ci.Icon=previous.Icon;
                    apiExecutable.Add(ci);
                    if(existing==null || !existing.Registered)
                    {
                        if(existing!=null)output.Remove(existing);
                        output.Add(ci);added++;
                    }
                    else if(string.IsNullOrWhiteSpace(existing.Icon)||!File.Exists(existing.Icon))
                        existing.Icon=ci.Icon;
                }
            }
            if(item is RevitPulldown menu)
            {
                foreach(var child in menu.GetItems())
                    WalkApi(child,tab,panel,output,old,ref inspected,ref added);
            }
        }
        static void ScanSupportedPanels(UIApplication ui,IEnumerable<string> ribbonTabs,
            List<CommandInfo> output,Dictionary<string,CommandInfo> old)
        {
            if(ui==null){LastDetail="Supported API scan unavailable (no UIApplication).";return;}
            int panels=0, inspected=0, added=0;
            var names=ribbonTabs.Where(t=>!string.IsNullOrWhiteSpace(t) &&
                !t.StartsWith(RibbonBuilder.TabName,StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach(string tab in names)
            {
                IList<Autodesk.Revit.UI.RibbonPanel> custom;
                try{custom=ui.GetRibbonPanels(tab);}
                catch {continue;} // Native tabs are not returned by this overload.
                if(custom==null)continue;
                foreach(var panel in custom)
                {
                    panels++;
                    try
                    {
                        foreach(var item in panel.GetItems())
                        {
                            try{WalkApi(item,tab,string.IsNullOrWhiteSpace(panel.Title)?panel.Name:panel.Title,
                                output,old,ref inspected,ref added);}
                            catch(Exception ex){ScanWarning("Supported command "+tab+" / "+panel.Name,ex);}
                        }
                    }
                    catch(Exception ex){Log.Error("Supported scan "+tab+"/"+panel.Name,ex);}
                }
            }
            // The overload without a tab name covers custom Add-Ins panels.
            try
            {
                foreach(var panel in ui.GetRibbonPanels())
                {
                    panels++;
                    foreach(var item in panel.GetItems())
                    {
                        try{WalkApi(item,"Add-Ins",string.IsNullOrWhiteSpace(panel.Title)?panel.Name:panel.Title,
                            output,old,ref inspected,ref added);}
                        catch(Exception ex){ScanWarning("Supported command Add-Ins / "+panel.Name,ex);}
                    }
                }
            }
            catch(Exception ex){Log.Error("Supported scan Add-Ins",ex);}
            LastDetail="Supported API: "+panels+" panels, "+inspected+
                " custom PushButtons examined, "+added+" additional launchable entries.";
        }
        // Paste Aligned commands live under Revit's dropdown and may not appear
        // as standalone leaf buttons. Expose only API-confirmed postable commands.
        static void AddPasteCommands(List<CommandInfo> output,Dictionary<string,CommandInfo> old)
        {
            var aligned=new[]{
                new[]{"AlignedToCurrentView","Paste Aligned - Current View"},
                new[]{"AlignedToPickedLevel","Paste Aligned - Picked Level"},
                new[]{"AlignedToSamePlace","Paste Aligned - Same Place"},
                new[]{"AlignedToSelectedLevels","Paste Aligned - Selected Levels"},
                new[]{"AlignedToSelectedViews","Paste Aligned - Selected Views"}
            };
            // The ordinary Paste command is not a PostableCommand enum member;
            // resolve its native ID only when Autodesk registered it. Never
            // pretend the dropdown heading is automatically an executable command.
            if(!output.Any(c=>c.Name.Trim().Equals("Paste",StringComparison.OrdinalIgnoreCase)
                 &&c.Registered))
            {
                string normalId="ID_EDIT_PASTE";
                var native=new CommandInfo{Id=normalId,Name="Paste",Tab="Modify",Panel="Clipboard"};
                native.Key=CommandInfo.MakeKey(native);
                native.Registered=RibbonBuilder.CommandAvailable(native);
                if(native.Registered)
                {
                    var pasteIcon=output.FirstOrDefault(c=>c.Name.Trim().Equals("Paste",StringComparison.OrdinalIgnoreCase)
                        &&!string.IsNullOrEmpty(c.Icon)&&File.Exists(c.Icon));
                    native.Icon=pasteIcon?.Icon;
                    output.Add(native);
                }
            }
            foreach(var x in aligned)
            {
                var ci=new CommandInfo{Id="POSTABLE:"+x[0],Name=x[1],Tab="Modify",Panel="Clipboard",Registered=true};
                ci.Key=CommandInfo.MakeKey(ci);
                if(!RibbonBuilder.CommandAvailable(ci))continue;
                // Prefer an actual Revit-captured Paste icon, if available.
                var live=output.FirstOrDefault(c=>c.Name.IndexOf(x[0],StringComparison.OrdinalIgnoreCase)>=0);
                var nativePaste=output.FirstOrDefault(c=>c.Name.IndexOf("Paste",StringComparison.OrdinalIgnoreCase)>=0 &&
                    !string.IsNullOrEmpty(c.Icon)&&File.Exists(c.Icon));
                ci.Icon=live?.Icon??nativePaste?.Icon??nativePasteIcon;
                if(old.TryGetValue(ci.Key,out var prev)&&File.Exists(prev.Icon??""))ci.Icon=prev.Icon;
                // Do not duplicate a native command already discoverable by its API ID.
                output.Add(ci);
            }
        }
        // The Autodesk.Windows visual tree and UIApplication.GetRibbonPanels()
        // describe the same command using different IDs. A hash of those IDs
        // alone cannot deduplicate the command browser. Only consolidate exact
        // same-tab / same-panel / same-caption discoveries; never merge buttons
        // from different panels or distinct explicitly identified C# classes.
        // This is intentionally a catalog-only operation: the saved shared
        // layout (selected keys, order, dividers, zoom) is never rewritten.
        internal static int LastDuplicatesHidden {get;private set;}
        static string NormalizeCaption(string value)
        {
            if(string.IsNullOrWhiteSpace(value))return "";
            // Ribbon sources disagree on newline, ampersand accelerator marks,
            // and non-breaking whitespace. Match display captions, not IDs.
            string cleaned=value.Replace('\u00a0',' ').Replace('\u200b',' ')
                .Replace("&","").Trim();
            return string.Join(" ",cleaned.Split((char[])null,
                StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }
        static string BrowserLocation(CommandInfo ci)
        {
            return NormalizeCaption(ci.Tab)+"|"+NormalizeCaption(ci.Panel)+"|"+
                NormalizeCaption(ci.Name);
        }
        static string ProvenExternal(CommandInfo ci)
        {
            if(ci==null||string.IsNullOrWhiteSpace(ci.ClassName))return "";
            if(string.IsNullOrWhiteSpace(ci.AssemblyPath))return "";
            // Same named button can legitimately refer to multiple different
            // external commands. Protect those independent identities.
            string filename;
            try { filename=Path.GetFileName(ci.AssemblyPath); }
            catch { filename=ci.AssemblyPath; }
            return NormalizeCaption(filename)+"|"+NormalizeCaption(ci.ClassName);
        }
        internal static List<CommandInfo> CollapseDuplicateDiscoveries(
            IEnumerable<CommandInfo> discovered,ICollection<string> selected)
        {
            var source=(discovered??Enumerable.Empty<CommandInfo>()).
                Where(x=>x!=null).ToList();
            var selectedSet=new HashSet<string>(selected??new string[0],
                StringComparer.OrdinalIgnoreCase);
            var result=new List<CommandInfo>();
            int dropped=0;
            foreach(var group in source.GroupBy(BrowserLocation))
            {
                var records=group.ToList();
                if(records.Count==1){result.Add(records[0]);continue;}
                // Two different registered C# classes (even with identical
                // labels) are genuinely different actions, not duplicates.
                var classes=records.Select(ProvenExternal).Where(v=>v.Length>0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if(classes.Count>1)
                {
                    int before=result.Count;
                    foreach(var identity in classes)
                    {
                        var matches=records.Where(x=>string.Equals(
                            ProvenExternal(x),identity,StringComparison.OrdinalIgnoreCase)).ToList();
                        var chosen=PreferredRecord(matches,selectedSet);
                        result.Add(chosen);dropped+=matches.Count-1;
                    }
                    // Ambiguous visual-only records may correspond to any
                    // class: don't invent a binding; preserve a selected key.
                    foreach(var visual in records.Where(x=>ProvenExternal(x).Length==0 &&
                        selectedSet.Contains(x.Key)))result.Add(visual);
                    dropped+=records.Count-(result.Count-before);
                    continue;
                }
                var selectedRecords=records.Where(x=>selectedSet.Contains(x.Key))
                    .GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)
                    .Select(g=>g.First()).ToList();
                if(selectedRecords.Count>1)
                {
                    // Never silently merge two existing saved button slots.
                    result.AddRange(selectedRecords);
                    dropped+=records.Count-selectedRecords.Count;
                    continue;
                }
                var preferred=PreferredRecord(records,selectedSet);
                result.Add(preferred);
                dropped+=records.Count-1;
            }
            LastDuplicatesHidden=dropped;
            return result;
        }
        static CommandInfo PreferredRecord(List<CommandInfo> records,HashSet<string> selected)
        {
            // Saved selection wins, so checking a command never disappears on
            // upgrade. Otherwise prefer the supported API's executable class.
            return records.OrderByDescending(x=>selected.Contains(x.Key))
                .ThenByDescending(x=>ProvenExternal(x).Length>0 && x.Registered)
                .ThenByDescending(x=>x.Registered)
                .ThenByDescending(x=>!string.IsNullOrWhiteSpace(x.Icon) && File.Exists(x.Icon))
                .First();
        }

        static void WriteScanReport(int year,List<CommandInfo> items)
        {
            try
            {
                Directory.CreateDirectory(State.Root);
                var report=new System.Text.StringBuilder();
                report.AppendLine("EK Ribbon Builder "+Application.Version+" scan report "+DateTime.Now.ToString("s"));
                report.AppendLine("Revit "+year+"; "+(LastSuccess?"SUCCESS":"INCOMPLETE/FAILED"));
                report.AppendLine("Commands in report / retained catalog: "+items.Count);
                report.AppendLine("Last error: "+(string.IsNullOrEmpty(LastError)?"(none)":LastError));
                report.AppendLine(LastDetail);
                report.AppendLine("Duplicate browser entries hidden: "+LastDuplicatesHidden);
                report.AppendLine("Shared ribbon selections were not changed by deduplication.");
                report.AppendLine("Non-fatal scan warnings: "+scanWarningCount+" (first "+scanWarnings.Count+" listed)");
                foreach(var warning in scanWarnings)report.AppendLine("WARNING "+warning);
                report.AppendLine("Supported-API custom C# buttons observed (including those not added):");
                foreach(var row in apiDetails)report.AppendLine(row);
                report.AppendLine("EK tab commands (including unregistered controls):");
                report.AppendLine("Modify Panels per-year manifest: "+(KnownAddins.ModifyCommand(year)!=null?"installed / direct command available":"not detected"));
                foreach(var ci in items.Where(x=>string.Equals(x.Tab,"EK",StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x=>x.Panel).ThenBy(x=>x.Name))
                    report.AppendLine(ci.Panel+" / "+ci.Name+" | "+ci.Id+" | " +
                        (ci.Registered?"registered":"not postable/ID missing"));
                File.WriteAllText(Path.Combine(State.Root,"scan_"+year+".txt"),report.ToString());
            }
            catch(Exception ex){Log.Error("Scanner diagnostic",ex);}
        }
        internal static List<CommandInfo> Scan(int year, UIApplication ui=null, bool refreshIcons=false)
        {
            LastSuccess=false;LastError="";LastDetail="";nativePasteIcon="";
            refreshCustomIcons=refreshIcons;
            LastLiveExternal=new List<CommandInfo>();
            LastDuplicatesHidden=0;
            apiDetails.Clear();apiExecutable.Clear();scanWarnings.Clear();scanWarningCount=0;
            var reportItems=new List<CommandInfo>();
            try
            {
                var old=State.ReadCatalog(year).GroupBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.First());
                reportItems=old.Values.ToList();
                var ad=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="AdWindows")??Assembly.Load("AdWindows");
                var cm=ad.GetType("Autodesk.Windows.ComponentManager");
                object ribbon=cm?.GetProperty("Ribbon",BindingFlags.Static|BindingFlags.Public)?.GetValue(null,null);
                var tabs=Get(ribbon,"Tabs") as IEnumerable;
                if(tabs==null) {LastError="Revit ribbon is not ready; previous catalog retained.";Log.Error("Scanner",new Exception(LastError));return old.Values.ToList();}
                var output=new List<CommandInfo>();
                reportItems=output;
                var currentTabs=new List<string>();
                foreach(object tab in tabs)
                {
                    string tabName=Name(tab);currentTabs.Add(tabName);if(tabName.StartsWith(RibbonBuilder.TabName,StringComparison.OrdinalIgnoreCase))continue;
                    var panels=Get(tab,"Panels") as IEnumerable;if(panels==null)continue;
                    foreach(object p in panels)
                    {
                        object src=Get(p,"Source")??p;
                        string panelName=Name(src);if(panelName=="")panelName=Name(p);
                        List<object> children;
                        try{children=Children(src).ToList();}
                        catch(Exception ex){ScanWarning("Visual panel "+tabName+" / "+panelName,ex);continue;}
                        var seen=new HashSet<int>();
                        for(int i=0;i<children.Count;i++)
                        {
                            try {Walk(children[i],tabName,panelName,i.ToString(),output,old,seen,0);}
                            catch(Exception ex){ScanWarning("Visual control "+tabName+" / "+panelName+" / "+i,ex);}
                        }
                    }
                }
                ScanSupportedPanels(ui,currentTabs,output,old);
                AddPasteCommands(output,old);
                KnownAddins.AugmentScan(year,output);
                // IMPORTANT: don't re-add stale add-ins. A complete rescan defines
                // which commands still exist in this running version of Revit.
                output=output.GroupBy(c=>c.Key).Select(g=>g.First()).ToList();
                LastLiveExternal=apiExecutable.Concat(output).Where(c=>ExternalCommands.IsExternal(c.Id) &&
                    !string.IsNullOrWhiteSpace(c.ClassName) && c.Registered &&
                    !string.IsNullOrWhiteSpace(c.AssemblyPath) && File.Exists(c.AssemblyPath))
                    .GroupBy(c=>c.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
                // Preserve selected layout keys while removing duplicate
                // discoveries from the browsable per-year command catalog.
                var selected=State.Load().Selected;
                output=CollapseDuplicateDiscoveries(output,selected);
                LastDetail+=" Duplicate browser entries hidden: "+LastDuplicatesHidden+".";
                reportItems=output;
                if(output.Count<3)
                {
                    LastError="Ribbon scan returned too few controls. Previous catalog retained for safety.";
                    Log.Error("Scanner",new Exception(LastError));return old.Values.ToList();
                }
                State.WriteCatalog(year,output);
                LastSuccess=true;
                return output;
            }
            catch(Exception ex)
            {
                LastError=ex.ToString();
                Log.Error("Scanner",ex);
                // Previous command catalog remains intact if a fatal error occurs.
                // The scan report still records the failure and skipped icons.
                try{return State.ReadCatalog(year);}catch{return reportItems;}
            }
            finally {WriteScanReport(year,reportItems);}
        }
    }
}

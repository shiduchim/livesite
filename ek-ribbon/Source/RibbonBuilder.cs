using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitRibbonItem = Autodesk.Revit.UI.RibbonItem;

namespace EKRibbon
{
    // Builds the EK Custom tab from the shared layout. Revit cannot remove ribbon
    // items, so the tab is built once per session; layout changes need a restart.
    internal static class RibbonBuilder
    {
        internal const string TabName="EK Custom", PanelName="My Tools", ToolsPanelName="EK";
        const int MaxSlots=320;
        static int slot;
        static readonly HashSet<string> spacers=new HashSet<string>();
        static readonly HashSet<string> twoRowButtons=new HashSet<string>();
        static PushButton pinButton;

        internal static int Build(UIControlledApplication app,int year,Layout layout,List<string> report)
        {
            slot=0;spacers.Clear();twoRowButtons.Clear();ProxyRuntime.Commands.Clear();LiveIcons.Clear();
            var items=State.ReadForBuild(year,layout).GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.First());
            var skipped=new List<string>();
            Func<string,bool> present=key=>{
                if(!items.TryGetValue(key,out var ci)||string.IsNullOrWhiteSpace(ci.Id))return false;
                if(Installed(ci,year))return true;
                skipped.Add(ci.Name+" ("+ci.Id+")");
                return false;
            };
            var groups=layout.Arrange(present);
            var panel=app.CreateRibbonPanel(TabName,PanelName);
            int added=0;
            bool first=true;
            foreach(var group in groups)
            {
                var columns=group.Columns.Where(c=>c.Tokens.Any(t=>!Layout.IsGap(t))).ToList();
                if(columns.Count==0)continue;
                if(!first)
                {
                    try{panel.AddSeparator();}
                    catch(Exception ex){Log.Error("Add separator",ex);}
                }
                first=false;
                foreach(var column in columns)
                {
                    if(slot>MaxSlots-3)
                    {
                        Log.ErrorOnce("Proxy slot limit",new InvalidOperationException("The layout has more buttons than proxy slots; the rest were skipped."));
                        break;
                    }
                    added+=column.Large?AddLarge(panel,items[column.Cells[0]],layout,year):AddStack(panel,column,group.Rows,items,layout,year);
                }
            }
            report.Add("Buttons created: "+added+" of "+layout.Selected.Count+" selected");
            foreach(var name in skipped.Distinct())report.Add("Not installed in Revit "+year+": "+name);
            return added;
        }

        static int AddLarge(RibbonPanel panel,CommandInfo ci,Layout layout,int year)
        {
            try
            {
                Register(panel.AddItem(Make(ci,layout,year,32)));
                return 1;
            }
            catch(Exception ex){Log.Error("Large button "+ci.Name+" ("+ci.Id+")",ex);return 0;}
        }

        static int AddStack(RibbonPanel panel,RibbonColumn column,int rows,Dictionary<string,CommandInfo> items,Layout layout,int year)
        {
            var data=new List<PushButtonData>();
            var commands=new HashSet<string>();
            // A failing cell becomes a spacer so it cannot take the whole stack down with it.
            foreach(string token in column.Cells)
            {
                PushButtonData button=null;
                if(token!=null&&!Layout.IsGap(token))
                {
                    try{button=Make(items[token],layout,year,32);commands.Add(button.Name);}
                    catch(Exception ex){Log.Error("Button "+items[token].Name+" ("+items[token].Id+")",ex);}
                }
                data.Add(button??Spacer(year));
            }
            if(commands.Count==0)return 0;
            try
            {
                var created=rows==2?panel.AddStackedItems(data[0],data[1]):panel.AddStackedItems(data[0],data[1],data[2]);
                foreach(var item in created)
                {
                    if(spacers.Contains(item.Name)){item.Enabled=false;continue;}
                    Register(item);
                    if(rows==2)twoRowButtons.Add(item.Name);
                }
                return commands.Count;
            }
            catch(Exception ex)
            {
                // Salvage the commands as individual buttons rather than losing them.
                Log.Error("Stacked buttons; adding them one by one",ex);
                int added=0;
                foreach(var button in data.Where(b=>commands.Contains(b.Name)))
                {
                    try{Register(panel.AddItem(button));added++;}
                    catch(Exception itemError){Log.Error("Button "+button.Name,itemError);}
                }
                return added;
            }
        }

        static void Register(RevitRibbonItem item)
        {
            if(item is PushButton push)LiveIcons.Attach(push);
        }

        static string Caption(string text,Layout layout,int year)
        {
            text=(text??"").Replace("\r"," ").Replace("\n"," ").Trim();
            // Revit 2024 hides captions with a zero-width space; 2025+ uses ShowText=false later.
            if(year==2024&&layout.Labels=="icon")return "​";
            if(layout.Labels=="short"&&text.Length>15)return text.Substring(0,15);
            return text;
        }

        static PushButtonData Make(CommandInfo ci,Layout layout,int year,int largePixels)
        {
            string id=ci.Id;
            string name="EKRB_"+(++slot).ToString("000");
            ProxyRuntime.Commands[slot]=id;
            string assembly=Application.Dll, type="EKRibbon.ProxySlot"+slot.ToString("000");
            bool external=false;
            if(KnownAddins.IsDirect(id))
            {
                if(!KnownAddins.Resolve(id,year,out assembly,out type))throw new InvalidOperationException("Not installed: "+id);
                var icon=KnownAddins.Icon(id,year);
                if(!string.IsNullOrWhiteSpace(icon))ci.Icon=icon;
                external=true;
            }
            else if(ExternalCommands.IsExternal(id))
            {
                if(!ExternalCommands.Resolve(ci,year,out assembly,out type))throw new InvalidOperationException("Not installed: "+id);
                external=true;
            }
            string text=(ci.Name??"").Trim();
            if(text.Length==0)text="EK Command "+slot.ToString("000");
            var b=new PushButtonData(name,Caption(text,layout,year),assembly,type)
            {
                ToolTip=text,
                LongDescription=(ci.Tab??"")+" › "+(ci.Panel??""),
            };
            int zoom=layout.ZoomOf(ci.Key);
            try
            {
                b.Image=Icons.Command(ci,16,zoom);
                b.LargeImage=Icons.Command(ci,largePixels,zoom);
            }
            catch(Exception ex){Log.Error("Icon for "+ci.Name,ex);}
            // Another add-in's button can change its own icon (toggles); mirror that live.
            if(external)LiveIcons.Expect(name,assembly,type,ci.Tab,zoom);
            return b;
        }

        static PushButtonData Spacer(int year)
        {
            string name="EKRB_"+(++slot).ToString("000");
            ProxyRuntime.Commands[slot]="";
            spacers.Add(name);
            return new PushButtonData(name,year==2024?"​":"Spacer",Application.Dll,"EKRibbon.ProxySlot"+slot.ToString("000"))
                {Image=Icons.Transparent(16),LargeImage=Icons.Transparent(32)};
        }

        // Small panel with the pin toggle and the Ribbon Builder, so EK Custom is the only tab you need.
        internal static void AddToolsPanel(UIControlledApplication app,Layout layout,int year)
        {
            var panel=app.CreateRibbonPanel(TabName,ToolsPanelName);
            var pin=new PushButtonData("EKRB_PIN",Caption("Pin tab",layout,year),Application.Dll,typeof(PinCommand).FullName)
                {AvailabilityClassName=typeof(AlwaysAvailable).FullName};
            var settings=new PushButtonData("EKRB_SETTINGS",Caption("Ribbon Builder",layout,year),Application.Dll,typeof(BuilderCommand).FullName)
            {
                AvailabilityClassName=typeof(AlwaysAvailable).FullName,
                ToolTip="Ribbon Builder",
                LongDescription="Choose, arrange and size the buttons on this tab. One layout is shared by Revit 2024-2027.",
                Image=Icons.App(16),LargeImage=Icons.App(32),
            };
            var created=panel.AddStackedItems(pin,settings);
            pinButton=created.OfType<PushButton>().FirstOrDefault(b=>b.Name=="EKRB_PIN");
            UpdatePinButton(layout.PinTab);
        }

        internal static void UpdatePinButton(bool pinned)
        {
            if(pinButton==null)return;
            try
            {
                pinButton.Image=Icons.Pin(pinned,16);
                pinButton.LargeImage=Icons.Pin(pinned,32);
                pinButton.ToolTip=pinned?"EK Custom is pinned":"Pin EK Custom";
                pinButton.LongDescription=pinned
                    ?"Revit keeps returning to this tab. Modify tabs still open while you select or edit, and any tab you click yourself stays open. Click to unpin."
                    :"Click to keep this tab open: Revit will come back here instead of jumping to other tabs.";
            }
            catch(Exception ex){Log.Error("Update pin button",ex);}
        }

        // Revit registers many command IDs late in startup, so native IDs are not
        // checked here; external commands must exist in this Revit year.
        static bool Installed(CommandInfo ci,int year)
        {
            try
            {
                if(KnownAddins.IsDirect(ci.Id))return KnownAddins.Resolve(ci.Id,year,out _,out _);
                if(ExternalCommands.IsExternal(ci.Id))return ExternalCommands.Resolve(ci,year,out _,out _);
                return true;
            }
            catch(Exception ex){Log.Error("Resolve "+ci.Id,ex);return false;}
        }

        internal static RevitCommandId ResolveId(string id)
        {
            if(string.IsNullOrWhiteSpace(id))return null;
            try
            {
                if(id.StartsWith("POSTABLE:",StringComparison.OrdinalIgnoreCase))
                    return Enum.TryParse<PostableCommand>(id.Substring(9),true,out var which)?RevitCommandId.LookupPostableCommandId(which):null;
                return RevitCommandId.LookupCommandId(id);
            }
            catch{return null;}
        }

        internal static bool CommandAvailable(CommandInfo ci)
        {
            if(ci==null)return false;
            if(KnownAddins.IsDirect(ci.Id)||ExternalCommands.IsExternal(ci.Id))return Installed(ci,Application.Year);
            return ResolveId(ci.Id)!=null;
        }

        // ---------- presentation through AdWindows (only on our own tab) ----------

        static object Property(object node,string name)
        {
            try{return node?.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public)?.GetValue(node,null);}
            catch{return null;}
        }

        // Hides captions in icon-only mode (2025+) and enlarges two-row buttons when
        // compact mode is off. Returns false until the tab exists in the visual ribbon.
        internal static bool ApplyAppearance(int year,Layout layout)
        {
            var tab=Autodesk.Windows.ComponentManager.Ribbon?.Tabs?.FirstOrDefault(t=>t.Id==TabName||t.Title==TabName);
            if(tab==null)return false;
            bool hideCaptions=year>=2025&&layout.Labels=="icon";
            foreach(var panel in tab.Panels)
            {
                string title=panel.Source?.Title;
                if(title!=PanelName&&title!=ToolsPanelName)continue;
                Style(panel.Source,new HashSet<object>(),0,hideCaptions,!layout.CompactRibbon&&title==PanelName,year);
            }
            return true;
        }

        static void Style(object node,HashSet<object> visited,int depth,bool hideCaptions,bool enlargeTwoRows,int year)
        {
            if(node==null||depth>9||!visited.Add(node))return;
            if(node is Autodesk.Windows.RibbonItem item)
            {
                string id=item.Id??"";
                bool spacer=spacers.Any(s=>id.IndexOf(s,StringComparison.Ordinal)>=0);
                bool ours=id.IndexOf("EKRB_",StringComparison.Ordinal)>=0;
                try
                {
                    if(ours&&year>=2025&&(hideCaptions||spacer))item.ShowText=false;
                    // Revit can drop the image of a captionless item unless asked explicitly.
                    if(ours&&hideCaptions)item.ShowImage=true;
                    if(ours&&enlargeTwoRows&&twoRowButtons.Any(n=>id.IndexOf(n,StringComparison.Ordinal)>=0))
                        item.Size=Autodesk.Windows.RibbonItemSize.Large;
                }
                catch(Exception ex){Log.ErrorOnce("Style "+id,ex);}
            }
            foreach(var source in new[]{node,Property(node,"Source")})
                if(Property(source,"Items") is IEnumerable children)
                    foreach(var child in children)Style(child,visited,depth+1,hideCaptions,enlargeTwoRows,year);
        }
    }

    // When EK Custom duplicates another add-in's button, that add-in only updates its
    // own copy (e.g. a toggle's on/off icon). Copy icon and tooltip across while idle.
    internal static class LiveIcons
    {
        sealed class Mirror
        {
            internal PushButton Replica, Original;
            internal string Assembly, ClassName, SourceTab;
            internal int Zoom;
            internal ImageSource LastSmall, LastLarge;
            internal string LastTip;
            internal DateTime NextErrorUtc=DateTime.MinValue;
        }
        static readonly Dictionary<string,Mirror> expected=new Dictionary<string,Mirror>(StringComparer.OrdinalIgnoreCase);
        static readonly List<Mirror> active=new List<Mirror>();
        static DateTime nextUtc=DateTime.MinValue;
        internal static bool Any {get{return active.Count>0;}}

        internal static void Clear() {expected.Clear();active.Clear();}
        internal static void Expect(string name,string assembly,string className,string tab,int zoom)
        {
            expected[name]=new Mirror{Assembly=assembly,ClassName=className,SourceTab=tab,Zoom=zoom};
        }
        internal static void Attach(PushButton button)
        {
            if(expected.TryGetValue(button.Name,out var m)){m.Replica=button;active.Add(m);}
        }

        internal static void Sync(UIControlledApplication app)
        {
            var now=DateTime.UtcNow;
            if(now<nextUtc)return;
            nextUtc=now.AddMilliseconds(800);
            foreach(var m in active)
            {
                try
                {
                    if(m.Original==null)m.Original=FindOriginal(app,m);
                    var source=m.Original;
                    if(source==null)continue;
                    var small=source.Image;
                    if(small!=null&&!ReferenceEquals(small,m.LastSmall)){m.Replica.Image=Icons.Zoomed(small,16,m.Zoom);m.LastSmall=small;}
                    var large=source.LargeImage;
                    if(large!=null&&!ReferenceEquals(large,m.LastLarge)){m.Replica.LargeImage=Icons.Zoomed(large,32,m.Zoom);m.LastLarge=large;}
                    string tip=source.ToolTip;
                    if(!string.IsNullOrWhiteSpace(tip)&&tip!=m.LastTip){m.Replica.ToolTip=tip;m.LastTip=tip;}
                }
                catch(Exception ex)
                {
                    // One broken add-in must not stop the other mirrors.
                    if(now>=m.NextErrorUtc){m.NextErrorUtc=now.AddMinutes(2);Log.Error("Live icon "+m.ClassName,ex);}
                    m.Original=null;
                }
            }
        }

        static IEnumerable<RevitRibbonItem> Flatten(RevitRibbonItem item)
        {
            yield return item;
            if(item is PulldownButton menu)
                foreach(RevitRibbonItem child in menu.GetItems())
                    foreach(var nested in Flatten(child))yield return nested;
        }

        static PushButton FindOriginal(UIControlledApplication app,Mirror m)
        {
            if(string.IsNullOrWhiteSpace(m.SourceTab))return null;
            IList<RibbonPanel> panels;
            try{panels=m.SourceTab.Equals("Add-Ins",StringComparison.OrdinalIgnoreCase)?app.GetRibbonPanels():app.GetRibbonPanels(m.SourceTab);}
            catch{return null;} // the other add-in may not have built its tab yet
            foreach(var panel in panels??new List<RibbonPanel>())
                foreach(RevitRibbonItem item in panel.GetItems())
                    foreach(var nested in Flatten(item))
                        if(nested is PushButton b&&!ReferenceEquals(b,m.Replica)&&b.ClassName==m.ClassName&&
                           string.Equals(Path.GetFileName(b.AssemblyName??""),Path.GetFileName(m.Assembly),StringComparison.OrdinalIgnoreCase))
                            return b;
            return null;
        }
    }
}

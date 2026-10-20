using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AW = Autodesk.Windows;

namespace EKRibbon
{
    // Keeps EK Custom as the active ribbon tab.
    //  * Contextual "Modify | ..." tabs are always allowed (they hold the options).
    //  * A tab the user clicks stays open until Revit switches tabs again.
    //  * Anything Revit switches to on its own (Architecture after opening a model,
    //    Modify or the previous tab after a command, ...) goes back to EK Custom.
    // A timer on Revit's UI thread watches the ribbon: Revit's Idling event fires too
    // rarely to notice every switch. Only the ribbon is touched, never the model.
    internal static class TabPin
    {
        static DispatcherTimer timer;
        static bool pinned, positioned;
        static string position="modify";
        static int positionAttempts;
        static AW.RibbonControl hooked;
        static AW.RibbonTab chosen, lastActive;
        static DateTime chosenPendingUtc=DateTime.MinValue, pausedUntilUtc=DateTime.MinValue;
        static readonly Queue<DateTime> returns=new Queue<DateTime>();
        static bool startupDone;

        internal static bool Pinned {get{return pinned;}}

        internal static void Start(Layout layout)
        {
            pinned=layout.PinTab;position=layout.TabPosition;
            timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
            timer.Tick+=OnTick;
            timer.Start();
            Trace("started, pinned="+pinned+", position="+position);
        }

        internal static void Stop()
        {
            if(timer!=null){timer.Stop();timer.Tick-=OnTick;timer=null;}
            Unhook();
        }

        internal static void SetPinned(bool value)
        {
            pinned=value;chosen=null;pausedUntilUtc=DateTime.MinValue;returns.Clear();
            RibbonBuilder.UpdatePinButton(value);
            Trace(value?"pinned":"unpinned");
            if(value)Activate(AW.ComponentManager.Ribbon,Home(AW.ComponentManager.Ribbon));
        }

        internal static void SetPosition(string value)
        {
            if(value==position)return;
            position=value;positioned=false;positionAttempts=0;
        }

        static void OnTick(object sender,EventArgs e)
        {
            try
            {
                var ribbon=AW.ComponentManager.Ribbon;
                if(ribbon==null)return;
                Hook(ribbon);
                var home=Home(ribbon);
                if(home==null)return;
                if(!positioned&&positionAttempts++<20)positioned=ApplyPosition(ribbon,home,!startupDone);
                var active=ribbon.ActiveTab;
                if(!ReferenceEquals(active,lastActive))
                {
                    if(active!=null&&chosenPendingUtc!=DateTime.MinValue&&(DateTime.UtcNow-chosenPendingUtc).TotalSeconds<2)
                    {chosen=active;Trace("you opened "+Describe(active));}
                    chosenPendingUtc=DateTime.MinValue;
                    lastActive=active;
                }
                if(!startupDone)
                {
                    startupDone=true;
                    Trace("ribbon ready: "+ribbon.Tabs.Count+" tabs, active "+Describe(active));
                }
                if(!pinned||active==null||DateTime.UtcNow<pausedUntilUtc)return;
                if(ReferenceEquals(active,home)){chosen=null;return;}
                if(IsContextual(active)||ReferenceEquals(active,chosen))return;
                // Never pull the ribbon from under the mouse, or open a minimized ribbon.
                if(Mouse.LeftButton==MouseButtonState.Pressed||IsMinimized(ribbon)||!home.IsVisible)return;
                GoHome(ribbon,home,active);
            }
            catch(Exception ex){Log.ErrorOnce("Tab pin",ex);}
        }

        static void GoHome(AW.RibbonControl ribbon,AW.RibbonTab home,AW.RibbonTab from)
        {
            // If Revit insists on another tab (some edit modes do), back off instead of fighting it.
            var now=DateTime.UtcNow;
            returns.Enqueue(now);
            while(returns.Count>0&&(now-returns.Peek()).TotalSeconds>10)returns.Dequeue();
            if(returns.Count>8)
            {
                returns.Clear();
                pausedUntilUtc=now.AddSeconds(30);
                Trace("Revit keeps switching to "+Describe(from)+"; pausing for 30 s");
                return;
            }
            Trace("Revit switched to "+Describe(from)+"; back to EK Custom");
            Activate(ribbon,home);
        }

        static void Activate(AW.RibbonControl ribbon,AW.RibbonTab home)
        {
            if(ribbon==null||home==null)return;
            try{ribbon.ActiveTab=home;}catch(Exception ex){Log.ErrorOnce("Tab pin: ActiveTab",ex);}
            if(!ReferenceEquals(ribbon.ActiveTab,home))
            {
                try{home.IsActive=true;}catch(Exception ex){Log.ErrorOnce("Tab pin: IsActive",ex);}
            }
            lastActive=ribbon.ActiveTab;
        }

        static AW.RibbonTab Home(AW.RibbonControl ribbon)
        {
            return ribbon?.Tabs?.FirstOrDefault(t=>t.Id==RibbonBuilder.TabName||t.Title==RibbonBuilder.TabName);
        }

        // "Modify | Walls" and the like. Revit does not always flag them as contextual.
        static bool IsContextual(AW.RibbonTab tab)
        {
            return tab.IsContextualTab||tab.IsMergedContextualTab||(tab.Title??"").Contains("|");
        }

        static bool IsModify(AW.RibbonTab tab)
        {
            return string.Equals(tab.Id,"Modify",StringComparison.OrdinalIgnoreCase)||string.Equals(tab.Title,"Modify",StringComparison.OrdinalIgnoreCase);
        }

        // With "Minimize to tabs" activating a tab would pop its panels open.
        static bool IsMinimized(AW.RibbonControl ribbon)
        {
            try{return ribbon.CurrentAutoHideMode!=Autodesk.Internal.Windows.RibbonAutoHideMode.None;}
            catch{return false;}
        }

        static string Describe(AW.RibbonTab tab) {return tab==null?"(none)":"\""+tab.Title+"\" ["+tab.Id+"]";}

        // "first", "modify" (right after Modify) or "default" (leave Revit's order;
        // when changed live, move to the end where Revit puts add-in tabs).
        static bool ApplyPosition(AW.RibbonControl ribbon,AW.RibbonTab home,bool atStartup)
        {
            var tabs=ribbon.Tabs;
            if(position=="default"&&atStartup)return true;
            AW.RibbonTab after=null;
            if(position=="modify")
            {
                after=tabs.FirstOrDefault(t=>IsModify(t)&&!IsContextual(t));
                if(after==null)return false;
                if(tabs.IndexOf(home)==tabs.IndexOf(after)+1)return true;
            }
            else if(position=="first"&&tabs.IndexOf(home)==0)return true;
            else if(position=="default"&&tabs.IndexOf(home)==tabs.Count-1)return true;
            bool wasActive=ReferenceEquals(ribbon.ActiveTab,home);
            tabs.Remove(home);
            tabs.Insert(position=="first"?0:position=="modify"?tabs.IndexOf(after)+1:tabs.Count,home);
            if(wasActive)Activate(ribbon,home);
            Trace("tab moved: "+position);
            return true;
        }

        // ---------- recognising the user's own tab clicks ----------

        static void Hook(AW.RibbonControl ribbon)
        {
            if(ReferenceEquals(hooked,ribbon))return;
            Unhook();
            ribbon.PreviewMouseLeftButtonDown+=OnRibbonMouseDown;
            hooked=ribbon;
        }

        static void Unhook()
        {
            try{if(hooked!=null)hooked.PreviewMouseLeftButtonDown-=OnRibbonMouseDown;}catch{}
            hooked=null;
        }

        static void OnRibbonMouseDown(object sender,MouseButtonEventArgs e)
        {
            try
            {
                // A click on a tab header: the tab that becomes active next is the user's choice.
                // Clicks on buttons and panels are ignored.
                for(var node=e.OriginalSource as DependencyObject;node!=null;node=Parent(node))
                {
                    object data=(node as FrameworkElement)?.DataContext;
                    if(data is AW.RibbonItem||data is AW.RibbonPanel)return;
                    if(data is AW.RibbonTab){chosenPendingUtc=DateTime.UtcNow;return;}
                }
                // No ribbon data found: trust the tab strip along the top of the ribbon.
                if(sender is IInputElement ribbon&&e.GetPosition(ribbon).Y<34)chosenPendingUtc=DateTime.UtcNow;
            }
            catch{}
        }

        static DependencyObject Parent(DependencyObject node)
        {
            if(node is Visual||node is System.Windows.Media.Media3D.Visual3D)return VisualTreeHelper.GetParent(node)??LogicalTreeHelper.GetParent(node);
            return LogicalTreeHelper.GetParent(node);
        }

        // pin_YEAR.txt: the last few hundred pin decisions, for DIAGNOSTICS.cmd.
        static readonly List<string> trace=new List<string>();
        static void Trace(string text)
        {
            try
            {
                trace.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+text);
                if(trace.Count>300)trace.RemoveRange(0,100);
                Directory.CreateDirectory(State.Root);
                File.WriteAllLines(Path.Combine(State.Root,"pin_"+Application.Year+".txt"),trace);
            }
            catch{}
        }
    }
}

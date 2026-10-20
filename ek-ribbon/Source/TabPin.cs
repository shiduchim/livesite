using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using AW = Autodesk.Windows;

namespace EKRibbon
{
    // Keeps EK Custom as the active ribbon tab.
    //  * Contextual "Modify | ..." tabs are always allowed (they hold the options).
    //  * The plain Modify tab is allowed while something is selected.
    //  * A tab the user clicks stays open until Revit switches tabs again.
    //  * Anything else Revit switches to on its own (Architecture after opening a
    //    model, the previous tab after a command, ...) goes back to EK Custom.
    // Checks run only while Revit is idle, so a running command is never interrupted.
    internal static class TabPin
    {
        static UIControlledApplication host;
        static bool pinned;
        static string position="modify";
        static bool positioned, startupDone;
        static int positionAttempts;
        static AW.RibbonControl hooked;
        static AW.RibbonTab chosen;                     // tab the user clicked
        static DateTime chosenPendingUtc=DateTime.MinValue;
        static DateTime nextUtc=DateTime.MinValue, pausedUntilUtc=DateTime.MinValue;
        static readonly Queue<DateTime> returns=new Queue<DateTime>();

        internal static bool Pinned {get{return pinned;}}

        internal static void Start(UIControlledApplication app,Layout layout)
        {
            host=app;pinned=layout.PinTab;position=layout.TabPosition;
            app.Idling+=OnIdle;
        }

        internal static void Stop()
        {
            try{if(host!=null)host.Idling-=OnIdle;}catch{}
            Unhook();
            host=null;
        }

        internal static void SetPinned(bool value)
        {
            pinned=value;chosen=null;pausedUntilUtc=DateTime.MinValue;returns.Clear();
            RibbonBuilder.UpdatePinButton(value);
            if(value)
            {
                var ribbon=AW.ComponentManager.Ribbon;
                var home=Home(ribbon);
                if(home!=null&&home.IsVisible)home.IsActive=true;
            }
        }

        internal static void SetPosition(string value)
        {
            if(value==position)return;
            position=value;positioned=false;positionAttempts=0;
        }

        static void OnIdle(object sender,IdlingEventArgs e)
        {
            var now=DateTime.UtcNow;
            if(now<nextUtc)return;
            nextUtc=now.AddMilliseconds(200);
            try
            {
                var ribbon=AW.ComponentManager.Ribbon;
                if(ribbon==null)return;
                Hook(ribbon);
                if(!positioned&&positionAttempts++<50)positioned=ApplyPosition(ribbon,!startupDone);
                var home=Home(ribbon);
                if(home==null)return;
                if(!startupDone)
                {
                    startupDone=true;
                    if(pinned&&home.IsVisible&&!IsMinimized(ribbon))home.IsActive=true;
                    return;
                }
                if(!pinned||now<pausedUntilUtc||IsMinimized(ribbon))return;
                var active=ribbon.ActiveTab;
                if(active==null||!home.IsVisible)return;
                if(ReferenceEquals(active,home)){chosen=null;chosenPendingUtc=DateTime.MinValue;return;}
                if(chosenPendingUtc!=DateTime.MinValue)
                {
                    if((now-chosenPendingUtc).TotalSeconds<3)chosen=active;
                    chosenPendingUtc=DateTime.MinValue;
                }
                if(active.IsContextualTab||active.IsMergedContextualTab)return;
                if(ReferenceEquals(active,chosen))return;
                if(IsModify(active)&&HasSelection(sender as UIApplication))return;
                GoHome(home,now);
            }
            catch(Exception ex){Log.ErrorOnce("Tab pin",ex);}
        }

        static void GoHome(AW.RibbonTab home,DateTime now)
        {
            // If Revit keeps switching away (some modes insist on a tab), back off instead of fighting it.
            returns.Enqueue(now);
            while(returns.Count>0&&(now-returns.Peek()).TotalSeconds>10)returns.Dequeue();
            if(returns.Count>8)
            {
                returns.Clear();
                pausedUntilUtc=now.AddSeconds(30);
                return;
            }
            home.IsActive=true;
        }

        static AW.RibbonTab Home(AW.RibbonControl ribbon)
        {
            return ribbon?.Tabs?.FirstOrDefault(t=>t.Id==RibbonBuilder.TabName||t.Title==RibbonBuilder.TabName);
        }

        static bool IsModify(AW.RibbonTab tab)
        {
            return string.Equals(tab.Id,"Modify",StringComparison.OrdinalIgnoreCase)||string.Equals(tab.Title,"Modify",StringComparison.OrdinalIgnoreCase);
        }

        // With "Minimize to tabs" activating a tab would pop its panels open.
        static bool IsMinimized(AW.RibbonControl ribbon)
        {
            return ribbon.CurrentAutoHideMode!=Autodesk.Internal.Windows.RibbonAutoHideMode.None;
        }

        static bool HasSelection(UIApplication app)
        {
            try{return app?.ActiveUIDocument?.Selection.GetElementIds().Count>0;}
            catch{return false;}
        }

        // "first", "modify" (right after Modify) or "default" (leave Revit's order;
        // when changed live, move to the end where Revit puts add-in tabs).
        static bool ApplyPosition(AW.RibbonControl ribbon,bool atStartup)
        {
            var tabs=ribbon.Tabs;
            var home=Home(ribbon);
            if(home==null)return false;
            if(position=="default"&&atStartup)return true;
            bool wasActive=home.IsActive;
            AW.RibbonTab after=null;
            if(position=="modify")
            {
                after=tabs.FirstOrDefault(t=>IsModify(t)&&!t.IsContextualTab);
                if(after==null)return true;
                if(tabs.IndexOf(home)==tabs.IndexOf(after)+1)return true;
            }
            else if(position=="first"&&tabs.IndexOf(home)==0)return true;
            else if(position=="default"&&tabs.IndexOf(home)==tabs.Count-1)return true;
            tabs.Remove(home);
            int target=position=="first"?0:position=="modify"?tabs.IndexOf(after)+1:tabs.Count;
            tabs.Insert(target,home);
            if(wasActive)home.IsActive=true;
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
                // Walk up from the clicked element: a button or panel first means a
                // command click; a tab first means its header (or empty tab space).
                for(var node=e.OriginalSource as DependencyObject;node!=null;node=Parent(node))
                {
                    object data=(node as FrameworkElement)?.DataContext;
                    if(data is AW.RibbonItem||data is AW.RibbonPanel)return;
                    if(data is AW.RibbonTab tab){chosen=tab;return;}
                }
                // Unknown element: trust the tab strip area along the top of the ribbon.
                if(sender is IInputElement ribbon&&e.GetPosition(ribbon).Y<36)chosenPendingUtc=DateTime.UtcNow;
            }
            catch{}
        }

        static DependencyObject Parent(DependencyObject node)
        {
            if(node is Visual||node is System.Windows.Media.Media3D.Visual3D)return VisualTreeHelper.GetParent(node)??LogicalTreeHelper.GetParent(node);
            return LogicalTreeHelper.GetParent(node);
        }
    }
}

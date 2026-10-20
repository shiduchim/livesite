using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AW = Autodesk.Windows;

namespace EKRibbon
{
    // Keeps EK Custom as the active ribbon tab.
    //  * Contextual "Modify | ..." tabs are always allowed (they hold the options).
    //  * A tab the user clicks stays open while they use it. As soon as they click
    //    back into the work area (a view, sheet, Properties, Project Browser),
    //    EK Custom comes back.
    //  * Anything Revit switches to on its own (Architecture after opening a model,
    //    Modify or the previous tab after a command, ...) goes back to EK Custom.
    // A timer on Revit's UI thread watches the ribbon: Revit's Idling event fires too
    // rarely to notice every switch. Only the ribbon is touched, never the model.
    internal static class TabPin
    {
        static DispatcherTimer timer;
        static bool pinned, returnOnClick=true, positioned, startupDone;
        static string position="modify";
        static int positionAttempts;
        static AW.RibbonControl hooked;
        static AW.RibbonTab chosen, lastActive;
        static DateTime chosenPendingUtc=DateTime.MinValue, pausedUntilUtc=DateTime.MinValue;
        static readonly Queue<DateTime> returns=new Queue<DateTime>();

        internal static bool Pinned {get{return pinned;}}

        internal static void Start(Layout layout,IntPtr mainWindow)
        {
            WorkClicks.MainWindow=mainWindow;
            pinned=layout.PinTab;returnOnClick=layout.ReturnOnClick;position=layout.TabPosition;
            timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
            timer.Tick+=(s,e)=>Check();
            timer.Start();
            WorkClicks.Start();
            Trace("started, pinned="+pinned+", return on click="+returnOnClick+", position="+position);
        }

        internal static void Stop()
        {
            if(timer!=null){timer.Stop();timer=null;}
            WorkClicks.Stop();
            Unhook();
        }

        internal static void SetPinned(bool value)
        {
            pinned=value;chosen=null;pausedUntilUtc=DateTime.MinValue;returns.Clear();
            RibbonBuilder.UpdatePinButton(value);
            Trace(value?"pinned":"unpinned");
            if(value)Activate(AW.ComponentManager.Ribbon,Home(AW.ComponentManager.Ribbon));
        }

        internal static void SetReturnOnClick(bool value)
        {
            returnOnClick=value;
            Trace("return on click="+value);
        }

        internal static void SetPosition(string value)
        {
            if(value==position)return;
            position=value;positioned=false;positionAttempts=0;
        }

        internal static void Check()
        {
            try
            {
                var ribbon=AW.ComponentManager.Ribbon;
                if(ribbon==null)return;
                Hook(ribbon);
                // The window that hosts the ribbon is Revit's main window.
                if(WorkClicks.RibbonWindow==IntPtr.Zero&&PresentationSource.FromVisual(ribbon) is HwndSource source)
                {
                    WorkClicks.RibbonWindow=WorkClicks.Root(source.Handle);
                    Trace("main window "+WorkClicks.Describe(WorkClicks.MainWindow)+", ribbon window "+WorkClicks.Describe(WorkClicks.RibbonWindow));
                }
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
                // Wait until the mouse is released so a click or drag in progress is never disturbed.
                if(WorkClicks.AnyButtonDown())return;
                bool backToWork=WorkClicks.Take();
                if(!pinned||active==null||IsMinimized(ribbon)||!home.IsVisible)return;
                if(ReferenceEquals(active,home)){chosen=null;return;}
                if(backToWork&&returnOnClick)
                {
                    // The user is working in the model again: whatever tab they visited is done.
                    chosen=null;
                    if(!IsContextual(active))
                    {
                        Trace("you clicked back into the model on "+Describe(active)+"; back to EK Custom");
                        Activate(ribbon,home);
                    }
                    return;
                }
                if(IsContextual(active)||ReferenceEquals(active,chosen)||DateTime.UtcNow<pausedUntilUtc)return;
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
                    if(data is AW.RibbonTab){ChoseTab();return;}
                }
                // No ribbon data found: trust the tab strip along the top of the ribbon.
                if(sender is IInputElement ribbon&&e.GetPosition(ribbon).Y<34)ChoseTab();
            }
            catch{}
        }

        // A newer tab click wins over an earlier click in the view that is not handled yet.
        static void ChoseTab()
        {
            chosenPendingUtc=DateTime.UtcNow;
            WorkClicks.Take();
        }

        static DependencyObject Parent(DependencyObject node)
        {
            if(node is Visual||node is System.Windows.Media.Media3D.Visual3D)return VisualTreeHelper.GetParent(node)??LogicalTreeHelper.GetParent(node);
            return LogicalTreeHelper.GetParent(node);
        }

        // pin_YEAR.txt: the last few hundred pin decisions, for DIAGNOSTICS.cmd.
        static readonly List<string> trace=new List<string>();
        internal static void Trace(string text)
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

    // Notices mouse clicks in Revit's work area: the drawing / sheet views, Properties,
    // Project Browser and floating views. Those are native windows the ribbon never
    // sees, so a mouse hook on Revit's UI thread watches them. It only records the
    // click; it never blocks or changes it.
    internal static class WorkClicks
    {
        const int WH_MOUSE=7, WM_LBUTTONDOWN=0x201, WM_LBUTTONUP=0x202, WM_RBUTTONDOWN=0x204, WM_RBUTTONUP=0x205,
            WM_MBUTTONDOWN=0x207, WM_MBUTTONUP=0x208, GA_ROOT=2, GW_OWNER=4, VK_LBUTTON=1, VK_RBUTTON=2, VK_MBUTTON=4;

        delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        struct MouseHook {public int X,Y;public IntPtr Window;public uint HitTest;public IntPtr Extra;}

        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int size);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

        static HookProc callback;   // kept alive: the hook calls it from native code
        static IntPtr hook;
        static bool clicked;
        // Revit's main window, from the Revit API and from the window hosting the ribbon.
        internal static IntPtr MainWindow, RibbonWindow;

        internal static void Start()
        {
            if(hook!=IntPtr.Zero)return;
            callback=OnMouse;
            hook=SetWindowsHookEx(WH_MOUSE,callback,IntPtr.Zero,GetCurrentThreadId());
            if(hook==IntPtr.Zero)Log.Error("Tab pin: mouse hook",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }

        internal static void Stop()
        {
            if(hook!=IntPtr.Zero)UnhookWindowsHookEx(hook);
            hook=IntPtr.Zero;
        }

        // True once per click in the work area since the last call.
        internal static bool Take()
        {
            bool result=clicked;
            clicked=false;
            return result;
        }

        internal static bool AnyButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON)&0x8000)!=0||(GetAsyncKeyState(VK_RBUTTON)&0x8000)!=0||(GetAsyncKeyState(VK_MBUTTON)&0x8000)!=0;
        }

        internal static IntPtr Root(IntPtr window) {return GetAncestor(window,GA_ROOT);}

        static IntPtr OnMouse(int code,IntPtr wParam,IntPtr lParam)
        {
            try
            {
                int message=(int)wParam;
                if(code>=0&&(message==WM_LBUTTONDOWN||message==WM_RBUTTONDOWN||message==WM_MBUTTONDOWN))
                {
                    var info=(MouseHook)Marshal.PtrToStructure(lParam,typeof(MouseHook));
                    if(IsWorkArea(info.Window))clicked=true;
                }
                // React as soon as the button is released instead of waiting for the timer.
                else if(code>=0&&clicked&&(message==WM_LBUTTONUP||message==WM_RBUTTONUP||message==WM_MBUTTONUP))
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(TabPin.Check));
            }
            catch{}
            return CallNextHookEx(hook,code,wParam,lParam);
        }

        static string ClassOf(IntPtr window)
        {
            var name=new StringBuilder(128);
            GetClassName(window,name,name.Capacity);
            return name.ToString();
        }

        internal static string Describe(IntPtr window) {return window==IntPtr.Zero?"(none)":"0x"+window.ToString("X")+" "+ClassOf(window);}

        static bool IsMain(IntPtr window) {return window!=IntPtr.Zero&&(window==MainWindow||window==RibbonWindow);}

        static bool IsWorkArea(IntPtr window)
        {
            if(window==IntPtr.Zero)return false;
            // WPF windows: the ribbon, its menus, the quick access toolbar, add-in panels.
            if(ClassOf(window).StartsWith("HwndWrapper",StringComparison.Ordinal))return false;
            var root=Root(window);
            var owner=GetWindow(root,GW_OWNER);
            bool main=IsMain(root);
            // Floating views and palettes belong to the main window; dialogs do not count.
            if(!main&&!(IsMain(owner)&&ClassOf(root)!="#32770"))return false;
            // A modal dialog is open (Revit's main window is disabled): not back at work yet.
            return IsWindowEnabled(main?root:owner);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace EKRibbon
{
    // Revit 2024 loads the net48 build, 2025/2026 the net8 build and 2027 the net10
    // build. All of them read the same layout_shared.json.
    public sealed class Application:IExternalApplication
    {
        internal static readonly string Dll=typeof(Application).Assembly.Location;
        internal static readonly string Version=typeof(Application).Assembly.GetName().Version.ToString(3);
        internal static int Year;
        static UIControlledApplication host;
        static Layout startupLayout;
        static int appearanceAttempts;

        public Result OnStartup(UIControlledApplication app)
        {
            var report=new List<string>();
            try
            {
                Year=int.Parse(app.ControlledApplication.VersionNumber.Substring(0,4));
                report.Add("EK Ribbon Builder "+Version+" in Revit "+Year+" ("+DateTime.Now.ToString("s")+")");
                report.Add("DLL: "+Dll);
                var layout=State.Load();
                startupLayout=layout;
                try{app.CreateRibbonTab(RibbonBuilder.TabName);}catch{}
                // Independent steps: a failure in one must never leave the tab empty.
                try
                {
                    ExternalCommands.BeginBuild();
                    RibbonBuilder.Build(app,Year,layout,report);
                }
                catch(Exception ex){Log.Error("Build EK Custom",ex);report.Add("Build failed: "+ex.Message);}
                finally{ExternalCommands.EndBuild();}
                try{RibbonBuilder.AddToolsPanel(app,layout,Year);}
                catch(Exception ex){Log.Error("Pin / Ribbon Builder buttons",ex);report.Add("Tools panel failed: "+ex.Message);}
                host=app;
                app.Idling+=OnIdle;
                TabPin.Start(app,layout);
                report.Add("Tab pinned: "+layout.PinTab+"; tab position: "+layout.TabPosition+"; labels: "+layout.Labels);
            }
            catch(Exception ex){Log.Error("Startup",ex);report.Add("Startup failed: "+ex.Message);}
            Log.Startup(Year,report);
            return Result.Succeeded;
        }

        // Caption hiding needs the visual ribbon, which exists from the first idle.
        // Live icon mirroring keeps running while Revit is idle.
        static void OnIdle(object sender,IdlingEventArgs e)
        {
            try
            {
                if(startupLayout!=null&&(RibbonBuilder.ApplyAppearance(Year,startupLayout)||++appearanceAttempts>20))startupLayout=null;
                if(LiveIcons.Any)LiveIcons.Sync(host);
                else if(startupLayout==null&&host!=null){host.Idling-=OnIdle;}
            }
            catch(Exception ex)
            {
                Log.ErrorOnce("Ribbon appearance",ex);
                startupLayout=null;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try{if(host!=null)host.Idling-=OnIdle;}catch{}
            TabPin.Stop();
            DeferredPasteAligned.Stop();
            LiveIcons.Clear();
            return Result.Succeeded;
        }
    }
}

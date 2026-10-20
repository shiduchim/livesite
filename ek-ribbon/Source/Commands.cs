using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace EKRibbon
{
    // Settings and pin buttons also work on Revit's start page, with no model open.
    public sealed class AlwaysAvailable:IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app,CategorySet selected) {return true;}
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class BuilderCommand:IExternalCommand
    {
        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            try
            {
                var window=new UI.Designer(data.Application,Application.Year);
                // Application.Current.MainWindow can be a hidden window in Revit 2027; use Revit's own HWND.
                var owner=data.Application.MainWindowHandle;
                if(owner!=IntPtr.Zero)new System.Windows.Interop.WindowInteropHelper(window).Owner=owner;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch(Exception ex){Log.Error("Open Ribbon Builder",ex);message=ex.Message;return Result.Failed;}
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PinCommand:IExternalCommand
    {
        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            try
            {
                bool pinned=!TabPin.Pinned;
                TabPin.SetPinned(pinned);
                State.Update(l=>l.PinTab=pinned);
                return Result.Succeeded;
            }
            catch(Exception ex){Log.Error("Pin tab",ex);message=ex.Message;return Result.Failed;}
        }
    }

    // Every EK Custom button is a numbered proxy (ProxySlots.cs) that posts the
    // Revit command mapped to its slot. Keyboard shortcuts bind to these slots.
    internal static class ProxyRuntime
    {
        internal static readonly Dictionary<int,string> Commands=new Dictionary<int,string>();

        internal static Result Run(ExternalCommandData data,int slot,ref string message)
        {
            if(!Commands.TryGetValue(slot,out var id)||string.IsNullOrWhiteSpace(id))return Result.Cancelled;
            try
            {
                if(DeferredPasteAligned.Handles(id))return DeferredPasteAligned.Queue(data,ref message);
                var cid=RibbonBuilder.ResolveId(id);
                if(cid==null||!data.Application.CanPostCommand(cid))
                {
                    TaskDialog.Show("EK Ribbon","Revit cannot run this command right now:\n"+id);
                    return Result.Cancelled;
                }
                data.Application.PostCommand(cid);
                return Result.Succeeded;
            }
            catch(Exception ex){message=ex.Message;Log.Error("Run "+id,ex);return Result.Failed;}
        }
    }

    // Paste Aligned to Current View is posted on the next idle, after the proxy
    // command has returned: posting it from inside the command can be accepted
    // and then silently dropped. Exactly one attempt; a retry could paste twice.
    internal static class DeferredPasteAligned
    {
        sealed class Request
        {
            internal UIApplication App;
            internal Document Document;
            internal ElementId View;
            internal DateTime CreatedUtc;
        }
        static Request pending;
        static readonly object gate=new object();

        internal static bool Handles(string id)
        {
            return string.Equals(id,"POSTABLE:AlignedToCurrentView",StringComparison.OrdinalIgnoreCase)||
                   string.Equals(id,"ID_EDIT_PASTE_ALIGNED",StringComparison.OrdinalIgnoreCase);
        }

        internal static Result Queue(ExternalCommandData data,ref string message)
        {
            var app=data.Application;
            var doc=app.ActiveUIDocument;
            if(doc?.ActiveView==null)
            {
                TaskDialog.Show("EK Ribbon","Open a model view before using Paste Aligned to Current View.");
                return Result.Cancelled;
            }
            if(RevitCommandId.LookupPostableCommandId(PostableCommand.AlignedToCurrentView)==null)
            {
                message="Revit did not register Paste Aligned to Current View.";
                return Result.Failed;
            }
            lock(gate)
            {
                if(pending!=null)return Result.Cancelled;
                pending=new Request{App=app,Document=doc.Document,View=doc.ActiveView.Id,CreatedUtc=DateTime.UtcNow};
                try{app.Idling+=OnIdle;}
                catch(Exception ex)
                {
                    pending=null;
                    Log.Error("Paste Aligned: subscribe to Idling",ex);
                    message="Could not schedule Paste Aligned: "+ex.Message;
                    return Result.Failed;
                }
            }
            return Result.Succeeded;
        }

        internal static void Stop()
        {
            lock(gate)
            {
                if(pending==null)return;
                try{pending.App.Idling-=OnIdle;}catch{}
                pending=null;
            }
        }

        static void OnIdle(object sender,IdlingEventArgs e)
        {
            Request request;
            lock(gate)
            {
                request=pending;
                if(request==null)return;
                pending=null;
                try{request.App.Idling-=OnIdle;}catch{}
            }
            try
            {
                // Never paste late into a different model or view.
                if((DateTime.UtcNow-request.CreatedUtc).TotalSeconds>8)return;
                var app=sender as UIApplication??request.App;
                var active=app.ActiveUIDocument;
                if(active?.ActiveView==null||!Equals(active.Document,request.Document)||!Equals(active.ActiveView.Id,request.View))return;
                var cid=RevitCommandId.LookupPostableCommandId(PostableCommand.AlignedToCurrentView);
                if(cid==null||!app.CanPostCommand(cid))
                {
                    TaskDialog.Show("EK Ribbon","Revit cannot paste aligned right now. Copy Revit elements first and open a compatible view.");
                    return;
                }
                app.PostCommand(cid);
            }
            catch(Exception ex)
            {
                Log.Error("Paste Aligned to Current View",ex);
                TaskDialog.Show("EK Ribbon","Paste Aligned could not be started: "+ex.Message);
            }
        }
    }
}

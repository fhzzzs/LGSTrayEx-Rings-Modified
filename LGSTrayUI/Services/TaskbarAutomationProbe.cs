using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// UI Automation crosses a process boundary and may block on Explorer. Never
// do it on the WPF dispatcher. One outstanding capture is allowed, and a
// stale/other-monitor/other-layout result cannot authorize painting.
internal static class TaskbarAutomationProbe
{
    private sealed record Snapshot(nint Bar,TaskbarPlacement.PixelRect Bounds,long Started,long Completed,long Generation,Rect[] Controls);
    private static Task<Snapshot?>? pending;
    private static Snapshot? last;
    private static long generation;
    internal static void Invalidate()=>Interlocked.Increment(ref generation);
    internal static bool Fresh(long now,long started,long completed)
        =>completed>=started&&completed<=now&&now-started<=1500;
    internal static bool TryRead(nint bar,TaskbarPlacement.PixelRect bounds,out Rect[] controls)
    {
        controls=[];
        long now=Environment.TickCount64;long currentGeneration=Volatile.Read(ref generation);
        if(pending?.IsCompleted==true){
            if(pending.IsCompletedSuccessfully)last=pending.Result;
            pending=null;
        }
        bool matching=last!=null&&last.Bar==bar&&last.Bounds.Equals(bounds)&&last.Generation==currentGeneration&&Fresh(now,last.Started,last.Completed);
        if(pending==null&&(!matching||now-last!.Completed>=400))
            pending=Task.Run(()=>Capture(bar,bounds,currentGeneration));
        if(!matching||last!.Controls.Length==0)return false;
        controls=last.Controls;return true;
    }
    private static Snapshot? Capture(nint bar,TaskbarPlacement.PixelRect bounds,long captureGeneration)
    {
        long start=Environment.TickCount64;
        try{
            var condition=new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox));
            var request=new CacheRequest{TreeScope=TreeScope.Element};
            foreach(var p in new[]{AutomationElement.BoundingRectangleProperty,AutomationElement.IsOffscreenProperty})request.Add(p);
            var result=new List<Rect>();
            using(request.Activate()){
                var root=AutomationElement.FromHandle(bar);
                foreach(AutomationElement item in root.FindAll(TreeScope.Descendants,condition)){
                    var r=item.Cached.BoundingRectangle;
                    if(item.Cached.IsOffscreen||r.IsEmpty||r.Width<=0||r.Height<=0||r.Right<=bounds.Left||r.Left>=bounds.Right||r.Bottom<=bounds.Top||r.Top>=bounds.Bottom)continue;
                    result.Add(r);
                }
            }
            return new(bar,bounds,start,Environment.TickCount64,captureGeneration,result.ToArray());
        }catch(Exception ex){DiagnosticLogger.Verbose("Taskbar UIA probe: "+ex.Message);return null;}
    }
}

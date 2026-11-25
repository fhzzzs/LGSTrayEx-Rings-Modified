using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal sealed record TaskbarControlBounds(Rect Rect,bool IsStart);

// UI Automation crosses a process boundary and may block on Explorer. Never
// do it on the WPF dispatcher. One outstanding capture is allowed, and a
// stale/other-monitor/other-layout result cannot authorize painting.
internal static class TaskbarAutomationProbe
{
    private sealed record Snapshot(nint Bar,TaskbarPlacement.PixelRect Bounds,long Started,long Completed,TaskbarControlBounds[] Controls);
    private static Task<Snapshot?>? pending;
    private static Snapshot? last;
    internal static bool Fresh(long now,long started,long completed,long minimum)
        =>started>=minimum&&completed>=started&&completed<=now&&now-started<=1500;
    internal static bool TryRead(nint bar,TaskbarPlacement.PixelRect bounds,long minimum,out TaskbarControlBounds[] controls)
    {
        controls=[];
        long now=Environment.TickCount64;
        if(pending?.IsCompleted==true){
            if(pending.IsCompletedSuccessfully)last=pending.Result;
            pending=null;
        }
        bool matching=last!=null&&last.Bar==bar&&last.Bounds.Equals(bounds)&&Fresh(now,last.Started,last.Completed,minimum);
        if(pending==null&&(!matching||now-last!.Completed>=400))
            pending=Task.Run(()=>Capture(bar,bounds));
        if(!matching||last!.Controls.Length==0)return false;
        controls=last.Controls;return true;
    }
    private static Snapshot? Capture(nint bar,TaskbarPlacement.PixelRect bounds)
    {
        long start=Environment.TickCount64;
        try{
            var condition=new OrCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty,"StartButton"),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox));
            var request=new CacheRequest{TreeScope=TreeScope.Element};
            foreach(var p in new[]{AutomationElement.BoundingRectangleProperty,AutomationElement.IsOffscreenProperty,AutomationElement.AutomationIdProperty,AutomationElement.ClassNameProperty,AutomationElement.NameProperty})request.Add(p);
            var result=new List<TaskbarControlBounds>();
            using(request.Activate()){
                var root=AutomationElement.FromHandle(bar);
                foreach(AutomationElement item in root.FindAll(TreeScope.Descendants,condition)){
                    var r=item.Cached.BoundingRectangle;
                    if(item.Cached.IsOffscreen||r.IsEmpty||r.Width<=0||r.Height<=0||r.Right<=bounds.Left||r.Left>=bounds.Right||r.Bottom<=bounds.Top||r.Top>=bounds.Bottom)continue;
                    bool startButton=item.Cached.AutomationId=="StartButton"||(item.Cached.ClassName=="ToggleButton"&&item.Cached.Name is "Start" or "开始");
                    result.Add(new(r,startButton));
                }
            }
            return new(bar,bounds,start,Environment.TickCount64,result.ToArray());
        }catch(Exception ex){DiagnosticLogger.Verbose("Taskbar UIA probe: "+ex.Message);return null;}
    }
}

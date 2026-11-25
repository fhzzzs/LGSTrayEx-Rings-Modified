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
    private static long retryAfter;
    private static int failures;
    private static long captures,cacheHits,rejected,totalMs;
    internal static string Metrics=>$"captures={Interlocked.Read(ref captures)} cache hits={Interlocked.Read(ref cacheHits)} rejected={Interlocked.Read(ref rejected)} total capture ms={Interlocked.Read(ref totalMs)}";
    internal static long RetryDelay(int failures)=>failures<=0?0:Math.Min(5000,250L*(1L<<Math.Min(4,failures)));
    internal static void Invalidate(){Interlocked.Increment(ref generation);Interlocked.Exchange(ref retryAfter,0);}
    internal static bool Fresh(long now,long started,long completed)
        =>completed>=started&&completed<=now&&now-started<=1500;
    internal static bool TryRead(nint bar,TaskbarPlacement.PixelRect bounds,out Rect[] controls)
    {
        controls=[];
        long now=Environment.TickCount64;long currentGeneration=Volatile.Read(ref generation);
        if(pending?.IsCompleted==true){
            bool superseded=pending.IsCompletedSuccessfully&&pending.Result is { } oldCapture&&oldCapture.Generation!=currentGeneration;
            if(pending.IsCompletedSuccessfully&&pending.Result is { } captured&&captured.Controls.Length>0&&captured.Generation==currentGeneration&&Fresh(now,captured.Started,captured.Completed)){
                last=captured;failures=0;retryAfter=0;
            }else{
                // A layout invalidation is not a provider failure; retry promptly.
                Interlocked.Increment(ref rejected);if(superseded){failures=0;retryAfter=0;}else{failures=Math.Min(16,failures+1);retryAfter=now+RetryDelay(failures);}
            }
            pending=null;
        }
        bool matching=last!=null&&last.Bar==bar&&last.Bounds.Equals(bounds)&&last.Generation==currentGeneration&&Fresh(now,last.Started,last.Completed);
        // Keep the existing 1.5s safety freshness limit. Back off on failed
        // UIA captures rather than continuously rebuilding cross-process trees.
        if(pending==null&&(now>=retryAfter||last!=null&&last.Generation!=currentGeneration)&&(!matching||now-last!.Completed>=650))
            pending=Task.Run(()=>Capture(bar,bounds,currentGeneration));
        if(!matching||last!.Controls.Length==0)return false;
        Interlocked.Increment(ref cacheHits);controls=last.Controls;return true;
    }
    private static Snapshot? Capture(nint bar,TaskbarPlacement.PixelRect bounds,long captureGeneration)
    {
        long start=Environment.TickCount64;Interlocked.Increment(ref captures);
        try{
            var condition=new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox));
            var root=AutomationElement.FromHandle(bar);
            var request=new CacheRequest{TreeScope=TreeScope.Element,AutomationElementMode=AutomationElementMode.None};
            foreach(var p in new[]{AutomationElement.BoundingRectangleProperty,AutomationElement.IsOffscreenProperty})request.Add(p);
            var result=new HashSet<Rect>();
            using(request.Activate()){
                foreach(AutomationElement item in root.FindAll(TreeScope.Descendants,condition)){
                    var r=item.Cached.BoundingRectangle;
                    if(item.Cached.IsOffscreen||r.IsEmpty||r.Width<=0||r.Height<=0||r.Right<=bounds.Left||r.Left>=bounds.Right||r.Bottom<=bounds.Top||r.Top>=bounds.Bottom)continue;
                    result.Add(r);
                }
            }
            var controls=new Rect[result.Count];result.CopyTo(controls);return new(bar,bounds,start,Environment.TickCount64,captureGeneration,controls);
        }catch(Exception ex){DiagnosticLogger.Verbose("Taskbar UIA probe: "+ex.Message);return null;}finally{Interlocked.Add(ref totalMs,Environment.TickCount64-start);}
    }
}

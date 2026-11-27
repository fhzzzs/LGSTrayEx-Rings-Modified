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
    private static long lastAttempt;
    private static bool softDirty,refreshRequested;
    private static long softSince;
    internal const int CaptureIntervalMs=950,SnapshotLifetimeMs=1500;
    private static long captures,cacheHits,rejected,totalMs;
    private static long occupiedCapture=-1;
    [ThreadStatic] private static OrCondition? controlsCondition;
    [ThreadStatic] private static CacheRequest? controlsRequest;
    private static CacheRequest CreateControlsRequest()
    {
        var request=new CacheRequest{TreeScope=TreeScope.Element,AutomationElementMode=AutomationElementMode.None};
        request.Add(AutomationElement.BoundingRectangleProperty);
        request.Add(AutomationElement.IsOffscreenProperty);
        return request;
    }
    internal static string Metrics=>$"captures={Interlocked.Read(ref captures)} cache hits={Interlocked.Read(ref cacheHits)} rejected={Interlocked.Read(ref rejected)} total capture ms={Interlocked.Read(ref totalMs)}";
    internal static long RetryDelay(int failures)=>failures<=0?0:Math.Min(5000,250L*(1L<<Math.Min(4,failures)));
    // Hard invalidation (display/DPI/Explorer changes) rejects every older capture.
    internal static void Invalidate(){Interlocked.Increment(ref generation);Interlocked.Exchange(ref retryAfter,0);softDirty=false;refreshRequested=true;}
    // Ordinary taskbar animation/layout notifications request a new capture but
    // cannot endlessly invalidate an in-flight query during a window drag.
    internal static void RequestRefresh(){
        if(!softDirty){softDirty=true;softSince=Environment.TickCount64;}
        refreshRequested=true;
        if(failures==0)retryAfter=0;
    }
    internal static void TrailingGapOccupied(){
        // Hide immediately. Recheck an unchanged, occupied shell at a lower rate.
        // Schedule only once per snapshot so polling cannot perpetually postpone it.
        if(last==null || last.Generation!=generation || occupiedCapture==last.Completed)return;
        occupiedCapture=last.Completed;
        retryAfter=Math.Max(retryAfter,last.Completed+2500);
    }
    internal static bool Fresh(long now,long started,long completed)
        =>completed>=started&&completed<=now&&now-started<=SnapshotLifetimeMs;
    internal static bool TryRead(nint bar,TaskbarPlacement.PixelRect bounds,out Rect[] controls)
        =>TryRead(bar,bounds,out controls,out _);
    internal static bool TryRead(nint bar,TaskbarPlacement.PixelRect bounds,out Rect[] controls,out bool awaiting)
    {
        controls=[];awaiting=false;
        long now=Environment.TickCount64;long currentGeneration=Volatile.Read(ref generation);
        if(pending?.IsCompleted==true){
            bool superseded=pending.IsCompletedSuccessfully&&pending.Result is { } oldCapture&&oldCapture.Generation!=currentGeneration;
            if(pending.IsCompletedSuccessfully&&pending.Result is { } captured&&captured.Controls.Length>0&&captured.Generation==currentGeneration&&Fresh(now,captured.Started,captured.Completed)){
                last=captured;failures=0;retryAfter=0;
                if(softDirty&&captured.Started>=softSince)softDirty=false;
            }else{
                // A layout invalidation is not a provider failure; retry promptly.
                Interlocked.Increment(ref rejected);if(superseded){failures=0;retryAfter=0;}else{failures=Math.Min(16,failures+1);retryAfter=now+RetryDelay(failures);}
            }
            pending=null;
        }
        bool matching=last!=null&&last.Bar==bar&&last.Bounds.Equals(bounds)&&last.Generation==currentGeneration&&Fresh(now,last.Started,last.Completed);
        // Keep the original 1.5 second freshness safety gate. Start fewer
        // speculative captures and invalidate immediately on a shell change.
        if(pending==null&&now>=retryAfter&&(refreshRequested||softDirty||!matching||now-last!.Completed>=CaptureIntervalMs)&&now-lastAttempt>=250){
            lastAttempt=now;refreshRequested=false;pending=Task.Run(()=>Capture(bar,bounds,currentGeneration));
        }
        if(!matching||softDirty||last!.Controls.Length==0){
            // Only a transient query wait is eligible for holding an existing
            // overlay. Actual failures/backoff must not extend stale geometry.
            awaiting=failures==0&&(pending!=null||now>=retryAfter);
            return false;
        }
        Interlocked.Increment(ref cacheHits);controls=last.Controls;return true;
    }
    private static Snapshot? Capture(nint bar,TaskbarPlacement.PixelRect bounds,long captureGeneration)
    {
        long start=Environment.TickCount64;Interlocked.Increment(ref captures);
        try{
            var condition=controlsCondition??=new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox));
            var root=AutomationElement.FromHandle(bar);
            // CacheRequest activation is thread-local; reuse only on the same worker.
            var request=controlsRequest??=CreateControlsRequest();
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

using System;
using System.Threading;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// Only an already-verified, unchanged native anchor may survive a short
// asynchronous UIA refresh. All confirmed unsafe results hide immediately.
internal sealed class TaskbarPlacementStability
{
    internal const int HoldMs=650;
    private TaskbarPlacement.Anchor? verified;
    private long? waitingSince;
    private static long holds,hides,shows;
    internal static string Metrics=>$"holds={Interlocked.Read(ref holds)} hides={Interlocked.Read(ref hides)} shows={Interlocked.Read(ref shows)}";
    internal void Verified(TaskbarPlacement.Anchor anchor){verified=anchor;waitingSince=null;}
    internal void Reset(){verified=null;waitingSince=null;}
    internal bool TryHold(TaskbarPlacementStatus status,TaskbarPlacement.Anchor anchor,long now,out int remaining)
    {
        remaining=0;
        if(status!=TaskbarPlacementStatus.AwaitingProbe||verified is not {} prior||prior!=anchor){
            Reset();return false;
        }
        if(waitingSince==null){
            waitingSince=now;
            Interlocked.Increment(ref holds);
            DiagnosticLogger.Verbose("Taskbar placement: retaining existing position during a pending UIA query.");
        }
        long elapsed=now-waitingSince.Value;
        if(elapsed<0||elapsed>=HoldMs){Reset();return false;}
        remaining=HoldMs-(int)elapsed;
        return true;
    }
    internal static void Hidden(TaskbarPlacementStatus status){
        Interlocked.Increment(ref hides);
        DiagnosticLogger.Verbose($"Taskbar placement: hide ({status}).");
    }
    internal static void Shown(){
        Interlocked.Increment(ref shows);
        DiagnosticLogger.Verbose("Taskbar placement: show after verification.");
    }
}

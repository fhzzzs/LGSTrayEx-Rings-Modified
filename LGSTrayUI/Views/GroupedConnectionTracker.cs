using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

internal sealed record ConnectionChange(TaskbarBattery Device,bool Online);

// The UI inventory is the source of truth: HID/G HUB transitions are not device events.
// Baseline initial discovery and rescans silently, then require a stable state change.
internal sealed class GroupedConnectionTracker
{
    private sealed class State
    {
        internal bool Online;
        internal bool? Pending;
        internal DateTimeOffset Since;
        internal HashSet<string> Sources=new(StringComparer.Ordinal);
    }
    private Dictionary<string,State> states=new(StringComparer.Ordinal);
    private DateTimeOffset suppressedUntil,lastObserved;
    internal GroupedConnectionTracker(DateTimeOffset now)
    {
        suppressedUntil=now.AddSeconds(10);lastObserved=now;
    }
    internal void Suppress(DateTimeOffset now,TimeSpan duration)
    {
        var until=now+duration;
        if(until>suppressedUntil)suppressedUntil=until;
    }
    internal bool IsSuppressed(DateTimeOffset now)=>now<suppressedUntil;
    private static bool Online(DeviceGroup g)=>g.Sources.Any(d=>!d.Offline&&d.Status!="等待连接");
    internal ConnectionChange[] Observe(IEnumerable<DeviceGroup> groups,DateTimeOffset now)
    {
        // A long observation gap is common during sleep/hibernate; rebaseline on wake.
        if(now-lastObserved>TimeSpan.FromSeconds(15))Suppress(now,TimeSpan.FromSeconds(10));
        lastObserved=now;
        bool quiet=IsSuppressed(now);
        var next=new Dictionary<string,State>(StringComparer.Ordinal);
        var changes=new List<ConnectionChange>();
        var snapshot=groups.ToArray();
        foreach(var g in snapshot)
        {
            var key=g.Reading.Key;
            var sources=g.Sources.Select(d=>d.Key).Append(key).ToHashSet(StringComparer.Ordinal);
            bool online=Online(g);
            var matches=states.Where(p=>p.Key==key||p.Value.Sources.Overlaps(sources)).Select(p=>p.Value).Distinct().ToArray();
            var state=matches.Length==1?matches[0]:new State{Online=matches.Length>1?online:false};
            // A merge/split or a user setting change is not a plug/unplug.
            bool split=matches.Length==1&&snapshot.Count(other=>other.Sources.Select(d=>d.Key).Append(other.Reading.Key).Any(state.Sources.Contains))>1;
            if(quiet||matches.Length>1||split){
                state=new State{Online=online};
            }else if(state.Online==online){
                state.Pending=null;
            }else if(state.Pending!=online){
                state.Pending=online;state.Since=now;
            }else if(now-state.Since>=TimeSpan.FromMilliseconds(2500)){
                state.Online=online;state.Pending=null;
                changes.Add(new ConnectionChange(g.Reading,online));
            }
            state.Sources=sources;next[key]=state;
        }
        states=next;
        return changes.ToArray();
    }
}

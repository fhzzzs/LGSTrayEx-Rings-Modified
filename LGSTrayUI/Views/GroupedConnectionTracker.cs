using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

internal sealed record ConnectionChange(TaskbarBattery Device,bool Online);
internal sealed record ConnectionSnapshot(DeviceGroup[] Groups,ConnectionChange[] Changes,bool Suppressed);

// A single confirmed lifecycle feeds rendering and notification. Raw source
// disappearance is not a confirmed physical disconnect.
internal sealed class GroupedConnectionTracker
{
    private sealed class State
    {
        internal bool Online;
        internal bool? Pending;
        internal DateTimeOffset Since;
        internal HashSet<string> Sources=new(StringComparer.Ordinal);
        internal TaskbarBattery? Last;
        internal bool HeldDuringSuppression;
    }
    private Dictionary<string,State> states=new(StringComparer.Ordinal);
    private DeviceGroup[] previousGroups=[];
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
    internal bool HasPendingTransitions=>states.Values.Any(s=>s.Pending.HasValue||s.HeldDuringSuppression);
    internal bool IsSuppressed(DateTimeOffset now)=>now<suppressedUntil;
    private static bool Online(DeviceGroup g)=>g.Sources.Any(d=>d.IsConnected);
    internal ConnectionChange[] Observe(IEnumerable<DeviceGroup> groups,DateTimeOffset now)=>ObserveSnapshot(groups,now).Changes;
    internal ConnectionSnapshot ObserveSnapshot(IEnumerable<DeviceGroup> groups,DateTimeOffset now)
    {
        if(now-lastObserved>TimeSpan.FromSeconds(15))Suppress(now,TimeSpan.FromSeconds(10));
        lastObserved=now;
        bool quiet=IsSuppressed(now);
        var input=groups as DeviceGroup[]??groups.ToArray();
        if(TaskbarBatteryWindow.SameGrouping(previousGroups,input) && Steady(input)){
            previousGroups=input;
            return new(input,[],quiet);
        }
        var next=new Dictionary<string,State>(StringComparer.Ordinal);
        var changes=new List<ConnectionChange>();
        var effective=new List<DeviceGroup>();
        var snapshot=input.Select(g=>(Group:g,Keys:g.Sources.Select(d=>d.Key).Append(g.Reading.Key).ToHashSet(StringComparer.Ordinal))).ToArray();
        foreach(var item in snapshot)
        {
            var g=item.Group;var key=g.Reading.Key;var sources=item.Keys;
            bool online=Online(g);
            var matches=states.Where(p=>p.Key==key||p.Value.Sources.Overlaps(sources)).Select(p=>p.Value).Distinct().ToArray();
            var state=matches.Length==1?matches[0]:new State{Online=matches.Length>1?online:false};
            bool split=matches.Length==1&&snapshot.Count(other=>other.Keys.Overlaps(state.Sources))>1;
            if(matches.Length>1||split){
                state=new State{Online=online};
            }else if(quiet){
                if(state.Online&&!online&&state.Last!=null)state.HeldDuringSuppression=true;
                else{state.Online=online;state.HeldDuringSuppression=false;}
                state.Pending=null;
            }else if(state.HeldDuringSuppression){
                // End a rescan by silently reconciling the settled inventory.
                state.Online=online;state.HeldDuringSuppression=false;state.Pending=null;
            }else if(state.Online==online){
                state.Pending=null;
            }else if(state.Pending!=online){
                state.Pending=online;state.Since=now;
            }else if(now-state.Since>=TimeSpan.FromMilliseconds(2500)){
                state.Online=online;state.Pending=null;
                changes.Add(new ConnectionChange(g.Reading,online));
            }
            TaskbarBattery reading=g.Reading;
            if(state.Online&&!online&&state.Last!=null)
                reading=state.Last.WithConnection(ConnectionState.Reconnecting) with{Key=key,Name=g.Reading.Name,LastKnownLevel=state.Last.Level??state.Last.LastKnownLevel,Level=null,ShortLabel=null,Stale=true,Charging=false};
            else if(!state.Online&&online)
                reading=g.Reading.WithConnection(ConnectionState.Disconnected) with{Level=null,Charging=false,ShortLabel=null};
            if(state.Online&&online)state.Last=g.Reading;
            state.Sources=sources;next[key]=state;
            effective.Add(g with{Reading=reading});
        }
        states=next;previousGroups=input;
        return new ConnectionSnapshot(effective.ToArray(),changes.ToArray(),quiet);
    }
    private bool Steady(DeviceGroup[] groups)
    {
        foreach(var g in groups){
            if(!states.TryGetValue(g.Reading.Key,out var state) || state.Pending.HasValue ||
               state.HeldDuringSuppression || state.Online!=Online(g))return false;
        }
        foreach(var g in groups)if(states[g.Reading.Key].Online)states[g.Reading.Key].Last=g.Reading;
        return true;
    }
}

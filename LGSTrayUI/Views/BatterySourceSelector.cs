using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

// Selection is independent of window placement and rendering. A source remains
// preferred while healthy; only confirmed usable data can replace it.
internal sealed class BatterySourceSelector
{
    private readonly Dictionary<string,string> selected=new(StringComparer.Ordinal);
    internal static bool Usable(TaskbarBattery row)=>row.IsConnected&&row.Level is >=0 and <=100;
    private static bool NativeFull(TaskbarBattery row)=>row.IsFull&&row.Key.Contains(":Native:",StringComparison.Ordinal);
    internal static TaskbarBattery? ConfirmedFull(SourceReading[] fresh){
        if(fresh.Any(s=>s.Quality==ReadingQuality.Exact&&s.Row.Level<95))return null;
        var full=fresh.Where(s=>s.Quality==ReadingQuality.Exact&&NativeFull(s.Row)).OrderByDescending(s=>s.Row.Updated).FirstOrDefault();
        if(full==null)return null;
        if(fresh.Any(s=>s.Row.Key!=full.Row.Key&&s.Quality==ReadingQuality.Exact&&s.Row.Updated>full.Row.Updated.AddSeconds(30)&&!s.Row.IsFull))return null;
        return full.Row;
    }
    internal DeviceGroup[] Select(IEnumerable<DeviceGroup> groups,RingPreferences preferences,DateTimeOffset now)
    {
        var result=new List<DeviceGroup>();
        var retained=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in groups)
        {
            string root=group.Reading.Key;
            retained.Add(root);
            var sources=group.Sources;
            var fresh=sources.Where(d=>d.IsConnected).Select(d=>SourceReading.Normalize(d,now,preferences)).Where(d=>d.Quality is ReadingQuality.Exact or ReadingQuality.Band).ToArray();
            var candidates=fresh.Where(d=>d.Quality==ReadingQuality.Exact).Select(d=>d.Row).ToArray();
            TaskbarBattery? chosen=null;
            if(preferences.PreferredSources.TryGetValue(root,out var preferred))
                chosen=fresh.FirstOrDefault(d=>d.Row.Key==preferred)?.Row;
            // Explicit user preference wins. Otherwise a fresh native FULL report
            // supersedes a near-full charging G HUB observation of the same group.
            if(chosen==null && Array.Exists(candidates,NativeFull)) {
                chosen=ConfirmedFull(fresh);
                if(chosen==null && Array.Exists(candidates,row=>!NativeFull(row))) {
                    fresh=Array.FindAll(fresh,s=>!NativeFull(s.Row));
                    candidates=Array.FindAll(candidates,row=>!NativeFull(row));
                }
            }
            if(chosen==null&&group.Inferred&&sources.Any(ControllerAliasMatcher.IsGip))chosen=candidates.FirstOrDefault(ControllerAliasMatcher.IsXboxBle);
            if(chosen==null&&selected.TryGetValue(root,out var previous))
                chosen=candidates.FirstOrDefault(d=>d.Key==previous);
            chosen??=candidates.Length>0?DeviceIdentityManager.Best(fresh.Where(d=>d.Quality==ReadingQuality.Exact).GroupBy(d=>d.Confidence).OrderByDescending(g=>g.Key).First().Select(d=>d.Row),null):
                DeviceIdentityManager.Best(fresh.Select(d=>d.Row).DefaultIfEmpty(DeviceIdentityManager.Best(sources,null)),null);
            if(SourceReading.Normalize(chosen,now,preferences).Quality==ReadingQuality.Stale)
                chosen=chosen with{LastKnownLevel=chosen.Level??chosen.LastKnownLevel,Level=null,ShortLabel=null,Stale=true,Charging=false};
            if(chosen.Level is int level&&(level<0||level>100))chosen=chosen with{Level=null};
            if(chosen.IsFull&&chosen.Charging)chosen=chosen with{Charging=false};
            selected[root]=chosen.Key;RingDiagnostics.Source(root,chosen.Key);
            // Preserve group identity/name and all individual source data.
            result.Add(group with{Reading=chosen with{Key=root,Name=group.Reading.Name}});
        }
        foreach(var key in selected.Keys.Where(k=>!retained.Contains(k)).ToArray())selected.Remove(key);
        return result.ToArray();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

// Selection is independent of window placement and rendering. A source remains
// preferred while healthy; only confirmed usable data can replace it.
internal sealed class BatterySourceSelector
{
    private readonly Dictionary<string,string> selected=new(StringComparer.Ordinal);
    // Native Logitech readers may legitimately poll only once per 600 seconds.
    internal static readonly TimeSpan StaleAfter=TimeSpan.FromMinutes(20);
    internal static bool Usable(TaskbarBattery row)=>row.IsConnected&&row.Level is >=0 and <=100;
    internal static bool Fresh(TaskbarBattery row,DateTimeOffset now)=>row.Updated!=DateTimeOffset.MinValue&&now-row.Updated<=StaleAfter;
    internal DeviceGroup[] Select(IEnumerable<DeviceGroup> groups,RingPreferences preferences,DateTimeOffset now)
    {
        var result=new List<DeviceGroup>();
        var retained=new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in groups)
        {
            string root=group.Reading.Key;
            retained.Add(root);
            var sources=group.Sources;
            var valid=sources.Where(Usable).ToArray();
            var candidates=valid.Where(d=>SourceReading.Normalize(d,now,preferences).Quality==ReadingQuality.Exact).ToArray();
            TaskbarBattery? chosen=null;
            if(preferences.PreferredSources.TryGetValue(root,out var preferred))
                chosen=candidates.FirstOrDefault(d=>d.Key==preferred);
            if(chosen==null&&selected.TryGetValue(root,out var previous))
                chosen=candidates.FirstOrDefault(d=>d.Key==previous);
            chosen??=candidates.Length>0?DeviceIdentityManager.Best(candidates,null):
                DeviceIdentityManager.Best(sources.Where(d=>d.IsConnected&&SourceReading.Normalize(d,now,preferences).Quality==ReadingQuality.Band).DefaultIfEmpty(DeviceIdentityManager.Best(sources,null)),null);
            if(SourceReading.Normalize(chosen,now,preferences).Quality==ReadingQuality.Stale)
                chosen=chosen with{LastKnownLevel=chosen.Level,Level=null,ShortLabel=null,Stale=true,Charging=false};
            selected[root]=chosen.Key;
            // Preserve group identity/name and all individual source data.
            result.Add(group with{Reading=chosen with{Key=root,Name=group.Reading.Name}});
        }
        foreach(var key in selected.Keys.Where(k=>!retained.Contains(k)).ToArray())selected.Remove(key);
        return result.ToArray();
    }
}

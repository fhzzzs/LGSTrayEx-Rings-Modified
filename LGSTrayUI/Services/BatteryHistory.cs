using System;
using System.Linq;
using System.Collections.Generic;
namespace LGSTrayUI;
internal sealed record BatteryHistoryEntry(DateTimeOffset Time,string Name,string Value);
internal sealed class BatteryHistory(int limit){
    private readonly Dictionary<string,(string Name,int? Level,string? Label,ConnectionState Connection,BatteryChargeState Charge,bool Stale)> last=new(StringComparer.Ordinal);
    private readonly Queue<BatteryHistoryEntry> entries=new();
    internal void Observe(DeviceGroup[] groups,DateTimeOffset now){
        foreach(var g in groups){
            var d=g.Reading;
            var state=(d.Name,d.Level,d.ShortLabel,d.Connection,d.ChargeState,d.Stale);
            if(last.TryGetValue(d.Key,out var old)&&old==state)continue;
            last[d.Key]=state;
            entries.Enqueue(new(now,d.Name,d.Percent+" · "+d.Connection+" · "+d.ChargeState));
            while(entries.Count>Math.Max(1,limit))entries.Dequeue();
        }
        if(last.Count>groups.Length){
            foreach(var key in last.Keys.ToArray())if(!groups.Any(g=>g.Reading.Key==key))last.Remove(key);
        }
    }
    internal BatteryHistoryEntry[] Snapshot()=>entries.ToArray();
}

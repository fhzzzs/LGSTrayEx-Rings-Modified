using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

internal sealed record PhysicalDeviceSnapshot(DeviceGroup[] Groups,TaskbarBattery[] Rows,ConnectionChange[] Changes,bool Suppressed);

// Own the raw inventory, source selection and confirmed lifecycle independently
// of the WPF window. Views consume the same immutable-by-convention snapshot.
internal sealed class PhysicalDeviceCenter
{
    internal Dictionary<string,TaskbarBattery> Inventory {get;}=new(StringComparer.Ordinal);
    private readonly BatterySourceSelector selector=new();
    private readonly GroupedConnectionTracker connections;
    private DeviceGroup[] rawGroups=[];
    internal PhysicalDeviceCenter(DateTimeOffset now){connections=new(now);}
    internal void Suppress(DateTimeOffset now,TimeSpan duration)=>connections.Suppress(now,duration);
    internal void Regroup(RingPreferences c){rawGroups=PhysicalIdentityRegistry.Assign(DeviceIdentityManager.Group(Inventory.Values,c),c);}
    internal bool RefreshInventory(IEnumerable<TaskbarBattery> readings,RingPreferences c)
    {
        var current=readings.Select(ControllerIdentity.Normalize).GroupBy(d=>d.Key,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>DeviceIdentityManager.Best(g,null),StringComparer.Ordinal);
        bool changed=false;
        var migrated=new HashSet<string>(StringComparer.Ordinal);
        // Only legacy synthetic model keys have three segments; a real
        // provider key can itself contain additional colon-separated data.
        foreach(var key in Inventory.Keys.Where(k=>k.StartsWith("logi:",StringComparison.Ordinal)&&k.Split(':').Length==3).ToArray()){
            var matches=current.Keys.Where(k=>k.StartsWith(key+":",StringComparison.Ordinal)).ToArray();
            if(matches.Length==0)continue;
            if(matches.Length==1){DeviceIdentityManager.Inherit(c,key,matches[0]);if(c.Hidden.Contains(key)&&!c.Hidden.Contains(matches[0]))c.Hidden.Add(matches[0]);migrated.Add(matches[0]);}
            Inventory.Remove(key);changed=true;
        }
        var previousKeys=rawGroups.Select(g=>g.Reading.Key).ToHashSet(StringComparer.Ordinal);
        var previousRaw=Inventory.Keys.ToHashSet(StringComparer.Ordinal);
        foreach(var d in current.Values){
            if(!Inventory.TryGetValue(d.Key,out var old)||old.Name!=d.Name||old.Type!=d.Type||old.PhysicalId!=d.PhysicalId||old.HardwareVendorId!=d.HardwareVendorId||old.HardwareProductId!=d.HardwareProductId||(old.Updated==DateTimeOffset.MinValue&&d.Updated!=DateTimeOffset.MinValue))changed=true;
            Inventory[d.Key]=old!=null&&d.Updated==DateTimeOffset.MinValue?d with{Updated=old.Updated}:d;
        }
        foreach(var key in Inventory.Keys.Where(k=>!current.ContainsKey(k)).ToArray())
            Inventory[key]=Inventory[key] with{Level=null,Charging=false,Status="离线",ShortLabel=null};
        // Only expire a missing, old source when another currently live
        // source has exactly the same physical evidence. No model-only cleanup.
        foreach(var key in ConfirmedHistoryRetention.Expired(Inventory.Values,current,c,DateTimeOffset.UtcNow)){
            Inventory.Remove(key);changed=true;
        }
        var previousControllerHints=new Dictionary<string,string>(c.ControllerHistoryHints);
        var previousSourceEvidence=new Dictionary<string,string>(c.PhysicalSourceEvidence);
        var previousIds=new Dictionary<string,string>(c.PhysicalSourceIds);var previousEvidence=new Dictionary<string,string>(c.PhysicalEvidenceIds);
        var previousAliases=new Dictionary<string,string>(c.AutomaticAliases);int previousModelRoots=c.ModelGroupRoots.Count;
        Regroup(c);
        if(previousControllerHints.Count!=c.ControllerHistoryHints.Count||previousControllerHints.Any(p=>c.ControllerHistoryHints.GetValueOrDefault(p.Key)!=p.Value)||previousSourceEvidence.Count!=c.PhysicalSourceEvidence.Count||previousSourceEvidence.Any(p=>c.PhysicalSourceEvidence.GetValueOrDefault(p.Key)!=p.Value)||previousIds.Count!=c.PhysicalSourceIds.Count||previousIds.Any(p=>c.PhysicalSourceIds.GetValueOrDefault(p.Key)!=p.Value)||
           previousEvidence.Count!=c.PhysicalEvidenceIds.Count||previousEvidence.Any(p=>c.PhysicalEvidenceIds.GetValueOrDefault(p.Key)!=p.Value))changed=true;
        if(previousAliases.Count!=c.AutomaticAliases.Count||previousAliases.Any(p=>!c.AutomaticAliases.TryGetValue(p.Key,out var v)||v!=p.Value)||previousModelRoots!=c.ModelGroupRoots.Count)changed=true;
        foreach(var group in rawGroups){
            var key=group.Reading.Key;
            if(group.Sources.All(d=>!previousRaw.Contains(d.Key)&&!migrated.Contains(d.Key))&&!previousKeys.Contains(key)){
                if(!c.AutoAdd&&!c.Hidden.Contains(key)){c.Hidden.Add(key);changed=true;}
                if(c.Order.Count>0&&!c.Order.Contains(key)){c.Order.Add(key);changed=true;}
            }
        }
        return changed;
    }
    internal PhysicalDeviceSnapshot Advance(RingPreferences c,DateTimeOffset now)
    {
        var selected=selector.Select(rawGroups,c,now);
        var snapshot=connections.ObserveSnapshot(selected,now);
        var rows=snapshot.Groups.Where(g=>!g.Ignored)
            .Select(g=>g.Reading).Select(d=>c.Names.TryGetValue(d.Key,out var name)&&!string.IsNullOrWhiteSpace(name)?d with{Name=name}:d).ToArray();
        return new(snapshot.Groups,rows,snapshot.Changes.Where(x=>!snapshot.Groups.Any(g=>g.Ignored&&g.Reading.Key==x.Device.Key)).ToArray(),snapshot.Suppressed);
    }
}
